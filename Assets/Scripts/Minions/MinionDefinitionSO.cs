using UnityEngine;

/// <summary>
/// What job a minion does in the wave. Drives which branch of <see cref="MinionAgent"/> runs,
/// so the three roles share one agent instead of needing three near-identical scripts.
/// </summary>
public enum MinionRole
{
    /// <summary>Ranged line infantry. The bulk of the wave and the main source of souls.</summary>
    Trooper = 0,

    /// <summary>Short range, tougher, hits harder. Walks at the front of the wave.</summary>
    Melee = 1,

    /// <summary>Heals damaged allies instead of attacking. Fragile, worth the most souls.</summary>
    Medic = 2,
}

/// <summary>
/// Stats for one kind of minion, kept as data so the lane can be rebalanced from the inspector
/// during playtests without touching code. Create via Assets > Create > Deadlock > Minion Definition.
/// </summary>
[CreateAssetMenu(fileName = "NewMinion", menuName = "Deadlock/Minion Definition", order = 10)]
public class MinionDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string minionName = "Trooper";

    [Tooltip("What this minion does in the wave.")]
    public MinionRole role = MinionRole.Trooper;

    [Tooltip("Prefab spawned for this minion. Needs a Health, a MinionAgent and a collider.")]
    public GameObject prefab;

    [Header("Survivability")]
    public float maxHealth = 120f;

    [Header("Movement")]
    [Tooltip("Metres per second while walking the lane.")]
    public float moveSpeed = 2.2f;

    [Tooltip("How close it gets to a waypoint before moving on to the next one.")]
    public float waypointTolerance = 0.6f;

    [Header("Combat")]
    [Tooltip("Ignored by medics, which heal instead of attacking.")]
    public float attackDamage = 12f;

    [Tooltip("Seconds between attacks.")]
    public float attackInterval = 1.1f;

    [Tooltip("How close it must be to attack. Melee minions use a short range, troopers a long one.")]
    public float attackRange = 2.5f;

    [Tooltip("How far it looks for a target while walking.")]
    public float detectionRange = 8f;

    [Tooltip("Distance from its lane position past which it gives up a chase and returns.")]
    public float maxChaseDistance = 10f;

    [Header("Medic")]
    [Tooltip("Hit points restored per pulse. Only used when role is Medic.")]
    public float healAmount = 20f;

    [Tooltip("Seconds between heal pulses.")]
    public float healInterval = 2f;

    [Tooltip("How far the medic can reach an ally to heal it.")]
    public float healRange = 6f;

    [Tooltip("Medics hang back this far behind the minion they are healing, to stay out of the fight.")]
    public float medicStandoff = 3f;

    [Header("Reward")]
    [Tooltip("Souls dropped when this minion dies. This is the main income of the ritual.")]
    public int soulValue = 10;

    [Tooltip("How many separate soul pickups the reward is split into.")]
    [Min(1)]
    public int soulDrops = 1;
}
