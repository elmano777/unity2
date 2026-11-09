using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's panel (RF-07, RF-10, RF-11): what the game shows without a menu. Left, the levels:
/// how much has been invested in weapon, spirit and vitality, and the bonus that earns. Centre, the
/// souls and each ability with its cooldown. Right, health.
/// </summary>
/// <remarks>
/// It follows the gaze lazily, low in the view (about 20° under the horizon), and is only drawn when it
/// is needed: while the player looks down at it, or for a few seconds after something changes (health
/// lost, an ability ready, souls spent). "The best menu is the one that appears when you need it."
/// It replaces the wrist display, which it hides. Builds itself when the Lane loads.
/// </remarks>
public class PlayerHud : MonoBehaviour
{
    private const string LaneScene = "Lane";

    private static readonly Color Panel = new Color(0.04f, 0.05f, 0.09f, 0.82f);
    private static readonly Color Cyan = new Color(0.5f, 0.9f, 1f, 1f);
    private static readonly Color HealthGreen = new Color(0.4f, 0.9f, 0.5f, 1f);
    private static readonly Color HealthRed = new Color(1f, 0.35f, 0.3f, 1f);
    private static readonly Color Ready = new Color(0.55f, 1f, 0.65f, 1f);
    private static readonly Color Cooling = new Color(0.55f, 0.6f, 0.75f, 1f);

    [Tooltip("Metres in front of the eyes.")]
    [SerializeField] private float distance = 1.5f;

    [Tooltip("Metres under eye level (about 17° down at 1.5 m).")]
    [SerializeField] private float below = 0.46f;

    [Tooltip("Seconds it stays up after something changes.")]
    [SerializeField] private float pokeSeconds = 4f;

    [Tooltip("How far down the head must pitch for the panel to count as looked at (head forward Y).")]
    [SerializeField] private float lookDownThreshold = -0.2f;

    [SerializeField] private float followSpeed = 2.2f;

    private Transform head;
    private Vector3 smoothForward = Vector3.forward;
    private GameObject root;
    private float shownUntil;
    private float alpha;
    private float nextRefresh;

    private TMP_Text levels;
    private TMP_Text souls;
    private TMP_Text healthText;
    private Transform healthFill;
    private Renderer healthFillRenderer;
    private TMP_Text[] abilityLabels = System.Array.Empty<TMP_Text>();
    private Transform[] abilityFills = System.Array.Empty<Transform>();
    private Renderer[] abilityFillRenderers = System.Array.Empty<Renderer>();
    private float[] abilityCentres = System.Array.Empty<float>();
    private Renderer[] all = System.Array.Empty<Renderer>();
    private TMP_Text[] allText = System.Array.Empty<TMP_Text>();

    private AbilityCaster caster;
    private Health body;
    private float nextBodySearch;
    private float lastHealth = -1f;
    private bool[] wasCooling = System.Array.Empty<bool>();

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

        Quad("Back", new Vector3(0f, 0f, 0.002f), new Vector2(1.5f, 0.34f), Panel);
        Quad("Line", new Vector3(0f, 0.168f, 0f), new Vector2(1.5f, 0.006f), Cyan);

        // Left: levels.
        Text("NIVELES", new Vector3(-0.52f, 0.12f, 0f), 0.05f, Cyan, new Vector2(0.45f, 0.06f), FontStyles.Bold);
        levels = Text(string.Empty, new Vector3(-0.52f, -0.03f, 0f), 0.05f, Color.white, new Vector2(0.48f, 0.22f), FontStyles.Normal);
        levels.alignment = TextAlignmentOptions.Left;

        // Centre: souls and abilities.
        souls = Text(string.Empty, new Vector3(0f, 0.115f, 0f), 0.06f, Cyan, new Vector2(0.5f, 0.07f), FontStyles.Bold);

        int count = caster != null ? caster.Abilities.Count : 0;
        abilityLabels = new TMP_Text[count];
        abilityFills = new Transform[count];
        abilityFillRenderers = new Renderer[count];
        wasCooling = new bool[count];
        abilityCentres = new float[count];

        for (int i = 0; i < count; i++)
        {
            float x = (i - (count - 1) / 2f) * 0.36f;
            abilityCentres[i] = x;
            Quad("CooldownBack" + i, new Vector3(x, -0.07f, 0f), new Vector2(0.32f, 0.05f), new Color(0.15f, 0.17f, 0.25f, 1f));
            GameObject fill = Quad("CooldownFill" + i, new Vector3(x, -0.07f, -0.001f), new Vector2(0.32f, 0.05f), Ready);
            abilityFills[i] = fill.transform;
            abilityFillRenderers[i] = fill.GetComponent<Renderer>();
            abilityLabels[i] = Text(string.Empty, new Vector3(x, 0.0f, -0.002f), 0.045f, Color.white, new Vector2(0.34f, 0.07f), FontStyles.Bold);
        }

