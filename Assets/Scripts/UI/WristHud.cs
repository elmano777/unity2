using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// A small display on the left wrist: souls held, each ability with its button and cooldown, and
/// the next rival objective (RF-07, RF-10, RF-12). Look at the left hand to read it, like a watch,
/// instead of having numbers glued to the view.
/// </summary>
/// <remarks>
/// It doesn't stay in the hand the whole ritual: it shows for <see cref="visibleSeconds"/> when the
/// Lane starts and hides, then comes back for the same time whenever the souls change, an ability is
/// cast or an ability is ready again. Only the renderers are switched, so this keeps listening.
/// </remarks>
public class WristHud : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private AbilityCaster caster;

    [Tooltip("Seconds between text rebuilds. The numbers don't need to change every frame.")]
    [SerializeField] private float refreshInterval = 0.2f;

    [Tooltip("Seconds the display stays up at the start and after each change before it hides.")]
    [SerializeField] private float visibleSeconds = 5f;

    [SerializeField] private Color readyColor = new Color(0.55f, 1f, 0.65f, 1f);
    [SerializeField] private Color coolingColor = new Color(0.65f, 0.65f, 0.7f, 1f);

    private readonly StringBuilder text = new StringBuilder(256);
    private float nextRefresh;
    private float hideAt;
    private bool shown = true;
    private Renderer[] renderers = System.Array.Empty<Renderer>();
    private bool[] wasCooling = System.Array.Empty<bool>();

    private void Awake()
    {
        if (caster == null)
        {
            caster = FindObjectOfType<AbilityCaster>();
        }

        renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable()
    {
        GameEvents.WalletChanged += OnWalletChanged;
        GameEvents.AbilityUsed += OnAbilityUsed;
        Show();
    }

    private void OnDisable()
    {
        GameEvents.WalletChanged -= OnWalletChanged;
        GameEvents.AbilityUsed -= OnAbilityUsed;
    }

    private void OnWalletChanged(int souls) => Show();

    private void OnAbilityUsed(AbilitySO ability) => Show();

    /// <summary>Brings the display up for <see cref="visibleSeconds"/> from now.</summary>
    public void Show()
    {
        hideAt = Time.time + visibleSeconds;
        nextRefresh = 0f;
        SetShown(true);
    }

    private void SetShown(bool value)
    {
        if (shown == value)
        {
            return;
        }

        shown = value;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = value;
            }
        }
    }

    private void Update()
    {
        WatchCooldowns();

        if (shown && Time.time >= hideAt)
        {
            SetShown(false);
        }

        if (!shown || label == null || Time.time < nextRefresh)
        {
            return;
        }

        nextRefresh = Time.time + refreshInterval;
        text.Clear();

        int souls = PlayerWallet.Instance != null ? PlayerWallet.Instance.Souls : 0;
        text.Append("<color=#7FE0FF>ALMAS</color> ").Append(souls);

        HeroProgression progression = HeroProgression.Instance;

        if (progression != null)
        {
            text.Append("   <color=#FFD27F>NIVEL ").Append(progression.Level).Append("</color>");

            if (progression.NextLevelSouls > 0)
            {
                text.Append(" <size=75%>(").Append(progression.Collected).Append('/').Append(progression.NextLevelSouls).Append(")</size>");
            }
        }

        text.Append('\n');

        if (caster != null)
        {
            for (int i = 0; i < caster.Abilities.Count && i < AbilityCaster.ButtonNames.Length; i++)
            {
                AbilitySO ability = caster.Abilities[i];

                if (ability == null)
                {
                    continue;
                }

                float wait = caster.CooldownRemaining(i);
                Color tint = wait > 0f ? coolingColor : readyColor;
                string state = wait > 0f ? $"{Mathf.CeilToInt(wait)} s" : "LISTA";

                text.Append('[').Append(AbilityCaster.ButtonNames[i]).Append("] ")
                    .Append(ability.abilityName).Append("  <color=#")
                    .Append(ColorUtility.ToHtmlStringRGB(tint)).Append('>').Append(state).Append("</color>\n");
            }
        }

        Structure next = Structure.NextObjective(FactionUtility.Opposite(MatchNet.LocalFaction));
        text.Append("<size=80%>Objetivo: ").Append(next != null ? next.DisplayName : "—").Append("</size>");

        label.text = text.ToString();
    }

    /// <summary>Shows the display again when an ability comes off cooldown.</summary>
    private void WatchCooldowns()
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
                Show();
            }

            wasCooling[i] = cooling;
        }
    }
}
