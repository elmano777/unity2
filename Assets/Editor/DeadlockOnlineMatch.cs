using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Avance 3, part one: the host runs the world and the rival sees it. Puts the network components on
/// the minions, souls, tower and player body, makes the "Rival" layer the weapons and towers can hit,
/// lists every spawnable prefab for Netcode and replaces the tower placed in the Lane with two tower
/// spawners (one per side) that the host creates once the rival is in the room. Safe to re-run.
/// </summary>
/// <remarks>
/// Re-run it after step 1 (prefabs), 8 (NetworkManager) or 17 (NetworkPlayer prefab) rebuild what it edits.
/// </remarks>
public static class DeadlockOnlineMatch
{
    private const string RivalLayer = "Rival";
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string MinionFolder = "Assets/Prefabs/Minions";
    private const string SoulPath = "Assets/Prefabs/Effects/SoulPickup.prefab";
    private const string TowerPath = "Assets/Prefabs/Structures/Guardian_Tower.prefab";
    private const string PlayerPath = "Assets/Prefabs/Network/NetworkPlayer.prefab";
    private const string ListFolder = "Assets/Data/Network";
    private const string ListPath = ListFolder + "/DuelPrefabs.asset";

    private static readonly string[] WeaponPaths =
    {
        "Assets/Prefabs/Weapons/Seven_Weapon.prefab",
        "Assets/Prefabs/Weapons/Vindicta_Weapon.prefab",
    };

