using UnityEditor;
using UnityEngine;

/// <summary>
/// Setup step 34: gives each attacking objective its own way of attacking (see
/// <see cref="StructureAttackFx"/>) instead of the one straight beam they all shared:
/// <list type="bullet">
/// <item>Guardian: a jet of fire from the top of its staff.</item>
/// <item>Walker: a volley of bolts from the eye in the middle of its dome.</item>
/// <item>Base guardians: a beam from their middle eye. Until now they fired from the hidden
/// Guardian body's staff tip, a metre off to one side, and the Sapphire model faced backwards
/// (its face is on -Z in the file, the Amber one's on +Z).</item>
/// </list>
/// Only edits the prefabs, so it can run any time after step 27; step 27 calls it at the end too,
/// so a re-run of 27 keeps these.
/// </summary>
public static class DeadlockStructureAttacks
{
    private const string MaterialDir = "Assets/Art/Materials/Combat";
    private const string ParticlePath = MaterialDir + "/Glow_StructureFx.mat";
    private const string BeamPath = MaterialDir + "/Glow_StructureBeam.mat";

    private const string GuardianPath = "Assets/Prefabs/Structures/Guardian_Tower.prefab";
    private const string WalkerPath = "Assets/Prefabs/Objectives/Walker_Objective.prefab";
    private const string BaseGuardianPath = "Assets/Prefabs/Objectives/BaseGuardian_Objective.prefab";
    private const string BaseGuardianSapphirePath = "Assets/Prefabs/Objectives/BaseGuardian_Sapphire_Objective.prefab";

    // Eye of each model in prefab space, measured on renders (models at the 0.62 scale of step 27,
    // face turned to +Z): the small ring in the middle of the Amber guardian's halo (which stands
    // behind its head), the round eye under the Sapphire guardian's crown, the circle in the middle
    // of the Walker's dome.
    public static readonly Vector3 AmberGuardianEye = new Vector3(0f, 6.2f, -1.5f);
    public static readonly Vector3 SapphireGuardianEye = new Vector3(0f, 6.48f, 0.5f);
    public static readonly Vector3 WalkerEye = new Vector3(0f, 6.8f, 3.3f);

    [MenuItem("Deadlock/Setup/34 Structure Attacks (flame, bolts, eye beams)")]
    public static void Run()
    {
        Material particle = GlowMaterial(ParticlePath, "Deadlock/GlowParticle");
        Material beam = GlowMaterial(BeamPath, "Deadlock/GlowBeam");

        if (particle == null || beam == null)
        {
            return;
        }

        Edit(GuardianPath, root =>
        {
            Tower tower = root.GetComponent<Tower>();
            Transform tip = (Transform)new SerializedObject(tower).FindProperty("beamOrigin").objectReferenceValue;
            Configure(root, StructureAttackFx.Style.FlameJet, tip != null ? tip : root.transform, particle, beam,
                eyeSize: 0.45f, thickness: 0.75f);
        });

        Edit(WalkerPath, root =>
        {
            Transform eye = Eye(root, WalkerEye);
            Configure(root, StructureAttackFx.Style.EyeBolts, eye, particle, beam, eyeSize: 1.2f, thickness: 1.1f);
        });

        Edit(BaseGuardianPath, root => BaseGuardian(root, AmberGuardianEye, modelYaw: 180f, particle, beam));
        Edit(BaseGuardianSapphirePath, root => BaseGuardian(root, SapphireGuardianEye, modelYaw: 0f, particle, beam));

        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup 34] Guardian flame jet, Walker eye bolts and base guardian eye beams set on the prefabs.");
    }

    private static void BaseGuardian(GameObject root, Vector3 eyePosition, float modelYaw, Material particle, Material beam)
    {
        Transform model = FindDeep(root.transform, "BaseGuardianModel");

        if (model != null)
        {
            model.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
        }

        Transform eye = Eye(root, eyePosition);
        Configure(root, StructureAttackFx.Style.EyeBeam, eye, particle, beam, eyeSize: 0.9f, thickness: 0.85f);
    }

    /// <summary>The tower's beam origin, moved to the eye, so the sound and the effect both leave from there.</summary>
    private static Transform Eye(GameObject root, Vector3 local)
    {
        Tower tower = root.GetComponent<Tower>();
        var so = new SerializedObject(tower);
        Transform eye = root.transform.Find("Eye");

        if (eye == null)
        {
            eye = new GameObject("Eye").transform;
            eye.SetParent(root.transform, false);
        }

        eye.localPosition = local;
        eye.localRotation = Quaternion.identity;
        so.FindProperty("beamOrigin").objectReferenceValue = eye;
        so.ApplyModifiedPropertiesWithoutUndo();
        return eye;
    }

    private static void Configure(GameObject root, StructureAttackFx.Style style, Transform origin, Material particle, Material beam, float eyeSize, float thickness)
    {
        StructureAttackFx fx = root.GetComponent<StructureAttackFx>();

        if (fx == null)
        {
            fx = root.AddComponent<StructureAttackFx>();
        }

        var so = new SerializedObject(fx);
        so.FindProperty("style").enumValueIndex = (int)style;
        so.FindProperty("origin").objectReferenceValue = origin;
        so.FindProperty("particleMaterial").objectReferenceValue = particle;
        so.FindProperty("beamMaterial").objectReferenceValue = beam;
        so.FindProperty("eyeSize").floatValue = eyeSize;
        so.FindProperty("thickness").floatValue = thickness;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Edit(string path, System.Action<GameObject> edit)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[Deadlock Setup 34] {path} missing: run step 27 first.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Material GlowMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);

        if (shader == null)
        {
            Debug.LogError($"[Deadlock Setup 34] Shader {shaderName} not found.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Radial")) material.SetFloat("_Radial", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }

        return null;
    }
}
