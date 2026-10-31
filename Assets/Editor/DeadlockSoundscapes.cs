using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

/// <summary>
/// Gives each place its sound: sets the import options of the ambience clips and adds an
/// <see cref="AmbientSoundscape"/> to the garage (TitleScreen), the intro, the hero select and the lane.
/// Re-run it after setup steps 6, 9, 10 or 11 rebuild those scenes. Safe to re-run.
/// </summary>
public static class DeadlockSoundscapes
{
    private const string Root = "Assets/Audio/Ambience";
    private const string ObjectName = "Soundscape";

    private struct BedSpec
    {
        public string clip;
        public float volume;
        public float pan;
    }

    private struct ScatterSpec
    {
        public string[] clips;
        public Vector2 interval;
        public float volume;
        public Vector2 distance;
        public Vector2 height;
    }

    private struct PlaceSpec
    {
        public string scene;
        public string[] replaces;
        public BedSpec[] beds;
        public ScatterSpec[] scatters;
    }

    private static readonly PlaceSpec[] Places =
    {
        // The garage: the four channels of its room tone (L and R across the ears, the surrounds quieter
        // and part way in), and a ring of crystal now and then.
        new PlaceSpec
        {
            scene = "Assets/Scenes/TitleScreen.unity",
            replaces = new[] { "GarageAmbience" },
            beds = new[]
            {
                new BedSpec { clip = "Garage/amb_garage_lp_l.mp3", volume = 0.8f, pan = -1f },
                new BedSpec { clip = "Garage/amb_garage_lp_r.mp3", volume = 0.8f, pan = 1f },
                new BedSpec { clip = "Garage/amb_garage_lp_ls.mp3", volume = 0.45f, pan = -0.5f },
                new BedSpec { clip = "Garage/amb_garage_lp_rs.mp3", volume = 0.45f, pan = 0.5f },
            },
            scatters = new[]
            {
                new ScatterSpec
                {
                    clips = new[] { "Crystal/ambience_base_crystal_ring_01.mp3", "Crystal/ambience_base_crystal_ring_02.mp3", "Crystal/ambience_base_crystal_ring_03.mp3" },
                    interval = new Vector2(10f, 24f), volume = 0.5f, distance = new Vector2(2f, 6f), height = new Vector2(-0.3f, 1.2f),
                },
            },
        },

        // The rooftop of the intro, quiet under the title music.
        new PlaceSpec
        {
            scene = "Assets/Scenes/Intro.unity",
            replaces = new string[0],
            beds = new[] { new BedSpec { clip = "Beds/ambience_bed_rooftops.mp3", volume = 0.4f, pan = 0f } },
            scatters = new ScatterSpec[0],
        },

        // The spirit vision of the hero select: swells of crystal.
        new PlaceSpec
        {
            scene = "Assets/Scenes/HeroSelect.unity",
            replaces = new string[0],
            beds = new BedSpec[0],
            scatters = new[]
            {
                new ScatterSpec
                {
                    clips = new[]
                    {
                        "Crystal/ambience_base_crystal_swell_01.mp3", "Crystal/ambience_base_crystal_swell_02.mp3", "Crystal/ambience_base_crystal_swell_03.mp3",
                        "Crystal/ambience_base_crystal_swell_05.mp3", "Crystal/ambience_base_crystal_swell_06.mp3",
                    },
                    interval = new Vector2(9f, 20f), volume = 0.45f, distance = new Vector2(2f, 6f), height = new Vector2(-0.3f, 1.5f),
                },
            },
        },

        // The street under the bridge: the alley's bed, low enough to fight over, and pigeons.
        new PlaceSpec
        {
            scene = "Assets/Scenes/Lane.unity",
            replaces = new string[0],
            beds = new[] { new BedSpec { clip = "Beds/ambience_bed_alley.mp3", volume = 0.45f, pan = 0f } },
            scatters = new[]
            {
                new ScatterSpec
                {
                    clips = new[] { "Birds/ambience_pigeons_01.mp3", "Birds/ambience_pigeons_02.mp3", "Birds/ambience_pigeons_03.mp3" },
                    interval = new Vector2(12f, 30f), volume = 0.5f, distance = new Vector2(6f, 16f), height = new Vector2(1f, 5f),
                },
            },
        },
    };

    [MenuItem("Deadlock/Setup/23 Soundscapes")]
    public static void Run()
    {
        ImportOptions();

        foreach (PlaceSpec place in Places)
        {
            Apply(place);
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>Beds stay compressed in memory; one-shots are short, so they load decoded and mono for 3D.</summary>
    private static void ImportOptions()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            bool bed = path.Contains("/Beds/") || path.Contains("/Garage/");

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = bed ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;

            if (importer.forceToMono == bed || importer.defaultSampleSettings.loadType != settings.loadType)
            {
                importer.forceToMono = !bed;
                importer.loadInBackground = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }
    }

    private static void Apply(PlaceSpec place)
    {
        Scene scene = SceneManager.GetSceneByPath(place.scene);
        bool opened = !scene.isLoaded;

        if (opened)
        {
            scene = EditorSceneManager.OpenScene(place.scene, OpenSceneMode.Additive);
        }

        var stale = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == ObjectName || System.Array.IndexOf(place.replaces, node.name) >= 0)
                {
                    stale.Add(node.gameObject);
                }
            }
        }

        foreach (GameObject node in stale)
        {
            if (node != null)
            {
                Object.DestroyImmediate(node);
            }
        }

        var go = new GameObject(ObjectName);
        SceneManager.MoveGameObjectToScene(go, scene);
        var soundscape = go.AddComponent<AmbientSoundscape>();
        var so = new SerializedObject(soundscape);

        SerializedProperty beds = so.FindProperty("beds");
        beds.arraySize = place.beds.Length;

        for (int i = 0; i < place.beds.Length; i++)
        {
            SerializedProperty element = beds.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("clip").objectReferenceValue = Load(place.beds[i].clip);
            element.FindPropertyRelative("volume").floatValue = place.beds[i].volume;
            element.FindPropertyRelative("pan").floatValue = place.beds[i].pan;
        }

        SerializedProperty scatters = so.FindProperty("scatters");
        scatters.arraySize = place.scatters.Length;

        for (int i = 0; i < place.scatters.Length; i++)
        {
            ScatterSpec spec = place.scatters[i];
            SerializedProperty element = scatters.GetArrayElementAtIndex(i);
            SerializedProperty clips = element.FindPropertyRelative("clips");
            clips.arraySize = spec.clips.Length;

            for (int c = 0; c < spec.clips.Length; c++)
            {
                clips.GetArrayElementAtIndex(c).objectReferenceValue = Load(spec.clips[c]);
            }

            element.FindPropertyRelative("interval").vector2Value = spec.interval;
            element.FindPropertyRelative("volume").floatValue = spec.volume;
            element.FindPropertyRelative("distance").vector2Value = spec.distance;
            element.FindPropertyRelative("height").vector2Value = spec.height;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (opened)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        Debug.Log($"[Deadlock Setup] Soundscape in {place.scene}: {place.beds.Length} beds, {place.scatters.Length} scatter sets.");
    }

    private static AudioClip Load(string relative)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{relative}");

        if (clip == null)
        {
            Debug.LogError($"[Deadlock Setup] Audio clip missing: {Root}/{relative}");
        }

        return clip;
    }
}
