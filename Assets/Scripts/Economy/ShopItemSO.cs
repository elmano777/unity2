using UnityEngine;

/// <summary>What stat a shop item improves.</summary>
public enum ItemStat
{
    MaxHealth = 0,
    WeaponDamage = 1,
    FireRate = 2,
    MoveSpeed = 3,
    DamageReduction = 4,
}

/// <summary>
/// One purchasable upgrade, bought with souls (RF-11). Data only for now — the shop kiosk and the
/// component that applies these live in a later deliverable; this exists so the stat events and
/// the hero/minion data all sit together and the balance can be filled in during playtests.
/// Create via Assets > Create > Deadlock > Shop Item.
/// </summary>
[CreateAssetMenu(fileName = "NewShopItem", menuName = "Deadlock/Shop Item", order = 20)]
public class ShopItemSO : ScriptableObject
{
    [Header("Presentation")]
    public string itemName = "New Item";

    [TextArea(2, 4)]
    [Tooltip("One short line shown on the kiosk button. Keep it readable at arm's length in VR.")]
    public string description;

    public Sprite icon;

    [Header("Cost")]
    [Tooltip("Souls charged when bought.")]
    [Min(0)]
    public int cost = 500;

    [Tooltip("How many times one player can buy this in a single ritual. 0 means unlimited.")]
    [Min(0)]
    public int maxStacks = 1;

    [Header("Effect")]
    public ItemStat stat = ItemStat.WeaponDamage;

    [Tooltip("Amount added to the stat. Flat for health and damage, a multiplier for fire rate and speed.")]
    public float delta = 10f;
}
