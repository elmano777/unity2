/// <summary>
/// Anything that can be shot: minions, towers, the patron, the rival player and soul pickups.
/// Weapons and abilities only ever talk to this interface, never to concrete types, so the same
/// shooting code works against every target in the ritual.
/// </summary>
public interface IDamageable
{
    /// <summary>Side this target belongs to, so attackers can skip their own allies.</summary>
    Faction Faction { get; }

    /// <summary>False once the target has died; further hits should be ignored.</summary>
    bool IsAlive { get; }

    /// <summary>
    /// Apply a hit. Implementations are responsible for checking
    /// <see cref="FactionUtility.IsHostile"/> and for ignoring damage once dead.
    /// </summary>
    void TakeDamage(DamageInfo info);
}
