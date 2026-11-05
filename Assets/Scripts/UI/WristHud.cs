using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// A small display on the left wrist: souls held, each ability with its button and cooldown, and
/// the next rival objective (RF-07, RF-10, RF-12). Look at the left hand to read it, like a watch,
/// instead of having numbers glued to the view.
/// </summary>
public class WristHud : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private AbilityCaster caster;

    [Tooltip("Seconds between text rebuilds. The numbers don't need to change every frame.")]
    [SerializeField] private float refreshInterval = 0.2f;

    [SerializeField] private Color readyColor = new Color(0.55f, 1f, 0.65f, 1f);
    [SerializeField] private Color coolingColor = new Color(0.65f, 0.65f, 0.7f, 1f);

    private readonly StringBuilder text = new StringBuilder(256);
    private float nextRefresh;

    private void Awake()
    {
        if (caster == null)
        {
            caster = FindObjectOfType<AbilityCaster>();
        }
    }

    private void Update()
    {
        if (label == null || Time.time < nextRefresh)
        {
            return;
        }

        nextRefresh = Time.time + refreshInterval;
        text.Clear();

        int souls = PlayerWallet.Instance != null ? PlayerWallet.Instance.Souls : 0;
        text.Append("<color=#7FE0FF>ALMAS</color> ").Append(souls).Append('\n');

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
}
