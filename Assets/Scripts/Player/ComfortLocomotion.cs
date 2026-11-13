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

    [Tooltip("Walking speed in m/s at full stick. 2.5 felt slow across the expanded map; the shop's boots multiply this.")]
    [SerializeField] private float moveSpeed = 3.75f;

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

    [Header("Ground")]
    [Tooltip("Layers the player stands on: the street, ramps, bridge decks. Left empty it is the Ground layer.")]
    [SerializeField] private LayerMask groundLayers;

    [Tooltip("Highest ledge the player steps onto without a ramp, in metres. Also how far above the feet the floor is looked for, so walking under a bridge never snaps up onto it.")]
    [SerializeField] private float stepHeight = 0.6f;

    [Tooltip("Fastest the view goes up or down with the floor, in m/s. Ramps stay well below this; it only softens a ledge.")]
    [SerializeField] private float riseSpeed = 5f;
    [SerializeField] private float fallSpeed = 7f;

    private InputAction move;
    private InputAction turn;
    private bool turnArmed = true;
    private PlayerSpawnHeight spawnHeight;
    private bool grounded;
    private float feetY;
    private Vector3 lastHeadFlat;

    /// <summary>Height of the floor under the player, once one has been found.</summary>
    public float FloorHeight => grounded ? feetY : Rig.position.y;

    /// <summary>Metres the player floats above the floor (the storm ability). Eased by whoever sets it.</summary>
    public float HeightOffset { get; set; }

    /// <summary>0 when standing still, 1 at full stick speed. Read by the vignette.</summary>
    public float Speed01 { get; private set; }

    /// <summary>Set to 1 while something else moves the player (the zipline), so the vignette closes in too.</summary>
    public float ExternalMotion01 { get; set; }

    /// <summary>Multiplies the walking speed. Raised by the shop's speed item.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>The transform the stick moves (the player's rig root).</summary>
    public Transform Rig => rig != null ? rig : transform;

    /// <summary>Walking speed in m/s at full stick before the shop: set from the hero being played.</summary>
    public float BaseSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0.5f, value);
    }

    private float slowFraction;
    private float slowUntil;
    private Vector3 tetherAnchor;
    private float tetherLength;
    private float tetherUntil;

    /// <summary>True while a rival's ability slows this player.</summary>
    public bool IsSlowed => Time.time < slowUntil;

    /// <summary>True while a rival's stake holds this player near it.</summary>
    public bool IsTethered => Time.time < tetherUntil;

    /// <summary>Slows walking by <paramref name="fraction"/> (0.3 = 30 % slower) for a while; the strongest slow wins.</summary>
    public void ApplySlow(float fraction, float seconds)
    {
        fraction = Mathf.Clamp(fraction, 0f, 0.9f);

        if (!IsSlowed || fraction >= slowFraction)
        {
            slowFraction = fraction;
        }

        slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
    }

    /// <summary>Keeps the player within <paramref name="length"/> metres (on the ground plane) of <paramref name="anchor"/> for a while.</summary>
    public void Tether(Vector3 anchor, float length, float seconds)
    {
        tetherAnchor = anchor;
        tetherLength = Mathf.Max(0.5f, length);
        tetherUntil = Time.time + seconds;
    }

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

        if (groundLayers.value == 0)
        {
            groundLayers = LayerMask.GetMask("Ground");
        }

        spawnHeight = FindObjectOfType<PlayerSpawnHeight>();
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
        FollowGround();
    }

    /// <summary>
    /// Keeps the rig on the floor under the head, so ramps, stairs and bridge decks carry the player
    /// up and down. The floor is looked for from just above the feet: standing under a bridge finds
    /// the street, standing on it finds the deck. Scenes without any Ground collider are left alone.
    /// </summary>
    private void FollowGround()
    {
        Vector3 flat = new Vector3(head.position.x, 0f, head.position.z);

        // Something else put the player somewhere far away (a respawn): no easing from the old floor.
        bool jumped = grounded && (flat - lastHeadFlat).sqrMagnitude > 9f;
        lastHeadFlat = flat;

        float from = grounded ? feetY + stepHeight : rig.position.y + 1f;
        float floor;

        if (Physics.Raycast(new Vector3(flat.x, from, flat.z), Vector3.down, out RaycastHit hit, 40f,
                groundLayers, QueryTriggerInteraction.Ignore))
        {
            floor = hit.point.y;
        }
        else if (!NearestFloor(flat, grounded ? feetY : rig.position.y, out floor))
        {
            return;
        }

        if (!grounded || jumped)
        {
            feetY = floor;
            grounded = true;
        }
        else
        {
            feetY = Mathf.MoveTowards(feetY, floor, (floor > feetY ? riseSpeed : fallSpeed) * Time.deltaTime);
        }

        float lift = spawnHeight != null ? spawnHeight.Lift : 0f;

        if (spawnHeight != null)
        {
            spawnHeight.Managed = true;
        }

        Vector3 position = rig.position;
        position.y = feetY + lift + HeightOffset;
        rig.position = position;
    }

    /// <summary>
    /// Nothing under the feet (the player was moved to a spot whose floor is higher than where they
    /// were): take the walkable surface closest in height to where they stand.
    /// </summary>
    private bool NearestFloor(Vector3 flat, float reference, out float floor)
    {
        floor = 0f;
        bool found = false;
        float best = float.MaxValue;

        foreach (RaycastHit candidate in Physics.RaycastAll(new Vector3(flat.x, reference + 60f, flat.z), Vector3.down, 120f,
                     groundLayers, QueryTriggerInteraction.Ignore))
        {
            float distance = Mathf.Abs(candidate.point.y - reference);

            if (distance < best)
            {
                best = distance;
                floor = candidate.point.y;
                found = true;
            }
        }

        return found;
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
        float slow = IsSlowed ? 1f - slowFraction : 1f;
        Vector3 step = (forward * stick.y + right * stick.x) * (moveSpeed * SpeedMultiplier * slow * Time.deltaTime);

        // Cast from the player's actual body (where the head is), not the rig origin, which can
        // be a metre away if the player has walked around their play space.
        Vector3 origin = new Vector3(head.position.x, FloorHeight + 1f, head.position.z);
        step = Slide(origin, step);
        step = Slide(origin, step);

        Vector3 target = rig.position + step;
        Vector3 headTarget = head.position + step;

        // Clamp by where the head would end up, then move the rig by the same correction.
        float clampedX = Mathf.Clamp(headTarget.x, laneX.x, laneX.y);
        float clampedZ = Mathf.Clamp(headTarget.z, laneZ.x, laneZ.y);
        target.x += clampedX - headTarget.x;
        target.z += clampedZ - headTarget.z;

        // A stake's tether: past its length the stick can't carry the head any further from the
        // anchor (only along or back towards it). Never a pull: a sudden shove is sickening in VR.
        if (IsTethered)
        {
            Vector3 moved = target - rig.position;
            Vector3 radial = head.position - tetherAnchor;
            radial.y = 0f;
            Vector3 after = radial + new Vector3(moved.x, 0f, moved.z);

            if (after.magnitude > tetherLength && after.magnitude > radial.magnitude && radial.sqrMagnitude > 0.0001f)
            {
                Vector3 outward = radial.normalized;
                float away = Vector3.Dot(moved, outward);

                if (away > 0f)
                {
                    target -= outward * away;
                }
            }
        }

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
