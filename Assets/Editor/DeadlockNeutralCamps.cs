using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Neutral camps: one in a recess of each district alley, so two on each side of the map. Builds
/// the creature prefab from the extracted <c>Neutral.glb</c>, lists it for Netcode and places the
/// four <see cref="NeutralCamp"/>s where step 26 left their dens. Safe to re-run.
/// </summary>
/// <remarks>Run it after step 26 (which cuts the recesses) and again after 18 or 26.</remarks>
public static class DeadlockNeutralCamps
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string ModelPath = "Assets/Models/Neutrals/Neutral.glb";
    private const string PrefabFolder = "Assets/Prefabs/Neutrals";
    public const string PrefabPath = PrefabFolder + "/Neutral_Creep.prefab";
    private const string SoulPath = "Assets/Prefabs/Effects/SoulPickup.prefab";
    private const string FadePath = "Assets/Art/Materials/Combat/Minion_DeathFade.mat";
    private const string MaterialDir = "Assets/Art/Materials/Neutrals";
    private const string ListPath = "Assets/Data/Network/DuelPrefabs.asset";

    /// <summary>Height the creature is scaled to: bigger than a minion, smaller than a tower.</summary>
    private const float CreatureHeight = 3f;

    [MenuItem("Deadlock/Setup/32 Neutral Camps")]
    public static void Run()
    {
        GameObject prefab = BuildPrefab();

        if (prefab == null)
        {
            return;
        }

        ListForNetwork(prefab);
        PlaceCamps(prefab);
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Neutral camps: prefab built and 4 camps placed.");
    }

    private static GameObject BuildPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        if (model == null)
        {
            Debug.LogError($"[Deadlock Setup] {ModelPath} missing. Convert Neutral.glb (gltf-transform copy → png → resize) and put it there.");
            return null;
        }

        DeadlockSceneKit.EnsureFolder(PrefabFolder);
        DeadlockSceneKit.EnsureFolder(MaterialDir);

        var root = new GameObject("Neutral_Creep");
        root.layer = LayerMask.NameToLayer(DeadlockSetupTools.MinionLayer);

        // Looks: the model scaled to size, turned to face +Z, standing on the root.
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        DeadlockExpandedMap.StaticizeSkins(instance);
        DeadlockExpandedMap.OpaqueMaterials(instance);

        Bounds raw = RendererBounds(instance);
        float scale = CreatureHeight / Mathf.Max(0.01f, raw.size.y);
        instance.transform.localScale = Vector3.one * scale;
        Bounds b = RendererBounds(instance);
        instance.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        b = RendererBounds(instance);

        foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(c);
        }

        // Hitbox: a capsule round the body, on the Minion layer the guns and abilities already hit.
        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.35f, 0.6f, 1.4f);
        capsule.height = Mathf.Max(capsule.radius * 2f, b.size.y * 0.95f);
        capsule.center = new Vector3(0f, capsule.height / 2f, 0f);

        Health health = root.AddComponent<Health>();
        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("faction").enumValueIndex = (int)Faction.Neutral;
        healthSo.FindProperty("maxHealth").floatValue = 320f;
        healthSo.FindProperty("destroyOnDeath").boolValue = false;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(root.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, CreatureHeight * 0.62f, 0.6f);

        Material boltMat = DeadlockSceneKit.Glow("Neutral_Bolt", new Color(0.55f, 1f, 0.35f, 0.9f), 0f);
        var boltGo = new GameObject("Bolt");
        boltGo.transform.SetParent(root.transform, false);
        LineRenderer bolt = boltGo.AddComponent<LineRenderer>();
        bolt.useWorldSpace = true;
        bolt.positionCount = 2;
        bolt.widthMultiplier = 0.12f;
        bolt.sharedMaterial = boltMat;
        bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bolt.enabled = false;

        NeutralCreep creep = root.AddComponent<NeutralCreep>();
        var so = new SerializedObject(creep);
        so.FindProperty("soulPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(SoulPath);
        so.FindProperty("soulValue").intValue = 30;
        so.FindProperty("soulDrops").intValue = 2;
        so.FindProperty("visualRoot").objectReferenceValue = visual.transform;
        so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        so.FindProperty("bolt").objectReferenceValue = bolt;
        so.FindProperty("sightBlockers").intValue = LayerMask.GetMask(DeadlockSetupTools.GroundLayer);
        so.FindProperty("deathFadeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(FadePath);
        so.ApplyModifiedPropertiesWithoutUndo();

        AddHealthBar(root, health, capsule.height + 0.4f);

        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkHealth>();
        root.AddComponent<NetworkNeutralActions>();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static Bounds RendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.one);

        foreach (Renderer r in renderers)
        {
            b.Encapsulate(r.bounds);
        }

        return b;
    }

    /// <summary>The same two-quad bar as the minions' (step 14), in the camps' green.</summary>
    private static void AddHealthBar(GameObject root, Health health, float height)
    {
        Material back = UnlitMaterial("Bar_Neutral_Back", new Color(0.05f, 0.05f, 0.06f, 1f));
        Material fill = UnlitMaterial("Bar_Neutral", new Color(0.55f, 1f, 0.35f, 1f));

        var bar = new GameObject("HealthBar");
        bar.transform.SetParent(root.transform, false);
        bar.transform.localPosition = new Vector3(0f, height, 0f);

        var visual = new GameObject("Visual");
        visual.transform.SetParent(bar.transform, false);
        Quad("Back", visual.transform, new Vector3(0f, 0f, 0.01f), new Vector3(1.4f, 0.16f, 1f), back);

        var pivot = new GameObject("FillPivot");
        pivot.transform.SetParent(visual.transform, false);
        pivot.transform.localPosition = new Vector3(-0.68f, 0f, 0f);
        GameObject fillQuad = Quad("Fill", pivot.transform, new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.12f, 1f), fill);
        fillQuad.transform.localScale = new Vector3(1.36f, 0.12f, 1f);
        fillQuad.transform.localPosition = new Vector3(0.68f, 0f, 0f);
        visual.SetActive(false);

        HealthBar healthBar = bar.AddComponent<HealthBar>();
        var so = new SerializedObject(healthBar);
        so.FindProperty("health").objectReferenceValue = health;
        so.FindProperty("fillPivot").objectReferenceValue = pivot.transform;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject Quad(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = scale;
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return quad;
    }

    private static Material UnlitMaterial(string name, Color color)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Adds the creature to the duel's spawnable prefabs (step 18 also does it when re-run).</summary>
    private static void ListForNetwork(GameObject prefab)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ListPath);

        if (list == null)
        {
            Debug.LogWarning("[Deadlock Setup] DuelPrefabs list missing: run step 18, then this one.");
            return;
        }

        foreach (NetworkPrefab entry in list.PrefabList)
        {
            if (entry.Prefab == prefab)
            {
                return;
            }
        }

        list.Add(new NetworkPrefab { Prefab = prefab });
        EditorUtility.SetDirty(list);
    }

    private static void PlaceCamps(GameObject prefab)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject old = GameObject.Find("NeutralCamps");
        if (old != null) Object.DestroyImmediate(old);

        Transform root = new GameObject("NeutralCamps").transform;

        foreach (int half in new[] { 1, -1 })
        {
            foreach (int side in new[] { 1, -1 })
            {
                Vector3 position = DeadlockExpandedMap.CampPosition(half, side, out Quaternion facing);
                string name = $"Camp_{(half > 0 ? "Amber" : "Sapphire")}_{(side > 0 ? "R" : "L")}";
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(position, facing);

                NeutralCamp camp = go.AddComponent<NeutralCamp>();
                var so = new SerializedObject(camp);
                SerializedProperty slots = so.FindProperty("slots");
                slots.arraySize = 2;

                for (int i = 0; i < 2; i++)
                {
                    SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                    slot.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                    slot.FindPropertyRelative("localPosition").vector3Value = new Vector3(i == 0 ? -2f : 2f, 0f, 0.4f);
                    slot.FindPropertyRelative("yaw").floatValue = i == 0 ? 12f : -12f;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
