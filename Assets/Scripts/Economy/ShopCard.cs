using TMPro;
using UnityEngine;

/// <summary>
/// One item card on a <see cref="ShopBoard"/>: icon, name and a pip per stack owned. It is a neutral
/// target like a soul (minions and towers ignore it), and the player's own bullets "hit" it.
/// </summary>
/// <remarks>
/// Only the first shot of a burst counts: hits closer together than <see cref="burstGap"/> are the
/// same held trigger, so automatic fire selects the item but never confirms the purchase.
/// </remarks>
public class ShopCard : MonoBehaviour, IDamageable
{
    [SerializeField] private ShopBoard board;
    [SerializeField] private int itemIndex;

    [Tooltip("Shots closer together than this are one burst and count once.")]
    [SerializeField] private float burstGap = 0.3f;

    [Header("Looks")]
    [SerializeField] private TMP_Text nameLabel;

    [Tooltip("Filled pip per stack, in order; shown up to the number owned.")]
    [SerializeField] private GameObject[] pips;

    [Tooltip("Grey veil over a card the player can't afford.")]
    [SerializeField] private GameObject dimmed;

    [Tooltip("Frame around the selected card.")]
    [SerializeField] private GameObject selectedFrame;

    [Tooltip("Gold frame once the player owns as many as the item allows.")]
    [SerializeField] private GameObject maxedFrame;

    // Linear values: TextMeshPro's vertex colours skip the sRGB conversion in a linear project.
    [SerializeField] private Color inkColor = new Color(0.033f, 0.016f, 0.007f, 1f);
    [SerializeField] private Color fadedInk = new Color(0.15f, 0.11f, 0.075f, 1f);

    private float lastHit = -10f;
    private float pulseUntil;
    private Vector3 restScale;
    private bool restKnown;

    public int ItemIndex => itemIndex;

    public Faction Faction => Faction.Neutral;

    public bool IsAlive => true;

    /// <summary>
    /// The board updates the cards of closed tabs too, before their Awake has run, so the size at
    /// rest is read on first use instead.
    /// </summary>
    private Vector3 RestScale
    {
        get
        {
            if (!restKnown)
            {
                restScale = transform.localScale;
                restKnown = true;
            }

            return restScale;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        // Only this player's own gun; a stray ability or a minion never touches the shop.
        if (info.type != DamageType.Bullet || info.source != MatchNet.LocalFaction)
        {
            return;
        }

        bool newBurst = Time.time - lastHit >= burstGap;
        lastHit = Time.time;

        if (newBurst && board != null)
        {
            pulseUntil = Time.time + 0.15f;
            board.OnCardShot(itemIndex);
        }
    }

    /// <summary>Updates the card for the board's current state.</summary>
    public void Show(int owned, bool maxed, bool affordable, bool selected, bool armed)
    {
        if (pips != null)
        {
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] != null)
                {
                    pips[i].SetActive(i < owned);
                }
            }
        }

        if (dimmed != null) dimmed.SetActive(!affordable && !maxed);
        if (maxedFrame != null) maxedFrame.SetActive(maxed);
        if (selectedFrame != null) selectedFrame.SetActive(selected);
        if (nameLabel != null) nameLabel.color = affordable || maxed ? inkColor : fadedInk;

        // An armed card stands slightly proud of the board, like a pressed key waiting to be released.
        float lift = armed ? 1.06f : 1f;
        transform.localScale = RestScale * lift;
    }

    private void Update()
    {
        if (Time.time < pulseUntil)
        {
            transform.localScale = RestScale * 1.1f;
        }
    }
}
