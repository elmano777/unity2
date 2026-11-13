using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The "feel" layer of the Lane (IHC: presence, emotion, comfort). It listens to what already happens
/// in the ritual and answers with the senses a headset has: controller vibration with a different
/// pattern for each event, short sounds, a red edge flash when the player is hurt, a warning before a
/// tower fires at them, a world that grows tenser as towers fall, and a gentle first 30 seconds.
/// </summary>
/// <remarks>
/// It builds itself when the Lane scene loads (see <see cref="Bootstrap"/>), so no scene needs editing
/// and no other script depends on it: remove this file and the match plays exactly as before.
/// </remarks>
public class ExperienceDirector : MonoBehaviour
{
    private const string LaneScene = "Lane";

    [Header("Hurt")]
    [Tooltip("Health fraction under which the edge keeps pulsing and the heartbeat plays.")]
    [SerializeField] private float lowHealthFraction = 0.3f;

    [Header("Tower warning")]
    [Tooltip("A tower counts as aiming at the player when its aim point is this close to the head, in metres.")]
    [SerializeField] private float aimedAtRadius = 2.5f;

    [Header("Mood")]
    [Tooltip("How far the sun shifts to a warm, low light when every tower has fallen (0-1).")]
    [Range(0f, 1f)]
    [SerializeField] private float maxTension = 0.6f;

    private static readonly Color Warm = new Color(1f, 0.62f, 0.38f, 1f);

    private Health playerHealth;
    private float lastHealth = -1f;
    private float nextHealthSearch;
    private float nextHeartbeat;
    private bool lowAlerted;

    private Tower[] towers = System.Array.Empty<Tower>();
    private bool[] warned = System.Array.Empty<bool>();
    private float nextTowerScan;

    private Renderer flashRenderer;
    private Material flashMaterial;
    private float flash;

    private Light sun;
    private Color sunBaseColor;
    private float sunBaseIntensity;
    private float tension;
    private float tensionTarget;
    private int fallen;

    private AbilityCaster caster;
    private bool[] wasCooling = System.Array.Empty<bool>();
    private bool matchOver;

