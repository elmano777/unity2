using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gives the Lane the welcome both players hear before an online match: copies of the hero select's story
/// overlay and black fader under the camera (saved once as prefabs), the <see cref="MatchStartSync"/> that makes
/// both start together next to the NetworkManager, and the <see cref="WelcomeSequence"/> that runs it.
/// Re-run it after setup step 2, 8 or 18 rebuild the Lane. Safe to re-run.
/// </summary>
public static class DeadlockWelcomeSetup
{
    private const string HeroSelectPath = "Assets/Scenes/HeroSelect.unity";
    private const string LanePath = "Assets/Scenes/Lane.unity";
    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string OverlayPrefab = PrefabFolder + "/IntroOverlay.prefab";
    private const string FaderPrefab = PrefabFolder + "/ScreenFader.prefab";

    [MenuItem("Deadlock/Setup/25 Welcome (lane)")]
    public static void Run()
    {
        SavePrefabs();

        var scene = EditorSceneManager.OpenScene(LanePath, OpenSceneMode.Single);
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        var network = Object.FindObjectOfType<DuelMatchmaker>();

        if (rig == null || network == null)
        {
            Debug.LogError("[Deadlock Welcome] The Lane has no camera rig or no Network object: run setup steps 2 and 8 first.");
            return;
        }

        Transform eye = rig.centerEyeAnchor;
        ScreenFader fader = eye.GetComponentInChildren<ScreenFader>(true);
        IntroSequencePlayer intro = eye.GetComponentInChildren<IntroSequencePlayer>(true);

        if (fader == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FaderPrefab), eye);
            fader = instance.GetComponent<ScreenFader>();
        }

        if (intro == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPrefab), eye);
            intro = instance.GetComponent<IntroSequencePlayer>();
        }

        MatchStartSync sync = network.GetComponent<MatchStartSync>();

        if (sync == null)
        {
            sync = network.gameObject.AddComponent<MatchStartSync>();
        }

        Transform old = GameObject.Find("Welcome") != null ? GameObject.Find("Welcome").transform : null;

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        var go = new GameObject("Welcome");
        var welcome = go.AddComponent<WelcomeSequence>();
        var so = new SerializedObject(welcome);
        so.FindProperty("intro").objectReferenceValue = intro;
        so.FindProperty("fader").objectReferenceValue = fader;
        so.FindProperty("matchmaker").objectReferenceValue = network;
        so.FindProperty("sync").objectReferenceValue = sync;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Welcome] Lane ready: welcome sequence, start sync, story overlay and fader.");
    }

    /// <summary>Copies the hero select's IntroOverlay and ScreenFader into prefabs so the Lane can have its own.</summary>
    private static void SavePrefabs()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }

        if (File.Exists(OverlayPrefab) && File.Exists(FaderPrefab))
        {
            return;
        }

        EditorSceneManager.OpenScene(HeroSelectPath, OpenSceneMode.Single);

        var overlay = Object.FindObjectOfType<IntroSequencePlayer>(true);
        var fader = Object.FindObjectOfType<ScreenFader>(true);

        if (overlay == null || fader == null)
        {
            Debug.LogError("[Deadlock Welcome] HeroSelect has no IntroOverlay or ScreenFader to copy.");
            return;
        }

        PrefabUtility.SaveAsPrefabAsset(overlay.gameObject, OverlayPrefab);
        PrefabUtility.SaveAsPrefabAsset(fader.gameObject, FaderPrefab);
        AssetDatabase.SaveAssets();
    }
}
