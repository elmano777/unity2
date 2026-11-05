using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player has bought this ritual and where each upgrade lands (RF-11): damage and fire
/// rate on the gun, speed on the locomotion, health and armour on the body.
/// </summary>
/// <remarks>
/// Online the body's hit points belong to the host, so health and armour go through
/// <see cref="NetworkPlayer.RequestBodyUpgrade"/>; offline they go straight to the rig's own
/// <see cref="Health"/>. Gun and speed are local either way: hits already carry their damage.
/// </remarks>
public class PlayerUpgrades : MonoBehaviour
{
    public static PlayerUpgrades Instance { get; private set; }

    private readonly Dictionary<ShopItemSO, int> stacks = new Dictionary<ShopItemSO, int>();

    /// <summary>Why the last <see cref="TryBuy"/> failed, for the kiosk to show.</summary>
    public enum Refusal { None, NotEnoughSouls, MaxedOut, NoWallet }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>How many of <paramref name="item"/> the player owns.</summary>
    public int Stacks(ShopItemSO item) => item != null && stacks.TryGetValue(item, out int count) ? count : 0;

    /// <summary>True once the player owns as many as the item allows.</summary>
    public bool IsMaxed(ShopItemSO item) => item.maxStacks > 0 && Stacks(item) >= item.maxStacks;

    /// <summary>Charges the souls and applies the item, or says why not.</summary>
    public bool TryBuy(ShopItemSO item, out Refusal refusal)
    {
        refusal = Refusal.None;
        PlayerWallet wallet = PlayerWallet.Instance;

        if (item == null || wallet == null)
        {
            refusal = Refusal.NoWallet;
            return false;
        }

        if (IsMaxed(item))
        {
            refusal = Refusal.MaxedOut;
            return false;
        }

        if (!wallet.TrySpend(item.cost))
        {
            refusal = Refusal.NotEnoughSouls;
            return false;
        }

        stacks[item] = Stacks(item) + 1;
        Apply(item.stat, item.delta);
        GameEvents.RaiseItemPurchased(item);
        MatchStatsRecorder.LogEvent("item_purchased", item.itemName);
        return true;
    }

    private void Apply(ItemStat stat, float delta)
    {
        switch (stat)
        {
            case ItemStat.WeaponDamage:
                foreach (Weapon weapon in GetComponentsInChildren<Weapon>(true))
                {
                    weapon.AddDamage(delta);
                }
                break;

            case ItemStat.FireRate:
                foreach (Weapon weapon in GetComponentsInChildren<Weapon>(true))
                {
                    weapon.ScaleFireInterval(delta);
                }
                break;

            case ItemStat.MoveSpeed:
                foreach (ComfortLocomotion locomotion in GetComponentsInChildren<ComfortLocomotion>(true))
                {
                    locomotion.SpeedMultiplier *= delta;
                }
                break;

            case ItemStat.MaxHealth:
            case ItemStat.DamageReduction:
                ApplyToBody(stat, delta);
                break;
        }
    }

    private void ApplyToBody(ItemStat stat, float delta)
    {
        NetworkPlayer local = NetworkPlayer.Local;

        if (local != null && local.IsSpawned)
        {
            local.RequestBodyUpgrade(stat, delta);
            return;
        }

        foreach (Health body in GetComponentsInChildren<Health>(true))
        {
            if (stat == ItemStat.MaxHealth)
            {
                body.AddMaxHealth(delta);
            }
            else
            {
                body.AddDamageReduction(delta);
            }
        }
    }
}