    // ---- Start-up ----

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != LaneScene || FindObjectOfType<ExperienceDirector>() != null)
        {
            return;
        }

        new GameObject(nameof(ExperienceDirector)).AddComponent<ExperienceDirector>();
    }

    private void Start()
    {
        towers = FindObjectsOfType<Tower>();
        warned = new bool[towers.Length];
        caster = FindObjectOfType<AbilityCaster>();

        sun = RenderSettings.sun;

        if (sun == null)
        {
            foreach (Light candidate in FindObjectsOfType<Light>())
            {
                if (candidate.type == LightType.Directional)
                {
                    sun = candidate;
                    break;
                }
            }
        }

        if (sun != null)
        {
            sunBaseColor = sun.color;
            sunBaseIntensity = sun.intensity;
        }

        StartCoroutine(FirstSeconds());
        StartCoroutine(PlayIntro());
    }

    /// <summary>The patron's intro as the ritual begins: the Hidden King for Seven, the Archmother for Vindicta.</summary>
    private IEnumerator PlayIntro()
    {
        bool practice = GameSession.Instance != null && GameSession.Instance.IsPracticeRun;

        if (practice)
        {
            yield break;
        }

        yield return new WaitForSeconds(0.6f);

        HeroDefinitionSO hero = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;
        bool vindicta = hero != null && hero.heroName != null && hero.heroName.Contains("Vindicta");
        ExperienceCues.Play(vindicta ? "intro_mother" : "intro_king", 0.7f);
    }

    private void OnEnable()
    {
        GameEvents.SoulsCollected += OnSouls;
        SoulPickup.Resolved += OnSoulResolved;
        GameEvents.ItemPurchased += OnPurchased;
        GameEvents.PatronDestroyed += OnPatronDestroyed;
        GameEvents.DamageDealt += OnDamageDealt;
        GameEvents.Died += OnDied;
        GameEvents.HintRequested += OnHint;
        GameEvents.MinionKilled += OnMinionKilled;
        GameEvents.TowerDestroyed += OnTowerDestroyed;
        GameEvents.MatchStateChanged += OnMatchState;
    }

    private void OnDisable()
    {
        GameEvents.SoulsCollected -= OnSouls;
        SoulPickup.Resolved -= OnSoulResolved;
        GameEvents.ItemPurchased -= OnPurchased;
        GameEvents.PatronDestroyed -= OnPatronDestroyed;
        GameEvents.DamageDealt -= OnDamageDealt;
        GameEvents.Died -= OnDied;
        GameEvents.HintRequested -= OnHint;
        GameEvents.MinionKilled -= OnMinionKilled;
        GameEvents.TowerDestroyed -= OnTowerDestroyed;
        GameEvents.MatchStateChanged -= OnMatchState;

        if (playerHealth != null)
        {
            playerHealth.HealthChanged -= OnHealthChanged;
        }

        if (sun != null)
        {
            sun.color = sunBaseColor;
            sun.intensity = sunBaseIntensity;
        }
    }

    // ---- Events ----

    private void OnSouls(int amount)
    {
        ExperienceCues.Play(ExperienceCues.Soul, 0.55f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.25f, 0.04f);
        StartCoroutine(After(0.07f, () => HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.4f, 0.05f)));
    }

    /// <summary>A soul ended. A denied one gets its own sound and a short note, from either side's point of view.</summary>
    private void OnSoulResolved(SoulOutcome outcome, Faction owner, Vector3 at)
    {
        if (outcome != SoulOutcome.Denied)
        {
            return;
        }

        bool mine = owner == MatchNet.LocalFaction;
        ExperienceCues.PlayAt(ExperienceCues.SoulDenied, at, 0.7f);
        HapticFeedback.Pulse(mine ? HapticFeedback.Hand.Both : HapticFeedback.Hand.Right, mine ? 0.5f : 0.3f, 0.1f);

        GameEvents.RaiseHint(mine ? "¡Te denegaron un alma!" : "¡Alma denegada!");
        CancelInvoke(nameof(ClearHint));
        Invoke(nameof(ClearHint), 2f);
    }

    private void ClearHint() => GameEvents.RaiseHint(string.Empty);

    private void OnPurchased(ShopItemSO item)
    {
        ExperienceCues.Play(ExperienceCues.ShopUpgrade, 0.6f);

        // Each item can have its own sound on top: Resources/Cues/item_<asset name>, e.g. item_coraza.
        if (item != null)
        {
            ExperienceCues.Play("item_" + item.name.Replace("Item_", string.Empty).ToLowerInvariant(), 0.7f);
        }
    }

    private float nextHitConfirm;
    private bool firstBloodDone;

    private static bool IsHeroLayer(GameObject go)
    {
        string layer = LayerMask.LayerToName(go.layer);
        return layer == "PlayerBody" || layer == "Rival";
    }

    /// <summary>A short tick when your own shot or ability lands on a hero or a minion.</summary>
    private void OnDamageDealt(IDamageable target, DamageInfo info)
    {
        if (info.source != MatchNet.LocalFaction || info.instigator == null || Time.time < nextHitConfirm)
        {
            return;
        }

        if (info.type != DamageType.Bullet && info.type != DamageType.Ability)
        {
            return;
        }

        if (target is Component part && (part.GetComponentInParent<MinionAgent>() != null || IsHeroLayer(part.gameObject)))
        {
            nextHitConfirm = Time.time + 0.06f;
            ExperienceCues.Play(ExperienceCues.HitConfirm, 0.35f);
        }
    }

    /// <summary>Confirms your kills: a minion, or the rival's hero with an extra sound.</summary>
    private void OnDied(GameObject victim, DamageInfo info)
    {
        // The rival falling is heard in 3D from where it happens, whoever got the kill (and on both screens).
        if (victim != null && LayerMask.LayerToName(victim.layer) == "Rival")
        {
            NetworkPlayer rival = victim.GetComponentInParent<NetworkPlayer>();
            ExperienceCues.PlayAt(DeathCue(rival != null ? rival.Hero : null), victim.transform.position, 0.9f);
        }

        // Replicas report deaths with an empty hit (no instigator): ignore those, they aren't ours.
        if (victim == null || info.instigator == null || info.source != MatchNet.LocalFaction)
        {
            return;
        }

        if (IsHeroLayer(victim))
        {
            if (!firstBloodDone)
            {
                firstBloodDone = true;
                ExperienceCues.Play("stinger_first_blood", 0.7f);
            }

            ExperienceCues.Play(ExperienceCues.HeroKill, 0.8f);
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.6f, 0.15f);
        }
        else if (victim.GetComponentInParent<MinionAgent>() != null)
        {
            ExperienceCues.Play(ExperienceCues.KillConfirm, 0.5f);
        }
    }

    private string lastHint = string.Empty;

    /// <summary>A soft ping when a new hint appears, so a message at the top of the view is noticed without staring at it.</summary>
    private void OnHint(string message)
    {
        if (!string.IsNullOrEmpty(message) && message != lastHint)
        {
            ExperienceCues.Play(ExperienceCues.HintPing, 0.35f);
        }

        lastHint = message ?? string.Empty;
    }

    private void OnMinionKilled(Faction faction)
    {
        if (faction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.12f, 0.03f);
        }
    }

    private void OnTowerDestroyed(Structure tower, Faction faction)
    {
        fallen++;
        tensionTarget = Mathf.Clamp01(fallen / Mathf.Max(1f, towers.Length)) * maxTension;

        Vector3 at = tower != null ? tower.transform.position : transform.position;
        PlayStructureFall(tower, faction, at);
        StartCoroutine(Rumble(1.6f, 0.9f, 0.1f));

        // Your minions cheer when a rival structure falls.
        if (faction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            ExperienceCues.Play("team_cheer", 0.5f);
        }
    }

    /// <summary>The sound of a structure falling depends on which one it is: Guardian, Walker or a shrine.</summary>
    private static void PlayStructureFall(Structure tower, Faction faction, Vector3 at)
    {
        string main;
        string layer = null;

        if (tower != null && tower.Tier >= 3)
        {
            main = faction == Faction.Player ? "shrine_destroy_amber" : "shrine_destroy_sapphire";
        }
        else if (tower != null && tower.Tier == 2)
        {
            main = "tower_fall_t2";
            layer = "tower_fall_t2_b";
        }
        else
        {
            main = "tower_fall_t1";
            layer = "tower_fall_t1_b";
        }

        // Without the real file the built-in rumble still plays.
        if (ExperienceCues.Clip(main) == null)
        {
            main = ExperienceCues.TowerFall;
        }

        ExperienceCues.PlayAt(main, at, 1f);

        // The music sting that marks it: a lighter one for the first two tiers, a heavier one beyond.
        ExperienceCues.Play(tower != null && tower.Tier >= 3 ? "stinger_t3" : "stinger_t1", 0.55f);

        if (layer != null)
        {
            ExperienceCues.PlayAt(layer, at, 0.9f);
        }
    }

    private void OnPatronDestroyed(Faction losing)
    {
        // Seven's patron is the Hidden King, Vindicta's the Archmother: the host's side is the king's here.
        ExperienceCues.Play(losing == Faction.Player ? "patron_fall_king" : "patron_fall_mother", 0.9f);
    }

    private void OnMatchState(MatchState state)
    {
        if (state == MatchState.Playing)
        {
            ExperienceCues.Play("match_start", 0.7f);
        }

        if (state == MatchState.Win)
        {
            matchOver = true;
            ExperienceCues.Play(ExperienceCues.Victory, 0.5f);
            StartCoroutine(Celebrate());
        }
        else if (state == MatchState.Lose)
        {
            matchOver = true;
            ExperienceCues.Play(ExperienceCues.Defeat, 0.5f);
            StartCoroutine(Rumble(1.4f, 0.6f, 0f));
        }
    }

    private void OnHealthChanged(float current, float max)
    {
        if (lastHealth >= 0f && current < lastHealth && max > 0f)
        {
            float lost = (lastHealth - current) / max;
            float strength = Mathf.Clamp01(0.35f + lost * 3f);

            HapticFeedback.Pulse(HapticFeedback.Hand.Both, strength, 0.12f);

            // A different sound for a normal hit, a big one (over a fifth of the life) and the one that downs you.
            string hitCue = current <= 0f ? ExperienceCues.PlayerDown : lost >= 0.2f ? ExperienceCues.HitMajor : ExperienceCues.Hit;
            ExperienceCues.Play(hitCue, 0.7f);
            flash = Mathf.Max(flash, Mathf.Clamp01(0.4f + lost * 2f));
        }
        else if (lastHealth <= 0f && current > 0f)
        {
            ExperienceCues.Play(ExperienceCues.Revive, 0.7f);
            ExperienceCues.Play("stinger_player_respawn", 0.5f);
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.5f, 0.2f);
        }

        // Going down used to send the player back to the base with no warning. Say so, and count the seconds.
        if (current <= 0f && lastHealth > 0f && !matchOver)
        {
            StartCoroutine(DownedCountdown());
        }

        lastHealth = current;
    }

    /// <summary>The hero's cry when it falls: Vindicta's voice for her, the male one for the others.</summary>
    private static string DeathCue(HeroDefinitionSO hero)
    {
        return hero != null && hero.heroName != null && hero.heroName.Contains("Vindicta") ? "death_female" : "death_male";
    }

    private IEnumerator DownedCountdown()
    {
        float seconds = NetworkPlayer.Local != null ? NetworkPlayer.Local.RespawnSeconds : 5f;
        HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.9f, 0.3f);

        // The hero's cry and the music sting underneath it (the generic sting if there is no file).
        ExperienceCues.Play(DeathCue(GameSession.Instance != null ? GameSession.Instance.SelectedHero : null), 0.85f);
        ExperienceCues.Play(ExperienceCues.Clip("stinger_player_death") != null ? "stinger_player_death" : ExperienceCues.Defeat, 0.5f);
        flash = 1f;

        for (float left = seconds; left > 0f && !matchOver; left -= 1f)
        {
            GameEvents.RaiseHint($"Caíste. Reapareces en tu base en {Mathf.CeilToInt(left)}…");
            yield return new WaitForSeconds(1f);
        }

        if (!matchOver)
        {
            GameEvents.RaiseHint("¡De vuelta en tu base!");
            yield return new WaitForSeconds(2f);
            GameEvents.RaiseHint(string.Empty);
        }
    }

    // ---- Per frame ----

    private void Update()
    {
        FindPlayerHealth();
        ScanTowers();
        WatchAbilities();
        UpdateMood();
    }

    private void LateUpdate()
    {
        UpdateFlash();
    }

    private void FindPlayerHealth()
    {
        if (playerHealth != null || Time.time < nextHealthSearch)
        {
            return;
        }

        nextHealthSearch = Time.time + 1f;
        int layer = LayerMask.NameToLayer("PlayerBody");

        if (layer < 0)
        {
            return;
        }

        foreach (Health health in FindObjectsOfType<Health>())
        {
            if (health.gameObject.layer == layer)
            {
                playerHealth = health;
                lastHealth = health.CurrentHealth;
                health.HealthChanged += OnHealthChanged;
                return;
            }
        }
    }

    /// <summary>
    /// Warns before a tower fires at the player: its staff already swings to point at them during
    /// the wind-up, so a rising buzz and a note from the tower give the same notice a Monster Hunter
    /// monster gives with its anticipation pose. It is feedback that lets the player react.
    /// </summary>
    private void ScanTowers()
    {
        if (matchOver || Time.time < nextTowerScan || Camera.main == null)
        {
            return;
        }

        nextTowerScan = Time.time + 0.1f;
        Vector3 head = Camera.main.transform.position;
        Faction local = MatchNet.LocalFaction;

        for (int i = 0; i < towers.Length; i++)
        {
            Tower tower = towers[i];

            if (tower == null)
            {
                continue;
            }

            bool aimed = tower.IsStanding
                         && FactionUtility.IsHostile(tower.Faction, local)
                         && tower.HasAimPoint
                         && Vector3.Distance(tower.AimPoint, head) < aimedAtRadius;

            if (aimed && !warned[i])
            {
                ExperienceCues.PlayAt(ExperienceCues.TowerWarning, tower.transform.position, 0.8f);
                StartCoroutine(Ramp(0.35f, 0.1f, 0.55f));
            }

            warned[i] = aimed;
        }
    }

    private void WatchAbilities()
    {
        if (caster == null)
        {
            return;
        }

        int count = caster.Abilities.Count;

        if (wasCooling.Length != count)
        {
            wasCooling = new bool[count];
        }

        for (int i = 0; i < count; i++)
        {
            bool cooling = caster.CooldownRemaining(i) > 0f;

            if (wasCooling[i] && !cooling)
            {
                ExperienceCues.Play(ExperienceCues.Ready, 0.4f);
                HapticFeedback.Pulse(i == 0 ? HapticFeedback.Hand.Right : HapticFeedback.Hand.Left, 0.2f, 0.05f);
            }

            wasCooling[i] = cooling;
        }
    }

    private void UpdateMood()
    {
        if (sun == null)
        {
            return;
        }

        tension = Mathf.MoveTowards(tension, tensionTarget, 0.15f * Time.deltaTime);
        sun.color = Color.Lerp(sunBaseColor, Warm, tension);
        sun.intensity = sunBaseIntensity * (1f - 0.18f * tension / Mathf.Max(0.01f, maxTension));
    }

    // ---- Red edge ----

    private void UpdateFlash()
    {
        EnsureFlash();

        if (flashRenderer == null)
        {
            return;
        }

        float pulse = 0f;
        bool low = playerHealth != null && playerHealth.IsAlive && playerHealth.Normalized < lowHealthFraction;

        // The alert sounds once when health first drops under the line, not on every heartbeat.
        if (low && !lowAlerted)
        {
            ExperienceCues.Play(ExperienceCues.LowHealthAlert, 0.6f);
        }

        lowAlerted = low;

        if (low)
        {
            pulse = 0.18f + 0.12f * Mathf.Sin(Time.time * 6f);

            if (Time.time >= nextHeartbeat)
            {
                nextHeartbeat = Time.time + 1.1f;
                ExperienceCues.Play(ExperienceCues.LowHealth, 0.5f);
                HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.18f, 0.06f);
            }
        }

        flash = Mathf.MoveTowards(flash, 0f, 1.6f * Time.deltaTime);
        float alpha = Mathf.Clamp01(Mathf.Max(flash * 0.8f, pulse));

        flashRenderer.enabled = alpha > 0.01f;
        flashMaterial.color = new Color(0.9f, 0.05f, 0.05f, alpha);
    }

    private void EnsureFlash()
    {
        if (flashRenderer != null || Camera.main == null)
        {
            return;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x / (size - 1f)) * 2f - 1f;
                float dy = (y / (size - 1f)) * 2f - 1f;
                float edge = Mathf.SmoothStep(0.45f, 1.1f, Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
            }
        }

        texture.Apply();

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "HurtEdge";
        quad.transform.SetParent(Camera.main.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.55f);
        quad.transform.localScale = new Vector3(1.9f, 1.9f, 1f);

        flashRenderer = quad.GetComponent<Renderer>();
        flashMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture, renderQueue = 4000 };
        flashRenderer.sharedMaterial = flashMaterial;
        flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flashRenderer.enabled = false;
    }

    // ---- Haptic patterns ----

    private static IEnumerator After(float seconds, System.Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    /// <summary>A buzz that grows from <paramref name="from"/> to <paramref name="to"/>: "something is coming".</summary>
    private static IEnumerator Ramp(float seconds, float from, float to)
    {
        float end = Time.time + seconds;

        while (Time.time < end)
        {
            float k = 1f - (end - Time.time) / seconds;
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, Mathf.Lerp(from, to, k), 0.06f);
            yield return new WaitForSeconds(0.07f);
        }
    }

    /// <summary>A low rumble that fades out: a tower falling, or the ritual lost.</summary>
    private static IEnumerator Rumble(float seconds, float from, float to)
    {
        float end = Time.time + seconds;

        while (Time.time < end)
        {
            float k = 1f - (end - Time.time) / seconds;
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, Mathf.Lerp(from, to, k), 0.08f);
            yield return new WaitForSeconds(0.09f);
        }
    }

    /// <summary>Three bright taps, then a long warm swell.</summary>
    private static IEnumerator Celebrate()
    {
        for (int i = 0; i < 3; i++)
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.8f, 0.07f);
            yield return new WaitForSeconds(0.16f);
        }

        yield return Ramp(1.2f, 0.15f, 0.7f);
    }

    // ---- First 30 seconds (Semana 5: orient, discover, interact, purpose) ----

    private IEnumerator FirstSeconds()
    {
        bool practice = GameSession.Instance != null && GameSession.Instance.IsPracticeRun;
        bool veteran = PlayerProgress.MissionCompleted && PlayerProgress.MatchesPlayed >= 2;

        // The guided practice has its own hints, and an experienced player doesn't need coaching.
        if (practice || veteran)
        {
            yield break;
        }

        string[] steps =
        {
            "Estás en el carril. Mira a tu alrededor.",
            "Dispara a los minions rivales: sueltan almas.",
            "Las almas flotan: dispáralas para asegurarlas con bono. Si el rival dispara primero, las deniega.",
            "Mira tu muñeca izquierda: almas y habilidades.",
            "Apunta la mano izquierda a la cuerda y mantén el agarre para viajar.",
            "Objetivo: derriba las torres rivales, una a una.",
        };
        float[] waits = { 5f, 5f, 6f, 6f, 7f, 8f };

        yield return new WaitUntil(() => MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Playing);

        for (int i = 0; i < steps.Length; i++)
        {
            // Stop coaching the moment something bigger happens (a tower fell, the match ended).
            if (fallen > 0 || matchOver)
            {
                yield break;
            }

            GameEvents.RaiseHint(steps[i]);
            yield return new WaitForSeconds(waits[i]);
        }

        if (fallen == 0 && !matchOver)
        {
            GameEvents.RaiseHint(string.Empty);
        }
    }
}
