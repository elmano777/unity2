using System;
using TMPro;
using UnityEngine;

/// <summary>
/// The hero's level and ability points, as in the original game: every soul collected (spent or not)
/// counts towards the next level; each level makes the hero a bit tougher (health, bullet damage,
/// ability power, from <see cref="HeroDefinitionSO"/>) and gives one ability point. Points buy each
/// ability's three upgrades (<see cref="AbilitySO.upgrades"/>, costing 1, 2 and 5), so over a ritual
/// the player maxes one ability and starts on the other: a choice, not a checklist.
/// </summary>
/// <remarks>
/// Upgrades are bought on a small panel above the wrist display that appears while there are points
/// to spend, by shooting a card twice like the shop (the first shot shows what it does, the second
/// buys it): no extra button in the middle of a fight. Level and tiers go to the rival through
/// <see cref="NetworkPlayer.PublishProgress"/> and to the match record (RF-16).
/// Added by <see cref="PlayerUpgrades"/> on the player's rig; local to each machine.
/// </remarks>
public class HeroProgression : MonoBehaviour
{
    public static HeroProgression Instance { get; private set; }

    /// <summary>Souls collected to reach each level (index = level - 1): about one wave for level 2, ~15 minutes for 10.</summary>
    public static readonly int[] LevelSouls = { 0, 50, 130, 240, 380, 550, 750, 980, 1240, 1530 };

    public static int MaxLevel => LevelSouls.Length;

    private const int AbilitySlots = 2;

    public int Level { get; private set; } = 1;

    /// <summary>Ability points not spent yet.</summary>
    public int Points { get; private set; }

    /// <summary>Souls collected this ritual (the wallet's lifetime count, seen from here).</summary>
    public int Collected { get; private set; }

    public int PointsSpent { get; private set; }

    /// <summary>Raised whenever the level, the points or a tier changes.</summary>
    public event Action Changed;

    private readonly int[] tiers = new int[AbilitySlots];
    private AbilityCaster caster;
    private HeroDefinitionSO hero;
    private UpgradePanel panel;

    /// <summary>0..1 towards the next level (1 at the top level).</summary>
    public float ProgressToNext => Level >= MaxLevel ? 1f
        : Mathf.InverseLerp(LevelSouls[Level - 1], LevelSouls[Level], Collected);

    /// <summary>Souls collected the next level needs, or 0 at the top.</summary>
    public int NextLevelSouls => Level >= MaxLevel ? 0 : LevelSouls[Level];

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable() => GameEvents.SoulsCollected += OnSouls;

    private void OnDisable() => GameEvents.SoulsCollected -= OnSouls;

    private void Start()
    {
        caster = FindObjectOfType<AbilityCaster>();
        hero = caster != null ? caster.Hero : GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;

        WristHud wrist = FindObjectOfType<WristHud>(true);

        if (wrist != null)
        {
            panel = UpgradePanel.Build(wrist.transform, this);
        }

        Publish();
    }

    // ---- Levels ----

    private void OnSouls(int amount)
    {
        if (amount <= 0 || MatchManager.IsOver)
        {
            return;
        }

        Collected += amount;

        while (Level < MaxLevel && Collected >= LevelSouls[Level])
        {
            LevelUp();
        }

        Changed?.Invoke();
    }

    private void LevelUp()
    {
        Level++;
        Points++;

        if (hero != null && PlayerUpgrades.Instance != null)
        {
            PlayerUpgrades.Instance.AddLevelBonus(hero.healthPerLevel, hero.bulletDamagePerLevel, hero.abilityPowerPerLevel);
        }

        ExperienceCues.Play(ExperienceCues.ShopUpgrade, 0.9f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.5f, 0.15f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.5f, 0.15f);
        GameEvents.RaiseHint($"¡Nivel {Level}! Tienes {Points} punto{(Points == 1 ? "" : "s")} de habilidad: mira tu muñeca y dispara a una mejora.");
        MatchStatsRecorder.LogEvent("level_up", Level.ToString());
        Publish();
    }

    // ---- Ability upgrades ----

    /// <summary>Upgrades bought for ability <paramref name="index"/>.</summary>
    public int Tier(int index) => index >= 0 && index < AbilitySlots ? tiers[index] : 0;

    /// <summary>The ability as the asset defines it (before upgrades).</summary>
    public AbilitySO BaseAbility(int index) =>
        hero != null && hero.abilities != null && index >= 0 && index < hero.abilities.Length ? hero.abilities[index] : null;

