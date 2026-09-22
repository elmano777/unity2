using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Makes Play in the editor always start at the title screen, whatever scene is open, so testing
/// runs the real flow: title → hero select → lane. Toggle it off from the Deadlock menu to play the
/// open scene directly.
/// </summary>
[InitializeOnLoad]
public static class DeadlockPlayFromTitle
{
    private const string TitleScenePath = "Assets/Scenes/TitleScreen.unity";
    private const string MenuPath = "Deadlock/Play From Title Screen";
    private const string PrefKey = "Deadlock.PlayFromTitle";

    static DeadlockPlayFromTitle()
    {
        EditorApplication.delayCall += Apply;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void Apply()
    {
        EditorSceneManager.playModeStartScene = Enabled
            ? AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath)
            : null;
    }
}
