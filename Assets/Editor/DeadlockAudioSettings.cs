using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RNF-08: volume and subtitle buttons on the title screen, and a subtitle for each hero's select
/// line on the hero select screen. Safe to re-run.
/// </summary>
public static class DeadlockAudioSettings
{
    // Lines from the informe (Table "Frases de los personajes"), matching each hero's selectClip.
    private const string SevenSelectLine = "With these hands I will destroy, and with these hands I will rebuild.";
    private const string VindictaSelectLine =
        "I was a child when they unjustly condemned me. Powerless in every sense of the word. " +
        "But now, in death, I've become all that they feared and more.";

    [MenuItem("Deadlock/Setup/7 Volume And Subtitles")]
    public static void Run()
    {
        SetSelectLine("Assets/Data/Heroes/Seven.asset", SevenSelectLine);
        SetSelectLine("Assets/Data/Heroes/Vindicta.asset", VindictaSelectLine);
        AssetDatabase.SaveAssets();

        BuildTitleSettings();
        BuildHeroSelectSubtitle();
        Debug.Log("[Deadlock Setup] Volume and subtitles done.");
    }

    private static void SetSelectLine(string path, string line)
    {
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(path);
        if (hero == null || !string.IsNullOrEmpty(hero.selectLine)) return;
        hero.selectLine = line;
        EditorUtility.SetDirty(hero);
    }

    private static void BuildTitleSettings()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity", OpenSceneMode.Single);
        Transform canvas = GameObject.Find("TitleCanvas").transform;
        TMP_Text style = GameObject.Find("SubtitleText").GetComponent<TMP_Text>();

        Transform old = canvas.Find("AudioSettings");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject("AudioSettings", typeof(RectTransform));
        root.transform.SetParent(canvas, false);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = Vector2.zero;

        // Under "Partida online": [-] VOLUMEN 100% [+]. Under "Mi misión" (below its label): subtitles.
        const float rowY = -1000f;
        Button down = SmallButton(root.transform, "Button_VolumeDown", "–", new Vector2(-1260f, rowY), new Vector2(240f, 200f), style);
        Button up = SmallButton(root.transform, "Button_VolumeUp", "+", new Vector2(-260f, rowY), new Vector2(240f, 200f), style);
        TMP_Text volume = Label(root.transform, "VolumeLabel", new Vector2(-760f, rowY), new Vector2(720f, 200f), style);
        Button subtitles = SmallButton(root.transform, "Button_Subtitles", "SUBTÍTULOS: SÍ", new Vector2(760f, rowY), new Vector2(1000f, 200f), style);

        var panel = root.AddComponent<AudioSettingsPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("volumeDownButton").objectReferenceValue = down;
        so.FindProperty("volumeUpButton").objectReferenceValue = up;
        so.FindProperty("volumeLabel").objectReferenceValue = volume;
        so.FindProperty("subtitlesButton").objectReferenceValue = subtitles;
        so.FindProperty("subtitlesLabel").objectReferenceValue = subtitles.GetComponentInChildren<TMP_Text>();
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static Button SmallButton(Transform parent, string name, string text, Vector2 pos, Vector2 size, TMP_Text style)
    {
        Button button = DeadlockTitleMenu.MakeButton(parent, name, text, pos, style);
        button.GetComponent<RectTransform>().sizeDelta = size;
        button.GetComponentInChildren<TMP_Text>().fontSize = 84f;
        return button;
    }

    private static TMP_Text Label(Transform parent, string name, Vector2 pos, Vector2 size, TMP_Text style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = style.font;
        tmp.text = "VOLUMEN 100%";
        tmp.fontSize = 84f;
        tmp.characterSpacing = 4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.85f, 0.7f, 1f);
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>
    /// A speech subtitle above the pedestal, on the right half of the canvas the cards leave free.
    /// The header moves over the cards so both fit.
    /// </summary>
    private static void BuildHeroSelectSubtitle()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/HeroSelect.unity", OpenSceneMode.Single);
        Transform canvas = GameObject.Find("HeroSelectCanvas").transform;
        var header = (RectTransform)canvas.Find("HeaderText");
        header.anchoredPosition = new Vector2(-590f, header.anchoredPosition.y);
        TMP_Text style = header.GetComponent<TMP_Text>();
        // Narrow enough to stay over the cards and leave the right half to the subtitle.
        style.enableAutoSizing = false;
        style.fontSize = 150f;

        Transform old = canvas.Find("SelectSubtitle");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var panel = new GameObject("SelectSubtitle", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas, false);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(1100f, 760f);
        prt.sizeDelta = new Vector2(1100f, 440f);
        var image = panel.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.65f);
        image.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(panel.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(40f, 30f);
        trt.offsetMax = new Vector2(-40f, -30f);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = style.font;
        text.fontSize = 60f;
        text.enableAutoSizing = false;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.characterSpacing = 0f;
        text.fontStyle = FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 0.95f, 0.88f, 1f);
        text.raycastTarget = false;

        var manager = Object.FindObjectOfType<HeroSelectManager>();
        var so = new SerializedObject(manager);
        so.FindProperty("selectSubtitle").objectReferenceValue = text;
        so.FindProperty("selectSubtitlePanel").objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
