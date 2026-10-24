using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// "Atrás" everywhere past the garage: a back rune (laser) plus B / Y in the hero select, and
/// hold B / Y in the lane, which also leaves the online room or the mission. Each lives on its own
/// "BackToGarage" root, so re-running steps 8 and 10 doesn't remove it; this step is safe to re-run.
/// </summary>
public static class DeadlockBackButtons
{
    [MenuItem("Deadlock/Setup/13 Back Buttons")]
    public static void Run()
    {
        AddToHeroSelect();
        AddToLane();
        Debug.Log("[Deadlock Setup] Back buttons added to HeroSelect and Lane.");
    }

    private static void AddToHeroSelect()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/HeroSelect.unity", OpenSceneMode.Single);
        DeadlockOnlineSetup.Replace("BackToGarage");

        var root = new GameObject("BackToGarage");
        BackToGarage back = root.AddComponent<BackToGarage>();

        // A rune low on the left of the confirm sigil, turned towards the player.
        var rune = new GameObject("BackRune");
        rune.transform.SetParent(root.transform, false);
        rune.transform.localPosition = new Vector3(-1.35f, 0.8f, 1.2f);
        rune.transform.localRotation = Quaternion.Euler(0f, -28f, 0f);
        var collider = rune.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.8f, 0.36f, 0.08f);

        var visual = new GameObject("Visual").transform;
        visual.SetParent(rune.transform, false);
        Material aura = DeadlockSceneKit.Glow("Glow_RuneAura", new Color(0.15f, 0.85f, 0.45f, 0.55f), 1f);
        Material line = DeadlockSceneKit.Glow("Glow_RitualLine", new Color(0.25f, 1f, 0.5f, 0.9f), 0f, 1.2f);
        GameObject glow = DeadlockSceneKit.Primitive(PrimitiveType.Quad, "Aura", visual, new Vector3(0f, 0f, 0.02f), new Vector3(1.05f, 0.6f, 1f), aura);
        var frame = new[] { new Vector3(-0.38f, -0.16f, 0f), new Vector3(0.38f, -0.16f, 0f), new Vector3(0.38f, 0.16f, 0f), new Vector3(-0.38f, 0.16f, 0f) };
        LineRenderer border = DeadlockSceneKit.Line("Frame", visual, frame, 0.012f, line, true, false);
        DeadlockSceneKit.Text("Label", visual, new Vector3(0f, 0.04f, 0f), "ATRÁS", 1.1f, new Color(0.9f, 1f, 0.93f), new Vector2(0.75f, 0.2f), FontStyles.Bold);
        DeadlockSceneKit.Text("Sub", visual, new Vector3(0f, -0.09f, 0f), "volver al garage  (B / Y)", 0.5f, new Color(0.6f, 0.95f, 0.75f, 0.9f), new Vector2(0.75f, 0.1f));

        WorldButton button = rune.AddComponent<WorldButton>();
        var so = new SerializedObject(button);
        so.FindProperty("scaleTarget").objectReferenceValue = visual;
        SerializedProperty glowRenderers = so.FindProperty("glowRenderers");
        glowRenderers.arraySize = 2;
        glowRenderers.GetArrayElementAtIndex(0).objectReferenceValue = glow.GetComponent<Renderer>();
        glowRenderers.GetArrayElementAtIndex(1).objectReferenceValue = border;
        so.FindProperty("hoverGlow").floatValue = 2.4f;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(button.OnClick, new UnityAction(back.GoBack));

        DeadlockMenuPolish.ApplyToOpenScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void AddToLane()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Lane.unity", OpenSceneMode.Single);
        DeadlockOnlineSetup.Replace("BackToGarage");
        DeadlockOnlineSetup.Replace("BackToGarageHint");
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();

        var root = new GameObject("BackToGarage");
        BackToGarage back = root.AddComponent<BackToGarage>();

        // Head-locked line under the matchmaking status: the hint at first, then the hold progress.
        TextMeshPro label = DeadlockSceneKit.Text("HoldLabel", rig != null ? rig.centerEyeAnchor : root.transform,
            new Vector3(0f, -0.62f, 1.5f), "", 0.6f, new Color(0.75f, 1f, 0.85f, 0.9f), new Vector2(1.6f, 0.2f));
        label.name = "BackToGarageHint";

        var so = new SerializedObject(back);
        so.FindProperty("holdSeconds").floatValue = 1.2f;
        so.FindProperty("holdLabel").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        DeadlockMenuPolish.ApplyToOpenScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
