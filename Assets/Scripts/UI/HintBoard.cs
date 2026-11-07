using TMPro;
using UnityEngine;

/// <summary>
/// A floating panel with the current hint (RF-13). It listens to <see cref="GameEvents.HintRequested"/>,
/// so anything can ask for a hint without knowing the panel exists, and it drifts after the player's
/// gaze instead of being glued to the head, which is uncomfortable to read in VR.
/// </summary>
/// <remarks>
/// It sits at the top of the view, above the line of sight, so it never covers the lane, the
/// minions or the rival. A new hint gets noticed anyway: the panel shows bright and a little larger
/// for <see cref="noticeSeconds"/>, then settles smaller and faded. Looking up at it brings it back
/// to full strength while the player reads it.
/// </remarks>
public class HintBoard : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Visible background behind the text. Hidden together with it when there is no hint.")]
    [SerializeField] private GameObject panel;

    [Header("Placement")]
    [SerializeField] private float distance = 1.9f;

    [Tooltip("Metres above eye level. At 1.9 m away, 0.55 m puts it about 16° up: the top of the view, clear of the line of sight.")]
    [SerializeField] private float heightAboveEyes = 0.55f;

    [Tooltip("Scale of the panel as step 19 builds it (1.7 × 0.62 m). Smaller is less in the way.")]
    [SerializeField] private float size = 0.68f;

    [Tooltip("How quickly the panel turns to follow where the player faces. Lower = lazier.")]
    [SerializeField] private float followSpeed = 1.8f;

    [Header("Fading")]
    [Tooltip("Seconds a new hint stays bright and slightly larger, so it is noticed above the view.")]
    [SerializeField] private float noticeSeconds = 2.5f;

    [Tooltip("Opacity once the hint has been noticed and the player isn't looking at it.")]
    [Range(0f, 1f)]
    [SerializeField] private float restOpacity = 0.55f;

    [Tooltip("Opacity of the dark backing at full strength. Step 19's material is 0.62, which over the sky and the " +
             "lane's light sandstone leaves the text with little contrast.")]
    [Range(0f, 1f)]
    [SerializeField] private float backingAlpha = 0.85f;

    [Tooltip("Floor for the dark backing while the text is faded, so a faded hint stays readable.")]
    [Range(0f, 1f)]
    [SerializeField] private float panelRestOpacity = 0.9f;

    [Tooltip("Looking within this many degrees of the panel shows it at full opacity. Must stay well under the " +
             "panel's elevation (~16°), or looking straight ahead already counts; it is capped at 60 % of it.")]
    [SerializeField] private float lookAngle = 9f;

    [Tooltip("How fast the opacity and size ease between states.")]
    [SerializeField] private float fadeSpeed = 4f;

    private Transform head;
    private Vector3 smoothForward = Vector3.forward;
    private float hideTime = -1f;
    private float noticeUntil;
    private float opacity = 1f;
    private float grow = 1f;
    private Renderer panelRenderer;
    private Color panelColor = Color.white;
    private MaterialPropertyBlock block;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        if (panel != null && panel.TryGetComponent(out panelRenderer) && panelRenderer.sharedMaterial != null
            && panelRenderer.sharedMaterial.HasProperty(ColorId))
        {
            panelColor = panelRenderer.sharedMaterial.GetColor(ColorId);
            panelColor.a = Mathf.Max(panelColor.a, backingAlpha);
        }

        block = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        GameEvents.HintRequested += Show;
        Apply(string.Empty);
    }

    private void OnDisable()
    {
        GameEvents.HintRequested -= Show;
    }

    /// <summary>Shows a hint. With <paramref name="seconds"/> above zero it clears itself afterwards.</summary>
    public void Show(string message) => Show(message, 0f);

    public void Show(string message, float seconds)
    {
        hideTime = seconds > 0f ? Time.time + seconds : -1f;

        // A changed hint is news: bring it up bright so it is seen at the top of the view.
        if (!string.IsNullOrEmpty(message) && (label == null || label.text != message))
        {
            noticeUntil = Time.time + noticeSeconds;
        }

        Apply(message);
    }

    private void Apply(string message)
    {
        if (label != null)
        {
            label.text = message;
        }

        if (panel != null)
        {
            panel.SetActive(!string.IsNullOrEmpty(message));
        }
    }

    private void LateUpdate()
    {
        if (hideTime > 0f && Time.time >= hideTime)
        {
            hideTime = -1f;
            Apply(string.Empty);
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
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            smoothForward = Vector3.Slerp(smoothForward, facing.normalized, t);
        }

        // Above the eyes and tilted down towards them, so it reads straight on when looked at.
        Vector3 position = head.position + smoothForward * distance + Vector3.up * heightAboveEyes;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));

        Fade(position);
    }

    private void Fade(Vector3 position)
    {
        bool noticing = Time.time < noticeUntil;
        // Straight ahead the panel is already `elevation` degrees off the gaze: the threshold has to be
        // smaller than that, or it never fades.
        float elevation = Mathf.Atan2(heightAboveEyes, distance) * Mathf.Rad2Deg;
        float threshold = Mathf.Min(lookAngle, elevation * 0.6f);
        bool lookedAt = Vector3.Angle(head.forward, position - head.position) < threshold;
        float targetOpacity = noticing || lookedAt ? 1f : restOpacity;
        float targetGrow = noticing ? 1.12f : 1f;

        float k = 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime);
        opacity = Mathf.Lerp(opacity, targetOpacity, k);
        grow = Mathf.Lerp(grow, targetGrow, k);
        transform.localScale = Vector3.one * (size * grow);

        if (label != null)
        {
            label.alpha = opacity;
        }

        if (panelRenderer != null)
        {
            Color c = panelColor;
            c.a *= Mathf.Max(opacity, panelRestOpacity);
            panelRenderer.GetPropertyBlock(block);
            block.SetColor(ColorId, c);
            panelRenderer.SetPropertyBlock(block);
        }
    }
}
