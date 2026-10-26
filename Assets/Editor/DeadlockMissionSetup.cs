using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Avance 3, part two: "Mi misión". Adds to the Lane the floating hint panel and the
/// <see cref="MissionGuide"/> that walks a first-time player through the loop. Both stay idle in a
/// duel: the guide only wakes up in a practice run. Safe to re-run.
/// </summary>
public static class DeadlockMissionSetup
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";

    [MenuItem("Deadlock/Setup/19 Mission Practice (guide and hints)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        DeadlockOnlineSetup.Replace("HintBoard");
        DeadlockOnlineSetup.Replace("MissionGuide");

        BuildHintBoard();

        var guide = new GameObject("MissionGuide");
        guide.AddComponent<MissionGuide>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Mission] Hint panel and mission guide added to the Lane.");
    }

    private static void BuildHintBoard()
    {
        var root = new GameObject("HintBoard");
        HintBoard board = root.AddComponent<HintBoard>();

        // Dark backing so the glowing text stays readable against the street.
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Panel";
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.transform.SetParent(root.transform, false);
        panel.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        panel.transform.localScale = new Vector3(1.7f, 0.62f, 1f);

        var material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.02f, 0.05f, 0.05f, 0.62f) };
        MeshRenderer panelRenderer = panel.GetComponent<MeshRenderer>();
        panelRenderer.sharedMaterial = material;
        panelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        panelRenderer.receiveShadows = false;

        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(root.transform, false);

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.fontSize = 1.15f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.55f, 1f, 0.8f, 1f);
        label.enableWordWrapping = true;
        label.rectTransform.sizeDelta = new Vector2(1.6f, 0.58f);
        DeadlockMenuPolish.Glow(label);

        var so = new SerializedObject(board);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("panel").objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
