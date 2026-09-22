using UnityEngine;

/// <summary>
/// A lane minion. Walks its <see cref="LanePath"/>, stops to fight whatever hostile it meets, and
/// drops souls when it dies (RF-07). Two opposing waves left alone will meet in the middle and
/// fight without the player, which is what makes the lane testable in isolation.
/// </summary>
/// <remarks>
/// Movement is waypoint steering with a simple ground snap rather than a NavMeshAgent — see
/// <see cref="LanePath"/> for why. Everything here is plain gameplay code, no XR, no networking.
/// </remarks>
[RequireComponent(typeof(Health))]
public class MinionAgent : MonoBehaviour
{
    /// <summary>What the minion is doing right now.</summary>
    public enum State
    {
        /// <summary>Walking the lane toward the enemy base.</summary>
        Advancing,

        /// <summary>Moving toward a target it spotted.</summary>
        Chasing,

        /// <summary>In range, trading damage.</summary>
        Attacking,

        /// <summary>Medic only: tending a damaged ally.</summary>
        Healing,

        /// <summary>Dead; the component stops doing anything.</summary>
        Dead,
    }

    [Header("Data")]
    [SerializeField] private MinionDefinitionSO definition;

    [Header("Scene references")]
    [Tooltip("Prefab spawned for each soul this minion drops. Needs a SoulPickup.")]
    [SerializeField] private GameObject soulPrefab;

    [Tooltip("Where souls appear from. Defaults to this transform.")]
    [SerializeField] private Transform soulOrigin;

    [Header("Movement")]
    [Tooltip("How fast it turns to face where it is going, in degrees per second.")]
    [SerializeField] private float turnSpeed = 360f;

    [Header("Walk motion")]
    [Tooltip("Child holding the mesh. The walk bob is applied here so it doesn't fight the ground snap on the root.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("How far the body rises and falls per step, in metres. These models have no skeleton, " +
             "so this fake gait is what stops them reading as sliding props in VR.")]
    [SerializeField] private float bobHeight = 0.07f;

    [Tooltip("Steps per metre travelled. Tied to distance, not time, so the gait matches the speed.")]
    [SerializeField] private float stepsPerMetre = 1.1f;

    [Tooltip("Degrees the body rolls side to side across a step.")]
    [SerializeField] private float walkRoll = 4f;

    [Tooltip("Degrees the body leans forward while moving.")]
    [SerializeField] private float walkLean = 6f;

    [Tooltip("How quickly the lean eases in and out when starting or stopping.")]
    [SerializeField] private float leanSmoothing = 6f;

    [Tooltip("Layers treated as ground when snapping the minion to the floor.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Targeting")]
    [Tooltip("Layers scanned for enemies to attack.")]
    [SerializeField] private LayerMask targetMask = ~0;

    [Tooltip("Seconds between target scans. Kept off the per-frame path to save time on Quest.")]
    [SerializeField] private float scanInterval = 0.25f;

    [Header("Medic")]
    [Tooltip("Layers scanned for allies to heal. Medics only.")]
    [SerializeField] private LayerMask allyMask = ~0;

    [Tooltip("Line drawn from the medic to whoever it is healing. Optional.")]
    [SerializeField] private LineRenderer healBeam;

    [SerializeField] private float healBeamDuration = 0.4f;

    private readonly Collider[] overlapBuffer = new Collider[24];
    private Health health;
    private LanePath lane;
    private Faction faction;
    private bool reverseLane;
    private int waypointIndex;
    private float nextScanTime;
    private float nextAttackTime;
    private IDamageable currentTarget;
    private Transform currentTargetTransform;
    private Vector3 lanePosition;
    private Health healTarget;
    private float nextHealTime;
    private float healBeamHideTime;
    private float walkPhase;
    private float currentLean;
    private Vector3 visualBasePosition;
    private Quaternion visualBaseRotation;
    private bool movedThisFrame;

    /// <summary>Current behaviour, exposed for debugging and for a future bot to read.</summary>
    public State CurrentState { get; private set; } = State.Advancing;

    /// <summary>Side this minion fights for.</summary>
    public Faction Faction => faction;

    private void Awake()
    {
        health = GetComponent<Health>();

        if (soulOrigin == null)
        {
            soulOrigin = transform;
        }

        if (healBeam != null)
        {
            healBeam.enabled = false;
        }

        if (visualRoot != null)
        {
            visualBasePosition = visualRoot.localPosition;
            visualBaseRotation = visualRoot.localRotation;
        }
    }

    private void LateUpdate()
    {
        if (healBeam != null && healBeam.enabled && Time.time >= healBeamHideTime)
        {
            healBeam.enabled = false;
        }

        ApplyWalkMotion();
        movedThisFrame = false;
    }

