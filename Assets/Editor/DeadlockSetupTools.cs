using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup for the lane slice: project layers, minion data assets and the prefabs that
/// wrap the models extracted from the game.
/// </summary>
/// <remarks>
/// Written as re-runnable editor commands rather than done by hand in the inspector for two
/// reasons: the same setup has to be reproduced on the university's Unity 2022 project, and
/// re-running it after a model is replaced is a single menu click instead of an afternoon.
/// Everything here uses APIs that behave the same in Unity 2022 and Unity 6.
/// </remarks>
public static class DeadlockSetupTools
{
    // Layers the gameplay masks rely on. Order matters only in that they must all exist.
    public const string GroundLayer = "Ground";
    public const string MinionLayer = "Minion";
    public const string StructureLayer = "Structure";
    public const string PlayerLayer = "PlayerBody";
    public const string SoulLayer = "Soul";

    private const string MinionModelFolder = "Assets/Models/Minions";
    private const string MinionDataFolder = "Assets/Data/Minions";
    private const string MinionPrefabFolder = "Assets/Prefabs/Minions";
    private const string StructurePrefabFolder = "Assets/Prefabs/Structures";
    private const string WeaponPrefabFolder = "Assets/Prefabs/Weapons";
    private const string EffectPrefabFolder = "Assets/Prefabs/Effects";
    private const string MaterialFolder = "Assets/Art/Materials";

    private const string SoulPrefabPath = EffectPrefabFolder + "/SoulPickup.prefab";

    /// <summary>
    /// Length in metres the gun is normalised to. Long enough to read as a rifle over the
    /// player's hand, short enough that it never reaches back into the camera.
    /// </summary>
    private const float WeaponLength = 0.55f;

    /// <summary>Stats for one minion, applied to a generated <see cref="MinionDefinitionSO"/>.</summary>
    private struct MinionSpec
    {
        public string modelName;
        public MinionRole role;
        public float health;
        public float speed;
        public float damage;
        public float attackInterval;
        public float attackRange;
        public float detectionRange;
        public int souls;
        public int drops;
    }

    /// <summary>
    /// The six minions, three per side. Melee are tougher and hit harder at knife range, troopers
    /// are the ranged bulk and the main source of souls, medics are fragile but worth the most —
    /// which is what makes killing the medic first the interesting decision.
    /// </summary>
    private static readonly MinionSpec[] Specs =
    {
        new MinionSpec { modelName = "Amber_Melee",      role = MinionRole.Melee,   health = 220f, speed = 2.4f, damage = 22f, attackInterval = 1.0f, attackRange = 2.2f, detectionRange = 7f,  souls = 14, drops = 1 },
        new MinionSpec { modelName = "Amber_Trooper",    role = MinionRole.Trooper, health = 130f, speed = 2.1f, damage = 12f, attackInterval = 1.2f, attackRange = 9f,   detectionRange = 12f, souls = 10, drops = 1 },
        new MinionSpec { modelName = "Amber_Medic",      role = MinionRole.Medic,   health = 90f,  speed = 2.2f, damage = 0f,  attackInterval = 1.5f, attackRange = 0f,   detectionRange = 8f,  souls = 25, drops = 2 },
        new MinionSpec { modelName = "Sapphire_Melee",   role = MinionRole.Melee,   health = 220f, speed = 2.4f, damage = 22f, attackInterval = 1.0f, attackRange = 2.2f, detectionRange = 7f,  souls = 14, drops = 1 },
        new MinionSpec { modelName = "Sapphire_Trooper", role = MinionRole.Trooper, health = 130f, speed = 2.1f, damage = 12f, attackInterval = 1.2f, attackRange = 9f,   detectionRange = 12f, souls = 10, drops = 1 },
        new MinionSpec { modelName = "Sapphire_Medic",   role = MinionRole.Medic,   health = 90f,  speed = 2.2f, damage = 0f,  attackInterval = 1.5f, attackRange = 0f,   detectionRange = 8f,  souls = 25, drops = 2 },
    };

    [MenuItem("Deadlock/Setup/1 - Layers, Minion Data and Prefabs")]
    public static void BuildLaneContent()
    {
        EnsureLayers();
        EnsureFolders();

        GameObject soulPrefab = CreateSoulPrefab();

        int built = 0;

        foreach (MinionSpec spec in Specs)
        {
            MinionDefinitionSO definition = CreateMinionDefinition(spec);
            GameObject prefab = CreateMinionPrefab(spec, definition, soulPrefab);

            if (prefab != null)
            {
                // The definition points at the prefab, and the prefab's agent points back at the
                // definition. Wiring the link here keeps the spawner's inspector to one field.
                SerializedObject so = new SerializedObject(definition);
                so.FindProperty("prefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);
                built++;
            }
        }

        CreateTowerPrefab();
        CreateWeaponPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Deadlock Setup] Done. {built}/{Specs.Length} minions, plus tower, weapon and soul prefabs.");
    }

