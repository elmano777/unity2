using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player has bought this ritual and where each upgrade lands (RF-11): damage and fire
/// rate on the gun, speed on the locomotion, health and armour on the body, and the passive bonuses
/// (close, long range, minion damage, ability power, regeneration, lifesteal).
/// </summary>
/// <remarks>
/// Online the body's hit points belong to the host, so health, armour and healing go through
/// <see cref="NetworkPlayer"/>; offline they go straight to the rig's own <see cref="Health"/>.
/// Gun and speed are local either way: hits already carry their damage.
/// On top of the items, the souls invested in each category give a growing bonus, as in Deadlock:
/// weapon damage, ability power or health (see <see cref="InvestmentBonus"/>).
/// </remarks>
public class PlayerUpgrades : MonoBehaviour
{
    public static PlayerUpgrades Instance { get; private set; }

    /// <summary>Souls invested at which the bonus steps up (Deadlock's 800, 1600... scaled to this economy).</summary>
    public static readonly int[] InvestmentSteps = { 200, 400, 600, 800, 1200, 1600, 2000 };

    // Accumulated bonus in percent at each step, per category (Weapon, Vitality, Spirit): the wiki's table.
    private static readonly float[][] InvestmentTable =
    {
        new float[] { 9, 12, 15, 18, 46, 54, 62 },
        new float[] { 9, 12, 15, 20, 38, 42, 46 },
        new float[] { 7, 11, 15, 19, 38, 45, 52 },
    };

    /// <summary>The hero's starting hit points: the vitality investment bonus is a share of it.</summary>
    private float baseBodyHealth = 300f;

    /// <summary>The hero's own regeneration, healed quietly while hurt (shop regen adds to it with its sound).</summary>
    private float heroRegen;
    private float quietHeal;

    private readonly Dictionary<ShopItemSO, int> stacks = new Dictionary<ShopItemSO, int>();
    private readonly int[] invested = new int[3];

    private float closeBonus;
    private float minionBonus;
    private float longBonus;
    private float abilityDamageBonus;
    private float abilityRadiusBonus;
    private float cooldownReduction;
    private float lifesteal;
    private float regenPerSecond;
    private float healthFromInvestment;
    private float pendingHeal;
    private float nextHealFlush;
    private float nextRegen;

    /// <summary>Why the last <see cref="TryBuy"/> failed, for the kiosk to show.</summary>
    public enum Refusal { None, NotEnoughSouls, MaxedOut, NoWallet }

