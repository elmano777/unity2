using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Free movement on the lane (RNF-02): left stick walks where the head looks, right stick snap
/// turns. Exposes how fast the player is moving so <see cref="ComfortVignette"/> can narrow the
/// view while the body is still and the world moves, which is what causes motion sickness.
/// </summary>
/// <remarks>
/// Input System bindings against the Touch controller layouts, like <see cref="Weapon"/>, so no
/// XRI is needed. WASD + Q/E are bound too so the lane can be walked in the editor without a headset.
/// </remarks>
public class ComfortLocomotion : MonoBehaviour
{
    [Tooltip("Rig root that gets moved. Defaults to this transform.")]
    [SerializeField] private Transform rig;

    [Tooltip("Head the movement direction follows. Found on the OVRCameraRig when left empty.")]
    [SerializeField] private Transform head;

    [SerializeField] private float moveSpeed = 2.5f;

    [Tooltip("Degrees per snap turn. Snap rather than smooth turning: smooth yaw is the worst for comfort.")]
    [SerializeField] private float snapAngle = 45f;

    [Tooltip("Stick deflection that counts as a turn request, and the level it must drop below to re-arm.")]
    [SerializeField] private float snapThreshold = 0.7f;
    [SerializeField] private float snapRelease = 0.3f;

    [Header("Collision")]
    [Tooltip("Layers that block walking: cover blocks, buildings, the tower.")]
    [SerializeField] private LayerMask blockingLayers = ~0;
    [SerializeField] private float bodyRadius = 0.25f;

    [Tooltip("Hard limits of the walkable lane in world space (x = min/max X, y = min/max Z).")]
    [SerializeField] private Vector2 laneX = new Vector2(-6.5f, 6.5f);
    [SerializeField] private Vector2 laneZ = new Vector2(-29f, 29f);

    private InputAction move;
    private InputAction turn;
    private bool turnArmed = true;

    /// <summary>0 when standing still, 1 at full stick speed. Read by the vignette.</summary>
    public float Speed01 { get; private set; }

    /// <summary>Raised on every snap turn, so the vignette can blink through the cut.</summary>
    public event System.Action Turned;

    private void Awake()
    {
        if (rig == null)
        {
            rig = transform;
        }

        if (head == null)
        {
            OVRCameraRig cameraRig = rig.GetComponentInChildren<OVRCameraRig>() ?? FindObjectOfType<OVRCameraRig>();
            head = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main?.transform;
        }
    }

    private void OnEnable()
    {
        move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        move.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

        turn = new InputAction("Turn", InputActionType.Value, expectedControlType: "Vector2");
        turn.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        turn.AddCompositeBinding("2DVector")
            .With("Left", "<Keyboard>/q").With("Right", "<Keyboard>/e");

        move.Enable();
        turn.Enable();
    }

    private void OnDisable()
    {
        move?.Dispose();
        turn?.Dispose();
        move = null;
        turn = null;
        Speed01 = 0f;
    }

    private void Update()
    {
        if (head == null)
        {
            return;
        }

        Walk(move.ReadValue<Vector2>());
        SnapTurn(turn.ReadValue<Vector2>().x);
    }

    private void Walk(Vector2 stick)
    {
        float amount = Mathf.Clamp01(stick.magnitude);
        Speed01 = amount;

        if (amount < 0.1f)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 step = (forward * stick.y + right * stick.x) * (moveSpeed * Time.deltaTime);

        // Cast from the player's actual body (where the head is), not the rig origin, which can
        // be a metre away if the player has walked around their play space.
        Vector3 origin = new Vector3(head.position.x, rig.position.y + 1f, head.position.z);
        step = Slide(origin, step);
        step = Slide(origin, step);

        Vector3 target = rig.position + step;
        Vector3 headTarget = head.position + step;

        // Clamp by where the head would end up, then move the rig by the same correction.
        float clampedX = Mathf.Clamp(headTarget.x, laneX.x, laneX.y);
        float clampedZ = Mathf.Clamp(headTarget.z, laneZ.x, laneZ.y);
        target.x += clampedX - headTarget.x;
        target.z += clampedZ - headTarget.z;

        rig.position = target;
    }

    /// <summary>Removes the part of the step that would walk into a wall, so the player slides along it.</summary>
    private Vector3 Slide(Vector3 origin, Vector3 step)
    {
        float distance = step.magnitude;

        if (distance < 0.0001f)
        {
            return step;
        }

        if (Physics.SphereCast(origin, bodyRadius, step / distance, out RaycastHit hit, distance,
                blockingLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            return Vector3.ProjectOnPlane(step, normal);
        }

        return step;
    }

    private void SnapTurn(float x)
    {
        if (turnArmed && Mathf.Abs(x) >= snapThreshold)
        {
            turnArmed = false;

            // Turn around the head, not the rig origin, so the player doesn't swing sideways.
            rig.RotateAround(head.position, Vector3.up, Mathf.Sign(x) * snapAngle);
            Turned?.Invoke();
        }
        else if (Mathf.Abs(x) <= snapRelease)
        {
            turnArmed = true;
        }
    }
}