    /// <summary>Points the next upgrade of ability <paramref name="index"/> costs, or 0 when it has none left.</summary>
    public int NextCost(int index)
    {
        AbilitySO ability = BaseAbility(index);
        int tier = Tier(index);
        return ability == null || tier >= ability.UpgradeCount ? 0 : AbilitySO.UpgradeCosts[tier];
    }

    /// <summary>The next upgrade of ability <paramref name="index"/>, or null.</summary>
    public AbilityUpgrade NextUpgrade(int index) => NextCost(index) > 0 ? BaseAbility(index).upgrades[Tier(index)] : null;

    public bool CanUpgrade(int index) => NextCost(index) > 0 && Points >= NextCost(index);

    /// <summary>Spends the points and applies the next upgrade of ability <paramref name="index"/>.</summary>
    public bool TryUpgrade(int index)
    {
        if (!CanUpgrade(index))
        {
            return false;
        }

        int cost = NextCost(index);
        Points -= cost;
        PointsSpent += cost;
        tiers[index]++;

        if (caster != null)
        {
            caster.SetTier(index, tiers[index]);
        }

        ExperienceCues.Play("shop_buy", 0.8f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.6f, 0.12f);
        MatchStatsRecorder.LogEvent("ability_upgrade", $"{BaseAbility(index).abilityName} {tiers[index]}");
        Publish();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Tells the rival and the match record.</summary>
    private void Publish()
    {
        NetworkPlayer local = NetworkPlayer.Local;

        if (local != null)
        {
            local.PublishProgress(Level, tiers[0], tiers[1]);
        }

        if (MatchManager.Instance != null && MatchManager.Instance.Stats != null)
        {
            MatchStats stats = MatchManager.Instance.Stats;
            stats.heroLevel = Level;
            stats.abilityPointsSpent = PointsSpent;
            stats.abilityTiers = $"{tiers[0]}-{tiers[1]}";
        }
    }

    /// <summary>Test hook: collects souls as if picked up (editor demo keys and tests).</summary>
    public void DebugCollect(int souls) => OnSouls(souls);

    // ------------------------------------------------------------------ the wrist panel

    /// <summary>
    /// Two cards above the wrist display, one per ability, shown while there are points to spend:
    /// the next upgrade, its cost and what it does. Built at runtime so no scene step is needed.
    /// </summary>
    private sealed class UpgradePanel : MonoBehaviour
    {
        private HeroProgression owner;
        private UpgradeCard[] cards;
        private TextMeshPro header;
        private GameObject root;
        private int armed = -1;
        private float armedUntil;
        private float messageUntil;

        public static UpgradePanel Build(Transform wrist, HeroProgression owner)
        {
            var go = new GameObject("AbilityUpgradePanel");
            go.transform.SetParent(wrist, false);
            // Just above the wrist display, in its plane (it faces the eyes when the wrist is raised).
            go.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            go.transform.localRotation = Quaternion.identity;

            UpgradePanel panel = go.AddComponent<UpgradePanel>();
            panel.owner = owner;
            panel.root = go;

            Material back = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.05f, 0.04f, 0.08f, 0.88f) };
            Quad(go.transform, new Vector3(0f, 0.005f, 0.004f), new Vector2(0.3f, 0.15f), back);

            panel.header = Label(go.transform, new Vector3(0f, 0.062f, 0f), new Vector2(0.28f, 0.025f), 0.12f, new Color(1f, 0.85f, 0.4f));
            panel.cards = new UpgradeCard[AbilitySlots];

            for (int i = 0; i < AbilitySlots; i++)
            {
                panel.cards[i] = UpgradeCard.Build(go.transform, panel, i, new Vector3(i == 0 ? -0.072f : 0.072f, -0.008f, 0f));
            }

            panel.Refresh();
            owner.Changed += panel.Refresh;
            return panel;
        }

        private void OnDestroy()
        {
            if (owner != null) owner.Changed -= Refresh;
        }

        public void OnCardShot(int index)
        {
            if (owner.NextCost(index) == 0)
            {
                ExperienceCues.Play(ExperienceCues.NotReady, 0.5f);
                return;
            }

            // First shot shows it, a second one within two seconds buys it (like the shop).
            if (armed == index && Time.time <= armedUntil)
            {
                armed = -1;

                if (!owner.TryUpgrade(index))
                {
                    ExperienceCues.Play(ExperienceCues.NotReady, 0.5f);
                    messageUntil = Time.time + 1.5f;
                }
            }
            else
            {
                armed = index;
                armedUntil = Time.time + 2f;
                ExperienceCues.Play("shop_select", 0.6f);
            }

            Refresh();
        }

        private void Update()
        {
            if (armed >= 0 && Time.time > armedUntil)
            {
                armed = -1;
                Refresh();
            }
        }

