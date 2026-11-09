using UnityEngine;

/// <summary>What stat a shop item improves.</summary>
public enum ItemStat
{
    MaxHealth = 0,
    WeaponDamage = 1,
    FireRate = 2,
    MoveSpeed = 3,
    DamageReduction = 4,

    /// <summary>Fraction more weapon damage when the target is under 8 m away.</summary>
    CloseDamage = 5,

    /// <summary>Fraction more weapon damage against minions.</summary>
    MinionDamage = 6,

    /// <summary>Fraction more weapon damage when the target is over 15 m away.</summary>
    LongDamage = 7,

    /// <summary>Health regained per second.</summary>
    HealthRegen = 8,

    /// <summary>Fraction of the damage you deal that comes back as health.</summary>
    Lifesteal = 9,

    /// <summary>Fraction more ability damage.</summary>
    AbilityDamage = 10,

    /// <summary>Fraction more ability radius.</summary>
    AbilityRadius = 11,

    /// <summary>Fraction less ability cooldown.</summary>
    CooldownReduction = 12,
}

/// <summary>Shop tab an item is listed under, each with its own colour on the <see cref="ShopBoard"/>.</summary>
public enum ShopCategory
{
    Weapon = 0,
    Vitality = 1,
    Spirit = 2,
}

/// <summary>
/// One purchasable upgrade, bought with souls (RF-11) on the <see cref="ShopBoard"/>, which lists it
/// under its <see cref="category"/> tab in the block of its <see cref="tier"/>; <see cref="PlayerUpgrades"/>
/// applies it. Create via Assets > Create > Deadlock > Shop Item.
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

    [Tooltip("Tab of the shop board this item is listed under.")]
    public ShopCategory category = ShopCategory.Weapon;

    [Tooltip("Block of its tab, 1 for the cheapest. Items of a tab are grouped by tier.")]
    [Min(1)]
    public int tier = 1;

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
