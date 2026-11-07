using UnityEngine;

/// <summary>
/// A soul dropped by a dead minion. Collected by shooting it (RF-07), which is why it implements
/// <see cref="IDamageable"/> instead of using a trigger volume: the player aims at it deliberately
/// rather than walking over it, and the same weapon code handles minions and souls alike.
/// </summary>
public class SoulPickup : MonoBehaviour, IDamageable
{
    [Header("Value")]
    [Tooltip("Souls added to the wallet when this pickup is shot.")]
    [SerializeField] private int value = 10;

    [Header("Lifetime")]
    [Tooltip("Seconds before an uncollected soul fades away, so the lane doesn't fill up.")]
    [SerializeField] private float lifetime = 12f;

    [Tooltip("Seconds of shrinking before it disappears, as a warning that it is about to expire.")]
    [SerializeField] private float fadeDuration = 1.5f;

    [Header("Motion")]
    [Tooltip("How far the soul bobs up and down, in metres.")]
    [SerializeField] private float bobAmplitude = 0.12f;

    [SerializeField] private float bobSpeed = 1.6f;
    [SerializeField] private float spinDegreesPerSecond = 90f;

    [Header("Feedback")]
    [Tooltip("Played at the soul's position when it is collected.")]
    [SerializeField] private AudioClip collectClip;

    [SerializeField] private GameObject collectEffect;

    private Vector3 anchor;
    private float age;
    private bool collected;
    private bool claimSent;
    private Vector3 baseScale;
    private NetworkSoul network;

    /// <summary>Souls belong to nobody, so any shot can claim them.</summary>
    public Faction Faction => Faction.Neutral;

    public bool IsAlive => !collected;

    /// <summary>Souls this pickup is worth.</summary>
    public int Value => value;

    private void Awake()
    {
        anchor = transform.position;
        baseScale = transform.localScale;
        network = GetComponent<NetworkSoul>();
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

    private void Update()
    {
        if (collected)
        {
            return;
        }

        age += Time.deltaTime;

        // Bob and spin so it reads as a pickup from across the lane.
        float offset = Mathf.Sin(age * bobSpeed * Mathf.PI) * bobAmplitude;
        transform.position = anchor + Vector3.up * offset;
        transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);

        float remaining = lifetime - age;
        if (remaining <= 0f)
        {
            // The rival's copy keeps its shrunken soul until the host removes the real one.
            if (!IsMirror)
            {
                Destroy(gameObject);
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

        // Only a ritualist farms souls (either side); a stray minion shot shouldn't consume them.
        if (info.source == Faction.Neutral)
        {
            return;
        }

        if (network != null && network.IsSpawned)
        {
            // In a duel the host decides who got it, so both players can't be paid for the same soul.
            if (!claimSent)
            {
                claimSent = true;
                network.Claim();
            }

            return;
        }

        Collect();
    }

    /// <summary>True on the joining player's copy of a soul the host created.</summary>
    private bool IsMirror => network != null && network.IsSpawned && !network.IsServer;

    /// <summary>
    /// Offline path: pays the wallet and removes the soul. Online, the host pays the winner of the
    /// claim through <see cref="NetworkSoul"/> and every screen then calls <see cref="PlayCollected"/>.
    /// </summary>
    private void Collect()
    {
        collected = true;

        if (PlayerWallet.Instance != null)
        {
            PlayerWallet.Instance.Add(value);
        }
        else
        {
            // Still report it so the stats stay correct in scenes without a wallet (the practice).
            GameEvents.RaiseSoulsCollected(value);
        }

        PlayCollected();
        Destroy(gameObject);
    }

    /// <summary>Marks the soul as taken and plays the pickup sound and spark. Safe to call once per screen.</summary>
    public void PlayCollected()
    {
        collected = true;

        if (collectClip != null)
        {
            AudioSource.PlayClipAtPoint(collectClip, transform.position);
        }

        if (collectEffect != null)
        {
            Instantiate(collectEffect, transform.position, Quaternion.identity);
        }

        // Gone from view at once; the host removes the networked object a moment later.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }
    }
}
