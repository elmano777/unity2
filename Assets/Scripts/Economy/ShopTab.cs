using UnityEngine;

/// <summary>
/// A category tab on the side of a <see cref="ShopBoard"/>; shooting it opens that category. The
/// open tab sticks out further and is lit, the others sit back and darker.
/// </summary>
public class ShopTab : MonoBehaviour, IDamageable
{
    [SerializeField] private ShopBoard board;
    [SerializeField] private int tabIndex;

    [Tooltip("Shown while this tab is open.")]
    [SerializeField] private GameObject openLook;

    [Tooltip("Shown while another tab is open.")]
    [SerializeField] private GameObject closedLook;

    [Tooltip("How far the open tab slides out from the board, in local X.")]
    [SerializeField] private float openOffset = -0.06f;

    [SerializeField] private float repeatDelay = 0.3f;

    private Vector3 restPosition;
    private bool restKnown;
    private float nextShot;

    public Faction Faction => Faction.Neutral;

    public bool IsAlive => true;

    // The rest position is read on the first SetOpen, not in Awake: the board opens its first tab
    // from its own OnEnable, which may run before this tab's Awake.

    public void TakeDamage(DamageInfo info)
    {
        if (info.type != DamageType.Bullet || info.source != MatchNet.LocalFaction || Time.time < nextShot)
        {
            return;
        }

        nextShot = Time.time + repeatDelay;

        if (board != null)
        {
            board.ShowTab(tabIndex);
        }
    }

    /// <summary>Shows the tab as open or closed.</summary>
    public void SetOpen(bool open)
    {
        if (!restKnown)
        {
            restPosition = transform.localPosition;
            restKnown = true;
        }

        if (openLook != null) openLook.SetActive(open);
        if (closedLook != null) closedLook.SetActive(!open);
        transform.localPosition = restPosition + new Vector3(open ? openOffset : 0f, 0f, 0f);
    }
}
