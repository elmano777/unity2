using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the playable lane scene from the prefabs created by <see cref="DeadlockSetupTools"/>:
/// ground, cover, the waypoint lane, one wave spawner per side, the first Guardian tower and the
/// match/economy managers.
/// </summary>
/// <remarks>
/// Generated rather than hand-built so the same lane can be rebuilt identically on the Unity 2022
/// project, and so tuning the layout is a code change that can be reviewed rather than an
/// undocumented drag in the scene view.
/// </remarks>
public static class DeadlockLaneBuilder
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";

    // The lane runs along Z. The player's side is negative, the rival's side positive.
    private const float LaneHalfLength = 26f;
    private const float LaneWidth = 14f;

    [MenuItem("Deadlock/Setup/2 - Build Lane Scene")]
    public static void BuildLaneScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLighting();
        CreateGround();
        CreateCover();

        LanePath lane = CreateLane();
        GameObject tower = CreateTower();

        CreateSpawner("Spawner_Amber", Faction.Player, lane, false, new Vector3(0f, 0f, -LaneHalfLength), "Amber");
        CreateSpawner("Spawner_Sapphire", Faction.Enemy, lane, true, new Vector3(0f, 0f, LaneHalfLength), "Sapphire");

        CreateManagers();
        CreatePlayerRig();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[Deadlock Lane] Scene saved to {ScenePath}. Tower at z={tower.transform.position.z:F0}.");
    }

    private static void CreateLighting()
    {
        GameObject sun = new GameObject("Directional Light");
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        light.color = new Color(1f, 0.95f, 0.85f);
        sun.transform.rotation = Quaternion.Euler(48f, 150f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.32f, 0.35f, 0.45f);
        RenderSettings.ambientEquatorColor = new Color(0.24f, 0.24f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.12f, 0.12f);
    }

    private static void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.layer = LayerMask.NameToLayer(DeadlockSetupTools.GroundLayer);
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(LaneWidth, 1f, LaneHalfLength * 2f + 8f);
        ground.GetComponent<MeshRenderer>().sharedMaterial =
            GetOrCreateMaterial("Lane_Ground", new Color(0.19f, 0.18f, 0.2f));
    }

    /// <summary>
    /// Blocks the player can duck behind (RF-15). Staggered left and right so pushing up the lane
    /// means moving between them rather than walking one straight line.
    /// </summary>
    private static void CreateCover()
    {
        GameObject parent = new GameObject("Cover");
        Material material = GetOrCreateMaterial("Lane_Cover", new Color(0.3f, 0.26f, 0.22f));

        Vector3[] positions =
        {
            new Vector3(-3.2f, 0.9f, -14f),
            new Vector3(3.4f, 0.9f, -7f),
            new Vector3(-3.6f, 0.9f, 0f),
            new Vector3(3.0f, 0.9f, 7f),
            new Vector3(-3.2f, 0.9f, 14f),
        };

        foreach (Vector3 position in positions)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Cover Block";
            block.layer = LayerMask.NameToLayer(DeadlockSetupTools.GroundLayer);
            block.transform.SetParent(parent.transform, false);
            block.transform.position = position;
            block.transform.localScale = new Vector3(2.2f, 1.8f, 1.2f);
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    private static LanePath CreateLane()
    {
        GameObject laneRoot = new GameObject("LanePath");
        LanePath lane = laneRoot.AddComponent<LanePath>();

        // Waypoints run from the player's spawn to the rival's. The Sapphire spawner walks the
        // same list backwards, so there is only one path to keep in sync.
        float[] zs = { -LaneHalfLength, -13f, 0f, 13f, LaneHalfLength };
        Transform[] points = new Transform[zs.Length];

        for (int i = 0; i < zs.Length; i++)
        {
            GameObject point = new GameObject($"Waypoint_{i}");
            point.transform.SetParent(laneRoot.transform, false);
            point.transform.position = new Vector3(0f, 0f, zs[i]);
            points[i] = point.transform;
        }

        SerializedObject so = new SerializedObject(lane);
        SerializedProperty array = so.FindProperty("waypoints");
        array.arraySize = points.Length;

        for (int i = 0; i < points.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return lane;
    }

    private static GameObject CreateTower()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Structures/Guardian_Tower.prefab");

        if (prefab == null)
        {
            Debug.LogError("[Deadlock Lane] Guardian_Tower.prefab missing. Run setup step 1 first.");
            return new GameObject("Missing Tower");
        }

        GameObject tower = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        tower.name = "Guardian_T1_Sapphire";
        // Off to the side of the lane so it covers the path without standing in it.
        tower.transform.position = new Vector3(4.5f, 0f, 17f);
        tower.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        SerializedObject towerSo = new SerializedObject(tower.GetComponent<Tower>());
        towerSo.FindProperty("faction").enumValueIndex = (int)Faction.Enemy;
        towerSo.FindProperty("tier").intValue = 1;
        towerSo.ApplyModifiedPropertiesWithoutUndo();

        return tower;
    }

    private static void CreateSpawner(string name, Faction faction, LanePath lane, bool reversed, Vector3 position, string sidePrefix)
    {
        GameObject spawnerObject = new GameObject(name);
        spawnerObject.transform.position = position;
        spawnerObject.transform.rotation = Quaternion.LookRotation(reversed ? Vector3.back : Vector3.forward);

        WaveSpawner spawner = spawnerObject.AddComponent<WaveSpawner>();

        // The wave the brief calls for: 2 melee at the front, 3 troopers behind, 1 medic at the back.
        MinionDefinitionSO melee = LoadDefinition($"{sidePrefix}_Melee");
        MinionDefinitionSO trooper = LoadDefinition($"{sidePrefix}_Trooper");
        MinionDefinitionSO medic = LoadDefinition($"{sidePrefix}_Medic");

        SerializedObject so = new SerializedObject(spawner);
        so.FindProperty("faction").enumValueIndex = (int)faction;
        so.FindProperty("lane").objectReferenceValue = lane;
        so.FindProperty("walkReversed").boolValue = reversed;
        so.FindProperty("firstWaveDelay").floatValue = 4f;
        so.FindProperty("waveInterval").floatValue = 30f;
        so.FindProperty("autoStart").boolValue = true;

        SerializedProperty composition = so.FindProperty("composition");
        composition.arraySize = 3;
        SetEntry(composition.GetArrayElementAtIndex(0), melee, 2);
        SetEntry(composition.GetArrayElementAtIndex(1), trooper, 3);
        SetEntry(composition.GetArrayElementAtIndex(2), medic, 1);

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEntry(SerializedProperty entry, MinionDefinitionSO definition, int count)
    {
        entry.FindPropertyRelative("definition").objectReferenceValue = definition;
        entry.FindPropertyRelative("count").intValue = count;
    }

    private static MinionDefinitionSO LoadDefinition(string assetName)
    {
        MinionDefinitionSO definition =
            AssetDatabase.LoadAssetAtPath<MinionDefinitionSO>($"Assets/Data/Minions/{assetName}.asset");

        if (definition == null)
        {
            Debug.LogError($"[Deadlock Lane] Missing minion definition '{assetName}'. Run setup step 1 first.");
        }

        return definition;
    }

    private static void CreateManagers()
    {
        GameObject managers = new GameObject("Managers");

        MatchManager match = managers.AddComponent<MatchManager>();
        SerializedObject matchSo = new SerializedObject(match);
        matchSo.FindProperty("verboseLogging").boolValue = true;
        matchSo.FindProperty("preMatchSeconds").floatValue = 2f;
        matchSo.ApplyModifiedPropertiesWithoutUndo();

        managers.AddComponent<PlayerWallet>();
        managers.AddComponent<GameSession>();
    }

    /// <summary>
    /// Drops in the XR rig and gives the player a body the minions and tower can actually shoot.
    /// The Unity 2022 project runs on the Meta SDK, so the OVRCameraRig (the same rig the title and
    /// hero select scenes use) is preferred; the XRI XR Origin is kept as a fallback for Unity 6.
    /// </summary>
    private static void CreatePlayerRig()
    {
        const string OvrRigPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvrRigPath);

        if (prefab == null)
        {
            string[] guids = AssetDatabase.FindAssets("\"XR Origin (XR Rig)\" t:Prefab");

            if (guids.Length > 0)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }

        if (prefab == null)
        {
            Debug.LogWarning("[Deadlock Lane] No OVRCameraRig or XR Origin prefab found — add the rig to the scene by hand.");
            return;
        }

        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.transform.position = new Vector3(0f, 0f, -LaneHalfLength + 3f);

        // The rig sits on the ground, so it must track from the floor: the prefab's Eye Level
        // default puts the headset at y = 0, inside the floor. PlayerSpawnHeight also lifts the
        // rig at runtime if tracking still reports the head too low.
        OVRManager manager = rig.GetComponent<OVRManager>();

        if (manager != null)
        {
            SerializedObject managerSo = new SerializedObject(manager);
            managerSo.FindProperty("_trackingOriginType").enumValueIndex = (int)OVRManager.TrackingOrigin.FloorLevel;
            managerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        new GameObject("PlayerSpawnHeight").AddComponent<PlayerSpawnHeight>();

        // OVRCameraRig has eye anchors next to the centre one; the head is the centre eye.
        Transform centerEye = FindDeep(rig.transform, "CenterEyeAnchor");
        Camera camera = centerEye != null ? centerEye.GetComponent<Camera>() : rig.GetComponentInChildren<Camera>();

        if (camera == null)
        {
            Debug.LogWarning("[Deadlock Lane] No camera under the XR rig; player body not created.");
            return;
        }

        // A collider on the head, so a tower or minion has something to hit. Parented to the
        // camera so it tracks the headset without needing a follow script.
        GameObject body = new GameObject("PlayerBody");
        body.layer = LayerMask.NameToLayer(DeadlockSetupTools.PlayerLayer);
        body.transform.SetParent(camera.transform, false);
        body.transform.localPosition = new Vector3(0f, -0.35f, 0f);

        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.height = 1.1f;
        capsule.radius = 0.28f;

        Health health = body.AddComponent<Health>();
        SerializedObject healthSo = new SerializedObject(health);
        healthSo.FindProperty("faction").enumValueIndex = (int)Faction.Player;
        healthSo.FindProperty("maxHealth").floatValue = 300f;
        healthSo.FindProperty("destroyOnDeath").boolValue = false;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        AttachWeapon(rig);
    }

    /// <summary>
    /// Parents the gun to the right-hand controller. Grabbing it properly needs an XR Grab
    /// Interactable, whose API differs between XRI 2.x and 3.x, so that is left as a deliberate
    /// manual step — the weapon still fires from the hand as-is.
    /// </summary>
    private static void AttachWeapon(GameObject rig)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/Seven_Weapon.prefab");

        if (prefab == null)
        {
            Debug.LogWarning("[Deadlock Lane] Seven_Weapon.prefab missing.");
            return;
        }

        // RightControllerAnchor follows the Touch controller on OVRCameraRig; the other two names
        // are XRI's. Exact OVR name first so "RightHandAnchorDetached" is never picked by accident.
        Transform hand = FindDeep(rig.transform, "RightControllerAnchor")
            ?? FindDeep(rig.transform, "Right Controller")
            ?? FindDeep(rig.transform, "RightHand");

        GameObject weapon = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        if (hand != null)
        {
            weapon.transform.SetParent(hand, false);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
        }
        else
        {
            weapon.transform.position = new Vector3(0.5f, 1f, -LaneHalfLength + 3f);
            Debug.LogWarning("[Deadlock Lane] Right controller not found; weapon left free in the scene.");
        }
    }

    private static Transform FindDeep(Transform root, string namePart)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Contains(namePart))
            {
                return child;
            }
        }

        return null;
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"Assets/Art/Materials/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);

        return material;
    }
}