        // Right: health.
        Text("VIDA", new Vector3(0.52f, 0.12f, 0f), 0.05f, HealthGreen, new Vector2(0.4f, 0.06f), FontStyles.Bold);
        Quad("HealthBack", new Vector3(0.52f, 0.0f, 0f), new Vector2(0.42f, 0.07f), new Color(0.15f, 0.17f, 0.25f, 1f));
        GameObject hp = Quad("HealthFill", new Vector3(0.52f, 0.0f, -0.001f), new Vector2(0.42f, 0.07f), HealthGreen);
        healthFill = hp.transform;
        healthFillRenderer = hp.GetComponent<Renderer>();
        healthText = Text(string.Empty, new Vector3(0.52f, -0.09f, -0.002f), 0.06f, Color.white, new Vector2(0.45f, 0.07f), FontStyles.Bold);

        all = root.GetComponentsInChildren<Renderer>(true);
        allText = root.GetComponentsInChildren<TMP_Text>(true);
        SetAlpha(0f);
    }

    private GameObject Quad(string name, Vector3 local, Vector2 size, Color color)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = local;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        Destroy(quad.GetComponent<Collider>());

        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color };
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

    /// <summary>Watches health and cooldowns so the panel comes up when something changes.</summary>
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
        // Souls.
        int wallet = PlayerWallet.Instance != null ? PlayerWallet.Instance.Souls : 0;
        souls.text = $"ALMAS  {wallet}";

        // Levels: one line per category, with the bonus now and the souls to the next step.
        PlayerUpgrades upgrades = PlayerUpgrades.Instance;
        levels.text = LevelLine("ARMA", ShopCategory.Weapon, upgrades) + "\n"
                      + LevelLine("ESPÍRITU", ShopCategory.Spirit, upgrades) + "\n"
                      + LevelLine("VITALIDAD", ShopCategory.Vitality, upgrades);

        // Abilities and cooldowns.
        if (caster != null)
        {
            float mult = upgrades != null ? upgrades.CooldownMultiplier : 1f;

            for (int i = 0; i < abilityLabels.Length && i < caster.Abilities.Count; i++)
            {
                AbilitySO ability = caster.Abilities[i];

                if (ability == null)
                {
                    continue;
                }

                float wait = caster.CooldownRemaining(i);
                float total = Mathf.Max(0.1f, ability.cooldown * mult);
                float fill = wait > 0f ? 1f - Mathf.Clamp01(wait / total) : 1f;
                string key = i < AbilityCaster.ButtonNames.Length ? AbilityCaster.ButtonNames[i] : "?";

                abilityLabels[i].text = wait > 0f ? $"[{key}] {ability.abilityName}  {Mathf.CeilToInt(wait)} s" : $"[{key}] {ability.abilityName}";
                SetBar(abilityFills[i], abilityFillRenderers[i], 0.32f, 0f, fill, wait > 0f ? Cooling : Ready, abilityCentres[i]);
            }
        }

        // Health.
        if (body != null)
        {
            float fraction = body.Normalized;
            healthText.text = $"{Mathf.CeilToInt(body.CurrentHealth)} / {Mathf.CeilToInt(body.MaxHealth)}";
            SetBar(healthFill, healthFillRenderer, 0.42f, 0f, fraction, Color.Lerp(HealthRed, HealthGreen, fraction * 1.4f), 0.52f);
        }
        else
        {
            healthText.text = "—";
            SetBar(healthFill, healthFillRenderer, 0.42f, 0f, 0f, HealthGreen, 0.52f);
        }
    }

    /// <summary>Resizes a bar from its left edge so it fills from left to right around its centre x.</summary>
    private static void SetBar(Transform bar, Renderer renderer, float fullWidth, float unusedY, float fraction, Color color, float centreX)
    {
        fraction = Mathf.Clamp01(fraction);
        Vector3 scale = bar.localScale;
        scale.x = Mathf.Max(0.001f, fullWidth * fraction);
        bar.localScale = scale;

        Vector3 position = bar.localPosition;
        position.x = centreX - fullWidth / 2f + scale.x / 2f;
        bar.localPosition = position;

        renderer.sharedMaterial.color = color;
    }

    private static string LevelLine(string label, ShopCategory category, PlayerUpgrades upgrades)
    {
        if (upgrades == null)
        {
            return $"{label}  —";
        }

        int percent = Mathf.RoundToInt(upgrades.InvestmentBonus(category) * 100f);
        int next = upgrades.NextStep(category);
        string progress = next > 0 ? $"{upgrades.Invested(category)}/{next}" : "máx";
        return $"<color=#7FE0FF>{label}</color>  +{percent} %  <size=70%>{progress}</size>";
    }
}
