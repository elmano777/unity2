using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for the things that hold ground in the lane: towers, the Walker, the shrines and the patron.
/// Wraps a <see cref="Health"/> and announces its destruction on <see cref="GameEvents"/>, which
/// is what drives the match's win condition and the "towers destroyed" stat (RF-12).
/// </summary>
/// <remarks>
/// Objectives fall in sequence: while a structure of the same side with a lower tier still stands,
/// this one is shielded and takes no damage. The host decides that and <see cref="NetworkStructure"/>
/// tells the rival's copy, so both screens show the same shields.
/// </remarks>
[RequireComponent(typeof(Health))]
public class Structure : MonoBehaviour
{
    private static readonly List<Structure> standing = new List<Structure>();

    [Header("Identity")]
    [SerializeField] private Faction faction = Faction.Enemy;

    [Tooltip("Position in the lane, 1 for the first tower. Higher tiers get more health and damage, and stay shielded until every lower tier of their side has fallen.")]
    [SerializeField] private int tier = 1;

    [Tooltip("Name shown on the HUD as the next objective, e.g. \"Walker\".")]
    [SerializeField] private string displayName = "Guardian";

    [Tooltip("The patron: its fall ends the ritual instead of counting as a tower.")]
    [SerializeField] private bool isPatron;

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

    [Tooltip("Shown while the structure is shielded because an earlier one of its side still stands.")]
    [SerializeField] private GameObject shield;

    private Health health;
    private bool replicaLocked;
    private bool hasReplicaState;

    /// <summary>Side this structure defends.</summary>
    public Faction Faction => faction;

    /// <summary>Position in the lane sequence, 1 being the closest to the middle of the lane.</summary>
    public int Tier => tier;

    /// <summary>Name for the HUD.</summary>
    public string DisplayName => displayName;

    /// <summary>True for the patron, whose fall ends the ritual.</summary>
    public bool IsPatron => isPatron;

    /// <summary>False once the structure has been destroyed.</summary>
    public bool IsStanding => health != null && health.IsAlive;

    /// <summary>Health component, exposed so a HUD can bind a bar to it.</summary>
    public Health Health => health;

    /// <summary>
    /// True while an earlier structure of the same side still stands, so this one can't be hurt
    /// yet. The rival's copy shows what the host decided.
    /// </summary>
    public bool IsLocked => hasReplicaState ? replicaLocked : HasStandingPredecessor();

    /// <summary>Every structure in the lane that has not fallen yet, both sides.</summary>
    public static IReadOnlyList<Structure> Standing => standing;

    protected virtual void Awake()
    {
        health = GetComponent<Health>();
        health.Configure(faction, ScaledHealth());
        health.Killed += OnKilled;
    }

    protected virtual void OnEnable()
    {
        if (!standing.Contains(this))
        {
            standing.Add(this);
        }
    }

    protected virtual void OnDisable()
    {
        standing.Remove(this);
    }

    protected virtual void OnDestroy()
    {
        if (health != null)
        {
            health.Killed -= OnKilled;
        }
    }

    /// <summary>
    /// Sets side and tier at runtime, for towers the lane creates when the duel starts rather than
    /// ones placed in the scene. Call it before the tower is spawned on the network.
    /// </summary>
    public void Setup(Faction newFaction, int newTier, string newDisplayName = null)
    {
        faction = newFaction;
        tier = Mathf.Max(1, newTier);

        if (!string.IsNullOrEmpty(newDisplayName))
        {
            displayName = newDisplayName;
        }

        health.Configure(faction, ScaledHealth());
    }

    /// <summary>
    /// The rival's copy: takes side, tier, name and shield as the host has them. The copy never
    /// decides any of them; its prefab defaults would be wrong for half the structures.
    /// </summary>
    public void ApplyReplicaState(Faction newFaction, int newTier, string newDisplayName, bool locked)
    {
        faction = newFaction;
        tier = Mathf.Max(1, newTier);
        replicaLocked = locked;
        hasReplicaState = true;

        if (!string.IsNullOrEmpty(newDisplayName))
        {
            displayName = newDisplayName;
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

    /// <summary>
    /// The lowest-tier structure of <paramref name="side"/> still standing: what the other side has
    /// to destroy next. Null once the patron has fallen.
    /// </summary>
    public static Structure NextObjective(Faction side)
    {
        Structure next = null;

        foreach (Structure s in standing)
        {
            if (s.faction == side && s.IsStanding && (next == null || s.tier < next.tier))
            {
                next = s;
            }
        }

        return next;
    }

    private bool HasStandingPredecessor()
    {
        foreach (Structure other in standing)
        {
            if (other != this && other.faction == faction && other.tier < tier && other.IsStanding)
            {
                return true;
            }
        }

        return false;
    }

    // Late so towers have run their own Update; the shield only has to be right by the next hit.
    private void LateUpdate()
    {
        bool locked = IsStanding && IsLocked;

        // Only the deciding machine's flag matters for damage: a replica forwards hits to the host,
        // which ignores them while its own copy is shielded.
        health.Invulnerable = locked;

        if (shield != null && shield.activeSelf != locked)
        {
            shield.SetActive(locked);
        }
    }

    private void OnKilled(DamageInfo info)
    {
        standing.Remove(this);
        OnDestroyed(info);

        if (shield != null)
        {
            shield.SetActive(false);
        }

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

        if (isPatron)
        {
            GameEvents.RaisePatronDestroyed(faction);
        }
        else
        {
            GameEvents.RaiseTowerDestroyed(this, faction);
        }
    }
}
