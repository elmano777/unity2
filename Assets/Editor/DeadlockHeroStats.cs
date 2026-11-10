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
        Hero(SevenHero, maxHealth: 290f, regen: 1f, speed: 3.75f);
        Hero(VindictaHero, maxHealth: 300f, regen: 2f, speed: 4.1f);

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

    private static void Hero(string path, float maxHealth, float regen, float speed)
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
        });

        // Vindicta's Stake: holds whoever is near where it lands (wiki: 1.75 s tether, 9 m, 40 % slow).
        Edit("Assets/Data/Abilities/Vindicta_Estaca.asset", a =>
        {
            a.description = "Clava una estaca: los enemigos cercanos quedan amarrados a ella y más lentos unos segundos.";
            a.shape = AbilityShape.AreaAtAim;
            a.damage = 55f;
            a.radius = 4f;
            a.tetherSeconds = 2f;
            a.tetherLength = 4.5f;
            a.slow = 0.4f;
            a.slowSeconds = 2f;
            a.cooldown = 10f;
        });

        // Vindicta's Assassinate: hold to charge, release to fire (wiki: 1 s to full, 50 % uncharged, double when full).
        Edit("Assets/Data/Abilities/Vindicta_Asesinar.asset", a =>
        {
            a.description = "Mantén el botón para cargar el disparo y suéltalo para disparar: cargado del todo hace el doble.";
            a.shape = AbilityShape.Line;
            a.damage = 160f;
            a.chargeSeconds = 1f;
            a.unchargedDamage = 0.5f;
            a.cooldown = 16f;
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