    /// <summary>Raised after any purchase, so a HUD can refresh the investment levels.</summary>
    public event System.Action Changed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        // Levels and ability points live next to the shop's upgrades, on the player's rig.
        if (GetComponent<HeroProgression>() == null)
        {
            gameObject.AddComponent<HeroProgression>();
        }
    }

    /// <summary>
    /// What a level up gives (see <see cref="HeroProgression"/>): hit points, bullet damage and ability
    /// power, the original game's per-level growth.
    /// </summary>
    public void AddLevelBonus(float health, float bulletDamage, float abilityPower)
    {
        if (health > 0f)
        {
            ApplyToBody(ItemStat.MaxHealth, health);
        }

        if (bulletDamage > 0f)
        {
            foreach (Weapon weapon in GetComponentsInChildren<Weapon>(true))
            {
                weapon.AddDamage(bulletDamage);
            }
        }

        abilityDamageBonus += abilityPower;
        Changed?.Invoke();
    }

    /// <summary>Flat damage reduction for a while (positive to add, the same negative to take it off).</summary>
    public void AddTemporaryArmor(float amount) => ApplyToBody(ItemStat.DamageReduction, amount);

    private void OnEnable()
    {
        GameEvents.DamageDealt += OnDamageDealt;
    }

    private void OnDisable()
    {
        GameEvents.DamageDealt -= OnDamageDealt;
    }

    /// <summary>
    /// The hero's own numbers: walking speed, regeneration and, offline, the body's hit points
    /// (online the host sets those when it learns the hero, see <see cref="NetworkPlayer"/>).
    /// </summary>
    private void Start()
    {
        AbilityCaster caster = GetComponentInChildren<AbilityCaster>(true) ?? FindObjectOfType<AbilityCaster>();
        HeroDefinitionSO hero = caster != null ? caster.Hero : null;

        if (hero == null && GameSession.Instance != null)
        {
            hero = GameSession.Instance.SelectedHero;
        }

        if (hero == null)
        {
            return;
        }

        baseBodyHealth = hero.maxHealth;
        heroRegen = hero.healthRegen;

        foreach (ComfortLocomotion locomotion in GetComponentsInChildren<ComfortLocomotion>(true))
        {
            locomotion.BaseSpeed = hero.moveSpeed;
        }

        if (NetworkPlayer.Local == null)
        {
            foreach (Health body in GetComponentsInChildren<Health>(true))
            {
                if (!Mathf.Approximately(body.MaxHealth, hero.maxHealth))
                {
                    body.AddMaxHealth(hero.maxHealth - body.MaxHealth);
                }
            }
        }
    }

    /// <summary>The local player's hit points, or null.</summary>
    private Health LocalBody()
    {
        NetworkPlayer local = NetworkPlayer.Local;

        if (local != null && local.IsSpawned)
        {
            return local.Body;
        }

        return GetComponentInChildren<Health>(true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ---- Modifiers other systems ask for ----

    /// <summary>Damage of one bullet after the passive bonuses, given how far the target is and what it is.</summary>
    public float WeaponDamage(float baseDamage, float distance, Component target)
    {
        float bonus = InvestmentBonus(ShopCategory.Weapon);

        if (distance < 8f) bonus += closeBonus;
        if (distance > 15f) bonus += longBonus;
        if (target != null && target.GetComponentInParent<MinionAgent>() != null) bonus += minionBonus;

        return baseDamage * (1f + bonus);
    }

    /// <summary>Ability damage after item and investment bonuses.</summary>
    public float AbilityDamage(float baseDamage) => baseDamage * (1f + abilityDamageBonus + InvestmentBonus(ShopCategory.Spirit));

    /// <summary>Ability radius after the radius bonus.</summary>
    public float AbilityRadius(float baseRadius) => baseRadius * (1f + abilityRadiusBonus);

    /// <summary>Multiply an ability's cooldown by this (1 = unchanged).</summary>
    public float CooldownMultiplier => Mathf.Clamp(1f - cooldownReduction, 0.4f, 1f);

    // ---- Investment bonuses ----

    /// <summary>Souls spent on items of that category so far.</summary>
    public int Invested(ShopCategory category) => invested[(int)category];

    /// <summary>Bonus from the souls invested in that category, as a fraction (0.09 = +9 %).</summary>
    public float InvestmentBonus(ShopCategory category)
    {
        int spent = invested[(int)category];
        float[] table = InvestmentTable[(int)category];
        float percent = 0f;

        for (int i = 0; i < InvestmentSteps.Length; i++)
        {
            if (spent >= InvestmentSteps[i])
            {
                percent = table[i];
            }
        }

        return percent / 100f;
    }

    /// <summary>Souls at which the bonus of that category grows next, or 0 when it is at its top.</summary>
    public int NextStep(ShopCategory category)
    {
        int spent = invested[(int)category];

        foreach (int step in InvestmentSteps)
        {
            if (spent < step)
            {
                return step;
            }
        }

        return 0;
    }

    // ---- Shop ----

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

        float bonusBefore = InvestmentBonus(item.category);

        stacks[item] = Stacks(item) + 1;
        invested[(int)item.category] += item.cost;

        // Reaching the next investment step is marked with a pip, as in Deadlock's shop.
        if (InvestmentBonus(item.category) > bonusBefore)
        {
            ExperienceCues.Play("shop_tier_pip", 0.6f);
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.4f, 0.1f);
        }
        Apply(item.stat, item.delta);
        ApplyInvestedHealth();
        Changed?.Invoke();
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

            case ItemStat.CloseDamage: closeBonus += delta; break;
            case ItemStat.MinionDamage: minionBonus += delta; break;
            case ItemStat.LongDamage: longBonus += delta; break;
            case ItemStat.AbilityDamage: abilityDamageBonus += delta; break;
            case ItemStat.AbilityRadius: abilityRadiusBonus += delta; break;
            case ItemStat.CooldownReduction: cooldownReduction += delta; break;
            case ItemStat.HealthRegen: regenPerSecond += delta; break;
            case ItemStat.Lifesteal: lifesteal += delta; break;
        }
    }

    /// <summary>The health investment bonus is a percentage of the base body health, granted as it grows.</summary>
    private void ApplyInvestedHealth()
    {
        float target = InvestmentBonus(ShopCategory.Vitality) * baseBodyHealth;
        float extra = target - healthFromInvestment;

        if (extra >= 1f)
        {
            healthFromInvestment += extra;
            ApplyToBody(ItemStat.MaxHealth, extra);
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

    // ---- Regeneration and lifesteal ----

    private void OnDamageDealt(IDamageable target, DamageInfo info)
    {
        if (lifesteal <= 0f || info.instigator == null || info.source != MatchNet.LocalFaction)
        {
            return;
        }

        if (info.type == DamageType.Bullet || info.type == DamageType.Ability)
        {
            pendingHeal += info.amount * lifesteal;
        }
    }

    private void Update()
    {
        if ((regenPerSecond > 0f || heroRegen > 0f) && Time.time >= nextRegen)
        {
            nextRegen = Time.time + 1f;
            Health body = LocalBody();
            bool hurt = body != null && body.IsAlive && body.CurrentHealth < body.MaxHealth - 0.5f;

            if (hurt)
            {
                pendingHeal += regenPerSecond;
                quietHeal += heroRegen;
            }
        }

        // The hero's own regeneration is silent; sent every couple of seconds.
        if (quietHeal >= 2f)
        {
            HealBody(quietHeal);
            quietHeal = 0f;
        }

        // Healing is sent in small batches rather than once per bullet.
        if (pendingHeal >= 1f && Time.time >= nextHealFlush)
        {
            nextHealFlush = Time.time + 0.5f;
            float amount = Mathf.Min(pendingHeal, 40f);
            pendingHeal -= amount;
            HealBody(amount);
            ExperienceCues.Play("heal_siphon", 0.25f);
        }
    }

    private void HealBody(float amount)
    {
        NetworkPlayer local = NetworkPlayer.Local;

        if (local != null && local.IsSpawned)
        {
            local.RequestBodyHeal(amount);
            return;
        }

        foreach (Health body in GetComponentsInChildren<Health>(true))
        {
            body.Heal(amount);
        }
    }
}
