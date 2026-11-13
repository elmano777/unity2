using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The strip high in the view, as the hero shooter's top bar: your hero's portrait on one side, the
/// rival's on the other and the match clock between them. Each portrait tells that hero's state at
/// a glance with the art the game uses for it: the normal face, the "critical" one when health runs
/// low and the "gloat" one while on a streak (a hero kill, a structure down, a run of minions).
/// </summary>
/// <remarks>
/// It follows the gaze lazily, about 27° above the horizon, so it is there when the player glances
/// up and out of the way otherwise. Builds itself when the Lane loads; hides once the match is over.
/// </remarks>
public class MatchTopBar : MonoBehaviour
{
    private const string LaneScene = "Lane";

    private static readonly Color Amber = new Color(1f, 0.66f, 0.2f, 1f);
    private static readonly Color Sapphire = new Color(0.35f, 0.65f, 1f, 1f);
    private static readonly Color CriticalRed = new Color(1f, 0.25f, 0.2f, 1f);
    private static readonly Color StreakGold = new Color(1f, 0.85f, 0.35f, 1f);
    private static readonly Color Back = new Color(0.03f, 0.04f, 0.07f, 0.8f);

    [Tooltip("Metres in front of the eyes.")]
    [SerializeField] private float distance = 1.8f;

    [Tooltip("Metres above eye level (about 27° up at 1.8 m), clear of the hint board.")]
    [SerializeField] private float above = 0.92f;

    [SerializeField] private float followSpeed = 2f;

    [Tooltip("Health fraction at or under which the portrait turns critical.")]
    [SerializeField] private float criticalFraction = 0.3f;

    [Tooltip("Seconds a streak face stays up after it was earned.")]
    [SerializeField] private float streakSeconds = 6f;

    [Tooltip("Minion kills within the window below that count as a streak.")]
    [SerializeField] private int minionStreak = 4;

    [SerializeField] private float minionWindow = 8f;

    private const float PortraitHeight = 0.22f;
    private const float PortraitX = 0.28f;

    private Transform head;
    private Vector3 smoothForward = Vector3.forward;
    private GameObject root;
    private TMP_Text clock;
    private Side mine;
    private Side theirs;
    private float nextRivalSearch;

