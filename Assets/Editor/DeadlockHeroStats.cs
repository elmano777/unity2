using UnityEditor;
using UnityEngine;

/// <summary>
/// Step 31: each hero's numbers, taken from the original game's wiki and scaled to this 1v1 VR lane
/// (about 0.4x health, our own DPS level, distances ~0.6x). What is kept is how the two heroes
/// differ: Seven is a little frailer and slower, fires three-round bursts from a bigger magazine and
/// loses damage sooner with distance; Vindicta is faster, regenerates more, hits harder per bullet,
/// reaches further and reloads slower. Their abilities get the original behaviour: Seven's lightning
/// ball travels and slows, Vindicta's stake tethers, and her Assassinate charges while held.
/// Safe to re-run.
/// </summary>
public static class DeadlockHeroStats
{
    private const string SevenHero = "Assets/Data/Heroes/Seven.asset";
    private const string VindictaHero = "Assets/Data/Heroes/Vindicta.asset";
    private const string SevenWeapon = "Assets/Prefabs/Weapons/Seven_Weapon.prefab";
    private const string VindictaWeapon = "Assets/Prefabs/Weapons/Vindicta_Weapon.prefab";

    [MenuItem("Deadlock/Setup/31 Hero Stats (wiki, scaled)")]
    public static void Run()
    {
        // Wiki: Seven 730 hp, 1 regen, 6.7 m/s; Vindicta 755 hp, 2 regen, 7.9 m/s.
        // Per level (wiki per boon, scaled like the base stats and x1.5 as there are 10 levels, not ~35):
        // Seven +41 hp / +0.24 bullet; Vindicta +28 hp / +0.495 bullet; both +1.1 spirit power.
        Hero(SevenHero, maxHealth: 290f, regen: 1f, speed: 3.75f, healthPerLevel: 24f, bulletPerLevel: 0.7f, powerPerLevel: 0.02f);
        Hero(VindictaHero, maxHealth: 300f, regen: 2f, speed: 4.1f, healthPerLevel: 17f, bulletPerLevel: 1.5f, powerPerLevel: 0.02f);

        // Wiki: Seven 3-round bursts, 10.8 dmg, 63 DPS, 29 rounds, 2.35 s, falloff 20-58 m.
        //       Vindicta automatic, 12.3 dmg, 53 DPS, 19 rounds, 2.91 s, falloff 20-64 m.
        Gun(SevenWeapon, damage: 22f, interval: 0.5f, burst: 3, spacing: 0.07f, magazine: 30, reload: 2.35f,
            falloffStart: 12f, falloffEnd: 35f, falloffMin: 0.45f);
        Gun(VindictaWeapon, damage: 26f, interval: 0.23f, burst: 1, spacing: 0.07f, magazine: 19, reload: 2.9f,
            falloffStart: 12f, falloffEnd: 38f, falloffMin: 0.45f);

        Abilities();
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Hero stats applied (Seven / Vindicta).");
    }

    private static void Hero(string path, float maxHealth, float regen, float speed, float healthPerLevel, float bulletPerLevel, float powerPerLevel)
    {
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(path);

        if (hero == null)
        {
            Debug.LogError($"[Deadlock Setup] {path} not found.");
            return;
        }

        hero.maxHealth = maxHealth;
        hero.healthRegen = regen;
        hero.moveSpeed = speed;
        hero.healthPerLevel = healthPerLevel;
        hero.bulletDamagePerLevel = bulletPerLevel;
        hero.abilityPowerPerLevel = powerPerLevel;
        EditorUtility.SetDirty(hero);
    }

