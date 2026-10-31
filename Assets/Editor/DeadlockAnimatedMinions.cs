using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Swaps a minion's static model for the rigged one and wires its animation: the idle, walk and action
/// clips (from the *_Anims.glb next to the model), the healing orb for medics, and the network
/// component that shows the host's strikes on the other screen. The fake walk bob of the static models is
/// switched off. Re-run it after setup step 1 or 18 rebuild the minion prefabs. Safe to re-run.
/// </summary>
public static class DeadlockAnimatedMinions
{
    private struct Entry
    {
        public string name;
        public string rigRoot;
        public string clipPrefix;
        public float walkClipSpeed;
        public bool healer;
        public string orbHand;
    }

    private static readonly Entry[] Minions =
    {
        new Entry { name = "Amber_Trooper", rigRoot = "Trooper_Rig", clipPrefix = "Trooper", walkClipSpeed = 1.2f },
        new Entry { name = "Amber_Melee", rigRoot = "Melee_Rig", clipPrefix = "Melee", walkClipSpeed = 1.1f },
        new Entry { name = "Amber_Medic", rigRoot = "Medic_Rig", clipPrefix = "Medic", walkClipSpeed = 1.0f, healer = true, orbHand = "LeftHand" },
        new Entry { name = "Sapphire_Trooper", rigRoot = "STrooper_Rig", clipPrefix = "STrooper", walkClipSpeed = 1.2f },
        new Entry { name = "Sapphire_Melee", rigRoot = "SMelee_Rig", clipPrefix = "SMelee", walkClipSpeed = 1.1f },
        new Entry { name = "Sapphire_Medic", rigRoot = "SMedic_Rig", clipPrefix = "SMedic", walkClipSpeed = 1.0f, healer = true, orbHand = "LeftHand" },
    };

    private const string PrefabFolder = "Assets/Prefabs/Minions";
    private const string ModelFolder = "Assets/Models/Minions";
    private const string OrbMaterialPath = "Assets/Art/Materials/Combat/Glow_HealOrb.mat";

    [MenuItem("Deadlock/Setup/22 Animated Minions")]
    public static void Run()
    {
        foreach (Entry entry in Minions)
        {
            Apply(entry);
        }

        AssetDatabase.SaveAssets();
    }

    private static void Apply(Entry entry)
    {
        string prefabPath = $"{PrefabFolder}/{entry.name}.prefab";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{entry.name}.glb");
        string clipsPath = $"{ModelFolder}/{entry.name}_Anims.glb";

        AnimationClip idle = null;
        AnimationClip walk = null;
        AnimationClip attack = null;
        AnimationClip heal = null;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(clipsPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
            {
                if (clip.name.EndsWith("Idle")) idle = clip;
                if (clip.name.EndsWith("Walk")) walk = clip;
                if (clip.name.EndsWith("Attack")) attack = clip;
                if (clip.name.EndsWith("Heal")) heal = clip;
            }
        }

        if (model == null || idle == null || walk == null || attack == null || (entry.healer && heal == null))
        {
            Debug.LogError($"[Deadlock Setup] {entry.name}: model or clips ({clipsPath}) missing or not imported yet.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        MinionAgent agent = root.GetComponent<MinionAgent>();

        if (agent == null)
        {
            Debug.LogError($"[Deadlock Setup] {prefabPath}: no MinionAgent.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        // The previous model: found by name, or as a "missing prefab" placeholder when the glb it
        // pointed at was replaced and its ids no longer exist.
        foreach (Transform child in root.transform)
        {
            if (child.name.StartsWith("Visual") || PrefabUtility.GetPrefabInstanceStatus(child.gameObject) == PrefabInstanceStatus.MissingAsset)
            {
                Object.DestroyImmediate(child.gameObject);
                break;
            }
        }

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // The skeleton does the walking now: no fake bob, roll or lean on top of it.
        var agentSo = new SerializedObject(agent);
        agentSo.FindProperty("visualRoot").objectReferenceValue = visual.transform;
        agentSo.FindProperty("bobHeight").floatValue = 0f;
        agentSo.FindProperty("walkRoll").floatValue = 0f;
        agentSo.FindProperty("walkLean").floatValue = 0f;
        agentSo.ApplyModifiedPropertiesWithoutUndo();

        MinionAnimator animator = root.GetComponent<MinionAnimator>();

        if (animator == null)
        {
            animator = root.AddComponent<MinionAnimator>();
        }

        var animSo = new SerializedObject(animator);
        animSo.FindProperty("idleClip").objectReferenceValue = idle;
        animSo.FindProperty("walkClip").objectReferenceValue = walk;
        animSo.FindProperty("attackClip").objectReferenceValue = attack;
        animSo.FindProperty("healClip").objectReferenceValue = entry.healer ? heal : null;
        animSo.FindProperty("rigRootName").stringValue = entry.rigRoot;
        animSo.FindProperty("walkClipSpeed").floatValue = entry.walkClipSpeed;

        if (entry.healer)
        {
            animSo.FindProperty("orbMaterial").objectReferenceValue = OrbMaterial();
            animSo.FindProperty("orbHandBone").stringValue = entry.orbHand;
        }

        animSo.ApplyModifiedPropertiesWithoutUndo();

        // The joining player's copy acts out what the host's minion did.
        if (root.GetComponent<NetworkObject>() != null && root.GetComponent<NetworkMinionActions>() == null)
        {
            root.AddComponent<NetworkMinionActions>();
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log($"[Deadlock Setup] {entry.name} animated: idle {idle.length:0.0}s, walk {walk.length:0.0}s, attack {attack.length:0.00}s" +
                  (entry.healer ? $", heal {heal.length:0.00}s." : "."));
    }

    private static Material OrbMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(OrbMaterialPath);

        if (material == null)
        {
            material = new Material(Shader.Find("Deadlock/GlowParticle")) { name = "Glow_HealOrb" };
            material.SetColor("_Color", new Color(0.45f, 1f, 0.55f, 1f));
            AssetDatabase.CreateAsset(material, OrbMaterialPath);
        }

        return material;
    }
}
