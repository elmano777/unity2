using System;
using UnityEngine;

/// <summary>How a soul ended, as Deadlock does it: secured, denied, or left to float away.</summary>
public enum SoulOutcome
{
    /// <summary>The side that killed the minion shot it first: it is paid the value plus a bonus.</summary>
    Secured = 0,

    /// <summary>The rival shot it first: nobody is paid.</summary>
    Denied = 1,

    /// <summary>Nobody shot it: it finished rising and its owner is paid the plain value.</summary>
    Expired = 2,
}

/// <summary>
/// A soul dropped by a dead minion. Like Deadlock's, it floats up and belongs to the side that made
/// the kill. Shoot it as that side to <b>secure</b> it (full value plus a bonus); if the rival shoots
/// it first they <b>deny</b> it and nobody is paid; if nobody does, it reaches the top and its owner
/// is paid the plain value (RF-07). It implements <see cref="IDamageable"/> so the same weapon code
/// that hits minions can hit it.
/// </summary>
public class SoulPickup : MonoBehaviour, IDamageable
{
    /// <summary>Raised on every screen when a soul ends. Arguments: how, which side owned it, where.</summary>
    public static event Action<SoulOutcome, Faction, Vector3> Resolved;

    [Header("Value")]
    [Tooltip("Souls the owner is paid for this pickup.")]
    [SerializeField] private int value = 10;

    [Tooltip("Extra share of the value paid on top when the owner secures it by shooting it.")]
    [SerializeField] private float secureBonus = 0.25f;

    [Header("Floating")]
    [Tooltip("Seconds the soul takes to float up. If nobody shoots it by then, its owner is paid.")]
    [SerializeField] private float riseSeconds = 7f;

    [Tooltip("Metres it floats up. Kept low enough to aim at without looking at the sky.")]
    [SerializeField] private float riseHeight = 4f;

    [Tooltip("Seconds of shrinking before it ends, as a warning that it is about to expire.")]
    [SerializeField] private float fadeDuration = 1.5f;

    [Header("Aiming")]
    [Tooltip("Radius in metres of the sphere a shot has to hit. Larger than the visible orb, which is small and far away in VR.")]
    [SerializeField] private float hitRadius = 0.5f;

    [Header("Motion")]
    [Tooltip("How far the soul bobs up and down, in metres.")]
    [SerializeField] private float bobAmplitude = 0.12f;

    [SerializeField] private float bobSpeed = 1.6f;
    [SerializeField] private float spinDegreesPerSecond = 90f;

    [Header("Feedback")]
    [Tooltip("Played at the soul's position when it is secured.")]
    [SerializeField] private AudioClip collectClip;

    [SerializeField] private GameObject collectEffect;

    private Vector3 anchor;
    private float age;
    private bool collected;
    private bool claimSent;
    private bool autoResolved;
    private Vector3 baseScale;
    private NetworkSoul network;
    private Faction localOwner = Faction.Player;

    /// <summary>Anyone's shot can reach a soul; whose it is only decides what the shot does.</summary>
    public Faction Faction => Faction.Neutral;

    public bool IsAlive => !collected;

    /// <summary>Souls this pickup is worth when nobody shoots it.</summary>
    public int Value => value;

    /// <summary>Extra souls paid on top when the owner secures it.</summary>
    public int BonusValue => Mathf.Max(1, Mathf.RoundToInt(value * secureBonus));

    /// <summary>What the owner is paid for securing it.</summary>
    public int SecuredValue => value + BonusValue;

    /// <summary>The side that killed the minion this soul came from.</summary>
    public Faction Owner => network != null && network.IsSpawned ? network.OwnerFaction(localOwner) : localOwner;

    /// <summary>The side recorded locally, which the network layer copies to the other screen.</summary>
    public Faction LocalOwner => localOwner;

    private void Awake()
    {
        anchor = transform.position;
        baseScale = transform.localScale;
        network = GetComponent<NetworkSoul>();

        // Grow only the shootable sphere; the orb keeps its size.
        if (TryGetComponent(out SphereCollider sphere))
        {
            Vector3 scale = transform.lossyScale;
            float largest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            if (largest > 0f)
            {
                sphere.radius = Mathf.Max(sphere.radius, hitRadius / largest);
            }
        }
    }

