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

    [Tooltip("Seconds the weak form takes to drop onto the dais and vanish once the patron falls.")]
    [SerializeField] private float collapseSeconds = 1.4f;

    private ObjectiveVisual visual;
    private Vector3 weakBase;
    private bool weak;
    private bool collapsed;

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
        if (collapsed)
        {
            return;
        }

        Structure linked = visual != null ? visual.Linked : null;

        // The last bar ran out: the weak form stops floating, drops and is gone.
        if (linked != null && !linked.IsStanding)
        {
            collapsed = true;

            if (normalForm != null) normalForm.SetActive(false);
            if (weakForm != null && weakForm.activeSelf) StartCoroutine(Collapse());
            return;
        }

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

    /// <summary>Falls onto the dais with a little tumble, shrinking to nothing, then hides.</summary>
    private System.Collections.IEnumerator Collapse()
    {
        Transform t = weakForm.transform;
        Vector3 from = t.localPosition;
        Vector3 to = weakBase + Vector3.down * Mathf.Max(0f, weakBase.y * 0.85f);
        Vector3 scale = t.localScale;

        for (float time = 0f; time < collapseSeconds; time += Time.deltaTime)
        {
            float k = time / collapseSeconds;
            t.localPosition = Vector3.Lerp(from, to, k * k);
            t.Rotate(140f * Time.deltaTime, 0f, 60f * Time.deltaTime, Space.Self);
            t.localScale = scale * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, k)));
            yield return null;
        }

        weakForm.SetActive(false);
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