    /// <summary>
    /// Fakes a gait on the mesh: a vertical bob and a side-to-side roll driven by distance
    /// travelled, plus a forward lean that eases in when the minion starts moving.
    /// </summary>
    /// <remarks>
    /// The models extracted from the game are static meshes with no skeleton, so there is no walk
    /// animation to play. Without this they slide along the ground, which reads as broken in VR
    /// where motion cues are judged much more harshly than on a flat screen.
    /// </remarks>
    private void ApplyWalkMotion()
    {
        if (visualRoot == null || CurrentState == State.Dead)
        {
            return;
        }

        float targetLean = movedThisFrame ? 1f : 0f;
        currentLean = Mathf.MoveTowards(currentLean, targetLean, leanSmoothing * Time.deltaTime);

        if (currentLean <= 0.001f)
        {
            visualRoot.localPosition = visualBasePosition;
            visualRoot.localRotation = visualBaseRotation;
            return;
        }

        // Two bobs per stride, so the body rises on each foot rather than once per cycle.
        float bob = Mathf.Abs(Mathf.Sin(walkPhase * Mathf.PI)) * bobHeight * currentLean;
        float roll = Mathf.Sin(walkPhase * Mathf.PI * 0.5f) * walkRoll * currentLean;

        visualRoot.localPosition = visualBasePosition + Vector3.up * bob;
        visualRoot.localRotation = visualBaseRotation * Quaternion.Euler(walkLean * currentLean, 0f, roll);
    }

    private void OnEnable()
    {
        health.Killed += OnKilled;
    }

    private void OnDisable()
    {
        health.Killed -= OnKilled;
    }

    /// <summary>
    /// Sets the minion up at spawn time. Called by <see cref="WaveSpawner"/> straight after
    /// instantiating, before the first Update runs.
    /// </summary>
    /// <param name="minionDefinition">Stats to use.</param>
    /// <param name="path">Lane to walk.</param>
    /// <param name="minionFaction">Side it fights for.</param>
    /// <param name="walkReversed">True for the wave that starts at the far end of the lane.</param>
    public void Initialize(MinionDefinitionSO minionDefinition, LanePath path, Faction minionFaction, bool walkReversed)
    {
        definition = minionDefinition;
        lane = path;
        faction = minionFaction;
        reverseLane = walkReversed;

        CurrentState = State.Advancing;
        waypointIndex = 0;
        lanePosition = transform.position;

        health.Configure(minionFaction, definition != null ? definition.maxHealth : 100f);
    }

    private void Update()
    {
        if (CurrentState == State.Dead || definition == null)
        {
            return;
        }

        // Medics never fight, so they run an entirely separate loop rather than sharing the
        // combat states and having to opt out of each one.
        if (definition.role == MinionRole.Medic)
        {
            UpdateMedic();
            return;
        }

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + scanInterval;
            AcquireTarget();
        }

