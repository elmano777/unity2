using UnityEditor;
using UnityEngine;

/// <summary>
/// Swaps the Guardian tower's static model for the rigged one and wires its animation: the idle,
/// attack and collapse clips (from Guardian_Anims.glb), the staff the beam leaves from and the
/// renderers tinted when it falls. Re-run it after setup step 1 or 18 rebuild the tower prefab.
/// Safe to re-run.
/// </summary>
public static class DeadlockAnimatedTower
{
    private const string PrefabPath = "Assets/Prefabs/Structures/Guardian_Tower.prefab";
    private const string ModelPath = "Assets/Models/Structures/Guardian.glb";
    private const string ClipsPath = "Assets/Models/Structures/Guardian_Anims.glb";
    private const string BeamMaterialPath = "Assets/Art/Materials/Combat/Glow_TowerBeam.mat";

    [MenuItem("Deadlock/Setup/21 Animated Tower")]
    public static void Run()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        AnimationClip idle = null;
        AnimationClip attack = null;
        AnimationClip die = null;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ClipsPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
            {
                if (clip.name.EndsWith("Idle")) idle = clip;
                if (clip.name.EndsWith("Attack")) attack = clip;
                if (clip.name.EndsWith("Die")) die = clip;
            }
        }

        if (model == null || idle == null || attack == null || die == null)
        {
            Debug.LogError("[Deadlock Setup] Guardian.glb or its clips (Guardian_Anims.glb) are missing or not imported yet.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        Tower tower = root.GetComponent<Tower>();

        if (tower == null)
        {
            Debug.LogError($"[Deadlock Setup] {PrefabPath}: no Tower.");
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

        Transform tip = null;

        foreach (Transform node in visual.GetComponentsInChildren<Transform>(true))
        {
            if (node.name == "StaffTip")
            {
                tip = node;
            }
        }

        if (tip == null)
        {
            Debug.LogError("[Deadlock Setup] Guardian.glb has no StaffTip bone.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        // The laser: a glowing line from the staff tip to whatever it hit, tinted per side at fire time.
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(BeamMaterialPath);

        if (glow == null)
        {
            glow = new Material(Shader.Find("Deadlock/GlowParticle")) { name = "Glow_TowerBeam" };
            glow.SetColor("_Color", Color.white);
            AssetDatabase.CreateAsset(glow, BeamMaterialPath);
        }

        Transform beamNode = tip.Find("Beam");
        LineRenderer line = beamNode != null ? beamNode.GetComponent<LineRenderer>() : null;

        if (line == null)
        {
            var beamGo = new GameObject("Beam");
                        beamGo.transform.SetParent(tip, false);
            line = beamGo.AddComponent<LineRenderer>();
        }

        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0.18f));
        line.numCapVertices = 6;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = glow;
        line.enabled = false;

        var towerSo = new SerializedObject(tower);
        towerSo.FindProperty("beamOrigin").objectReferenceValue = tip;
        towerSo.FindProperty("beam").objectReferenceValue = line;
        towerSo.ApplyModifiedPropertiesWithoutUndo();

        // The structure tints its renderers when it falls.
        Renderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var structureSo = new SerializedObject(tower);
        SerializedProperty tinted = structureSo.FindProperty("renderersToDarken");
        tinted.arraySize = renderers.Length;

        for (int i = 0; i < renderers.Length; i++)
        {
            tinted.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }

        structureSo.ApplyModifiedPropertiesWithoutUndo();

        TowerAnimator animator = root.GetComponent<TowerAnimator>();

        if (animator == null)
        {
            animator = root.AddComponent<TowerAnimator>();
        }

        var animSo = new SerializedObject(animator);
        animSo.FindProperty("idleClip").objectReferenceValue = idle;
        animSo.FindProperty("attackClip").objectReferenceValue = attack;
        animSo.FindProperty("dieClip").objectReferenceValue = die;
        animSo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Deadlock Setup] Animated tower done: {renderers.Length} renderers, clips {idle.length:0.0}s / {attack.length:0.00}s / {die.length:0.0}s.");
    }
}
