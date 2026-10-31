using System.Collections;
using UnityEngine;

/// <summary>
/// One end of a teleporter pair, like the linked teleporters of Deadlock's map: standing on the
/// pad charges it for a moment, then the view fades to black and the player reappears on the
/// linked pad. Shortens the walk back to the front after respawning on the bigger map.
/// </summary>
/// <remarks>
/// A fade instead of a visible slide keeps it comfortable (RNF-02): the body never sees the world
/// move without walking. The charge time is the "short wind-up" of the original, so brushing past
/// a pad does not throw the player across the map. Arriving disarms the destination until the
/// player steps off it, so the two pads cannot bounce the player back and forth.
/// </remarks>
public class TeleportPad : MonoBehaviour
{
    [Tooltip("Pad the player arrives on. The player arrives facing that pad's forward (blue) axis.")]
    [SerializeField] private TeleportPad destination;

    [Tooltip("Horizontal distance from the centre, in metres, at which the head counts as standing on the pad.")]
    [SerializeField] private float radius = 1.1f;

    [Tooltip("Seconds the player has to stay on the pad before it fires.")]
    [SerializeField] private float windUp = 1f;

    [SerializeField] private float fadeDuration = 0.2f;

    [Tooltip("Ring that brightens while the pad charges.")]
    [SerializeField] private Renderer ring;

    [SerializeField] private Color idleColor = new Color(0.15f, 0.75f, 1f, 1f);
    [SerializeField] private Color chargedColor = new Color(0.6f, 1f, 1f, 1f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Only one teleport at a time, whichever pad started it.
    private static bool busy;
    private static Renderer fadeQuad;

    private Transform rig;
    private Transform head;
    private MaterialPropertyBlock block;
    private float charge;
    private bool armed = true;

    /// <summary>Pad the player arrives on.</summary>
    public TeleportPad Destination => destination;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        SetRingColor(0f);
    }

    private void OnDisable()
    {
        busy = false;
    }

    private void Update()
    {
        if (!FindRig() || destination == null)
        {
            return;
        }

        Vector3 offset = head.position - transform.position;
        offset.y = 0f;
        bool inside = offset.sqrMagnitude <= radius * radius;

        if (!inside)
        {
            armed = true;
            charge = 0f;
        }
        else if (armed && !busy)
        {
            charge += Time.deltaTime;

            if (charge >= windUp)
            {
                StartCoroutine(Teleport());
            }
        }

        SetRingColor(windUp > 0f ? charge / windUp : 0f);
    }

    private bool FindRig()
    {
        if (head != null)
        {
            return true;
        }

        OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();

        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            return false;
        }

        rig = cameraRig.transform;
        head = cameraRig.centerEyeAnchor;
        return true;
    }

    private IEnumerator Teleport()
    {
        busy = true;
        armed = false;
        charge = 0f;

        HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.4f, 0.1f);

        Renderer fade = FadeQuad(head);
        yield return Fade(fade, 0f, 1f);

        // Arrive facing where the destination pad points (the lane, or the base gate) rather than
        // whatever wall was ahead. Turned while the view is black, so it costs no comfort.
        Vector3 look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (look.sqrMagnitude > 0.0001f)
        {
            rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(look, destination.transform.forward, Vector3.up));
        }

        // Move the rig by the head's offset, not the rig origin: the player may be standing a
        // metre away from the middle of their play space.
        Vector3 shift = destination.transform.position - head.position;
        shift.y = 0f;
        rig.position += shift;

        // Disarm the destination only now that the player stands on it: while the view was
        // fading the player was still away from it, and that re-arms a pad.
        destination.armed = false;
        destination.charge = 0f;

        yield return Fade(fade, 1f, 0f);
        busy = false;
    }

    private IEnumerator Fade(Renderer quad, float from, float to)
    {
        quad.enabled = true;

        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            quad.sharedMaterial.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / fadeDuration));
            yield return null;
        }

        quad.sharedMaterial.color = new Color(0f, 0f, 0f, to);
        quad.enabled = to > 0f;
    }

    /// <summary>
    /// A black quad just in front of the eyes, shared by every pad. The Lane has no
    /// <see cref="ScreenFader"/>, and a world-space quad works in VR where a screen overlay would not.
    /// </summary>
    private static Renderer FadeQuad(Transform head)
    {
        if (fadeQuad != null)
        {
            return fadeQuad;
        }

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "TeleportFade";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(head, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        quad.transform.localScale = Vector3.one * 2f;

        // Same always-included shader the rival's tracer uses, so it survives the Android build.
        var material = new Material(Shader.Find("Sprites/Default")) { color = Color.clear, renderQueue = 5000 };
        fadeQuad = quad.GetComponent<MeshRenderer>();
        fadeQuad.sharedMaterial = material;
        fadeQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fadeQuad.enabled = false;
        return fadeQuad;
    }

    private void SetRingColor(float amount)
    {
        if (ring == null)
        {
            return;
        }

        ring.GetPropertyBlock(block);
        block.SetColor(BaseColorId, Color.Lerp(idleColor, chargedColor, Mathf.Clamp01(amount)));
        ring.SetPropertyBlock(block);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, radius);

        if (destination != null)
        {
            Gizmos.DrawLine(transform.position + Vector3.up, destination.transform.position + Vector3.up);
        }
    }
}