    /// <summary>
    /// Adds the layers the gameplay masks depend on. Without these, the minion ground-snap
    /// raycast hits the minion's own collider and every target scan picks up soul pickups.
    /// </summary>
    private static void EnsureLayers()
    {
        string[] wanted = { GroundLayer, MinionLayer, StructureLayer, PlayerLayer, SoulLayer };

        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        foreach (string layerName in wanted)
        {
            if (LayerMask.NameToLayer(layerName) != -1)
            {
                continue;
            }

            bool placed = false;

            // 0-7 are Unity's built-in layers and must not be touched.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);

                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                Debug.LogError($"[Deadlock Setup] No free user layer left for '{layerName}'.");
            }
        }

        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolders()
    {
        string[] folders =
        {
            MinionDataFolder, MinionPrefabFolder, StructurePrefabFolder,
            WeaponPrefabFolder, EffectPrefabFolder, MaterialFolder,
        };

        foreach (string folder in folders)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                continue;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                string grandparent = Path.GetDirectoryName(parent).Replace('\\', '/');
                AssetDatabase.CreateFolder(grandparent, Path.GetFileName(parent));
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    private static MinionDefinitionSO CreateMinionDefinition(MinionSpec spec)
    {
        string path = $"{MinionDataFolder}/{spec.modelName}.asset";
        MinionDefinitionSO definition = AssetDatabase.LoadAssetAtPath<MinionDefinitionSO>(path);

        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<MinionDefinitionSO>();
            AssetDatabase.CreateAsset(definition, path);
        }

        definition.minionName = spec.modelName.Replace('_', ' ');
        definition.role = spec.role;
        definition.maxHealth = spec.health;
        definition.moveSpeed = spec.speed;
        definition.attackDamage = spec.damage;
        definition.attackInterval = spec.attackInterval;
        definition.attackRange = spec.attackRange;
        definition.detectionRange = spec.detectionRange;
        definition.soulValue = spec.souls;
        definition.soulDrops = spec.drops;

        if (spec.role == MinionRole.Medic)
        {
            definition.healAmount = 25f;
            definition.healInterval = 2f;
            definition.healRange = 6f;
            definition.medicStandoff = 3f;
        }

        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static GameObject CreateMinionPrefab(MinionSpec spec, MinionDefinitionSO definition, GameObject soulPrefab)
    {
        string modelPath = $"{MinionModelFolder}/{spec.modelName}.glb";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

        if (model == null)
        {
            Debug.LogError($"[Deadlock Setup] Model not found or not imported: {modelPath}");
            return null;
        }

        GameObject root = new GameObject(spec.modelName);
        root.layer = LayerMask.NameToLayer(MinionLayer);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);

        // Capsule sized from the actual mesh so each minion's hitbox matches what the player sees.
        Bounds bounds = CalculateBounds(visual);
        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.height = Mathf.Max(0.5f, bounds.size.y);
        capsule.radius = Mathf.Max(0.2f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.45f);
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);

        Health health = root.AddComponent<Health>();
        MinionAgent agent = root.AddComponent<MinionAgent>();

        SerializedObject healthSo = new SerializedObject(health);
        healthSo.FindProperty("maxHealth").floatValue = spec.health;
        healthSo.FindProperty("destroyOnDeath").boolValue = true;
        healthSo.FindProperty("destroyDelay").floatValue = 2f;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject agentSo = new SerializedObject(agent);
        agentSo.FindProperty("definition").objectReferenceValue = definition;
        agentSo.FindProperty("soulPrefab").objectReferenceValue = soulPrefab;
        agentSo.FindProperty("visualRoot").objectReferenceValue = visual.transform;
        agentSo.FindProperty("groundMask").intValue = LayerMask.GetMask(GroundLayer);
        agentSo.FindProperty("targetMask").intValue = LayerMask.GetMask(MinionLayer, StructureLayer, PlayerLayer);
        agentSo.FindProperty("allyMask").intValue = LayerMask.GetMask(MinionLayer);
        agentSo.ApplyModifiedPropertiesWithoutUndo();

        string prefabPath = $"{MinionPrefabFolder}/{spec.modelName}.prefab";
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    private static void CreateTowerPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Structures/Guardian.glb");

        if (model == null)
        {
            Debug.LogError("[Deadlock Setup] Guardian.glb not found or not imported.");
            return;
        }

