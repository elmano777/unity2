using TMPro;
using UnityEngine;

/// <summary>
/// Small line of text below the player's view that says where the online connection is
/// ("Buscando sala...", "Rival conectado"). Hides itself a few seconds after the rival arrives.
/// </summary>
public class MatchmakingStatusView : MonoBehaviour
{
    [SerializeField] private DuelMatchmaker matchmaker;
    [SerializeField] private TextMeshPro label;

    [Tooltip("Seconds the text stays up after the rival connects or the run is offline.")]
    [SerializeField] private float hideAfter = 4f;

    private float hideAt = float.PositiveInfinity;

    private void Awake()
    {
        if (matchmaker == null)
        {
            matchmaker = FindObjectOfType<DuelMatchmaker>();
        }
    }

    private void OnEnable()
    {
        if (matchmaker != null)
        {
            matchmaker.StatusChanged += Show;
            Show(matchmaker.Current, matchmaker.Detail);
        }
    }

    private void OnDisable()
    {
        if (matchmaker != null)
        {
            matchmaker.StatusChanged -= Show;
        }
    }

    private void Show(DuelMatchmaker.Status status, string detail)
    {
        string text;

        switch (status)
        {
            case DuelMatchmaker.Status.SigningIn: text = "Conectando..."; break;
            case DuelMatchmaker.Status.Searching: text = "Buscando sala..."; break;
            case DuelMatchmaker.Status.Joining: text = "Entrando a la sala..." + Small(detail); break;
            case DuelMatchmaker.Status.WaitingForRival: text = "Esperando al rival..." + Small(detail) + Small(BackHint); break;
            case DuelMatchmaker.Status.Connected: text = "<color=#7CFC9A>Rival conectado. ¡Que empiece el ritual!</color>"; break;
            case DuelMatchmaker.Status.RivalLeft: text = $"<color=#FFB347>{detail}</color>" + Small(BackHint); break;
            case DuelMatchmaker.Status.Offline: text = detail; break;
            case DuelMatchmaker.Status.Failed: text = $"<color=#FF6B6B>No se pudo conectar</color>{Small(detail)}{Small(BackHint)}"; break;
            default: text = ""; break;
        }

        label.text = text;
        label.gameObject.SetActive(text.Length > 0);

        bool settled = status == DuelMatchmaker.Status.Connected || status == DuelMatchmaker.Status.Offline;
        hideAt = settled ? Time.time + hideAfter : float.PositiveInfinity;
    }

    private const string BackHint = "Mantén B o Y para volver al garage";

    private static string Small(string detail)
    {
        return string.IsNullOrEmpty(detail) ? "" : $"\n<size=60%>{detail}</size>";
    }

    private void Update()
    {
        if (Time.time >= hideAt)
        {
            label.gameObject.SetActive(false);
            hideAt = float.PositiveInfinity;
        }
    }
}
