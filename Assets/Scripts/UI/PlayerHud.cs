using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's panel (RF-07, RF-10, RF-11): what the game shows without a menu, laid out the way
/// a hero shooter's screen is read. Bottom left, the souls and, under them, three vertical bars for
/// what has been invested in weapon (yellow), vitality (green) and spirit (purple). Centre, the
/// bullets left in the magazine, big, and under them each ability's icon with its cooldown. Right,
/// health as a vertical bar that drains from the top, with the number under it.
/// </summary>
/// <remarks>
/// It follows the gaze lazily, low in the view (about 17° under the horizon), and is only drawn when it
/// is needed: while the player looks down at it, or for a few seconds after something changes (health
/// lost, an ability ready, souls spent, a reload). "The best menu is the one that appears when you need it."
/// It replaces the wrist display, which it hides. Builds itself when the Lane loads.
/// </remarks>
public class PlayerHud : MonoBehaviour
{
    private const string LaneScene = "Lane";

    private static readonly Color Panel = new Color(0.04f, 0.05f, 0.09f, 0.78f);
    private static readonly Color Slot = new Color(0.13f, 0.15f, 0.22f, 1f);
    private static readonly Color Cyan = new Color(0.45f, 0.88f, 1f, 1f);
    private static readonly Color HealthGreen = new Color(0.45f, 0.92f, 0.45f, 1f);
    private static readonly Color HealthRed = new Color(1f, 0.32f, 0.28f, 1f);
    private static readonly Color WeaponYellow = new Color(1f, 0.78f, 0.25f, 1f);
    private static readonly Color VitalityGreen = new Color(0.45f, 0.9f, 0.4f, 1f);
    private static readonly Color SpiritPurple = new Color(0.72f, 0.5f, 1f, 1f);
    private static readonly Color CoolingShade = new Color(0f, 0f, 0f, 0.68f);
    private static readonly Color Ready = new Color(0.55f, 1f, 0.7f, 1f);

    [Tooltip("Metres in front of the eyes.")]
    [SerializeField] private float distance = 1.5f;

    [Tooltip("Metres under eye level (about 17° down at 1.5 m).")]
    [SerializeField] private float below = 0.46f;

    [Tooltip("Seconds it stays up after something changes.")]
    [SerializeField] private float pokeSeconds = 4f;

    [Tooltip("How far down the head must pitch for the panel to count as looked at (head forward Y).")]
    [SerializeField] private float lookDownThreshold = -0.2f;

    [SerializeField] private float followSpeed = 2.2f;

    // Layout, in metres on the panel (+X right, +Y up).
    private const float Width = 1.56f, Height = 0.42f;
    private const float LeftX = -0.6f, RightX = 0.64f;
    private const float StatBarHeight = 0.2f, StatBarWidth = 0.045f, StatBarBottom = -0.17f;
    private const float HealthBarHeight = 0.27f, HealthBarWidth = 0.075f, HealthBarBottom = -0.09f;
    private const float IconSize = 0.12f, IconY = -0.06f, IconGap = 0.16f;

    private Transform head;
    private Vector3 smoothForward = Vector3.forward;
    private GameObject root;
    private float shownUntil;
    private float alpha;
    private float nextRefresh;

    private TMP_Text souls;
    private TMP_Text ammoText;
    private TMP_Text ammoMax;
    private Transform reloadFill;
    private Renderer reloadFillRenderer;
    private TMP_Text healthText;
    private TMP_Text healthMax;
    private Transform healthFill;
    private Renderer healthFillRenderer;

    private readonly Transform[] statFills = new Transform[3];
    private readonly TMP_Text[] statLabels = new TMP_Text[3];
    private static readonly ShopCategory[] StatOrder = { ShopCategory.Weapon, ShopCategory.Vitality, ShopCategory.Spirit };

    private Transform[] abilityShades = System.Array.Empty<Transform>();
    private Renderer[] abilityShadeRenderers = System.Array.Empty<Renderer>();
    private TMP_Text[] abilityCharges = System.Array.Empty<TMP_Text>();
    private TMP_Text[] abilityTimers = System.Array.Empty<TMP_Text>();
    private Renderer[] abilityRings = System.Array.Empty<Renderer>();

    private Renderer[] all = System.Array.Empty<Renderer>();
    private TMP_Text[] allText = System.Array.Empty<TMP_Text>();

    private Weapon weapon;
    private bool wasReloading;
    private AbilityCaster caster;
    private Health body;
    private float nextBodySearch;
    private float lastHealth = -1f;
    private bool[] wasCooling = System.Array.Empty<bool>();

