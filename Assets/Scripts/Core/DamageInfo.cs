using UnityEngine;

/// <summary>
/// Where a hit came from. Kept separate from <see cref="DamageInfo"/> so the HUD, the stats
/// recorder and the audio feedback can react differently to a bullet, an ability or a tower shot.
/// </summary>
public enum DamageType
{
    Bullet = 0,
    Ability = 1,
    Tower = 2,
    Minion = 3,
}

/// <summary>
/// A single damage event travelling from an attacker to an <see cref="IDamageable"/>.
/// Passed by value so nothing downstream can mutate the attacker's data.
/// </summary>
public struct DamageInfo
{
    /// <summary>Raw damage before any target-side reduction.</summary>
    public float amount;

    /// <summary>Side that dealt the damage, checked against the target's faction.</summary>
    public Faction source;

    /// <summary>Object that caused the hit (weapon, minion, tower). May be null.</summary>
    public GameObject instigator;

    /// <summary>World-space impact point, used for hit VFX and for spawning souls.</summary>
    public Vector3 point;

    /// <summary>Direction the hit travelled in, used for knockback and impact decals.</summary>
    public Vector3 direction;

    /// <summary>What kind of attack this was.</summary>
    public DamageType type;

    public DamageInfo(float amount, Faction source, GameObject instigator, DamageType type = DamageType.Bullet)
    {
        this.amount = amount;
        this.source = source;
        this.instigator = instigator;
        this.type = type;
        point = instigator != null ? instigator.transform.position : Vector3.zero;
        direction = instigator != null ? instigator.transform.forward : Vector3.forward;
    }

    /// <summary>Copy of this hit with the impact point and direction filled in from a raycast.</summary>
    public DamageInfo AtImpact(Vector3 impactPoint, Vector3 impactDirection)
    {
        DamageInfo copy = this;
        copy.point = impactPoint;
        copy.direction = impactDirection;
        return copy;
    }
}