        GameObject root = new GameObject("Guardian_Tower");
        root.layer = LayerMask.NameToLayer(StructureLayer);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);

        Bounds bounds = CalculateBounds(visual);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = bounds.size;
        box.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);

        // Muzzle at the top of the brazier, where the beam should read as coming from.
        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(root.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, bounds.size.y * 0.85f, 0f);

        Health health = root.AddComponent<Health>();
        Tower tower = root.AddComponent<Tower>();

        SerializedObject healthSo = new SerializedObject(health);
        healthSo.FindProperty("maxHealth").floatValue = 800f;
        healthSo.FindProperty("destroyOnDeath").boolValue = false;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject towerSo = new SerializedObject(tower);
        towerSo.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        towerSo.FindProperty("targetMask").intValue = LayerMask.GetMask(MinionLayer, PlayerLayer);
        towerSo.FindProperty("baseHealth").floatValue = 800f;
        towerSo.FindProperty("baseDamage").floatValue = 25f;
        towerSo.FindProperty("attackRange").floatValue = 14f;
        towerSo.FindProperty("attackInterval").floatValue = 1.2f;
        towerSo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, $"{StructurePrefabFolder}/Guardian_Tower.prefab");
        Object.DestroyImmediate(root);
    }

    private static void CreateWeaponPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/Seven_Weapon.glb");

        if (model == null)
        {
            Debug.LogError("[Deadlock Setup] Seven_Weapon.glb not found or not imported.");
            return;
        }

        GameObject root = new GameObject("Seven_Weapon");

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);

        // The extracted model is about 2 m long, which in VR fills the whole view and puts the
        // camera inside the mesh. Normalise it to something that reads as a weapon held in one
        // hand, then slide it so the grip end sits at the hand rather than its middle.
        Bounds raw = WorldBounds(visual);
        float longestAxis = Mathf.Max(raw.size.x, Mathf.Max(raw.size.y, raw.size.z));

        if (longestAxis > 0.001f)
        {
            visual.transform.localScale = Vector3.one * (WeaponLength / longestAxis);
        }

        Bounds scaled = WorldBounds(visual);
        visual.transform.localPosition -= new Vector3(scaled.center.x, scaled.center.y, scaled.min.z);

        Bounds placed = WorldBounds(visual);

        // Muzzle at the far end; the barrel direction is verified by eye afterwards and nudged in
        // the prefab if the model's forward is not +Z.
        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(root.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, placed.max.z);

        Weapon weapon = root.AddComponent<Weapon>();
        AudioSource audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;

        SerializedObject weaponSo = new SerializedObject(weapon);
        weaponSo.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        weaponSo.FindProperty("audioSource").objectReferenceValue = audio;
        weaponSo.FindProperty("owner").enumValueIndex = (int)Faction.Player;
        weaponSo.FindProperty("hitMask").intValue =
            LayerMask.GetMask(MinionLayer, StructureLayer, SoulLayer, GroundLayer);
        weaponSo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, $"{WeaponPrefabFolder}/Seven_Weapon.prefab");
        Object.DestroyImmediate(root);
    }

    private static GameObject CreateSoulPrefab()
    {
        Material soulMaterial = CreateSoulMaterial();

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        root.name = "SoulPickup";
        root.layer = LayerMask.NameToLayer(SoulLayer);
        root.transform.localScale = Vector3.one * 0.22f;

        SphereCollider collider = root.GetComponent<SphereCollider>();
        // A trigger so souls never block movement or a bullet meant for the minion behind them.
        collider.isTrigger = true;

        root.GetComponent<MeshRenderer>().sharedMaterial = soulMaterial;
        root.AddComponent<SoulPickup>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SoulPrefabPath);
        Object.DestroyImmediate(root);

        return saved;
    }

    private static Material CreateSoulMaterial()
    {
        string path = $"{MaterialFolder}/Soul_Glow.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader);

        Color soulColor = new Color(0.45f, 0.85f, 1f);
        material.color = soulColor;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", soulColor * 3f);

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// World bounds of every renderer under <paramref name="target"/>, recentred on the object's
    /// own origin so the result can be used for a local-space collider.
    /// </summary>
    private static Bounds CalculateBounds(GameObject target)
    {
        Bounds bounds = WorldBounds(target);
        bounds.center -= target.transform.position;
        return bounds;
    }

    /// <summary>
    /// World-space bounds of every renderer under <paramref name="target"/>. Used while building
    /// prefabs, where the root sits at the origin unrotated, so world space and the root's local
    /// space are the same thing — which is what makes it safe to place children from these values.
    /// </summary>
    private static Bounds WorldBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }
}
