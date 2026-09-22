using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lane tower: auto-attacks the nearest hostile target in range (RF-12). Prefers minions over
/// the player, the way a MOBA tower does, so the player can push behind their own wave instead of
/// being punished for stepping forward.
/// </summary>
public class Tower : Structure
{
    [Header("Attack")]
    [Tooltip("Muzzle the tower fires from. Also the centre of its range check.")]
    [SerializeField] private Transform muzzle;

    [Tooltip("Damage of a tier 1 tower. Scales with tier like health does.")]
    [SerializeField] private float baseDamage = 25f;

    [Tooltip("Damage multiplier applied per tier above 1, compounding.")]
    [SerializeField] private float damagePerTier = 1.4f;

    [SerializeField] private float attackRange = 14f;
    [SerializeField] private float attackInterval = 1.2f;

    [Tooltip("Layers the tower scans for targets.")]
    [SerializeField] private LayerMask targetMask = ~0;

    [Header("Targeting")]
    [Tooltip("Attack minions first and only turn on the player when no minion is in range.")]
    [SerializeField] private bool preferMinions = true;

    [Header("Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip attackClip;
    [SerializeField] private LineRenderer beam;
    [SerializeField] private float beamDuration = 0.12f;

    private readonly Collider[] overlapBuffer = new Collider[32];
    private float nextAttackTime;
    private float beamHideTime;
    private bool disabled;

    /// <summary>Damage this tower deals per shot, after tier scaling.</summary>
    public float Damage => baseDamage * Mathf.Pow(damagePerTier, Mathf.Max(0, Tier - 1));

    /// <summary>Radius the tower can shoot within.</summary>
    public float AttackRange => attackRange;

    protected override void Awake()
    {
        base.Awake();

        if (muzzle == null)
        {
            muzzle = transform;
        }

        if (beam != null)
        {
            beam.enabled = false;
        }
    }

    private void Update()
    {
        if (beam != null && beam.enabled && Time.time >= beamHideTime)
        {
            beam.enabled = false;
        }

        if (disabled || !IsStanding || Time.time < nextAttackTime)
        {
            return;
        }

        IDamageable target = FindTarget(out Vector3 targetPoint);

        if (target == null)
        {
            return;
        }

        nextAttackTime = Time.time + attackInterval;
        Fire(target, targetPoint);
    }

    protected override void OnDestroyed(DamageInfo info)
    {
        disabled = true;

        if (beam != null)
        {
            beam.enabled = false;
        }
    }

    private IDamageable FindTarget(out Vector3 point)
    {
        point = Vector3.zero;

        int count = Physics.OverlapSphereNonAlloc(
            muzzle.position, attackRange, overlapBuffer, targetMask, QueryTriggerInteraction.Ignore);

        IDamageable bestMinion = null;
        Vector3 bestMinionPoint = Vector3.zero;
        float bestMinionDistance = float.MaxValue;

        IDamageable bestOther = null;
        Vector3 bestOtherPoint = Vector3.zero;
        float bestOtherDistance = float.MaxValue;

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

            // Souls are neutral and would otherwise read as a valid target.
            if (candidate.Faction == Faction.Neutral)
            {
                continue;
            }

            if (!FactionUtility.IsHostile(Faction, candidate.Faction))
            {
                continue;
            }

            Vector3 candidatePoint = collider.bounds.center;
            float distance = Vector3.SqrMagnitude(candidatePoint - muzzle.position);

            bool isMinion = collider.GetComponentInParent<MinionAgent>() != null;

            if (isMinion && distance < bestMinionDistance)
            {
                bestMinion = candidate;
                bestMinionPoint = candidatePoint;
                bestMinionDistance = distance;
            }
            else if (!isMinion && distance < bestOtherDistance)
            {
                bestOther = candidate;
                bestOtherPoint = candidatePoint;
                bestOtherDistance = distance;
            }
        }

        if (preferMinions && bestMinion != null)
        {
            point = bestMinionPoint;
            return bestMinion;
        }

        if (bestOther != null)
        {
            point = bestOtherPoint;
            return bestOther;
        }

        point = bestMinionPoint;
        return bestMinion;
    }

    private void Fire(IDamageable target, Vector3 targetPoint)
    {
        DamageInfo info = new DamageInfo(Damage, Faction, gameObject, DamageType.Tower)
            .AtImpact(targetPoint, (targetPoint - muzzle.position).normalized);

        target.TakeDamage(info);

        if (audioSource != null && attackClip != null)
        {
            audioSource.PlayOneShot(attackClip);
        }

        if (beam != null)
        {
            beam.positionCount = 2;
            beam.SetPosition(0, muzzle.position);
            beam.SetPosition(1, targetPoint);
            beam.enabled = true;
            beamHideTime = Time.time + beamDuration;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(muzzle != null ? muzzle.position : transform.position, attackRange);
    }
}
