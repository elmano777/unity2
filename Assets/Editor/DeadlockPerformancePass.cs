using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Step 36: lag fix for the Lane (measured in Play: 1654 active renderers, 1081 casting shadows, 21 lights,
/// ~27 FPS in the editor). Everything is idempotent and touches only rendering, never gameplay:
/// <list type="bullet">
/// <item>small dressing (MapDetail, StreetLife, awnings, shutters, shop kiosks) stops casting shadows;</item>
/// <item>only every third street lamp keeps a real point light (range 10 m); the rest keep their glass,
/// halo and light pool, which already read as lit;</item>
/// <item>the URP assets' shadow distance drops to 25 m (the lane is read at close range);</item>
/// <item>renderers under the dressing roots are marked static so they can batch.</item>
/// </list>
/// Run it after steps 26, 30, 33, 35 or 28 (they rebuild what it trims).
/// </summary>
public static class DeadlockPerformancePass
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const float ShadowDistance = 25f;
    private const float LampRange = 10f;
    private const int KeepEveryNthLamp = 3;

    private static readonly string[] NoShadowRoots = { "MapDetail", "StreetLife", "Awning", "Shutter" };
    private static readonly string[] UrpAssets =
    {
        "Assets/Settings/URP-Performant.asset",
        "Assets/Settings/URP-Balanced.asset",
        "Assets/Settings/URP-HighFidelity.asset",
    };

    [MenuItem("Deadlock/Setup/36 Performance Pass (shadows, lamps)")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        int noShadow = 0, lamps = 0, lampsOff = 0, batched = 0;

        foreach (Renderer r in Object.FindObjectsOfType<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is SkinnedMeshRenderer) continue;
            if (!UnderAny(r.transform, NoShadowRoots)) continue;

            if (r.shadowCastingMode != ShadowCastingMode.Off)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                noShadow++;
            }

            if (r is MeshRenderer && r.GetComponentInParent<Rigidbody>() == null && !GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.BatchingStatic);
                batched++;
            }
        }

        var lights = new List<Light>();

        foreach (Light l in Object.FindObjectsOfType<Light>(true))
        {
            if (l.name == "StreetLamp") lights.Add(l);
        }

        // Stable order so re-running keeps the same lamps lit.
        lights.Sort((a, b) =>
        {
            int c = a.transform.position.z.CompareTo(b.transform.position.z);
            return c != 0 ? c : a.transform.position.x.CompareTo(b.transform.position.x);
        });

        for (int i = 0; i < lights.Count; i++)
        {
            lamps++;
            bool keep = i % KeepEveryNthLamp == 0;
            lights[i].enabled = keep;
            lights[i].range = LampRange;
            lights[i].shadows = LightShadows.None;
            if (!keep) lampsOff++;
        }

        foreach (string path in UrpAssets)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null) continue;
            asset.shadowDistance = ShadowDistance;
            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[Deadlock Setup] 36 Performance: {noShadow} renderers without shadows, {batched} marked static, " +
                  $"{lampsOff}/{lamps} street lamp lights off, shadow distance {ShadowDistance} m.");
    }

    private static bool UnderAny(Transform t, string[] names)
    {
        for (; t != null; t = t.parent)
        {
            foreach (string n in names)
            {
                if (t.name == n || t.name.StartsWith(n)) return true;
            }
        }

        return false;
    }
}
