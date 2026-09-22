using System;
using UnityEngine;

/// <summary>
/// Central notice board for the ritual. Systems raise events here instead of holding references
/// to each other: a minion dies without knowing the wallet exists, the wallet grows without
/// knowing the HUD exists, and the stats recorder listens to everything without touching anything.
/// </summary>
/// <remarks>
/// Static state survives scene loads, so every listener must unsubscribe in OnDisable/OnDestroy.
/// <see cref="ResetAll"/> clears the board between matches as a safety net.
/// </remarks>
public static class GameEvents
{
    // ---- Combat ----

    /// <summary>Something took damage. Arguments: the target, the hit that landed.</summary>
    public static event Action<IDamageable, DamageInfo> DamageDealt;

    /// <summary>Something died. Arguments: the dead object, the hit that killed it.</summary>
    public static event Action<GameObject, DamageInfo> Died;

    /// <summary>The player pulled the trigger. Used for the "shots fired" stat.</summary>
    public static event Action ShotFired;

    // ---- Economy ----

    /// <summary>A soul was spawned in the world at the given position.</summary>
    public static event Action<Vector3> SoulDropped;

    /// <summary>The player collected souls. Argument: how many.</summary>
    public static event Action<int> SoulsCollected;

    /// <summary>The player's soul balance changed. Argument: the new total.</summary>
    public static event Action<int> WalletChanged;

    /// <summary>The player bought a shop item.</summary>
    public static event Action<ShopItemSO> ItemPurchased;

    // ---- Progression ----

    /// <summary>A minion died. Argument: the faction it belonged to.</summary>
    public static event Action<Faction> MinionKilled;

    /// <summary>A tower was destroyed. Arguments: the tower, its faction.</summary>
    public static event Action<Structure, Faction> TowerDestroyed;

    /// <summary>A patron core fell — this ends the match. Argument: the losing faction.</summary>
    public static event Action<Faction> PatronDestroyed;

    /// <summary>The player cast an ability. Argument: the ability used.</summary>
    public static event Action<AbilitySO> AbilityUsed;

    // ---- Match flow ----

    /// <summary>The match changed state (pre-match, playing, win, lose).</summary>
    public static event Action<MatchState> MatchStateChanged;

    /// <summary>
    /// A contextual hint should be shown to the player (RF-13). Argument: the message.
    /// </summary>
    public static event Action<string> HintRequested;

    // ---- Raisers ----
    // Wrapped in methods so callers can't null out another system's subscription.

    public static void RaiseDamageDealt(IDamageable target, DamageInfo info) => DamageDealt?.Invoke(target, info);
    public static void RaiseDied(GameObject victim, DamageInfo info) => Died?.Invoke(victim, info);
    public static void RaiseShotFired() => ShotFired?.Invoke();
    public static void RaiseSoulDropped(Vector3 position) => SoulDropped?.Invoke(position);
    public static void RaiseSoulsCollected(int amount) => SoulsCollected?.Invoke(amount);
    public static void RaiseWalletChanged(int newTotal) => WalletChanged?.Invoke(newTotal);
    public static void RaiseItemPurchased(ShopItemSO item) => ItemPurchased?.Invoke(item);
    public static void RaiseMinionKilled(Faction faction) => MinionKilled?.Invoke(faction);
    public static void RaiseTowerDestroyed(Structure tower, Faction faction) => TowerDestroyed?.Invoke(tower, faction);
    public static void RaisePatronDestroyed(Faction faction) => PatronDestroyed?.Invoke(faction);
    public static void RaiseAbilityUsed(AbilitySO ability) => AbilityUsed?.Invoke(ability);
    public static void RaiseMatchStateChanged(MatchState state) => MatchStateChanged?.Invoke(state);
    public static void RaiseHint(string message) => HintRequested?.Invoke(message);

    /// <summary>
    /// Drops every subscription. Called when a match tears down so a listener that forgot to
    /// unsubscribe can't keep a destroyed object alive into the next match.
    /// </summary>
    public static void ResetAll()
    {
        DamageDealt = null;
        Died = null;
        ShotFired = null;
        SoulDropped = null;
        SoulsCollected = null;
        WalletChanged = null;
        ItemPurchased = null;
        MinionKilled = null;
        TowerDestroyed = null;
        PatronDestroyed = null;
        AbilityUsed = null;
        MatchStateChanged = null;
        HintRequested = null;
    }
}