    private static Texture2D disc;
    private static Texture2D ring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<PlayerHud>() == null)
        {
            new GameObject(nameof(PlayerHud)).AddComponent<PlayerHud>();
        }
    }

    private void Start()
    {
        caster = FindObjectOfType<AbilityCaster>();

        // This panel takes over the wrist display's job.
        foreach (WristHud wrist in FindObjectsOfType<WristHud>(true))
        {
            wrist.gameObject.SetActive(false);
        }

        Build();
        Poke();
    }

    private void OnEnable()
    {
        GameEvents.WalletChanged += OnChanged;
        GameEvents.AbilityUsed += OnAbility;
    }

    private void OnDisable()
    {
        GameEvents.WalletChanged -= OnChanged;
        GameEvents.AbilityUsed -= OnAbility;
    }

    private void OnChanged(int value) => Poke();

    private void OnAbility(AbilitySO ability) => Poke();

    /// <summary>Brings the panel up for a few seconds.</summary>
    public void Poke()
    {
        shownUntil = Time.time + pokeSeconds;
        nextRefresh = 0f;
    }

    // ---- Building ----

    private void Build()
    {
        root = new GameObject("PlayerHudPanel");
        root.transform.SetParent(transform, false);

        Quad("Back", new Vector3(0f, 0f, 0.004f), new Vector2(Width, Height), Panel);
        Quad("TopLine", new Vector3(0f, Height / 2f, 0.002f), new Vector2(Width, 0.005f), Cyan);

        BuildSouls();
        BuildStats();
        BuildAmmo();
        BuildAbilities();
        BuildHealth();

        all = root.GetComponentsInChildren<Renderer>(true);
        allText = root.GetComponentsInChildren<TMP_Text>(true);
        SetAlpha(0f);
    }

    /// <summary>Bottom left, top: a soul glyph and the count, in the souls' own cyan.</summary>
    private void BuildSouls()
    {
        GameObject glyph = Quad("SoulGlyph", new Vector3(LeftX - 0.1f, 0.135f, 0f), new Vector2(0.045f, 0.045f), Cyan);
        glyph.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        souls = Text("0", new Vector3(LeftX - 0.065f, 0.135f, 0f), 0.075f, Cyan, new Vector2(0.24f, 0.07f), FontStyles.Bold);
        souls.alignment = TextAlignmentOptions.Left;
        souls.rectTransform.pivot = new Vector2(0f, 0.5f);
    }

    /// <summary>Bottom left: weapon, vitality and spirit investment as three vertical bars.</summary>
    private void BuildStats()
    {
        Color[] colors = { WeaponYellow, VitalityGreen, SpiritPurple };

        for (int i = 0; i < 3; i++)
        {
            float x = LeftX - 0.08f + i * 0.08f;
            Quad("StatBack" + i, new Vector3(x, StatBarBottom + StatBarHeight / 2f, 0.001f), new Vector2(StatBarWidth, StatBarHeight), Slot);
            GameObject fill = Quad("StatFill" + i, new Vector3(x, StatBarBottom, 0f), new Vector2(StatBarWidth, StatBarHeight), colors[i]);
            statFills[i] = fill.transform;
            statLabels[i] = Text("+0%", new Vector3(x, StatBarBottom - 0.028f, 0f), 0.036f, colors[i], new Vector2(0.08f, 0.04f), FontStyles.Bold);
        }
    }

    /// <summary>Centre, top: the bullets left, the most read number, with a reload strip under it.</summary>
    private void BuildAmmo()
    {
        ammoText = Text("--", new Vector3(-0.02f, 0.12f, 0f), 0.1f, Color.white, new Vector2(0.2f, 0.1f), FontStyles.Bold);
        ammoText.alignment = TextAlignmentOptions.Right;
        ammoText.rectTransform.pivot = new Vector2(1f, 0.5f);
        ammoMax = Text("/ --", new Vector3(0.0f, 0.105f, 0f), 0.05f, new Color(0.75f, 0.8f, 0.9f, 1f), new Vector2(0.14f, 0.06f), FontStyles.Bold);
        ammoMax.alignment = TextAlignmentOptions.Left;
        ammoMax.rectTransform.pivot = new Vector2(0f, 0.5f);

        Quad("ReloadBack", new Vector3(0f, 0.058f, 0.001f), new Vector2(0.3f, 0.012f), Slot);
        GameObject fill = Quad("ReloadFill", new Vector3(0f, 0.058f, 0f), new Vector2(0.3f, 0.012f), Cyan);
        reloadFill = fill.transform;
        reloadFillRenderer = fill.GetComponent<Renderer>();
    }

    /// <summary>Centre, bottom: one round icon per ability, its button under it, darkened while cooling down.</summary>
    private void BuildAbilities()
    {
        int count = caster != null ? caster.Abilities.Count : 0;
        abilityShades = new Transform[count];
        abilityShadeRenderers = new Renderer[count];
        abilityCharges = new TMP_Text[count];
        abilityTimers = new TMP_Text[count];
        abilityRings = new Renderer[count];
        wasCooling = new bool[count];

        for (int i = 0; i < count; i++)
        {
            AbilitySO ability = caster.Abilities[i];
            float x = (i - (count - 1) / 2f) * IconGap;
            Vector3 centre = new Vector3(x, IconY, 0f);

            GameObject ringGo = Quad("AbilityRing" + i, centre + new Vector3(0f, 0f, 0.002f), Vector2.one * (IconSize + 0.016f), Ready, Ring());
            abilityRings[i] = ringGo.GetComponent<Renderer>();
            Quad("AbilityDisc" + i, centre + new Vector3(0f, 0f, 0.001f), Vector2.one * IconSize, new Color(0.08f, 0.09f, 0.14f, 1f), Disc());

            if (ability != null && ability.icon != null)
            {
                Quad("AbilityIcon" + i, centre, Vector2.one * IconSize * 0.78f, Color.white, ability.icon.texture);
            }
            else
            {
                // No icon for this ability yet: its initial on the disc.
                string initial = ability != null && !string.IsNullOrEmpty(ability.abilityName) ? ability.abilityName.Substring(0, 1).ToUpperInvariant() : "?";
                Text(initial, centre, 0.07f, Color.white, new Vector2(IconSize, IconSize), FontStyles.Bold);
            }

            // A dark veil over the icon while it cools down; the seconds left on top of it.
            GameObject shade = Quad("AbilityShade" + i, centre + new Vector3(0f, 0f, -0.001f), Vector2.one * IconSize, CoolingShade, Disc());
            abilityShades[i] = shade.transform;
            abilityShadeRenderers[i] = shade.GetComponent<Renderer>();
            abilityTimers[i] = Text(string.Empty, centre + new Vector3(0f, 0f, -0.002f), 0.055f, Color.white, new Vector2(IconSize, IconSize), FontStyles.Bold);

            // Charges left, in a small badge at the top right of the icon (only for multi-charge abilities).
            if (ability != null && ability.charges > 1)
            {
                Vector3 badge = centre + new Vector3(IconSize * 0.42f, IconSize * 0.42f, -0.003f);
                Quad("ChargeBadge" + i, badge + new Vector3(0f, 0f, 0.001f), new Vector2(0.036f, 0.036f), new Color(0.55f, 0.3f, 0.85f, 1f), Disc());
                abilityCharges[i] = Text(ability.charges.ToString(), badge, 0.026f, Color.white, new Vector2(0.036f, 0.036f), FontStyles.Bold);
            }

            string key = i < AbilityCaster.ButtonNames.Length ? AbilityCaster.ButtonNames[i] : "?";
            Quad("KeyBack" + i, centre + new Vector3(0f, -IconSize / 2f - 0.025f, 0f), new Vector2(0.034f, 0.034f), new Color(0.85f, 0.88f, 0.95f, 1f));
            Text(key, centre + new Vector3(0f, -IconSize / 2f - 0.025f, -0.001f), 0.026f, new Color(0.05f, 0.06f, 0.1f, 1f), new Vector2(0.034f, 0.034f), FontStyles.Bold);
        }
    }

    /// <summary>Right: health as a tall bar that drains from the top, the number under it.</summary>
    private void BuildHealth()
    {
        Quad("HealthBack", new Vector3(RightX, HealthBarBottom + HealthBarHeight / 2f, 0.001f), new Vector2(HealthBarWidth + 0.012f, HealthBarHeight + 0.012f), Slot);
        GameObject fill = Quad("HealthFill", new Vector3(RightX, HealthBarBottom, 0f), new Vector2(HealthBarWidth, HealthBarHeight), HealthGreen);
        healthFill = fill.transform;
        healthFillRenderer = fill.GetComponent<Renderer>();

        healthText = Text("—", new Vector3(RightX, HealthBarBottom - 0.04f, 0f), 0.065f, Color.white, new Vector2(0.2f, 0.06f), FontStyles.Bold);
        healthMax = Text(string.Empty, new Vector3(RightX, HealthBarBottom - 0.085f, 0f), 0.035f, new Color(0.75f, 0.8f, 0.9f, 1f), new Vector2(0.2f, 0.04f), FontStyles.Normal);
    }

    private GameObject Quad(string name, Vector3 local, Vector2 size, Color color, Texture texture = null)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = local;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        Destroy(quad.GetComponent<Collider>());

        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color, mainTexture = texture };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return quad;
    }

    private TMP_Text Text(string content, Vector3 local, float size, Color color, Vector2 box, FontStyles style)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = local + new Vector3(0f, 0f, -0.003f);

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = content;
        text.fontSize = size * 10f;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.rectTransform.sizeDelta = box;
        return text;
    }

    /// <summary>A soft-edged white disc, made once.</summary>
    private static Texture2D Disc()
    {
        if (disc == null)
        {
            disc = Circle(64, 0f);
        }

        return disc;
    }

    /// <summary>A white ring, made once.</summary>
    private static Texture2D Ring()
    {
        if (ring == null)
        {
            ring = Circle(64, 0.82f);
        }

        return ring;
    }

    private static Texture2D Circle(int size, float hole)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude / r;
                float a = Mathf.Clamp01((1f - d) * size * 0.5f);
                if (hole > 0f) a *= Mathf.Clamp01((d - hole) * size * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply();
        return tex;
    }

    // ---- Per frame ----

    private void LateUpdate()
    {
        if (root == null)
        {
            return;
        }

        if (head == null)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            head = camera.transform;
            smoothForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        }

        Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);

        if (facing.sqrMagnitude > 0.01f)
        {
            smoothForward = Vector3.Slerp(smoothForward, facing.normalized, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        }

        Vector3 position = head.position + smoothForward * distance + Vector3.down * below;
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));

        Track();

        bool lookingDown = head.forward.y < lookDownThreshold;
        bool visible = lookingDown || Time.time < shownUntil;
        alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, 4f * Time.deltaTime);
        SetAlpha(alpha);

        if (alpha > 0.01f && Time.time >= nextRefresh)
        {
            nextRefresh = Time.time + 0.1f;
            Refresh();
        }
    }

    /// <summary>Watches health, reloads and cooldowns so the panel comes up when something changes.</summary>
    private void Track()
    {
        if (body == null && Time.time >= nextBodySearch)
        {
            nextBodySearch = Time.time + 1f;
            int layer = LayerMask.NameToLayer("PlayerBody");

            if (layer >= 0)
            {
                foreach (Health health in FindObjectsOfType<Health>())
                {
                    if (health.gameObject.layer == layer)
                    {
                        body = health;
                        break;
                    }
                }
            }
        }

        if (body != null)
        {
            if (lastHealth >= 0f && body.CurrentHealth < lastHealth)
            {
                Poke();
            }

            lastHealth = body.CurrentHealth;
        }

        // The hero's gun replaces the placeholder a moment after the Lane loads: look it up lazily.
        if (weapon == null)
        {
            weapon = FindObjectOfType<Weapon>();
        }

        if (weapon != null)
        {
            bool reloading = weapon.IsReloading;

            if (reloading && !wasReloading)
            {
                Poke();
            }

            wasReloading = reloading;
        }

        if (caster != null && (caster.ChargeAmount > 0f || caster.IsChanneling))
        {
            Poke();
        }

        if (caster != null)
        {
            for (int i = 0; i < wasCooling.Length && i < caster.Abilities.Count; i++)
            {
                bool cooling = caster.CooldownRemaining(i) > 0f;

                if (wasCooling[i] && !cooling)
                {
                    Poke();
                }

                wasCooling[i] = cooling;
            }
        }
    }

    private void SetAlpha(float value)
    {
        foreach (Renderer renderer in all)
        {
            if (renderer != null)
            {
                renderer.enabled = value > 0.01f;
            }
        }

        foreach (TMP_Text text in allText)
        {
            if (text != null)
            {
                text.alpha = value;
            }
        }
    }

    private void Refresh()
    {
        PlayerUpgrades upgrades = PlayerUpgrades.Instance;

        // Souls.
        int wallet = PlayerWallet.Instance != null ? PlayerWallet.Instance.Souls : 0;
        souls.text = wallet.ToString("N0");

        // Investment bars: how far each category's bonus has climbed (the top step is about +60 %).
        for (int i = 0; i < 3; i++)
        {
            float bonus = upgrades != null ? upgrades.InvestmentBonus(StatOrder[i]) : 0f;
            SetVertical(statFills[i], StatBarHeight, StatBarBottom, Mathf.Clamp01(bonus / 0.6f));
            statLabels[i].text = $"+{Mathf.RoundToInt(bonus * 100f)}%";
        }

        // Magazine.
        if (weapon != null && weapon.MagazineSize > 0)
        {
            bool reloading = weapon.IsReloading;
            ammoText.text = reloading ? "··" : weapon.Ammo.ToString();
            ammoText.color = !reloading && weapon.Ammo <= Mathf.Max(3, weapon.MagazineSize / 5) ? HealthRed : Color.white;
            ammoMax.text = reloading ? "RECARGANDO" : $"/ {weapon.MagazineSize}";
            SetHorizontal(reloadFill, reloadFillRenderer, 0.3f, reloading ? weapon.ReloadProgress : (float)weapon.Ammo / weapon.MagazineSize, reloading ? Cyan : new Color(0.75f, 0.8f, 0.9f, 1f));
        }
        else
        {
            ammoText.text = "∞";
            ammoMax.text = string.Empty;
            SetHorizontal(reloadFill, reloadFillRenderer, 0.3f, 1f, Slot);
        }

        // Abilities: the shade rises from the bottom with the cooldown left; a charge shows its %.
        if (caster != null)
        {
            float mult = upgrades != null ? upgrades.CooldownMultiplier : 1f;

            for (int i = 0; i < abilityShades.Length && i < caster.Abilities.Count; i++)
            {
                AbilitySO ability = caster.Abilities[i];

                if (ability == null)
                {
                    continue;
                }

                float wait = caster.CooldownRemaining(i);
                float total = caster.CooldownTotal(i);

                if (ability.charges > 1)
                {
                    abilityCharges[i].text = caster.Charges(i).ToString();
                }
                float left = wait > 0f ? Mathf.Clamp01(wait / total) : 0f;
                bool charging = caster.ChargeAmount > 0f && ability.chargeSeconds > 0f && wait <= 0f;
                bool channeling = caster.IsChanneling && caster.ChannelingIndex == i;

                // A dark veil over the icon while it cools down, lifting as it gets ready.
                Color veil = CoolingShade;
                veil.a = left > 0f ? Mathf.Lerp(0.35f, 0.75f, left) : 0f;
                abilityShadeRenderers[i].sharedMaterial.color = veil;
                abilityTimers[i].text = charging ? $"{Mathf.RoundToInt(caster.ChargeAmount * 100f)}%"
                    : channeling ? Mathf.CeilToInt(caster.ChannelRemaining).ToString()
                    : wait > 0f ? Mathf.CeilToInt(wait).ToString()
                    : string.Empty;
                abilityRings[i].sharedMaterial.color = charging || channeling ? Cyan : wait > 0f ? new Color(0.35f, 0.38f, 0.5f, 1f) : Ready;
            }
        }

        // Health.
        if (body != null)
        {
            float fraction = body.Normalized;
            SetVertical(healthFill, HealthBarHeight, HealthBarBottom, fraction);
            healthFillRenderer.sharedMaterial.color = Color.Lerp(HealthRed, HealthGreen, fraction * 1.4f);
            healthText.text = Mathf.CeilToInt(body.CurrentHealth).ToString();
            healthMax.text = $"/ {Mathf.CeilToInt(body.MaxHealth)}";
        }
        else
        {
            SetVertical(healthFill, HealthBarHeight, HealthBarBottom, 0f);
            healthText.text = "—";
            healthMax.text = string.Empty;
        }
    }

    /// <summary>Sizes a bar standing on <paramref name="bottom"/> to <paramref name="fraction"/> of its full height.</summary>
    private static void SetVertical(Transform bar, float fullHeight, float bottom, float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        Vector3 scale = bar.localScale;
        scale.y = Mathf.Max(0.0001f, fullHeight * fraction);
        bar.localScale = scale;

        Vector3 position = bar.localPosition;
        position.y = bottom + scale.y / 2f;
        bar.localPosition = position;
    }

    /// <summary>Sizes a bar from its left edge to <paramref name="fraction"/> of its full width, around x = 0.</summary>
    private static void SetHorizontal(Transform bar, Renderer renderer, float fullWidth, float fraction, Color color)
    {
        fraction = Mathf.Clamp01(fraction);
        Vector3 scale = bar.localScale;
        scale.x = Mathf.Max(0.0001f, fullWidth * fraction);
        bar.localScale = scale;

        Vector3 position = bar.localPosition;
        position.x = -fullWidth / 2f + scale.x / 2f;
        bar.localPosition = position;
        renderer.sharedMaterial.color = color;
    }
}
