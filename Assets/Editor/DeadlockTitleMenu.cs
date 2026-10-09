using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds the "Partida online" and "Mi misión" buttons (RF-01) to the TitleScreen scene. Safe to re-run.</summary>
public static class DeadlockTitleMenu
{
    [MenuItem("Deadlock/Setup/4 Title Menu Buttons")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity", OpenSceneMode.Single);
        var mgr = Object.FindObjectOfType<TitleScreenManager>();
        Transform canvas = GameObject.Find("TitleCanvas").transform;
        TMP_Text subtitle = GameObject.Find("SubtitleText").GetComponent<TMP_Text>();
        subtitle.text = "Apunta con el láser y presiona el gatillo";

        foreach (string n in new[] { "Button_Online", "Button_Mission" })
        {
            Transform old = canvas.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        Button online = MakeButton(canvas, "Button_Online", "PARTIDA ONLINE", new Vector2(-760f, -520f), subtitle);
        Button mission = MakeButton(canvas, "Button_Mission", "MI MISIÓN", new Vector2(760f, -520f), subtitle);

        var tag = new GameObject("RecommendedLabel", typeof(RectTransform));
        tag.transform.SetParent(mission.transform, false);
        var rt = tag.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -215f);
        rt.sizeDelta = new Vector2(1300f, 120f);
        var label = tag.AddComponent<TextMeshProUGUI>();
        label.font = subtitle.font;
        label.text = "RECOMENDADO LA PRIMERA VEZ";
        label.fontSize = 72f;
        label.characterSpacing = 6f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.85f, 0.55f, 0.18f, 0.9f);
        label.raycastTarget = false;

        var so = new SerializedObject(mgr);
        so.FindProperty("onlineButton").objectReferenceValue = online;
        so.FindProperty("missionButton").objectReferenceValue = mission;
        so.FindProperty("missionRecommendedLabel").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Setup] Title menu buttons added.");
    }

    private static Button MakeButton(Transform parent, string name, string text, Vector2 pos, TMP_Text styleSource)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1300f, 300f);

        var img = go.GetComponent<Image>();
        img.color = Color.white;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = new ColorBlock
        {
            normalColor = new Color(0.85f, 0.45f, 0.1f, 1f),
            highlightedColor = new Color(1f, 0.62f, 0.2f, 1f),
            pressedColor = new Color(0.65f, 0.32f, 0.06f, 1f),
            selectedColor = new Color(0.85f, 0.45f, 0.1f, 1f),
            disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        var tgo = new GameObject("Label", typeof(RectTransform));
        tgo.transform.SetParent(go.transform, false);
        var trt = tgo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var tmp = tgo.AddComponent<TextMeshProUGUI>();
        tmp.font = styleSource.font;
        tmp.text = text;
        tmp.fontSize = 120f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.characterSpacing = 8f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.08f, 0.05f, 0.02f, 1f);
        tmp.raycastTarget = false;
        return btn;
    }
}