    /// <summary>Re-reads where the soul floats, once the network has placed it.</summary>
    public void ResetAnchor()
    {
        anchor = transform.position;
    }

    /// <summary>Overrides how many souls this pickup is worth, set by the minion that dropped it.</summary>
    public void SetValue(int souls)
    {
        value = Mathf.Max(1, souls);
    }

    /// <summary>Sets which side the soul belongs to, set by the minion that dropped it.</summary>
    public void SetOwner(Faction owner)
    {
        localOwner = owner;

        if (network != null)
        {
            network.PublishOwner();
        }
    }

    private void Update()
    {
        if (collected)
        {
            return;
        }

        age += Time.deltaTime;

        // Float up (eased), then bob and spin so it reads as a pickup from across the lane. Every screen
        // computes this from the same drop point and age, so it needs no syncing.
        float rise = riseSeconds > 0f ? 1f - Mathf.Pow(1f - Mathf.Clamp01(age / riseSeconds), 2f) : 1f;
        float offset = Mathf.Sin(age * bobSpeed * Mathf.PI) * bobAmplitude;
        transform.position = anchor + Vector3.up * (rise * riseHeight + offset);
        transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);

        float remaining = riseSeconds - age;

        if (remaining <= 0f)
        {
            if (!autoResolved)
            {
                autoResolved = true;
                ResolveByTime();
            }

            return;
        }

        // Shrink during the last moments instead of vanishing without warning.
        if (remaining < fadeDuration && fadeDuration > 0f)
        {
            transform.localScale = baseScale * Mathf.Clamp01(remaining / fadeDuration);
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (collected)
        {
            return;
        }

        // Only a ritualist farms or denies souls (either side); a stray minion shot shouldn't touch them.
        if (info.source == Faction.Neutral)
        {
            return;
        }

        if (network != null && network.IsSpawned)
        {
            // In a duel the host decides who got there first, so both players can't be paid for the same soul.
            if (!claimSent)
            {
                claimSent = true;
                network.Claim();
            }

            return;
        }

        SecureOrDeny(info.source);
    }

    /// <summary>Offline: the shooter secures it if it is theirs, denies it if it is the rival's.</summary>
    private void SecureOrDeny(Faction shooter)
    {
        collected = true;

        if (shooter == Owner)
        {
            Pay(SecuredValue, Owner);
            PlayCollected(SoulOutcome.Secured);
        }
        else
        {
            PlayCollected(SoulOutcome.Denied);
        }

        Destroy(gameObject);
    }

    /// <summary>Nobody shot it: it reached the top and its owner is paid the plain value.</summary>
    private void ResolveByTime()
    {
        if (network != null && network.IsSpawned)
        {
            // Online the host pays and tells both screens; the other screen waits for it.
            if (network.IsServer)
            {
                network.AutoAward();
            }

            return;
        }

        collected = true;
        Pay(value, Owner);
        PlayCollected(SoulOutcome.Expired);
        Destroy(gameObject);
    }

    /// <summary>Pays the wallet only when the owner is this machine's player.</summary>
    private static void Pay(int amount, Faction owner)
    {
        if (owner != MatchNet.LocalFaction)
        {
            return;
        }

        if (PlayerWallet.Instance != null)
        {
            PlayerWallet.Instance.Add(amount);
        }
        else
        {
            // Still report it so the stats stay correct in scenes without a wallet (the practice).
            GameEvents.RaiseSoulsCollected(amount);
        }
    }

    /// <summary>Marks the soul as ended and plays its sound and spark. Safe to call once per screen.</summary>
    public void PlayCollected(SoulOutcome outcome = SoulOutcome.Secured)
    {
        collected = true;

        if (outcome == SoulOutcome.Secured)
        {
            if (collectClip != null)
            {
                AudioSource.PlayClipAtPoint(collectClip, transform.position);
            }

            if (collectEffect != null)
            {
                Instantiate(collectEffect, transform.position, Quaternion.identity);
            }
        }

        Resolved?.Invoke(outcome, Owner, transform.position);

        // Gone from view at once; the host removes the networked object a moment later.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }
    }
}
