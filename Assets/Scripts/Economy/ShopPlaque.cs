using UnityEngine;

/// <summary>
/// One item plaque on a <see cref="ShopKiosk"/>. It is a neutral target like a soul: the player's
/// shot "hits" it and that buys the item. Minions and towers ignore neutral targets, and a held
/// trigger buys only once per <see cref="repeatDelay"/>.
/// </summary>
public class ShopPlaque : MonoBehaviour, IDamageable
{
    [SerializeField] private ShopKiosk kiosk;
    [SerializeField] private int itemIndex;

    [Tooltip("Seconds before the same plaque accepts another purchase, so automatic fire doesn't buy a stack at once.")]
    [SerializeField] private float repeatDelay = 0.8f;

    private float nextBuyTime;

    public Faction Faction => Faction.Neutral;

    public bool IsAlive => true;

    public void TakeDamage(DamageInfo info)
    {
        // Only this player's own gun buys; a stray ability or a minion never does.
        if (info.type != DamageType.Bullet || info.source != MatchNet.LocalFaction || Time.time < nextBuyTime)
        {
            return;
        }

        nextBuyTime = Time.time + repeatDelay;

        if (kiosk != null)
        {
            kiosk.Buy(itemIndex);
        }
    }
}
