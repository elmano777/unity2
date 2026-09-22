using UnityEngine;

/// <summary>
/// Draws the aiming laser for <see cref="OVRInputModule"/>: a beam from the controller plus a dot
/// where it lands. The input module feeds it the same ray it uses for clicks every frame, so what
/// you see is always where the click will actually go.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class LaserPointer : OVRCursor
{
    [Tooltip("How far the beam reaches when it is not pointing at anything.")]
    [SerializeField] private float maxLength = 10f;

    [Tooltip("Dot drawn where the beam lands. Hidden while the beam hits nothing.")]
    [SerializeField] private Transform cursorDot;

    [Tooltip("Dot diameter one metre out. It grows with distance so it looks the same size from anywhere.")]
    [SerializeField] private float cursorSizeAtOneMetre = 0.02f;

    /// <summary>Lifts the dot off the surface it landed on so the two do not z-fight.</summary>
    private const float SurfaceOffset = 0.01f;

    private LineRenderer beam;
    private Vector3 start;
    private Vector3 end;
    private bool isHitting;
    private bool gotRayThisFrame;

    private void Awake()
    {
        beam = GetComponent<LineRenderer>();
        beam.useWorldSpace = true;
        beam.positionCount = 2;
    }

    private void Start()
    {
        if (cursorDot != null)
        {
            cursorDot.gameObject.SetActive(false);
        }
    }

    /// <summary>Called every frame with the raw ray, before any hit is known.</summary>
    public override void SetCursorRay(Transform ray)
    {
        start = ray.position;
        end = ray.position + ray.forward * maxLength;
        isHitting = false;
        gotRayThisFrame = true;
    }

    /// <summary>Called after <see cref="SetCursorRay"/> when the ray actually hit something.</summary>
    public override void SetCursorStartDest(Vector3 rayStart, Vector3 hitPoint, Vector3 hitNormal)
    {
        start = rayStart;
        end = hitPoint;
        isHitting = true;
        gotRayThisFrame = true;

        if (cursorDot != null)
        {
            cursorDot.position = hitPoint + (rayStart - hitPoint).normalized * SurfaceOffset;
            cursorDot.rotation = Quaternion.LookRotation(hitNormal);
        }
    }

    private void LateUpdate()
    {
        beam.enabled = gotRayThisFrame;
        if (gotRayThisFrame)
        {
            beam.SetPosition(0, start);
            beam.SetPosition(1, end);
        }

        if (cursorDot != null)
        {
            bool showDot = gotRayThisFrame && isHitting;
            cursorDot.gameObject.SetActive(showDot);
            if (showDot)
            {
                float size = cursorSizeAtOneMetre * Vector3.Distance(start, end);
                cursorDot.localScale = new Vector3(size, size, size);
            }
        }

        gotRayThisFrame = false;
    }
}
