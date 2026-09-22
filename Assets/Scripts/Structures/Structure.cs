using UnityEngine;

/// <summary>
/// Base for the things that hold ground in the lane: towers and, later, the patron core.
/// Wraps a <see cref="Health"/> and announces its destruction on <see cref="GameEvents"/>, which
/// is what drives the match's win condition and the "towers destroyed" stat (RF-12).
/// </summary>
[RequireComponent(typeof(Health))]
public class Structure : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private Faction faction = Faction.Enemy;

    [Tooltip("Position in the lane, 1 for the first tower. Higher tiers get more health and damage.")]
    [SerializeField] private int tier = 1;

    [Header("Scaling")]
    [Tooltip("Health of a tier 1 structure. Each further tier multiplies this.")]
    [SerializeField] private float baseHealth = 800f;

    [Tooltip("Health multiplier applied per tier above 1, compounding.")]
    [SerializeField] private float healthPerTier = 1.5f;

    [Header("Presentation")]
    [Tooltip("Renderers tinted when the structure is destroyed, to read as rubble.")]
    [SerializeField] private Renderer[] renderersToDarken;

    [SerializeField] private Color destroyedTint = new Color(0.25f, 0.22f, 0.2f, 1f);

    [Tooltip("Spawned at the structure's position when it falls.")]
    [SerializeField] private GameObject destructionEffect;

    [SerializeField] private AudioClip destructionClip;

    private Health health;

    /// <summary>Side this structure defends.</summary>
    public Faction Faction => faction;

    /// <summary>Position in the lane sequence, 1 being the closest to the player.</summary>
    public int Tier => tier;

    /// <summary>False once the structure has been destroyed.</summary>
    public bool IsStanding => health != null && health.IsAlive;

    /// <summary>Health component, exposed so a HUD can bind a bar to it.</summary>
    public Health Health => health;

    protected virtual void Awake()
    {
        health = GetComponent<Health>();
        health.Configure(faction, ScaledHealth());
        health.Killed += OnKilled;
    }

    protected virtual void OnDestroy()
    {
        if (health != null)
        {
            health.Killed -= OnKilled;
        }
    }

    /// <summary>Health for this structure's tier, compounding <see cref="healthPerTier"/>.</summary>
    protected float ScaledHealth()
    {
        return baseHealth * Mathf.Pow(healthPerTier, Mathf.Max(0, tier - 1));
    }

    /// <summary>
    /// Hook for subclasses. <see cref="Tower"/> uses it to stop shooting once it falls.
    /// </summary>
    protected virtual void OnDestroyed(DamageInfo info)
    {
    }

    private void OnKilled(DamageInfo info)
    {
        OnDestroyed(info);

        foreach (Renderer r in renderersToDarken)
        {
            if (r != null && r.material != null)
            {
                r.material.color = destroyedTint;
            }
        }

        if (destructionEffect != null)
        {
            Instantiate(destructionEffect, transform.position, Quaternion.identity);
        }

        if (destructionClip != null)
        {
            AudioSource.PlayClipAtPoint(destructionClip, transform.position);
        }

        GameEvents.RaiseTowerDestroyed(this, faction);
    }
}
