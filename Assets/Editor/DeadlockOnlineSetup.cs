using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Avance 2: online base and comfort movement in the Lane. Creates the networked player prefab
/// and the vignette material, then adds to the scene the NetworkManager + Relay transport +
/// <see cref="DuelMatchmaker"/>, both spawn points, <see cref="ComfortLocomotion"/>, the
/// <see cref="ComfortVignette"/> and the connection status text. Safe to re-run: it replaces
/// what it created the previous time.
/// </summary>
public static class DeadlockOnlineSetup
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/Network/NetworkPlayer.prefab";
    private const string VignetteMaterialPath = "Assets/Art/Materials/ComfortVignette.mat";
    private const string RivalMaterialPath = "Assets/Art/Materials/Rival.mat";

    // Same spawn distance as the single-player rig: 3 m in from each end of the lane.
    private const float SpawnZ = 23f;

    [MenuItem("Deadlock/Setup/7 Online And Comfort Movement")]
    public static void Run()
    {
        GameObject playerPrefab = CreatePlayerPrefab();
        Material vignetteMaterial = CreateVignetteMaterial();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();

        if (rig == null)
        {
            Debug.LogError("[Deadlock Online] No OVRCameraRig in the Lane. Run setup step 2 first.");
            return;
        }

        CreateSpawnPoints();
        CreateNetwork(playerPrefab);
        AddLocomotion(rig);
        AddVignette(rig, vignetteMaterial);
        AddStatusText(rig);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Online] Lane ready: NetworkManager + Relay matchmaking, spawn points, locomotion, vignette.");
    }

    private static GameObject CreatePlayerPrefab()
    {
        EnsureFolder("Assets/Prefabs", "Network");

        Material rivalMaterial = AssetDatabase.LoadAssetAtPath<Material>(RivalMaterialPath);

        if (rivalMaterial == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            rivalMaterial = new Material(lit) { color = new Color(0.75f, 0.22f, 0.2f) };
            AssetDatabase.CreateAsset(rivalMaterial, RivalMaterialPath);
        }

        var root = new GameObject("NetworkPlayer");
        root.AddComponent<NetworkObject>();
        NetworkPlayer player = root.AddComponent<NetworkPlayer>();

        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);

        Transform head = Primitive(PrimitiveType.Sphere, "Head", visual.transform, new Vector3(0.24f, 0.28f, 0.26f), rivalMaterial);
        Transform body = Primitive(PrimitiveType.Capsule, "Body", visual.transform, new Vector3(0.45f, 0.5f, 0.32f), rivalMaterial);
        Transform left = Primitive(PrimitiveType.Cube, "LeftHand", visual.transform, new Vector3(0.07f, 0.09f, 0.15f), rivalMaterial);
        Transform right = Primitive(PrimitiveType.Cube, "RightHand", visual.transform, new Vector3(0.07f, 0.09f, 0.15f), rivalMaterial);

        // The weapon copy is parented to the hand, so the hand's cube scale must not stretch it:
        // the cube becomes a child mesh of an unscaled hand transform.
        right = WrapUnscaled(right, visual.transform);
        left = WrapUnscaled(left, visual.transform);

        var labelObject = new GameObject("NameLabel");
        labelObject.transform.SetParent(visual.transform, false);
        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.text = "Rival";
        label.fontSize = 1.4f;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(1.5f, 0.3f);

        var so = new SerializedObject(player);
        SerializedProperty heroes = so.FindProperty("heroes");
        string[] heroPaths = { "Assets/Data/Heroes/Seven.asset", "Assets/Data/Heroes/Vindicta.asset" };
        heroes.arraySize = heroPaths.Length;

        for (int i = 0; i < heroPaths.Length; i++)
        {
            heroes.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(heroPaths[i]);
        }

        so.FindProperty("visualRoot").objectReferenceValue = visual;
        so.FindProperty("head").objectReferenceValue = head;
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("leftHand").objectReferenceValue = left;
        so.FindProperty("rightHand").objectReferenceValue = right;
        so.FindProperty("nameLabel").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static Transform Primitive(PrimitiveType type, string name, Transform parent, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;

        // Rival visuals are only drawn: a collider would block this player's own shots and walking.
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }

    private static Transform WrapUnscaled(Transform mesh, Transform parent)
    {
        var pivot = new GameObject(mesh.name);
        pivot.transform.SetParent(parent, false);
        mesh.name += "Mesh";
        mesh.SetParent(pivot.transform, false);
        return pivot.transform;
    }

    private static Material CreateVignetteMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(VignetteMaterialPath);

        if (material == null)
        {
            material = new Material(Shader.Find("Deadlock/ComfortVignette"));
            AssetDatabase.CreateAsset(material, VignetteMaterialPath);
        }

        return material;
    }

    private static void CreateSpawnPoints()
    {
        Replace("SpawnPoint_Amber");
        Replace("SpawnPoint_Sapphire");

        var amber = new GameObject("SpawnPoint_Amber");
        amber.transform.SetPositionAndRotation(new Vector3(0f, 0f, -SpawnZ), Quaternion.identity);

        var sapphire = new GameObject("SpawnPoint_Sapphire");
        sapphire.transform.SetPositionAndRotation(new Vector3(0f, 0f, SpawnZ), Quaternion.Euler(0f, 180f, 0f));
    }

    private static void CreateNetwork(GameObject playerPrefab)
    {
        Replace("Network");

        var go = new GameObject("Network");
        UnityTransport transport = go.AddComponent<UnityTransport>();
        NetworkManager manager = go.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
        };
        go.AddComponent<DuelMatchmaker>();
        EditorUtility.SetDirty(manager);
    }

    private static void AddLocomotion(OVRCameraRig rig)
    {
        ComfortLocomotion locomotion = rig.GetComponent<ComfortLocomotion>() ?? rig.gameObject.AddComponent<ComfortLocomotion>();

        // Walls and cover block; the player's own body, minions and souls don't.
        int ignored = LayerMask.GetMask(DeadlockSetupTools.PlayerLayer, DeadlockSetupTools.SoulLayer,
            DeadlockSetupTools.MinionLayer, "UI", "Ignore Raycast");

        var so = new SerializedObject(locomotion);
        so.FindProperty("rig").objectReferenceValue = rig.transform;
        so.FindProperty("head").objectReferenceValue = rig.centerEyeAnchor;
        so.FindProperty("blockingLayers").intValue = ~ignored;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddVignette(OVRCameraRig rig, Material material)
    {
        Replace("ComfortVignette");

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "ComfortVignette";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(rig.centerEyeAnchor, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 1f);
        quad.transform.localScale = new Vector3(4f, 4f, 1f);

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;

        ComfortVignette vignette = quad.AddComponent<ComfortVignette>();
        var so = new SerializedObject(vignette);
        so.FindProperty("locomotion").objectReferenceValue = rig.GetComponent<ComfortLocomotion>();
        so.FindProperty("quad").objectReferenceValue = renderer;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddStatusText(OVRCameraRig rig)
    {
        Replace("MatchmakingStatus");

        var root = new GameObject("MatchmakingStatus");
        root.transform.SetParent(rig.centerEyeAnchor, false);

        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(root.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, -0.35f, 1.5f);

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.text = "";
        label.fontSize = 0.9f;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(1.6f, 0.4f);

        MatchmakingStatusView view = root.AddComponent<MatchmakingStatusView>();
        var so = new SerializedObject(view);
        so.FindProperty("matchmaker").objectReferenceValue = Object.FindObjectOfType<DuelMatchmaker>();
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Replace(string name)
    {
        var matches = new System.Collections.Generic.List<GameObject>();

        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name == name)
            {
                matches.Add(t.gameObject);
            }
        }

        foreach (GameObject match in matches)
        {
            if (match != null)
            {
                Object.DestroyImmediate(match);
            }
        }
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
