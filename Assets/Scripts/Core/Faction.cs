/// <summary>
/// Which side an entity fights for. Used by <see cref="IDamageable"/> so weapons, minions,
/// towers and abilities can all decide friend from foe with the same check.
/// </summary>
public enum Faction
{
    /// <summary>Hit by everything, hits nothing (souls, props).</summary>
    Neutral = 0,

    /// <summary>The local player and their minions/towers/patron.</summary>
    Player = 1,

    /// <summary>The rival ritualist and their minions/towers/patron.</summary>
    Enemy = 2,
}

/// <summary>
/// Helpers for reasoning about factions without repeating the same comparisons everywhere.
/// </summary>
public static class FactionUtility
{
    /// <summary>True when <paramref name="attacker"/> is allowed to damage <paramref name="target"/>.</summary>
    public static bool IsHostile(Faction attacker, Faction target)
    {
        if (target == Faction.Neutral)
        {
            return true;
        }

        if (attacker == Faction.Neutral)
        {
            return false;
        }

        return attacker != target;
    }

    /// <summary>
    /// True for neutral things that are not creatures: souls and the shop's cards, which a shot can
    /// reach but an ability should not strike. A neutral <see cref="Health"/> is a creature (the
    /// camps beside the lane) and abilities do hurt it.
    /// </summary>
    public static bool IsProp(IDamageable target)
    {
        return target.Faction == Faction.Neutral && !(target is Health);
    }

    /// <summary>The side opposing <paramref name="faction"/>; neutral has no opposite.</summary>
    public static Faction Opposite(Faction faction)
    {
        switch (faction)
        {
            case Faction.Player: return Faction.Enemy;
            case Faction.Enemy: return Faction.Player;
            default: return Faction.Neutral;
        }
    }
}
