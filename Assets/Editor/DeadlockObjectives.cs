using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Avance 4: the objectives fall in sequence and the player gets abilities and a shop (RF-10, RF-11,
/// RF-12). Turns the map's placeholder Walker, Base Guardians, shrines and patron into objectives the
/// host spawns, each shielded until the one before it falls; gives each hero two abilities (A and X)
/// with a cooldown display on the left wrist; and puts a shop in each base where items are bought by
/// shooting them. Safe to re-run.
/// </summary>
/// <remarks>
/// The map's markers stay as the looks (<see cref="ObjectiveVisual"/>); what fights and can be hit is a
/// prefab from <see cref="PrefabFolder"/> spawned on the same spot. Run after step 26 (it needs the
/// markers) and after 18 (it appends to the network prefab list); step 18 also picks these prefabs up
/// on its own when re-run.
/// </remarks>
public static class DeadlockObjectives
{
    public const string PrefabFolder = "Assets/Prefabs/Objectives";

    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string GuardianPath = "Assets/Prefabs/Structures/Guardian_Tower.prefab";
    private const string PrefabListPath = "Assets/Data/Network/DuelPrefabs.asset";
    private const string AbilityFolder = "Assets/Data/Abilities";
    private const string ShopFolder = "Assets/Data/Shop";
    private const string MaterialDir = "Assets/Art/Materials/Objectives";
    private const string SevenPath = "Assets/Data/Heroes/Seven.asset";
    private const string VindictaPath = "Assets/Data/Heroes/Vindicta.asset";
    private const string ShotClipPath = "Assets/Audio/Combat/Shot.wav";
    private const string HitClipPath = "Assets/Audio/Combat/Hit.wav";

    private static Material shieldMaterial, barBack, barFill, effectMaterial;

    [MenuItem("Deadlock/Setup/27 Objectives, Abilities and Shop (Avance 4)")]
    public static void Run()
    {
        DeadlockSceneKit.EnsureFolder(PrefabFolder);
        DeadlockSceneKit.EnsureFolder(AbilityFolder);
        DeadlockSceneKit.EnsureFolder(ShopFolder);
        DeadlockSceneKit.EnsureFolder(MaterialDir);

        CreateMaterials();
        HeroDefinitionSO seven = CreateAbilities();
        ShopItemSO[] items = CreateShopItems();

        EditGuardian();
        GameObject walker = BuildObjective("Walker_Objective", "Walker", tier: 2, isPatron: false, attacks: true,
            colliderCenter: new Vector3(0f, 7f, 0f), colliderSize: new Vector3(5.2f, 3f, 4.2f),
            muzzle: new Vector3(0f, 7f, 0f), beamOrigin: new Vector3(0f, 8.8f, 1.95f), barHeight: 10.2f,
            range: 16f, interval: 1.4f, shieldRadius: 4.2f);
        GameObject shrine = BuildObjective("Shrine_Objective", "Santuario", tier: 4, isPatron: false, attacks: false,
            colliderCenter: new Vector3(0f, 2.5f, 0f), colliderSize: new Vector3(1.6f, 5f, 1.6f),
            muzzle: Vector3.zero, beamOrigin: Vector3.zero, barHeight: 5.6f, range: 0f, interval: 0f, shieldRadius: 3.2f);
        GameObject patron = BuildObjective("Patron_Objective", "Patrón", tier: 5, isPatron: true, attacks: true,
            colliderCenter: new Vector3(0f, 5.5f, 0f), colliderSize: new Vector3(3.2f, 9f, 3.2f),
            muzzle: new Vector3(0f, 4f, 0f), beamOrigin: new Vector3(0f, 13f, 0f), barHeight: 15f,
            range: 16f, interval: 1f, shieldRadius: 6f);

        AppendToPrefabList(walker, shrine, patron);
        BuildScene(walker, shrine, patron, seven, items);

        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Objectives] Objective sequence, abilities, shop and wrist HUD set up in the Lane.");
    }

    // ------------------------------------------------------------------ materials

    private static void CreateMaterials()
    {
        shieldMaterial = LoadOrCreate("Objective_Shield", "Deadlock/Hologram");
        shieldMaterial.SetColor("_Color", new Color(1.1f, 0.85f, 0.4f, 1f));
        shieldMaterial.SetFloat("_Intensity", 1f);

        effectMaterial = LoadOrCreate("Ability_Fx", "Deadlock/GlowParticle");
        effectMaterial.SetFloat("_Radial", 0f);

        barBack = LoadOrCreate("Bar_Objective_Back", "Universal Render Pipeline/Unlit");
        barBack.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.06f, 1f));
        barFill = LoadOrCreate("Bar_Objective_Fill", "Universal Render Pipeline/Unlit");
        barFill.SetColor("_BaseColor", new Color(1f, 0.82f, 0.4f, 1f));

        foreach (Material m in new[] { shieldMaterial, effectMaterial, barBack, barFill })
        {
            EditorUtility.SetDirty(m);
        }
    }

    private static Material LoadOrCreate(string name, string shaderName)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(shaderName);

        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader)
        {
            m.shader = shader;
        }

        return m;
    }

    // ------------------------------------------------------------------ data

    /// <summary>Two abilities per hero, after their kits in Deadlock, assigned in button order. Returns Seven.</summary>
    private static HeroDefinitionSO CreateAbilities()
    {
        AudioClip shot = AssetDatabase.LoadAssetAtPath<AudioClip>(ShotClipPath);
        AudioClip hit = AssetDatabase.LoadAssetAtPath<AudioClip>(HitClipPath);

        AbilitySO lightning = Ability("Seven_BolaRelampago", "Bola relámpago",
            "Lanza una bola eléctrica que estalla donde apuntas.", AbilityShape.AreaAtAim,
            damage: 70f, radius: 3.5f, range: 30f, cooldown: 8f, travel: 0.35f, new Color(0.5f, 0.85f, 1f, 1f), shot, hit);
        AbilitySO storm = Ability("Seven_NubeTormenta", "Nube de tormenta",
            "Descarga rayos sobre todo lo que te rodea.", AbilityShape.AreaAroundCaster,
            damage: 90f, radius: 6f, range: 0f, cooldown: 20f, travel: 0f, new Color(0.6f, 0.55f, 1f, 1f), shot, hit);
        AbilitySO stake = Ability("Vindicta_Estaca", "Estaca",
            "Clava una estaca que hiere a los enemigos cercanos.", AbilityShape.AreaAtAim,
            damage: 55f, radius: 3f, range: 25f, cooldown: 10f, travel: 0.25f, new Color(0.85f, 0.4f, 1f, 1f), shot, hit);
        AbilitySO assassinate = Ability("Vindicta_Asesinar", "Asesinar",
            "Un disparo de francotiradora que atraviesa el carril.", AbilityShape.Line,
            damage: 150f, radius: 0f, range: 70f, cooldown: 16f, travel: 0f, new Color(1f, 0.25f, 0.3f, 1f), shot, hit);

        HeroDefinitionSO seven = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(SevenPath);
        HeroDefinitionSO vindicta = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(VindictaPath);

        if (seven != null)
        {
            seven.abilities = new[] { lightning, storm };
            EditorUtility.SetDirty(seven);
        }

        if (vindicta != null)
        {
            vindicta.abilities = new[] { stake, assassinate };
            EditorUtility.SetDirty(vindicta);
        }

        return seven;
    }

    private static AbilitySO Ability(string file, string name, string description, AbilityShape shape, float damage,
        float radius, float range, float cooldown, float travel, Color tint, AudioClip cast, AudioClip impact)
    {
        AbilitySO ability = LoadOrCreateAsset<AbilitySO>($"{AbilityFolder}/{file}.asset");
        ability.abilityName = name;
        ability.description = description;
        ability.shape = shape;
        ability.damage = damage;
        ability.radius = radius;
        ability.castRange = range;
        ability.cooldown = cooldown;
        ability.travelTime = travel;
        ability.tint = tint;
        ability.castClip = cast;
        ability.impactClip = impact;
        EditorUtility.SetDirty(ability);
        return ability;
    }

    /// <summary>
    /// Prices are set against the farm: a full wave is worth about 83 souls, so a first item comes
    /// after two or three waves and the whole shop is not affordable in one ritual.
    /// </summary>
    private static ShopItemSO[] CreateShopItems()
    {
        return new[]
        {
            Item("Item_Vitalidad", "Vitalidad extra", "+75 de vida máxima", 200, 3, ItemStat.MaxHealth, 75f),
            Item("Item_Balas", "Balas potenciadas", "+5 de daño por bala", 250, 3, ItemStat.WeaponDamage, 5f),
            Item("Item_Gatillo", "Gatillo rápido", "Disparas un 15 % más rápido", 300, 2, ItemStat.FireRate, 0.85f),
            Item("Item_Botas", "Botas de carrera", "Caminas un 20 % más rápido", 150, 2, ItemStat.MoveSpeed, 1.2f),
            Item("Item_Coraza", "Coraza", "-4 de daño por golpe recibido", 250, 2, ItemStat.DamageReduction, 4f),
        };
    }

    private static ShopItemSO Item(string file, string name, string description, int cost, int stacks, ItemStat stat, float delta)
    {
        ShopItemSO item = LoadOrCreateAsset<ShopItemSO>($"{ShopFolder}/{file}.asset");
        item.itemName = name;
        item.description = description;
        item.cost = cost;
        item.maxStacks = stacks;
        item.stat = stat;
        item.delta = delta;
        EditorUtility.SetDirty(item);
        return item;
    }

    private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);

        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }

        return asset;
    }

    // ------------------------------------------------------------------ prefabs

    /// <summary>The Guardian is tier 1 in the middle and tier 3 at the base gates: it needs the shield and the synced name too.</summary>
    private static void EditGuardian()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GuardianPath);

        try
        {
            Ensure<NetworkStructure>(root);

            Bounds bounds = RendererBounds(root);
            float top = bounds.size.y > 0.1f ? bounds.max.y - root.transform.position.y : 6.5f;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z, 2f) + 0.6f;

            Structure structure = root.GetComponent<Structure>();
            var so = new SerializedObject(structure);
            so.FindProperty("displayName").stringValue = "Guardián";
            so.FindProperty("shield").objectReferenceValue = Shield(root.transform, new Vector3(0f, top * 0.5f, 0f), new Vector3(radius * 2f, top + 1f, radius * 2f));
            so.ApplyModifiedPropertiesWithoutUndo();

            HealthBarOn(root, top + 0.8f, 2.5f);
            PrefabUtility.SaveAsPrefabAsset(root, GuardianPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// An objective with no looks of its own: hit box, hit points, network sync, shield, health bar
    /// and, for those that fight, the Guardian's beam and targeting. It stands on its map marker.
    /// </summary>
    private static GameObject BuildObjective(string name, string displayName, int tier, bool isPatron, bool attacks,
        Vector3 colliderCenter, Vector3 colliderSize, Vector3 muzzle, Vector3 beamOrigin, float barHeight,
        float range, float interval, float shieldRadius)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        GameObject guardian = AssetDatabase.LoadAssetAtPath<GameObject>(GuardianPath);
        Tower guardianTower = guardian != null ? guardian.GetComponent<Tower>() : null;

        var root = new GameObject(name);
        root.layer = LayerMask.NameToLayer(DeadlockSetupTools.StructureLayer);

        try
        {
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = colliderCenter;
            box.size = colliderSize;

            Health health = root.AddComponent<Health>();
            var healthSo = new SerializedObject(health);
            healthSo.FindProperty("faction").enumValueIndex = (int)Faction.Enemy;
            healthSo.FindProperty("destroyOnDeath").boolValue = false;
            healthSo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkHealth>();

            Structure structure;

            if (attacks)
            {
                Tower tower = root.AddComponent<Tower>();
                structure = tower;
                ConfigureTower(tower, guardianTower, root.transform, muzzle, beamOrigin, range, interval);
                root.AddComponent<NetworkTower>();
            }
            else
            {
                structure = root.AddComponent<Structure>();
            }

            root.AddComponent<NetworkStructure>();

            var so = new SerializedObject(structure);
            so.FindProperty("faction").enumValueIndex = (int)Faction.Enemy;
            so.FindProperty("tier").intValue = tier;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("isPatron").boolValue = isPatron;
            so.FindProperty("shield").objectReferenceValue = Shield(root.transform, colliderCenter,
                new Vector3(shieldRadius * 2f, colliderSize.y + 1.5f, shieldRadius * 2f));

            if (guardian != null)
            {
                // Same crash of the Guardian when it falls.
                var guardianSo = new SerializedObject(guardian.GetComponent<Structure>());
                so.FindProperty("destructionClip").objectReferenceValue = guardianSo.FindProperty("destructionClip").objectReferenceValue;
                so.FindProperty("destructionEffect").objectReferenceValue = guardianSo.FindProperty("destructionEffect").objectReferenceValue;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            HealthBarOn(root, barHeight, isPatron ? 4f : 3f);

            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureTower(Tower tower, Tower template, Transform root, Vector3 muzzle, Vector3 beamOrigin, float range, float interval)
    {
        var muzzleT = new GameObject("Muzzle").transform;
        muzzleT.SetParent(root, false);
        muzzleT.localPosition = muzzle;

        var originT = new GameObject("BeamOrigin").transform;
        originT.SetParent(root, false);
        originT.localPosition = beamOrigin;

        AudioSource audio = root.gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.maxDistance = 45f;
        audio.rolloffMode = AudioRolloffMode.Linear;

        var beamGo = new GameObject("Beam");
        beamGo.transform.SetParent(root, false);
        LineRenderer beam = beamGo.AddComponent<LineRenderer>();
        beam.useWorldSpace = true;
        beam.positionCount = 2;
        beam.shadowCastingMode = ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.enabled = false;

        var so = new SerializedObject(tower);

        if (template != null)
        {
            var templateSo = new SerializedObject(template);
            so.FindProperty("attackClip").objectReferenceValue = templateSo.FindProperty("attackClip").objectReferenceValue;
            so.FindProperty("targetMask").intValue = templateSo.FindProperty("targetMask").intValue;

            LineRenderer templateBeam = templateSo.FindProperty("beam").objectReferenceValue as LineRenderer;

            if (templateBeam != null)
            {
                beam.sharedMaterial = templateBeam.sharedMaterial;
                beam.widthMultiplier = templateBeam.widthMultiplier * 1.6f;
                beam.widthCurve = templateBeam.widthCurve;
            }
        }

        so.FindProperty("muzzle").objectReferenceValue = muzzleT;
        so.FindProperty("beamOrigin").objectReferenceValue = originT;
        so.FindProperty("beam").objectReferenceValue = beam;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        so.FindProperty("attackRange").floatValue = range;
        so.FindProperty("attackInterval").floatValue = interval;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject Shield(Transform parent, Vector3 center, Vector3 size)
    {
        Transform old = parent.Find("Shield");
        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        GameObject shield = DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "Shield", parent, center, size, shieldMaterial);
        Renderer renderer = shield.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        shield.SetActive(false);
        return shield;
    }

    /// <summary>The minions' two-quad bar, scaled up and hung over the objective.</summary>
    private static void HealthBarOn(GameObject root, float height, float width)
    {
        Transform old = root.transform.Find("HealthBar");
        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        var bar = new GameObject("HealthBar");
        bar.transform.SetParent(root.transform, false);
        bar.transform.localPosition = new Vector3(0f, height, 0f);
        bar.transform.localScale = Vector3.one * width;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(bar.transform, false);
        Quad("Back", visual.transform, new Vector3(0f, 0f, 0.01f), new Vector3(1.04f, 0.12f, 1f), barBack);

        var pivot = new GameObject("FillPivot");
        pivot.transform.SetParent(visual.transform, false);
        pivot.transform.localPosition = new Vector3(-0.5f, 0f, 0f);
        Quad("Fill", pivot.transform, new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.08f, 1f), barFill);
        visual.SetActive(false);

        HealthBar healthBar = bar.AddComponent<HealthBar>();
        var so = new SerializedObject(healthBar);
        so.FindProperty("health").objectReferenceValue = root.GetComponent<Health>();
        so.FindProperty("fillPivot").objectReferenceValue = pivot.transform;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Quad(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject quad = DeadlockSceneKit.Primitive(PrimitiveType.Quad, name, parent, position, scale, material);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Bounds RendererBounds(GameObject root)
    {
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        bool first = true;

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is LineRenderer || r is ParticleSystemRenderer || r.name == "Shield" || r.GetComponentInParent<HealthBar>() != null)
            {
                continue;
            }

            if (first)
            {
                bounds = r.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return bounds;
    }

    private static void AppendToPrefabList(params GameObject[] prefabs)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);

        if (list == null)
        {
            Debug.LogWarning("[Deadlock Objectives] No network prefab list yet: run step 18, which lists these prefabs too.");
            return;
        }

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null && !list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
            }
        }

        EditorUtility.SetDirty(list);
    }

    // ------------------------------------------------------------------ scene

    private static void BuildScene(GameObject walker, GameObject shrine, GameObject patron, HeroDefinitionSO seven, ShopItemSO[] items)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (GameObject old in FindRoots("Objectives", "Shop_Amber", "Shop_Sapphire"))
        {
            Object.DestroyImmediate(old);
        }

        var spawners = new GameObject("Objectives").transform;
        GameObject guardian = AssetDatabase.LoadAssetAtPath<GameObject>(GuardianPath);

        foreach (Transform marker in AllTransforms())
        {
            string n = marker.name;
            Faction side = marker.position.z < 0f ? Faction.Player : Faction.Enemy;
            string sideName = side == Faction.Player ? "Amber" : "Sapphire";

            if (n == "Walker (placeholder)")
            {
                Spawner(spawners, $"{sideName}_Walker", marker, walker, side, 2, "Walker");
                Link(marker.gameObject, side);
            }
            else if (n.StartsWith("Shrine_") && n.EndsWith("(placeholder)"))
            {
                Spawner(spawners, $"{sideName}_{n.Substring(0, 8)}", marker, shrine, side, 4, "Santuario");
                Link(marker.gameObject, side);
            }
            else if (n.StartsWith("Patron_") && n.EndsWith("(placeholder)"))
            {
                Spawner(spawners, $"{sideName}_Patron", marker, patron, side, 5, "Patrón");
                Link(marker.gameObject, side);
            }
            else if (n.StartsWith("BaseGuardian_"))
            {
                // The map's stripped copies stand in until now; the real tower is spawned on the spot.
                string slot = n.Contains("_L") ? "L" : "R";
                Spawner(spawners, $"{sideName}_BaseGuardian_{slot}", marker, guardian, side, 3, "Guardián de base");
                marker.gameObject.SetActive(false);
            }
        }

        // The shop boards are step 28's; building them here keeps a re-run of 27 complete.
        DeadlockShop.BuildInOpenScene();

        SetUpRig(seven);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[Deadlock Objectives] {spawners.childCount} objective spawners and a shop board in each base.");
    }

    private static IEnumerable<Transform> AllTransforms()
    {
        var all = new List<Transform>();

        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            all.AddRange(root.GetComponentsInChildren<Transform>(true));
        }

        return all;
    }

    private static IEnumerable<GameObject> FindRoots(params string[] names)
    {
        var found = new List<GameObject>();

        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (System.Array.IndexOf(names, root.name) >= 0)
            {
                found.Add(root);
            }
        }

        return found;
    }

    private static void Spawner(Transform parent, string name, Transform marker, GameObject prefab, Faction side, int tier, string displayName)
    {
        var go = new GameObject($"ObjectiveSpawn_{name}");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(marker.position, marker.rotation);

        StructureSpawner spawner = go.AddComponent<StructureSpawner>();
        var so = new SerializedObject(spawner);
        so.FindProperty("prefab").objectReferenceValue = prefab;
        so.FindProperty("faction").enumValueIndex = (int)side;
        so.FindProperty("tier").intValue = tier;
        so.FindProperty("displayName").stringValue = displayName;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Link(GameObject marker, Faction side)
    {
        ObjectiveVisual visual = Ensure<ObjectiveVisual>(marker);
        var so = new SerializedObject(visual);
        so.FindProperty("faction").enumValueIndex = (int)side;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Abilities, upgrades and the wrist display on the player's rig; effects and the result on Managers.</summary>
    private static void SetUpRig(HeroDefinitionSO seven)
    {
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>(true);
        GameObject managers = GameObject.Find("Managers");

        if (rig == null || managers == null)
        {
            Debug.LogError("[Deadlock Objectives] The Lane needs its OVRCameraRig and Managers. Run steps 2 and 7 first.");
            return;
        }

        GameObject rigRoot = rig.transform.root.gameObject;
        Ensure<PlayerUpgrades>(rigRoot);

        Weapon weapon = Object.FindObjectOfType<Weapon>(true);
        AbilityCaster caster = Ensure<AbilityCaster>(rigRoot);
        var casterSo = new SerializedObject(caster);
        casterSo.FindProperty("fallbackHero").objectReferenceValue = seven;
        casterSo.FindProperty("aim").objectReferenceValue = rig.rightControllerAnchor;
        casterSo.FindProperty("groundMask").intValue = 1 << LayerMask.NameToLayer(DeadlockSetupTools.GroundLayer);

        if (weapon != null)
        {
            casterSo.FindProperty("hitMask").intValue = new SerializedObject(weapon).FindProperty("hitMask").intValue;
        }

        casterSo.ApplyModifiedPropertiesWithoutUndo();

        AbilityFx fx = Ensure<AbilityFx>(managers);
        var fxSo = new SerializedObject(fx);
        fxSo.FindProperty("effectMaterial").objectReferenceValue = effectMaterial;
        fxSo.ApplyModifiedPropertiesWithoutUndo();

        MatchOutcome outcome = Ensure<MatchOutcome>(managers);
        AudioSource voice = Ensure<AudioSource>(managers);
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        var outcomeSo = new SerializedObject(outcome);
        outcomeSo.FindProperty("fallbackHero").objectReferenceValue = seven;
        outcomeSo.FindProperty("voice").objectReferenceValue = voice;
        outcomeSo.ApplyModifiedPropertiesWithoutUndo();

        BuildWristHud(rig, caster);
    }

    /// <summary>
    /// Text on the back of the left wrist, tilted towards the eyes when the hand is raised to look
    /// at it, like a watch.
    /// </summary>
    private static void BuildWristHud(OVRCameraRig rig, AbilityCaster caster)
    {
        Transform anchor = rig.leftControllerAnchor != null ? rig.leftControllerAnchor : rig.leftHandAnchor;
        Transform old = anchor.Find("WristHud");

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        var hud = new GameObject("WristHud");
        hud.transform.SetParent(anchor, false);
        hud.transform.localPosition = new Vector3(0f, 0.06f, -0.1f);
        hud.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

        Quad("Back", hud.transform, new Vector3(0f, 0f, 0.003f), new Vector3(0.26f, 0.15f, 1f), barBack);

        TextMeshPro label = DeadlockSceneKit.Text("Label", hud.transform, Vector3.zero, "ALMAS 0", 0.17f,
            new Color(0.9f, 1f, 0.92f), new Vector2(0.25f, 0.14f));
        label.alignment = TextAlignmentOptions.Left;
        label.enableWordWrapping = false;
        DeadlockMenuPolish.Glow(label);

        WristHud wrist = hud.AddComponent<WristHud>();
        var so = new SerializedObject(wrist);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("caster").objectReferenceValue = caster;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(SerializedProperty property, Object[] values)
    {
        property.arraySize = values.Length;

        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();
        return existing != null ? existing : go.AddComponent<T>();
    }
}