    /// <summary>One hero's portrait and what it needs to choose its face.</summary>
    private class Side
    {
        public Faction faction;
        public HeroDefinitionSO hero;
        public Health health;
        public Transform portrait;
        public Material face;
        public Material frame;
        public Transform healthFill;
        public Material healthMaterial;
        public Color sideColor;
        public float streakUntil;
        public int minionCount;
        public float minionWindowEnd;
        public Texture shown;
        public float bump;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<MatchTopBar>() == null)
        {
            new GameObject(nameof(MatchTopBar)).AddComponent<MatchTopBar>();
        }
    }

    private void OnEnable()
    {
        GameEvents.Died += OnDied;
        GameEvents.TowerDestroyed += OnTowerDestroyed;
        GameEvents.MinionKilled += OnMinionKilled;
    }

    private void OnDisable()
    {
        GameEvents.Died -= OnDied;
        GameEvents.TowerDestroyed -= OnTowerDestroyed;
        GameEvents.MinionKilled -= OnMinionKilled;
    }

    private void Start()
    {
        root = new GameObject("TopBar");
        root.transform.SetParent(transform, false);

        Faction local = MatchNet.LocalFaction;
        HeroDefinitionSO myHero = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;

        Quad("Back", new Vector3(0f, 0f, 0.003f), new Vector2(PortraitX * 2f + 0.2f, 0.09f), Back, null, 3000);
        clock = Text("00:00", new Vector3(0f, 0f, -0.002f), 0.065f, Color.white);

        // Your side on the left, as the hero shooter puts your team.
        mine = BuildSide(-PortraitX, local, myHero);
        theirs = BuildSide(PortraitX, FactionUtility.Opposite(local), null);
        theirs.portrait.gameObject.SetActive(false);
    }

    private Side BuildSide(float x, Faction faction, HeroDefinitionSO hero)
    {
        var side = new Side
        {
            faction = faction,
            hero = hero,
            sideColor = faction == Faction.Player ? Amber : Sapphire,
        };

        float width = PortraitHeight * 280f / 380f;
        side.portrait = new GameObject("Portrait_" + faction).transform;
        side.portrait.SetParent(root.transform, false);
        side.portrait.localPosition = new Vector3(x, 0f, 0f);

        side.frame = Quad("Frame", new Vector3(0f, 0f, 0.002f), new Vector2(width + 0.014f, PortraitHeight + 0.014f), side.sideColor, null, 3001, side.portrait);
        Quad("FaceBack", new Vector3(0f, 0f, 0.001f), new Vector2(width, PortraitHeight), new Color(0.08f, 0.08f, 0.1f, 1f), null, 3002, side.portrait);
        side.face = Quad("Face", Vector3.zero, new Vector2(width, PortraitHeight), Color.white, null, 3003, side.portrait);

        // A thin health strip under the portrait.
        float y = -PortraitHeight / 2f - 0.018f;
        Quad("HealthBack", new Vector3(0f, y, 0.001f), new Vector2(width, 0.014f), new Color(0.12f, 0.13f, 0.18f, 1f), null, 3002, side.portrait);
        side.healthMaterial = Quad("HealthFill", new Vector3(0f, y, 0f), new Vector2(width, 0.014f), side.sideColor, null, 3003, side.portrait);
        side.healthFill = side.portrait.Find("HealthFill");
        return side;
    }

    // ---- Events ----

    private void OnDied(GameObject victim, DamageInfo info)
    {
        if (victim == null || mine == null)
        {
            return;
        }

        string layer = LayerMask.LayerToName(victim.layer);

        // A hero down is the other hero's streak, whoever's machine reports it.
        if (layer == "Rival")
        {
            StartStreak(mine);
        }
        else if (layer == "PlayerBody")
        {
            StartStreak(theirs);
        }
    }

    private void OnTowerDestroyed(Structure tower, Faction faction)
    {
        if (mine == null)
        {
            return;
        }

        StartStreak(faction == mine.faction ? theirs : mine);
    }

    private void OnMinionKilled(Faction faction)
    {
        if (mine == null)
        {
            return;
        }

        Side killer = faction == mine.faction ? theirs : mine;

        if (killer == null)
        {
            return;
        }

        if (Time.time > killer.minionWindowEnd)
        {
            killer.minionCount = 0;
            killer.minionWindowEnd = Time.time + minionWindow;
        }

        killer.minionCount++;

        if (killer.minionCount >= minionStreak)
        {
            killer.minionCount = 0;
            StartStreak(killer);
        }
    }

    private void StartStreak(Side side)
    {
        if (side != null)
        {
            side.streakUntil = Time.time + streakSeconds;
        }
    }

    // ---- Per frame ----

    private void LateUpdate()
    {
        if (root == null)
        {
            return;
        }

        if (MatchManager.IsOver)
        {
            // The summary panel takes over.
            root.SetActive(false);
            return;
        }

        if (!Follow())
        {
            return;
        }

        FindBodies();

        float elapsed = MatchManager.Instance != null ? MatchManager.Instance.Elapsed : 0f;
        clock.text = $"{Mathf.FloorToInt(elapsed / 60f):00}:{Mathf.FloorToInt(elapsed % 60f):00}";

        UpdateSide(mine);

        if (theirs.hero != null)
        {
            theirs.portrait.gameObject.SetActive(true);
            UpdateSide(theirs);
        }
    }

    private bool Follow()
    {
        if (head == null)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return false;
            }

            head = camera.transform;
            smoothForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        }

        Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);

        if (facing.sqrMagnitude > 0.01f)
        {
            smoothForward = Vector3.Slerp(smoothForward, facing.normalized, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        }

        Vector3 position = head.position + smoothForward * distance + Vector3.up * above;
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        return true;
    }

    private void FindBodies()
    {
        if (mine.health == null)
        {
            if (NetworkPlayer.Local != null && NetworkPlayer.Local.Body != null)
            {
                mine.health = NetworkPlayer.Local.Body;
            }
            else
            {
                int layer = LayerMask.NameToLayer("PlayerBody");

                foreach (Health h in FindObjectsOfType<Health>())
                {
                    if (h.gameObject.layer == layer)
                    {
                        mine.health = h;
                        break;
                    }
                }
            }
        }

        if (mine.hero == null && NetworkPlayer.Local != null)
        {
            mine.hero = NetworkPlayer.Local.Hero;
        }

        // The rival only exists in a duel, and joins a moment after the Lane loads.
        if (theirs.hero == null && Time.time >= nextRivalSearch)
        {
            nextRivalSearch = Time.time + 1f;

            foreach (NetworkPlayer p in FindObjectsOfType<NetworkPlayer>())
            {
                if (p != NetworkPlayer.Local && p.Hero != null)
                {
                    theirs.hero = p.Hero;
                    theirs.health = p.Body;
                    break;
                }
            }
        }
    }

    private void UpdateSide(Side side)
    {
        HeroDefinitionSO hero = side.hero;
        float fraction = side.health != null ? side.health.Normalized : 1f;
        bool alive = side.health == null || side.health.IsAlive;
        bool critical = alive && fraction <= criticalFraction;
        bool streak = alive && Time.time < side.streakUntil;

        Texture normal = hero == null ? null : hero.hudNormal != null ? (Texture)hero.hudNormal : hero.portrait != null ? hero.portrait.texture : null;
        Texture face = critical && hero != null && hero.hudCritical != null ? hero.hudCritical
            : streak && hero != null && hero.hudStreak != null ? hero.hudStreak
            : normal;

        if (face != side.shown)
        {
            side.shown = face;
            side.face.mainTexture = face;
            side.bump = 1f;
        }

        // Down: the face goes grey until the hero is back.
        side.face.color = alive ? Color.white : new Color(0.3f, 0.3f, 0.33f, 1f);

        // The frame tells the state too: red pulse when critical, gold on a streak.
        Color frame = side.sideColor;

        if (critical)
        {
            frame = Color.Lerp(side.sideColor, CriticalRed, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
        }
        else if (streak)
        {
            frame = StreakGold;
        }

        side.frame.color = frame;

        float width = PortraitHeight * 280f / 380f;
        Vector3 scale = side.healthFill.localScale;
        scale.x = Mathf.Max(0.0001f, width * Mathf.Clamp01(fraction));
        side.healthFill.localScale = scale;
        side.healthFill.localPosition = new Vector3(-width / 2f + scale.x / 2f, side.healthFill.localPosition.y, 0f);
        side.healthMaterial.color = critical ? CriticalRed : side.sideColor;

        // A short pop whenever the face changes.
        side.bump = Mathf.MoveTowards(side.bump, 0f, 3f * Time.deltaTime);
        side.portrait.localScale = Vector3.one * (1f + 0.18f * side.bump * side.bump);
    }

    // ---- Building blocks ----

    private Material Quad(string name, Vector3 local, Vector2 size, Color color, Texture texture, int queue, Transform parent = null)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent != null ? parent : root.transform, false);
        quad.transform.localPosition = local;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        Destroy(quad.GetComponent<Collider>());

        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color, mainTexture = texture, renderQueue = queue };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer.sharedMaterial;
    }

    private TMP_Text Text(string content, Vector3 local, float size, Color color)
    {
        GameObject go = new GameObject("Clock");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = local;

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = content;
        text.fontSize = size * 10f;
        text.color = color;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.rectTransform.sizeDelta = new Vector2(0.25f, 0.08f);
        text.fontMaterial.renderQueue = 3004;
        return text;
    }
}