    [MenuItem("Deadlock/Setup/18 Online Match (minions, damage, souls)")]
    public static void Run()
    {
        EnsureLayer(RivalLayer);
        int rivalBit = 1 << LayerMask.NameToLayer(RivalLayer);

        var networked = new List<GameObject>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { MinionFolder }))
        {
            networked.Add(EditPrefab(AssetDatabase.GUIDToAssetPath(guid), root => AddMinionNetwork(root, rivalBit)));
        }

        networked.Add(EditPrefab(SoulPath, AddSoulNetwork));
        networked.Add(EditPrefab(TowerPath, root => AddTowerNetwork(root, rivalBit)));

        foreach (string path in WeaponPaths)
        {
            EditPrefab(path, root => AddLayerToMask(root.GetComponent<Weapon>(), "hitMask", rivalBit));
        }

        EditPrefab(PlayerPath, AddPlayerBody);

        networked.RemoveAll(prefab => prefab == null);
        NetworkPrefabsList list = CreatePrefabList(networked);

        UpdateScene(list, rivalBit);

        AssetDatabase.SaveAssets();
        Debug.Log($"[Deadlock Online Match] {networked.Count} spawnable prefabs listed; towers now spawn from the host.");
    }

    private static GameObject EditPrefab(string path, System.Action<GameObject> edit)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[Deadlock Online Match] Missing prefab {path}; run its setup step first.");
            return null;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            edit(root);
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AddMinionNetwork(GameObject root, int rivalBit)
    {
        Ensure<NetworkObject>(root);
        Ensure<NetworkHealth>(root);

        // The host moves the minion; this is what moves its copy on the other screen.
        NetworkTransform transform = Ensure<NetworkTransform>(root);
        transform.SyncPositionX = transform.SyncPositionY = transform.SyncPositionZ = true;
        transform.SyncRotAngleX = false;
        transform.SyncRotAngleY = true;
        transform.SyncRotAngleZ = false;
        transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
        transform.Interpolate = true;

        AddLayerToMask(root.GetComponent<MinionAgent>(), "targetMask", rivalBit);
    }

    private static void AddSoulNetwork(GameObject root)
    {
        // No network transform: souls bob on the spot, which every screen draws for itself.
        Ensure<NetworkObject>(root);
        Ensure<NetworkSoul>(root);
    }

    private static void AddTowerNetwork(GameObject root, int rivalBit)
    {
        Ensure<NetworkObject>(root);
        Ensure<NetworkHealth>(root);
        Ensure<NetworkTower>(root);
        AddLayerToMask(root.GetComponent<Tower>(), "targetMask", rivalBit);
    }

    /// <summary>The capsule under the head that minions, towers and the rival's gun hit.</summary>
    private static void AddPlayerBody(GameObject root)
    {
        Transform hitbox = root.transform.Find("Hitbox");

        if (hitbox == null)
        {
            hitbox = new GameObject("Hitbox").transform;
            hitbox.SetParent(root.transform, false);
        }

        hitbox.gameObject.layer = LayerMask.NameToLayer(DeadlockSetupTools.PlayerLayer);

        CapsuleCollider capsule = Ensure<CapsuleCollider>(hitbox.gameObject);
        capsule.height = 1.1f;
        capsule.radius = 0.28f;
        capsule.center = Vector3.zero;

        Health health = Ensure<Health>(hitbox.gameObject);
        Ensure<NetworkHealth>(hitbox.gameObject);

        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("faction").enumValueIndex = (int)Faction.Player;
        healthSo.FindProperty("maxHealth").floatValue = 300f;
        healthSo.FindProperty("destroyOnDeath").boolValue = false;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        var playerSo = new SerializedObject(root.GetComponent<NetworkPlayer>());
        playerSo.FindProperty("bodyHealth").objectReferenceValue = health;
        playerSo.FindProperty("bodyHitbox").objectReferenceValue = hitbox;
        playerSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static NetworkPrefabsList CreatePrefabList(List<GameObject> prefabs)
    {
        if (!AssetDatabase.IsValidFolder(ListFolder))
        {
            AssetDatabase.CreateFolder("Assets/Data", "Network");
        }

        AssetDatabase.DeleteAsset(ListPath);

        var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();

        foreach (GameObject prefab in prefabs)
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
        }

        AssetDatabase.CreateAsset(list, ListPath);
        return list;
    }

    private static void UpdateScene(NetworkPrefabsList list, int rivalBit)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        NetworkManager manager = Object.FindObjectOfType<NetworkManager>();

        if (manager == null)
        {
            Debug.LogError("[Deadlock Online Match] No NetworkManager in the Lane. Run setup step 8 first.");
            return;
        }

        // Logs what each machine sees of the duel; lets a two-machine run be checked from the logs.
        if (manager.GetComponent<DuelDiagnostics>() == null)
        {
            manager.gameObject.AddComponent<DuelDiagnostics>();
        }

        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        EditorUtility.SetDirty(manager);

        // Weapons already in the scene keep their own copy of the mask.
        foreach (Weapon weapon in Object.FindObjectsOfType<Weapon>(true))
        {
            AddLayerToMask(weapon, "hitMask", rivalBit);
        }

        // Walking should not stop against the rival's body capsule.
        foreach (ComfortLocomotion locomotion in Object.FindObjectsOfType<ComfortLocomotion>(true))
        {
            var locomotionSo = new SerializedObject(locomotion);
            locomotionSo.FindProperty("blockingLayers").intValue &= ~rivalBit;
            locomotionSo.ApplyModifiedPropertiesWithoutUndo();
        }

        ReplaceSceneTower();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// The tower placed by the lane builder is only on this machine. Swap it for two spawners, one
    /// tower per side, so the host creates them for both players and each can attack the other's.
    /// </summary>
    private static void ReplaceSceneTower()
    {
        GameObject towerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TowerPath);

        foreach (Tower placed in Object.FindObjectsOfType<Tower>(true))
        {
            Object.DestroyImmediate(placed.gameObject);
        }

        DeadlockOnlineSetup.Replace("TowerSpawn_Sapphire");
        DeadlockOnlineSetup.Replace("TowerSpawn_Amber");

        // Off to the side of the lane so each tower covers the path without standing in it.
        MakeTowerSpawner("TowerSpawn_Sapphire", towerPrefab, Faction.Enemy,
            new Vector3(4.5f, 0f, 17f), Quaternion.Euler(0f, 180f, 0f));
        MakeTowerSpawner("TowerSpawn_Amber", towerPrefab, Faction.Player,
            new Vector3(-4.5f, 0f, -17f), Quaternion.identity);
    }

    private static void MakeTowerSpawner(string name, GameObject prefab, Faction faction, Vector3 position, Quaternion rotation)
    {
        var spawnPoint = new GameObject(name);
        spawnPoint.transform.SetPositionAndRotation(position, rotation);

        StructureSpawner spawner = spawnPoint.AddComponent<StructureSpawner>();
        var so = new SerializedObject(spawner);
        so.FindProperty("prefab").objectReferenceValue = prefab;
        so.FindProperty("faction").enumValueIndex = (int)faction;
        so.FindProperty("tier").intValue = 1;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddLayerToMask(Object component, string maskField, int bit)
    {
        if (component == null)
        {
            return;
        }

        var so = new SerializedObject(component);
        SerializedProperty mask = so.FindProperty(maskField);
        mask.intValue |= bit;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();
        return existing != null ? existing : go.AddComponent<T>();
    }

    private static void EnsureLayer(string layerName)
    {
        if (LayerMask.NameToLayer(layerName) != -1)
        {
            return;
        }

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        // 0-7 are Unity's built-in layers and must not be touched.
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);

            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }

        Debug.LogError($"[Deadlock Online Match] No free user layer left for '{layerName}'.");
    }
}
