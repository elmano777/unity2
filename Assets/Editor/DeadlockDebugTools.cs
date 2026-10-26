using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Helpers for checking the duel without a headset: a switch that plays the Lane as the offline
/// practice, and a Windows build of just the Lane to run as the second player next to the editor.
/// </summary>
public static class DeadlockDebugTools
{
    private const string PracticeKey = "Deadlock.ForcePractice";
    private const string PracticeMenu = "Deadlock/Debug/Force Practice Run";

    [MenuItem(PracticeMenu)]
    private static void TogglePractice()
    {
        EditorPrefs.SetBool(PracticeKey, !EditorPrefs.GetBool(PracticeKey, false));
    }

    [MenuItem(PracticeMenu, true)]
    private static bool TogglePracticeCheck()
    {
        Menu.SetChecked(PracticeMenu, EditorPrefs.GetBool(PracticeKey, false));
        return true;
    }

    [MenuItem("Deadlock/Debug/Build Windows Test Client")]
    public static void BuildTestClient()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/TestClient"));
        Directory.CreateDirectory(folder);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Lane.unity" },
            locationPathName = Path.Combine(folder, "DeadlockClient.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Deadlock Build] {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0}s -> {options.locationPathName}");
    }
}
