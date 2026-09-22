using System;
using UnityEngine;

/// <summary>
/// Hit points for anything that can be shot. Attach next to a collider and the object becomes a
/// valid target for weapons, abilities, minions and towers through <see cref="IDamageable"/>.
/// </summary>
/// <remarks>
/// Deliberately free of XR and networking references: this is plain gameplay logic so it ports
/// to Unity 2022 unchanged, and so an authoritative host can drive it later without a rewrite.
/// </remarks>
public class Health : MonoBehaviour, IDamageable
{
    [Header("Identity")]
    [Tooltip("Side this target fights for. Attackers of the same faction are ignored.")]
    [SerializeField] private Faction faction = Faction.Neutral;

    [Header("Hit points")]
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("Flat damage subtracted from every hit, floored at 1. Raised by shop armour items.")]
    [SerializeField] private float damageReduction;

    [Tooltip("While true the target takes no damage (used by the patron until its towers fall).")]
    [SerializeField] private bool invulnerable;

    [Header("Death")]
    [Tooltip("Seconds the object stays in the scene after dying, to let VFX and sound finish.")]
    [SerializeField] private float destroyDelay = 2f;

    [Tooltip("Leave off for objects a pool or spawner owns, so they aren't destroyed twice.")]
    [SerializeField] private bool destroyOnDeath = true;

    private float currentHealth;

    /// <summary>Raised on every hit that got through. Arguments: current, max.</summary>
    public event Action<float, float> HealthChanged;

    /// <summary>Raised once, the moment this target dies.</summary>
    public event Action<DamageInfo> Killed;

    public Faction Faction => faction;

    public bool IsAlive { get; private set; } = true;

    public float CurrentHealth => currentHealth;

    public float MaxHealth => maxHealth;

    /// <summary>Fraction of health left, 0..1. Safe to read for health bars.</summary>
    public float Normalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    /// <summary>Turns damage immunity on or off — the patron uses this while its towers stand.</summary>
    public bool Invulnerable
    {
        get => invulnerable;
        set => invulnerable = value;
    }

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        // Pooled objects come back to life here rather than in Awake.
        IsAlive = true;
        currentHealth = maxHealth;
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>
    /// Re-rolls this target's hit points at runtime, for minions whose stats come from a
    /// <see cref="MinionDefinitionSO"/> or towers that scale with their position in the lane.
    /// </summary>
    public void Configure(Faction newFaction, float newMaxHealth)
    {
        faction = newFaction;
        maxHealth = Mathf.Max(1f, newMaxHealth);
        currentHealth = maxHealth;
        IsAlive = true;
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(DamageInfo info)
    {
        if (!IsAlive || invulnerable)
        {
            return;
        }

        if (!FactionUtility.IsHostile(info.source, faction))
        {
            return;
        }

        float applied = Mathf.Max(1f, info.amount - damageReduction);
        currentHealth -= applied;

        GameEvents.RaiseDamageDealt(this, info);
        HealthChanged?.Invoke(Mathf.Max(0f, currentHealth), maxHealth);

        if (currentHealth <= 0f)
        {
            Die(info);
        }
    }

    /// <summary>Restores hit points without going over the maximum. Used by shop items.</summary>
    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>Raises the ceiling and heals by the same amount, so a health item feels immediate.</summary>
    public void AddMaxHealth(float amount)
    {
        maxHealth = Mathf.Max(1f, maxHealth + amount);
        currentHealth = Mathf.Min(maxHealth, currentHealth + Mathf.Max(0f, amount));
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>Adds flat damage reduction, applied before the 1-damage floor.</summary>
    public void AddDamageReduction(float amount)
    {
        damageReduction = Mathf.Max(0f, damageReduction + amount);
    }

    private void Die(DamageInfo info)
    {
        IsAlive = false;
        currentHealth = 0f;

        Killed?.Invoke(info);
        GameEvents.RaiseDied(gameObject, info);

        if (destroyOnDeath)
        {
            Destroy(gameObject, destroyDelay);
        }
    }
}