        public void Refresh()
        {
            bool show = owner.Points > 0 && !MatchManager.IsOver;
            root.SetActive(show);

            if (!show)
            {
                armed = -1;
                return;
            }

            header.text = Time.time < messageUntil
                ? "Faltan puntos"
                : $"NIVEL {owner.Level}  ·  {owner.Points} PUNTO{(owner.Points == 1 ? "" : "S")}  ·  dispara 2 veces";

            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].Show(owner, i, armed == i);
            }
        }

        public static GameObject Quad(Transform parent, Vector3 position, Vector2 size, Material material, int order = 0)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            Renderer renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Transparent quads and text are sorted by distance, which on a panel this flat flips at
            // random: backgrounds always first, text always last.
            renderer.sortingOrder = order;
            return quad;
        }

        public static TextMeshPro Label(Transform parent, Vector3 position, Vector2 box, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = box;
            tmp.fontSize = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = size * 0.5f;
            tmp.fontSizeMax = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.color = color;
            tmp.GetComponent<Renderer>().sortingOrder = 5;
            return tmp;
        }
    }

    /// <summary>One ability's card: a target the player's own bullets "hit", like a shop card.</summary>
    private sealed class UpgradeCard : MonoBehaviour, IDamageable
    {
        private UpgradePanel panel;
        private int index;
        private float lastHit = -10f;
        private TextMeshPro title, body;
        private Material face;
        private Vector3 rest;

        public Faction Faction => Faction.Neutral;
        public bool IsAlive => true;

        public static UpgradeCard Build(Transform parent, UpgradePanel panel, int index, Vector3 position)
        {
            var go = new GameObject($"UpgradeCard_{index}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            UpgradeCard card = go.AddComponent<UpgradeCard>();
            card.panel = panel;
            card.index = index;
            card.rest = go.transform.localScale;
            card.face = new Material(Shader.Find("Sprites/Default"));
            UpgradePanel.Quad(go.transform, new Vector3(0f, 0f, 0.002f), new Vector2(0.135f, 0.1f), card.face, 1);

            card.title = UpgradePanel.Label(go.transform, new Vector3(0f, 0.032f, 0f), new Vector2(0.125f, 0.03f), 0.11f, Color.white);
            card.body = UpgradePanel.Label(go.transform, new Vector3(0f, -0.014f, 0f), new Vector2(0.125f, 0.06f), 0.085f, new Color(0.9f, 0.9f, 0.95f));

            // On the souls' layer, like the shop cards: the gun's ray hits it, minions and towers don't look.
            int soulLayer = LayerMask.NameToLayer("Soul");
            go.layer = soulLayer >= 0 ? soulLayer : 0;

            // A slightly bigger target than the card, so a shot at the wrist is easy to land.
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.14f, 0.105f, 0.03f);
            return card;
        }

        public void TakeDamage(DamageInfo info)
        {
            // Only this player's own gun, and one shot per burst.
            if (info.type != DamageType.Bullet || info.source != MatchNet.LocalFaction || Time.time - lastHit < 0.3f)
            {
                return;
            }

            lastHit = Time.time;
            transform.localScale = rest * 1.08f;
            Invoke(nameof(Settle), 0.12f);
            panel.OnCardShot(index);
        }

        private void Settle() => transform.localScale = rest;

        public void Show(HeroProgression owner, int slot, bool armed)
        {
            AbilitySO ability = owner.BaseAbility(slot);
            int tier = owner.Tier(slot);
            int cost = owner.NextCost(slot);
            string button = slot < AbilityCaster.ButtonNames.Length ? AbilityCaster.ButtonNames[slot] : "?";
            int count = ability != null ? ability.UpgradeCount : 3;

            title.text = $"[{button}] {(ability != null ? ability.abilityName : "-")}  {tier}/{count}";

            if (ability == null || cost == 0)
            {
                body.text = "<b>AL MÁXIMO</b>";
                face.color = new Color(0.35f, 0.28f, 0.08f, 0.95f);
                return;
            }

            AbilityUpgrade next = ability.upgrades[tier];
            bool affordable = owner.Points >= cost;
            body.text = $"<b>Mejora {tier + 1} · {cost} pt{(cost == 1 ? "" : "s")}</b>\n{next.summary}" +
                        (armed ? "\n<color=#FFE070>¡Dispara otra vez!</color>" : "");
            face.color = armed ? new Color(0.45f, 0.35f, 0.08f, 0.95f)
                : affordable ? new Color(0.08f, 0.3f, 0.16f, 0.95f)
                : new Color(0.18f, 0.18f, 0.2f, 0.95f);
        }
    }
}