        switch (CurrentState)
        {
            case State.Advancing:
                Advance();
                break;

            case State.Chasing:
                Chase();
                break;

            case State.Attacking:
                Attack();
                break;
        }
    }

    /// <summary>
    /// Medic loop: find the most hurt ally within reach, walk to a standoff distance behind it and
    /// pulse healing. With nobody to heal it just walks the lane with the rest of the wave.
    /// </summary>
    private void UpdateMedic()
    {
        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + scanInterval;
            healTarget = FindWoundedAlly();
        }

        if (healTarget == null || !healTarget.IsAlive)
        {
            healTarget = null;
            CurrentState = State.Advancing;
            Advance();
            return;
        }

        CurrentState = State.Healing;

        Vector3 allyPosition = healTarget.transform.position;
        float distance = Vector3.Distance(transform.position, allyPosition);

        // Hang back: close in only when out of reach, and stop short so the medic doesn't walk
        // into the front line it is trying to keep alive.
        if (distance > definition.medicStandoff)
        {
            MoveToward(allyPosition);
        }
        else
        {
            FaceToward(allyPosition);
        }

        if (distance > definition.healRange || Time.time < nextHealTime)
        {
            return;
        }

        nextHealTime = Time.time + definition.healInterval;
        healTarget.Heal(definition.healAmount);

        if (healBeam != null)
        {
            healBeam.positionCount = 2;
            healBeam.SetPosition(0, soulOrigin.position);
            healBeam.SetPosition(1, allyPosition + Vector3.up * 0.8f);
            healBeam.enabled = true;
            healBeamHideTime = Time.time + healBeamDuration;
        }
    }

    /// <summary>
    /// The nearby ally with the lowest fraction of health left, ignoring anyone at full health so
    /// the medic doesn't lock onto an undamaged minion and follow it around.
    /// </summary>
    private Health FindWoundedAlly()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, definition.healRange * 2f, overlapBuffer, allyMask, QueryTriggerInteraction.Ignore);

        Health best = null;
        float worstFraction = 0.98f;

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlapBuffer[i];
            if (collider == null)
            {
                continue;
            }

            Health candidate = collider.GetComponentInParent<Health>();

            if (candidate == null || candidate == health || !candidate.IsAlive)
            {
                continue;
            }

            if (candidate.Faction != faction)
            {
                continue;
            }

            if (candidate.Normalized < worstFraction)
            {
                worstFraction = candidate.Normalized;
                best = candidate;
            }
        }

        return best;
    }

    private void Advance()
    {
        if (lane == null || lane.Count == 0)
        {
            return;
        }

        int index = reverseLane ? lane.ReverseIndex(waypointIndex) : waypointIndex;
        Vector3 destination = lane.GetPoint(index);

        MoveToward(destination);
        lanePosition = transform.position;

        Vector3 flat = destination - transform.position;
        flat.y = 0f;

        if (flat.magnitude <= definition.waypointTolerance && waypointIndex < lane.Count - 1)
        {
            waypointIndex++;
        }
    }

    private void Chase()
    {
        if (!HasLiveTarget())
        {
            ReturnToLane();
            return;
        }

        // Don't let a minion get dragged across the map by a kiting player.
        if (Vector3.Distance(transform.position, lanePosition) > definition.maxChaseDistance)
        {
            currentTarget = null;
            currentTargetTransform = null;
            ReturnToLane();
            return;
        }

        float distance = Vector3.Distance(transform.position, currentTargetTransform.position);

        if (distance <= definition.attackRange)
        {
            CurrentState = State.Attacking;
            return;
        }

        MoveToward(currentTargetTransform.position);
    }

    private void Attack()
    {
        if (!HasLiveTarget())
        {
            ReturnToLane();
            return;
        }

        float distance = Vector3.Distance(transform.position, currentTargetTransform.position);

        if (distance > definition.attackRange)
        {
            CurrentState = State.Chasing;
            return;
        }

        FaceToward(currentTargetTransform.position);

        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + definition.attackInterval;

        DamageInfo info = new DamageInfo(definition.attackDamage, faction, gameObject, DamageType.Minion)
            .AtImpact(currentTargetTransform.position, (currentTargetTransform.position - transform.position).normalized);

        currentTarget.TakeDamage(info);
    }

    private void ReturnToLane()
    {
        CurrentState = State.Advancing;

        if (lane != null && lane.Count > 0)
        {
            // Rejoin at the nearest waypoint so it doesn't walk all the way back to the spawn.
            int nearest = lane.NearestIndex(transform.position);
            waypointIndex = reverseLane ? lane.ReverseIndex(nearest) : nearest;
        }
    }

    private void AcquireTarget()
    {
        if (HasLiveTarget() && CurrentState != State.Advancing)
        {
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, definition.detectionRange, overlapBuffer, targetMask, QueryTriggerInteraction.Ignore);

        IDamageable best = null;
        Transform bestTransform = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider collider = overlapBuffer[i];
            if (collider == null)
            {
                continue;
            }

            IDamageable candidate = collider.GetComponentInParent<IDamageable>();

            if (candidate == null || !candidate.IsAlive)
            {
                continue;
            }

            // Skip souls, which are neutral and would read as a target.
            if (candidate.Faction == Faction.Neutral || !FactionUtility.IsHostile(faction, candidate.Faction))
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(collider.transform.position - transform.position);

            if (distance < bestDistance)
            {
                best = candidate;
                bestTransform = collider.transform;
                bestDistance = distance;
            }
        }

        if (best == null)
        {
            return;
        }

        currentTarget = best;
        currentTargetTransform = bestTransform;
        CurrentState = State.Chasing;
    }

    private bool HasLiveTarget()
    {
        return currentTarget != null && currentTarget.IsAlive && currentTargetTransform != null;
    }

    private void MoveToward(Vector3 destination)
    {
        Vector3 direction = destination - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        direction.Normalize();

        Vector3 next = transform.position + direction * (definition.moveSpeed * Time.deltaTime);

        // Snap to the ground so a sloped or stepped lane doesn't leave minions floating.
        if (Physics.Raycast(next + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, groundMask, QueryTriggerInteraction.Ignore))
        {
            next.y = hit.point.y;
        }
        else
        {
            next.y = transform.position.y;
        }

        // Advance the gait by distance, not time, so a slowed minion takes slower steps.
        walkPhase += Vector3.Distance(transform.position, next) * stepsPerMetre * 2f;
        movedThisFrame = true;

        transform.position = next;
        FaceToward(destination);
    }

    private void FaceToward(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion desired = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
    }

    private void OnKilled(DamageInfo info)
    {
        CurrentState = State.Dead;
        currentTarget = null;
        currentTargetTransform = null;

        GameEvents.RaiseMinionKilled(faction);
        DropSouls();
    }

    private void DropSouls()
    {
        if (soulPrefab == null || definition == null)
        {
            return;
        }

        int drops = Mathf.Max(1, definition.soulDrops);
        int perDrop = Mathf.Max(1, definition.soulValue / drops);

        for (int i = 0; i < drops; i++)
        {
            // Spread multiple drops in a small ring so they don't overlap into one blob.
            Vector3 offset = drops == 1
                ? Vector3.zero
                : Quaternion.Euler(0f, 360f / drops * i, 0f) * Vector3.forward * 0.35f;

            Vector3 position = soulOrigin.position + offset + Vector3.up * 0.4f;
            GameObject spawned = Instantiate(soulPrefab, position, Quaternion.identity);

            if (spawned.TryGetComponent(out SoulPickup pickup))
            {
                pickup.SetValue(perDrop);
            }

            GameEvents.RaiseSoulDropped(position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (definition == null)
        {
            return;
        }

        Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, definition.detectionRange);

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, definition.attackRange);
    }
}
