using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// A 3D object the laser pointer can press: a floating rune, the confirm sigil. Needs a collider on
/// the same object and an <c>OVRPhysicsRaycaster</c> on the camera rig so the OVRInputModule's
/// laser reaches physics objects, not only canvases.
/// </summary>
/// <remarks>
/// Hover feedback is a small grow, a brighter glow (a float shader property set through a property
/// block, so the shared material is untouched) and a light haptic tick, so the player knows what
/// the trigger will press before pressing it.
/// </remarks>
public class WorldButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private UnityEvent onClick = new UnityEvent();

    [Header("Hover feedback")]
    [Tooltip("Grows while hovered. Defaults to this transform.")]
    [SerializeField] private Transform scaleTarget;
    [SerializeField] private float hoverScale = 1.1f;

    [Tooltip("Renderers whose glow property rises while hovered.")]
    [SerializeField] private Renderer[] glowRenderers;
    [SerializeField] private string glowProperty = "_Intensity";
    [SerializeField] private float normalGlow = 1f;
    [SerializeField] private float hoverGlow = 2.2f;

    [SerializeField] private float animationSpeed = 12f;

    private MaterialPropertyBlock block;
    private Vector3 baseScale;
    private float glow;
    private bool hovered;

    public UnityEvent OnClick => onClick;

    private void Awake()
    {
        if (scaleTarget == null)
        {
            scaleTarget = transform;
        }

        baseScale = scaleTarget.localScale;
        block = new MaterialPropertyBlock();
        glow = normalGlow;
        ApplyGlow();
    }

    private void OnDisable()
    {
        hovered = false;
        scaleTarget.localScale = baseScale;
        glow = normalGlow;
        ApplyGlow();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.15f, 0.03f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Press();
    }

    /// <summary>Same as a laser click. Public so menus and tests can trigger it directly.</summary>
    public void Press()
    {
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.5f, 0.08f);
        onClick.Invoke();
    }

    private void Update()
    {
        float k = 1f - Mathf.Exp(-animationSpeed * Time.deltaTime);
        Vector3 targetScale = baseScale * (hovered ? hoverScale : 1f);
        scaleTarget.localScale = Vector3.Lerp(scaleTarget.localScale, targetScale, k);

        float targetGlow = hovered ? hoverGlow : normalGlow;

        if (!Mathf.Approximately(glow, targetGlow))
        {
            glow = Mathf.Lerp(glow, targetGlow, k);
            ApplyGlow();
        }
    }

    private void ApplyGlow()
    {
        if (glowRenderers == null || block == null)
        {
            return;
        }

        foreach (Renderer r in glowRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(glowProperty, glow);
            r.SetPropertyBlock(block);
        }
    }
}
