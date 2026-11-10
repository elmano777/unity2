using UnityEngine;

/// <summary>
/// The patron's two phases on the map marker. The patron has two health bars: while the first one
/// lasts it shows its full form; once it is gone (half of its total health) the full form vanishes
/// and its weak form floats over the dais until the second bar runs out. Read from the linked
/// structure's health, which the network keeps in sync, so both players see the same phase.
/// </summary>
[RequireComponent(typeof(ObjectiveVisual))]
public class PatronPhaseVisual : MonoBehaviour
{
    [SerializeField] private GameObject normalForm;
    [SerializeField] private GameObject weakForm;

    [Tooltip("Fraction of total health at which the first bar is empty and the weak form appears.")]
    [SerializeField] private float weakThreshold = 0.5f;

    [SerializeField] private float spinDegreesPerSecond = 22f;
    [SerializeField] private float bobHeight = 0.35f;

    private ObjectiveVisual visual;
    private Vector3 weakBase;
    private bool weak;

    /// <summary>True while the patron is in its second (weak) phase.</summary>
    public bool IsWeak => weak;

    private void Awake()
    {
        visual = GetComponent<ObjectiveVisual>();

        if (weakForm != null)
        {
            weakBase = weakForm.transform.localPosition;
            weakForm.SetActive(false);
        }
    }

    private void Update()
    {
        Structure linked = visual != null ? visual.Linked : null;
        bool nowWeak = linked != null && linked.Health != null && linked.Health.Normalized <= weakThreshold;

        if (nowWeak != weak)
        {
            weak = nowWeak;

            if (normalForm != null) normalForm.SetActive(!weak);
            if (weakForm != null) weakForm.SetActive(weak);

            if (weak && linked.IsStanding)
            {
                Announce(linked);
            }
        }

        if (weak && weakForm != null)
        {
            weakForm.transform.localPosition = weakBase + Vector3.up * (Mathf.Sin(Time.time * 1.3f) * bobHeight);
            weakForm.transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }

    private void Announce(Structure patron)
    {
        bool ours = patron.Faction == MatchNet.LocalFaction;
        GameEvents.RaiseHint(ours
            ? "¡Tu patrón perdió su primera barra y está débil! Defiéndelo."
            : "¡El patrón rival está débil! Acaba con su última barra.");
        CancelInvoke(nameof(ClearHint));
        Invoke(nameof(ClearHint), 4f);
    }

    private void ClearHint() => GameEvents.RaiseHint(string.Empty);
}
