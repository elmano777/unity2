using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Finishing touches shared by every scene: all TextMeshPro text glows like the intro's title art
/// (green for the ritual's text, amber for the warm labels), and the laser clicks with either
/// trigger instead of only the left one. Safe to re-run; the lobby, spirit-vision and intro
/// builders call <see cref="ApplyToOpenScene"/> themselves.
/// </summary>
public static class DeadlockMenuPolish
{
    private static readonly string[] Scenes =
    {
        "Assets/Scenes/Intro.unity",
        "Assets/Scenes/TitleScreen.unity",
        "Assets/Scenes/HeroSelect.unity",
        "Assets/Scenes/Lane.unity",
    };

    private const string MaterialDir = "Assets/Art/Materials/Text";

    private static readonly Color GreenGlow = new Color(0.2f, 1f, 0.62f, 0.55f);
    private static readonly Color WarmGlow = new Color(1f, 0.6f, 0.22f, 0.5f);

    [MenuItem("Deadlock/Setup/12 Glowing Text And Both Triggers")]
    public static void Run()
    {
        foreach (string path in Scenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log("[Deadlock Setup] Glowing text and both triggers applied.");
    }

    public static void ApplyToOpenScene()
    {
        foreach (TMP_Text text in Object.FindObjectsOfType<TMP_Text>(true))
        {
            Glow(text);
        }

        // OVRInput's "Primary" trigger is the left controller's; add the right one.
        foreach (OVRInputModule module in Object.FindObjectsOfType<OVRInputModule>(true))
        {
            module.joyPadClickButton = OVRInput.Button.PrimaryIndexTrigger | OVRInput.Button.SecondaryIndexTrigger;
            EditorUtility.SetDirty(module);
        }
    }

    /// <summary>Gives a text on the default font the glowing material that matches its colour.</summary>
    public static void Glow(TMP_Text text)
    {
        TMP_FontAsset font = text.font != null ? text.font : TMP_Settings.defaultFontAsset;
        if (font == null || font != TMP_Settings.defaultFontAsset) return;

        Color c = text.color;
        bool warm = c.r > c.g + 0.1f;
        text.fontSharedMaterial = Material(font, warm);
        EditorUtility.SetDirty(text);
    }

    private static Material Material(TMP_FontAsset font, bool warm)
    {
        DeadlockSceneKit.EnsureFolder(MaterialDir);
        string path = $"{MaterialDir}/{font.name} Glow {(warm ? "Warm" : "Green")}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            m = new Material(font.material) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(m, path);
        }

        // The mobile SDF shader the font ships with has no glow; the full one does.
        m.shader = Shader.Find("TextMeshPro/Distance Field");
        m.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
        m.EnableKeyword(ShaderUtilities.Keyword_Glow);
        m.SetColor(ShaderUtilities.ID_GlowColor, warm ? WarmGlow : GreenGlow);
        m.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
        m.SetFloat(ShaderUtilities.ID_GlowInner, 0.06f);
        m.SetFloat(ShaderUtilities.ID_GlowOuter, 0.45f);
        m.SetFloat(ShaderUtilities.ID_GlowPower, 0.7f);
        m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.05f);
        ShaderUtilities.UpdateShaderRatios(m);
        EditorUtility.SetDirty(m);
        return m;
    }
}