    private static void Gun(string path, float damage, float interval, int burst, float spacing, int magazine, float reload,
        float falloffStart, float falloffEnd, float falloffMin)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);

        if (root == null)
        {
            Debug.LogError($"[Deadlock Setup] {path} not found.");
            return;
        }

        foreach (Weapon weapon in root.GetComponentsInChildren<Weapon>(true))
        {
            var so = new SerializedObject(weapon);
            so.FindProperty("damage").floatValue = damage;
            so.FindProperty("fireInterval").floatValue = interval;
            so.FindProperty("burstCount").intValue = burst;
            so.FindProperty("burstSpacing").floatValue = spacing;
            so.FindProperty("magazineSize").intValue = magazine;
            so.FindProperty("reloadSeconds").floatValue = reload;
            so.FindProperty("falloffStart").floatValue = falloffStart;
            so.FindProperty("falloffEnd").floatValue = falloffEnd;
            so.FindProperty("falloffMin").floatValue = falloffMin;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void Abilities()
    {
        // Seven's Lightning Ball (wiki: straight line, 4.25 m radius, 75 DPS, 5 s lifetime, slows down
        // while damaging enemies, stops where it hits the world).
        Edit("Assets/Data/Abilities/Seven_BolaRelampago.asset", a =>
        {
            a.description = "Una bola eléctrica que avanza en línea recta: electrocuta lo que toca, se frena mientras daña enemigos y se queda donde choca con el piso o una pared.";
            a.icon = Icon("Seven_LightningBall");
            a.tint = new Color(1f, 0.85f, 0.2f, 1f);
            a.shape = AbilityShape.Projectile;
            a.travelSpeed = 7f;
            a.slowedSpeed = 0.2f;
            a.duration = 4.5f;
            // A target that stays inside it for the whole life takes ~125; one that walks out, far less.
            a.damagePerSecond = 28f;
            a.tickInterval = 0.25f;
            a.damage = 0f;
            a.radius = 3.2f;
            a.slow = 0.25f;
            a.slowSeconds = 0.6f;
            a.cooldown = 10f;
            // Wiki: T1 +1 charge; T2 -28 % move speed on hit and +1 s lifetime; T3 +58.5 DPS and +1.75 m
            // radius (x0.37 DPS, x0.75 radius like the base numbers).
            a.upgrades = new[]
            {
                new AbilityUpgrade { summary = "+1 carga: dos bolas seguidas", charges = 1 },
                new AbilityUpgrade { summary = "Frena un 28 % más y dura +1 s", slow = 0.28f, duration = 1f },
                new AbilityUpgrade { summary = "+22 de daño por segundo y +1.3 m de radio", damagePerSecond = 22f, radius = 1.3f },
            };
        });

        // Seven's Storm Cloud (wiki: channel 7 s, 95 DPS, radius 10 -> 30 m at 1.5 m/s, no damage out of
        // line of sight). Here: he floats up and keeps moving, can't shoot or cast, lightning everywhere.
        Edit("Assets/Data/Abilities/Seven_NubeTormenta.asset", a =>
        {
            a.description = "Seven se convierte en una nube de tormenta: flota, puede moverse pero no disparar, y sus rayos golpean a todos los enemigos que ve alrededor.";
            a.icon = Icon("Seven_StormCloud");
            a.tint = new Color(0.62f, 0.75f, 1f, 1f);
            a.shape = AbilityShape.Channel;
            a.channelSeconds = 6f;
            a.riseHeight = 2.5f;
            a.radius = 6f;
            a.radiusEnd = 12f;
            a.growSpeed = 1f;
            a.damagePerSecond = 35f;
            a.tickInterval = 0.3f;
            a.damage = 0f;
            a.cooldown = 30f;
            a.channelArmor = 0f;
            // Wiki: T1 +55 % bullet resist while channelling (here: -12 to each hit, about half a rival
            // bullet); T2 +7 s channel, +10 m final and +5 m initial radius (x0.86 time, x0.6 radius);
            // T3 +65 DPS (x0.37).
            a.upgrades = new[]
            {
                new AbilityUpgrade { summary = "Recibes 12 menos de daño por golpe mientras eres tormenta", channelArmor = 12f },
                new AbilityUpgrade { summary = "+5 s de tormenta y crece 3 m / 6 m más", channelSeconds = 5f, radius = 3f, radiusEnd = 6f },
                new AbilityUpgrade { summary = "+24 de daño por segundo", damagePerSecond = 24f },
            };
        });

        // Vindicta's Stake (wiki: thrown, tethers enemies to where it lands for 1.75 s, capture radius
        // and tether length 9 m, 40 % move slow, 40 damage).
        Edit("Assets/Data/Abilities/Vindicta_Estaca.asset", a =>
        {
            a.description = "Lanza una estaca: los enemigos cerca de donde cae quedan amarrados a ella, sin poder alejarse, y más lentos.";
            a.icon = Icon("Vindicta_Stake");
            a.tint = new Color(0.62f, 0.4f, 1f, 1f);
            a.shape = AbilityShape.AreaAtAim;
            a.travelTime = 0.35f;
            a.castRange = 20f;
            a.damage = 40f;
            a.radius = 4.5f;
            a.tetherSeconds = 1.75f;
            a.tetherLength = 4.5f;
            a.slow = 0.4f;
            a.slowSeconds = 1.75f;
            a.cooldown = 12f;
            // Wiki: T1 +45 damage; T2 -22 s cooldown (x0.3, as 40 s became 12); T3 +0.75 s tether and
            // +2 m capture radius (x0.5).
            a.upgrades = new[]
            {
                new AbilityUpgrade { summary = "+45 de daño", damage = 45f },
                new AbilityUpgrade { summary = "Recarga 6 s más rápido", cooldown = -6f },
                new AbilityUpgrade { summary = "Amarra 0.75 s más y atrapa 1 m más lejos", tetherSeconds = 0.75f, slowSeconds = 0.75f, radius = 1f },
            };
        });

        // Vindicta's Assassinate (wiki: hitscan, pierces, 1 s to full charge, 50 % uncharged, bonus damage
        // under 50 % health, 2 charges 2.5 s apart). Scaled so a full shot plus the finisher bonus can
        // take a hurt rival down, as the original's "one shot kill".
        Edit("Assets/Data/Abilities/Vindicta_Asesinar.asset", a =>
        {
            a.description = "Mantén el botón para apuntar con la mira y suéltalo para disparar: atraviesa enemigos, cargado hace el doble y remata a los que tienen menos de media vida. Dos cargas.";
            a.icon = Icon("Vindicta_Assassinate");
            a.tint = new Color(0.72f, 0.42f, 1f, 1f);
            a.shape = AbilityShape.Line;
            a.castRange = 70f;
            a.damage = 120f;
            a.chargeSeconds = 1f;
            a.unchargedDamage = 0.5f;
            a.pierce = true;
            a.lowHealthBonus = 120f;
            a.charges = 2;
            a.chargeGap = 2.5f;
            a.cooldown = 30f;
            // Wiki: T1 -15 s cooldown (x0.55, as 55 s became 30); T2 +80 max bonus damage; T3 +4 % weapon
            // damage per kill. This game has no kill stacks, so T3 adds the same weight as damage instead.
            a.upgrades = new[]
            {
                new AbilityUpgrade { summary = "Recarga 8 s más rápido", cooldown = -8f },
                new AbilityUpgrade { summary = "+80 de remate a enemigos con menos de media vida", lowHealthBonus = 80f },
                new AbilityUpgrade { summary = "+50 de daño del disparo", damage = 50f },
            };
        });
    }

    /// <summary>An ability icon from Assets/Art/Abilities/Icons, imported as a sprite with its alpha.</summary>
    private static Sprite Icon(string name)
    {
        string path = $"Assets/Art/Abilities/Icons/{name}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogWarning($"[Deadlock Setup] {path} missing: the HUD will show the ability's initial.");
            return null;
        }

        if (importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void Edit(string path, System.Action<AbilitySO> change)
    {
        var ability = AssetDatabase.LoadAssetAtPath<AbilitySO>(path);

        if (ability == null)
        {
            Debug.LogError($"[Deadlock Setup] {path} not found.");
            return;
        }

        change(ability);
        EditorUtility.SetDirty(ability);
    }
}
