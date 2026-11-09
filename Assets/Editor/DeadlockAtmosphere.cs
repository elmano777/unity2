using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Setup step 29: atmosphere pass over the menu scenes so they stop reading as grey boxes.
/// - Post-processing on every camera + a global Volume per scene (ACES, bloom, grading, vignette),
///   HDR on the PC pipeline asset so emissive windows / flames bloom.
/// - Garage (TitleScreen / OracleLobby): procedural wood floor, wallpaper, wainscot and rug textures,
///   coffered ceiling, mouldings, bookcases full of books, fireplace with fire, armchairs, a writing
///   desk, chandelier, sconces, moonlight shafts, floating dust and a baked reflection probe.
/// - Intro rooftop: night instead of dusk, moon, searchlights sweeping over the city, string lights,
///   puddles, steam, an antenna beacon and a reflection probe.
/// - Spirit vision (HeroSelect): grading + drifting motes.
/// Everything new lives under a root called "Atmosphere" in each scene; re-running rebuilds it.
/// Static props are merged into one mesh per material (saved under Assets/Art/Meshes/Menus) to keep
/// draw calls low on Quest.
/// </summary>
public static class DeadlockAtmosphere
{
    private const string RootName = "Atmosphere";
    private const string TexDir = "Assets/Art/Textures/Menus";
    private const string ProfileDir = "Assets/Settings/Volumes";

    // Garage room (matches DeadlockOracleLobby).
    private const float HW = 7f;
    private const float BackZ = 6.5f;
    private const float FrontZ = -7.5f;
    private const float RoomH = 7f;
    private const float PanelFace = 0.08f; // wainscot thickness off the wall

    [MenuItem("Deadlock/Setup/29 Atmosphere Pass (menus)")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Run();
    }

    /// <summary>Builds everything without any dialog (safe to call from automation).</summary>
    public static string Run()
    {
        string previous = SceneManager.GetActiveScene().path;
        var log = new System.Text.StringBuilder();

        SetupPipeline(log);
        BuildLobby(log);
        BuildIntro(log);
        BuildSpirit(log);

        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        Debug.Log("[Atmosphere] " + log);
        return log.ToString();
    }

    // =====================================================================================
    // Pipeline + post-processing
    // =====================================================================================

    private static void SetupPipeline(System.Text.StringBuilder log)
    {
        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-HighFidelity.asset");
        if (pc != null && !pc.supportsHDR)
        {
            pc.supportsHDR = true;
            EditorUtility.SetDirty(pc);
            log.Append("HDR on (PC). ");
        }
    }

    private static VolumeProfile Profile(string name, float exposure, float contrast, float saturation,
        float bloomIntensity, float bloomThreshold, Vector4 shadows, Vector4 highlights, float vignette, Color vignetteColor)
    {
        DeadlockSceneKit.EnsureFolder(ProfileDir);
        string path = $"{ProfileDir}/{name}.asset";
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

        if (p == null)
        {
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
        }

        foreach (VolumeComponent c in p.components.ToArray())
        {
            AssetDatabase.RemoveObjectFromAsset(c);
            Object.DestroyImmediate(c, true);
        }
        p.components.Clear();

        var tone = p.Add<Tonemapping>();
        tone.mode.Override(TonemappingMode.ACES);

        var bloom = p.Add<Bloom>();
        bloom.threshold.Override(bloomThreshold);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.72f);
        bloom.highQualityFiltering.Override(false);

        var color = p.Add<ColorAdjustments>();
        color.postExposure.Override(exposure);
        color.contrast.Override(contrast);
        color.saturation.Override(saturation);

        var smh = p.Add<ShadowsMidtonesHighlights>();
        smh.shadows.Override(shadows);
        smh.highlights.Override(highlights);

        var vig = p.Add<Vignette>();
        vig.intensity.Override(vignette);
        vig.smoothness.Override(0.45f);
        vig.color.Override(vignetteColor);

        foreach (VolumeComponent c in p.components)
        {
            c.name = c.GetType().Name;
            AssetDatabase.AddObjectToAsset(c, p);
        }

        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    private static Transform FreshRoot(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == RootName) Object.DestroyImmediate(go);
        }

        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<PostFxPlatformGate>(); // no post-processing on the Quest's own GPU
        return root.transform;
    }

    private static void AddVolume(Transform root, VolumeProfile profile)
    {
        var go = new GameObject("PostFX Volume");
        go.transform.SetParent(root, false);
        var v = go.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = 1f;
        v.sharedProfile = profile;
    }

    private static int EnablePostOnCameras()
    {
        int n = 0;
        foreach (Camera cam in Object.FindObjectsOfType<Camera>(true))
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data == null) continue;
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
            n++;
        }
        return n;
    }

    private static void BakeProbe(Transform root, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = center;
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.size = size;
        probe.boxProjection = true;
        probe.resolution = 128;
        probe.hdr = true;
        probe.importance = 1;

        DeadlockSceneKit.EnsureFolder(TexDir);
        string path = $"{TexDir}/{name}.exr";
        if (Lightmapping.BakeReflectionProbe(probe, path))
        {
            AssetDatabase.ImportAsset(path);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
        }
    }

    // =====================================================================================
    // Garage (TitleScreen)
    // =====================================================================================

    private static void BuildLobby(System.Text.StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity", OpenSceneMode.Single);
        Transform root = FreshRoot(scene);

        AddVolume(root, Profile("Garage_PostFX", 1.0f, 14f, 8f, 0.9f, 0.95f,
            new Vector4(0.92f, 1.0f, 1.03f, -0.02f), new Vector4(1.05f, 1.0f, 0.93f, 0f), 0.26f, new Color(0.01f, 0.02f, 0.02f)));
        int cams = EnablePostOnCameras();

        // ---- Textures + materials ----
        Texture2D floorTex = WoodPlanks("Garage_FloorPlanks", new Color(0.36f, 0.22f, 0.13f));
        Texture2D wallTex = Wallpaper("Garage_Wallpaper");
        Texture2D wainTex = Wainscot("Garage_Wainscot");
        Texture2D rugTex = Rug("Garage_Rug");
        Texture2D paintTex = Painting("Garage_Painting");

        Material floor = DeadlockSceneKit.Lit("Atmo_Floor", Color.white, 0.58f, 0f, floorTex);
        Material wall = DeadlockSceneKit.Lit("Atmo_Wallpaper", Color.white, 0.12f, 0f, wallTex);
        Material wain = DeadlockSceneKit.Lit("Atmo_Wainscot", Color.white, 0.42f, 0f, wainTex);
        wain.SetTextureScale("_BaseMap", new Vector2(1f, 1f / 1.15f));
        Material rug = DeadlockSceneKit.Lit("Atmo_Rug", Color.white, 0.04f, 0f, rugTex);
        rug.SetTextureScale("_BaseMap", new Vector2(1f / 6.4f, 1f / 7.6f));
        Material darkWood = DeadlockSceneKit.Lit("Atmo_DarkWood", new Color(0.42f, 0.32f, 0.27f), 0.45f, 0f, floorTex);
        Material blackWood = DeadlockSceneKit.Lit("Atmo_BlackWood", new Color(0.09f, 0.06f, 0.05f), 0.5f);
        Material brass = DeadlockSceneKit.Lit("Atmo_Brass", new Color(0.62f, 0.45f, 0.2f), 0.6f, 0.9f);
        Material stone = DeadlockSceneKit.Lit("Atmo_Stone", new Color(0.3f, 0.29f, 0.27f), 0.22f);
        Material soot = DeadlockSceneKit.Lit("Atmo_Soot", new Color(0.015f, 0.012f, 0.01f), 0.1f);
        Material leather = DeadlockSceneKit.Lit("Atmo_Leather", new Color(0.3f, 0.07f, 0.045f), 0.55f);
        Material paper = DeadlockSceneKit.Lit("Atmo_Paper", new Color(0.78f, 0.73f, 0.6f), 0.1f);
        Material wax = DeadlockSceneKit.Lit("Atmo_Wax", new Color(0.85f, 0.8f, 0.68f), 0.3f);
        Material canvas = DeadlockSceneKit.Lit("Atmo_Canvas", Color.white, 0.2f, 0f, paintTex, new Color(0.45f, 0.4f, 0.32f));
        canvas.SetTexture("_EmissionMap", paintTex); // lit by the picture lamp above the frame
        Material lampShade = DeadlockSceneKit.Lit("Atmo_LampShade", new Color(0.05f, 0.25f, 0.12f), 0.85f, 0f, null, new Color(0.04f, 0.3f, 0.12f));
        Material[] bookMats =
        {
            DeadlockSceneKit.Lit("Atmo_Book_Oxblood", new Color(0.34f, 0.06f, 0.05f), 0.32f),
            DeadlockSceneKit.Lit("Atmo_Book_Forest", new Color(0.07f, 0.19f, 0.11f), 0.32f),
            DeadlockSceneKit.Lit("Atmo_Book_Navy", new Color(0.07f, 0.09f, 0.21f), 0.32f),
            DeadlockSceneKit.Lit("Atmo_Book_Tan", new Color(0.46f, 0.34f, 0.2f), 0.25f),
            DeadlockSceneKit.Lit("Atmo_Book_Black", new Color(0.05f, 0.045f, 0.04f), 0.35f),
            DeadlockSceneKit.Lit("Atmo_Book_Mustard", new Color(0.5f, 0.37f, 0.1f), 0.3f),
        };

        Material flame = DeadlockSceneKit.Glow("Atmo_Flame", new Color(1f, 0.62f, 0.28f, 0.95f), 1f, 11f);
        Material ember = DeadlockSceneKit.Glow("Atmo_Ember", new Color(1f, 0.32f, 0.08f, 0.85f), 1f, 1.7f);
        Material warmPool = DeadlockSceneKit.Glow("Atmo_WarmPool", new Color(1f, 0.55f, 0.25f, 0.16f), 1f);
        Material bulb = DeadlockSceneKit.Glow("Atmo_Bulb", new Color(1f, 0.8f, 0.55f, 1f), 1f);
        Material moonPool = DeadlockSceneKit.Glow("Atmo_MoonPool", new Color(0.45f, 0.8f, 0.85f, 0.07f), 1f);
        Material shaft = DeadlockSceneKit.Shader("Atmo_MoonShaft", "Deadlock/LightShaft");
        shaft.SetColor("_Color", new Color(0.5f, 0.82f, 0.9f, 0.075f));
        shaft.SetFloat("_FarFade", 1.3f);
        shaft.SetFloat("_NearFade", 1.5f);
        Material dust = DeadlockSceneKit.Shader("Atmo_Dust", "Deadlock/GlowParticle");
        dust.SetColor("_Color", new Color(1f, 0.9f, 0.75f, 0.55f));
        dust.SetFloat("_Radial", 1f);
        Material spark = DeadlockSceneKit.Shader("Atmo_Spark", "Deadlock/GlowParticle");
        spark.SetColor("_Color", new Color(1f, 0.55f, 0.2f, 1f));
        spark.SetFloat("_Radial", 1f);

        // ---- Re-skin the existing shell ----
        Transform lobby = GameObject.Find("OracleLobby")?.transform;
        if (lobby != null)
        {
            foreach (Renderer r in lobby.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n == "Floor") r.sharedMaterial = floor;
                else if (n.StartsWith("Wall_")) r.sharedMaterial = wall;
                else if (n.StartsWith("Panel_")) r.sharedMaterial = wain;
                else if (n == "Ceiling") r.sharedMaterial = darkWood;
                else if (n == "Rug") r.sharedMaterial = rug;
                else if (n == "Pillar") r.sharedMaterial = darkWood;
                else if (n == "Cornice") r.sharedMaterial = darkWood;
                else continue;
                EditorUtility.SetDirty(r);
            }
        }

        var rng = new System.Random(29);
        var arch = new Batch();
        var props = new Batch();
        // Bookcases are merged per wall so each side gets its own nearby lights (URP per-object limit).
        var shelvesL = new Batch();
        var shelvesR = new Batch();
        var booksL = new Batch();
        var booksR = new Batch();
        var glow = new Batch();

        // ---- Architecture: baseboards, crown moulding, coffered ceiling, pillar bases/capitals ----
        float wx = HW - PanelFace;          // wainscot face (x)
        float bz = BackZ - PanelFace;       // back wainscot face
        float fz = FrontZ + PanelFace;      // front wainscot face
        float depth = BackZ - FrontZ;
        float midZ = (BackZ + FrontZ) * 0.5f;
        float width = HW * 2f;

        foreach (int side in new[] { -1, 1 })
        {
            arch.Box(blackWood, new Vector3(side * (wx - 0.025f), 0.09f, midZ), new Vector3(0.05f, 0.18f, depth), Quaternion.identity);
            for (int k = 0; k < 3; k++)
            {
                float d = 0.26f - k * 0.08f, h = 0.2f - k * 0.06f, y = RoomH - 0.1f - k * 0.16f;
                arch.Box(k == 1 ? brass : darkWood, new Vector3(side * (HW - d * 0.5f), y, midZ), new Vector3(d, h, depth), Quaternion.identity, 2f);
            }
        }
        foreach (float z in new[] { bz - 0.025f, fz + 0.025f })
        {
            arch.Box(blackWood, new Vector3(0f, 0.09f, z), new Vector3(width, 0.18f, 0.05f), Quaternion.identity);
        }
        foreach (int side in new[] { -1, 1 })
        {
            float wallZ = side < 0 ? FrontZ : BackZ;
            for (int k = 0; k < 3; k++)
            {
                float d = 0.26f - k * 0.08f, h = 0.2f - k * 0.06f, y = RoomH - 0.1f - k * 0.16f;
                arch.Box(k == 1 ? brass : darkWood, new Vector3(0f, y, wallZ - side * d * 0.5f), new Vector3(width, h, d), Quaternion.identity, 2f);
            }
        }

        // Coffers: a grid of deep beams with a brass bead under each.
        for (float z = FrontZ + 1.4f; z < BackZ - 0.5f; z += 2.8f)
        {
            arch.Box(darkWood, new Vector3(0f, RoomH - 0.2f, z), new Vector3(width, 0.4f, 0.3f), Quaternion.identity, 2f);
            arch.Box(brass, new Vector3(0f, RoomH - 0.41f, z), new Vector3(width, 0.025f, 0.12f), Quaternion.identity);
        }
        for (float x = -4.2f; x <= 4.21f; x += 2.8f)
        {
            arch.Box(darkWood, new Vector3(x, RoomH - 0.2f, midZ), new Vector3(0.3f, 0.4f, depth), Quaternion.identity, 2f);
            arch.Box(brass, new Vector3(x, RoomH - 0.41f, midZ), new Vector3(0.12f, 0.025f, depth), Quaternion.identity);
        }

        foreach (float x in new[] { -6.6f, -4.6f, -1.6f, 1.6f, 4.6f, 6.6f })
        {
            arch.Box(blackWood, new Vector3(x, 0.2f, BackZ - 0.27f), new Vector3(0.75f, 0.4f, 0.62f), Quaternion.identity);
            arch.Box(brass, new Vector3(x, 0.42f, BackZ - 0.27f), new Vector3(0.68f, 0.04f, 0.58f), Quaternion.identity);
            arch.Box(darkWood, new Vector3(x, RoomH - 0.75f, BackZ - 0.27f), new Vector3(0.75f, 0.3f, 0.62f), Quaternion.identity, 2f);
            arch.Box(brass, new Vector3(x, RoomH - 0.92f, BackZ - 0.27f), new Vector3(0.65f, 0.04f, 0.56f), Quaternion.identity);
        }

        // ---- Bookcases along the side walls (between the windows) ----
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * wx;
            Quaternion inward = Quaternion.LookRotation(new Vector3(-side, 0f, 0f));
            Batch shelves = side < 0 ? shelvesL : shelvesR;
            Batch books = side < 0 ? booksL : booksR;
            Bookcase(shelves, books, bookMats, darkWood, blackWood, brass, rng, new Vector3(x, 0f, -1.7f), inward, 1.7f, 3.1f);
            Bookcase(shelves, books, bookMats, darkWood, blackWood, brass, rng, new Vector3(x, 0f, 3.25f), inward, 1.55f, 3.1f);
            Bookcase(shelves, books, bookMats, darkWood, blackWood, brass, rng, new Vector3(x, 0f, 4.85f), inward, 1.55f, 3.1f);
            Bookcase(shelves, books, bookMats, darkWood, blackWood, brass, rng, new Vector3(x, 0f, -6.55f), inward, 1.55f, 3.1f);
        }

        // Rolling library ladder leaning on the long left case.
        {
            // Top rests on the front of the case (0.42 m off the wainscot).
            Quaternion lean = Quaternion.Euler(0f, 0f, 14f);
            Vector3 basePos = new Vector3(-wx + 0.42f + 0.78f, 0f, 4.05f);
            foreach (float dz in new[] { -0.24f, 0.24f })
            {
                props.Box(darkWood, basePos + lean * new Vector3(0f, 1.6f, dz), new Vector3(0.05f, 3.25f, 0.06f), lean);
            }
            for (float y = 0.3f; y < 3.1f; y += 0.32f)
            {
                props.Box(brass, basePos + lean * new Vector3(0f, y, 0f), new Vector3(0.035f, 0.035f, 0.5f), lean);
            }
        }

        // ---- Fireplace on the front wall ----
        Transform fire = new GameObject("Fireplace").transform;
        fire.SetParent(root, false);
        {
            float z0 = fz;
            props.Box(stone, new Vector3(0f, 0.06f, z0 + 0.5f), new Vector3(3.3f, 0.12f, 1.0f), Quaternion.identity);
            foreach (int s in new[] { -1, 1 })
            {
                props.Box(stone, new Vector3(s * 1.2f, 0.82f, z0 + 0.25f), new Vector3(0.5f, 1.4f, 0.5f), Quaternion.identity);
                props.Box(stone, new Vector3(s * 1.2f, 0.18f, z0 + 0.27f), new Vector3(0.6f, 0.24f, 0.56f), Quaternion.identity);
                props.Box(soot, new Vector3(s * 0.93f, 0.8f, z0 + 0.2f), new Vector3(0.04f, 1.36f, 0.4f), Quaternion.identity);
            }
            props.Box(stone, new Vector3(0f, 1.72f, z0 + 0.25f), new Vector3(2.9f, 0.45f, 0.5f), Quaternion.identity);
            props.Box(darkWood, new Vector3(0f, 2.0f, z0 + 0.32f), new Vector3(3.3f, 0.1f, 0.66f), Quaternion.identity);
            props.Box(brass, new Vector3(0f, 1.94f, z0 + 0.32f), new Vector3(3.22f, 0.02f, 0.6f), Quaternion.identity);
            props.Box(soot, new Vector3(0f, 0.8f, z0 + 0.03f), new Vector3(1.9f, 1.4f, 0.06f), Quaternion.identity);
            props.Box(soot, new Vector3(0f, 1.48f, z0 + 0.2f), new Vector3(1.9f, 0.04f, 0.4f), Quaternion.identity);
            // Chimney breast to the ceiling.
            props.Box(stone, new Vector3(0f, 4.5f, z0 + 0.17f), new Vector3(2.7f, 5.0f, 0.34f), Quaternion.identity, 1.5f);

            // Painting over the mantel.
            float py = 3.55f, pz = z0 + 0.36f;
            props.Box(brass, new Vector3(0f, py + 0.72f, pz), new Vector3(2.0f, 0.1f, 0.08f), Quaternion.identity);
            props.Box(brass, new Vector3(0f, py - 0.72f, pz), new Vector3(2.0f, 0.1f, 0.08f), Quaternion.identity);
            props.Box(brass, new Vector3(-0.95f, py, pz), new Vector3(0.1f, 1.54f, 0.08f), Quaternion.identity);
            props.Box(brass, new Vector3(0.95f, py, pz), new Vector3(0.1f, 1.54f, 0.08f), Quaternion.identity);
            GameObject art = DeadlockSceneKit.Primitive(PrimitiveType.Quad, "Painting", fire, new Vector3(0f, py, pz + 0.01f), new Vector3(1.8f, 1.34f, 1f), canvas);
            art.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Picture lamp: brass bar on an arm, warm wash down the canvas.
            props.Box(brass, new Vector3(0f, py + 0.85f, pz + 0.2f), new Vector3(0.03f, 0.03f, 0.3f), Quaternion.identity);
            props.Cylinder(brass, new Vector3(0f, py + 0.86f, pz + 0.34f), 0.035f, 0.7f, Quaternion.Euler(0f, 0f, 90f));
            glow.Quad(warmPool, new Vector3(-0.9f, py + 0.82f, pz + 0.02f), new Vector3(0.9f, py + 0.82f, pz + 0.02f), new Vector3(0.9f, py - 0.1f, pz + 0.02f), new Vector3(-0.9f, py - 0.1f, pz + 0.02f));

            // Logs, embers and flames.
            props.Box(blackWood, new Vector3(-0.1f, 0.2f, z0 + 0.35f), new Vector3(1.1f, 0.14f, 0.16f), Quaternion.Euler(0f, 8f, 0f));
            props.Box(blackWood, new Vector3(0.12f, 0.33f, z0 + 0.3f), new Vector3(0.95f, 0.13f, 0.14f), Quaternion.Euler(0f, -12f, 6f));
            props.Box(blackWood, new Vector3(0f, 0.2f, z0 + 0.48f), new Vector3(0.14f, 0.12f, 0.5f), Quaternion.Euler(10f, 20f, 0f));
            glow.Quad(ember, new Vector3(-0.75f, 0.13f, z0 + 0.12f), new Vector3(0.75f, 0.13f, z0 + 0.12f), new Vector3(0.75f, 0.13f, z0 + 0.75f), new Vector3(-0.75f, 0.13f, z0 + 0.75f));
            // Overlapping tongues: wide dim outer flames, then a bright yellow core.
            Material flameCore = DeadlockSceneKit.Glow("Atmo_FlameCore", new Color(1f, 0.85f, 0.45f, 0.9f), 1f, 14f);
            for (int i = 0; i < 12; i++)
            {
                float u = i / 11f;
                float fx = Mathf.Lerp(-0.6f, 0.6f, u) + (float)(rng.NextDouble() - 0.5) * 0.08f;
                float fh = (0.45f + (float)rng.NextDouble() * 0.3f) * (1f - Mathf.Abs(u - 0.5f) * 0.9f);
                Vector3 c = new Vector3(fx, 0.22f + fh * 0.5f, z0 + 0.36f + (float)(rng.NextDouble() - 0.5) * 0.14f);
                glow.Billboardish(flame, c, 0.38f + (float)rng.NextDouble() * 0.14f, fh);
            }
            for (int i = 0; i < 5; i++)
            {
                float fx = -0.35f + i * 0.175f;
                float fh = 0.28f + (float)rng.NextDouble() * 0.18f;
                glow.Billboardish(flameCore, new Vector3(fx, 0.24f + fh * 0.45f, z0 + 0.36f), 0.2f, fh);
            }
            // Firelight on the sooty back wall of the firebox.
            glow.Quad(warmPool, new Vector3(-0.9f, 0.1f, z0 + 0.07f), new Vector3(0.9f, 0.1f, z0 + 0.07f), new Vector3(0.9f, 1.4f, z0 + 0.07f), new Vector3(-0.9f, 1.4f, z0 + 0.07f));
            glow.Quad(warmPool, new Vector3(-2.2f, 0.012f, z0 + 0.1f), new Vector3(2.2f, 0.012f, z0 + 0.1f), new Vector3(2.2f, 0.012f, z0 + 3.4f), new Vector3(-2.2f, 0.012f, z0 + 3.4f));

            Light fireLight = DeadlockSceneKit.PointLight("FireLight", fire, new Vector3(0f, 0.7f, z0 + 0.9f), new Color(1f, 0.52f, 0.22f), 2.6f, 8f);
            var flick = fireLight.gameObject.AddComponent<LightFlicker>();
            flick.amount = 0.3f;
            flick.speed = 1.8f;
            DeadlockSceneKit.Particles("Sparks", fire, new Vector3(0f, 0.35f, z0 + 0.38f), spark, new Color(1f, 0.6f, 0.25f, 1f),
                true, 7f, 0, 1.8f, 0.6f, 0.025f, new Vector3(0.9f, 0.05f, 0.2f), false, -0.12f);

            // Candles on the mantel.
            foreach (float cx in new[] { -1.35f, -1.15f, 1.2f })
            {
                float ch = cx < -1.2f ? 0.3f : 0.2f;
                props.Cylinder(brass, new Vector3(cx, 2.07f, z0 + 0.47f), 0.05f, 0.04f);
                props.Cylinder(wax, new Vector3(cx, 2.09f + ch * 0.5f, z0 + 0.47f), 0.022f, ch);
                glow.Billboardish(flame, new Vector3(cx, 2.13f + ch, z0 + 0.47f), 0.035f, 0.07f);
            }
            // A stack of books on the mantel.
            for (int i = 0; i < 4; i++)
            {
                props.Box(bookMats[i], new Vector3(0.75f, 2.07f + i * 0.045f, z0 + 0.46f), new Vector3(0.28f - i * 0.02f, 0.04f, 0.2f), Quaternion.Euler(0f, i * 7f - 8f, 0f));
            }
        }

        // ---- Armchairs + side table facing the fire ----
        Armchair(props, leather, blackWood, brass, new Vector3(-1.9f, 0f, fz + 2.3f), Quaternion.Euler(0f, 145f, 0f));
        Armchair(props, leather, blackWood, brass, new Vector3(1.9f, 0f, fz + 2.3f), Quaternion.Euler(0f, 215f, 0f));
        {
            Vector3 t = new Vector3(-2.95f, 0f, fz + 1.35f);
            props.Cylinder(darkWood, t + Vector3.up * 0.6f, 0.32f, 0.04f);
            props.Cylinder(blackWood, t + Vector3.up * 0.31f, 0.04f, 0.58f);
            props.Cylinder(blackWood, t + Vector3.up * 0.02f, 0.2f, 0.04f);
            props.Cylinder(brass, t + new Vector3(0.08f, 0.64f, 0f), 0.04f, 0.04f);
            props.Cylinder(wax, t + new Vector3(0.08f, 0.73f, 0f), 0.02f, 0.15f);
            glow.Billboardish(flame, t + new Vector3(0.08f, 0.84f, 0f), 0.035f, 0.07f);
            props.Box(bookMats[2], t + new Vector3(-0.1f, 0.64f, 0.05f), new Vector3(0.22f, 0.04f, 0.16f), Quaternion.Euler(0f, 25f, 0f));
        }

        // ---- Writing desk under the front-right window ----
        {
            // Local +Z points at the wall (back of the desk), -Z into the room where the chair is.
            float x = wx - 0.45f, z = -4.2f;
            Quaternion r = Quaternion.LookRotation(Vector3.right);
            Vector3 P(float lx, float ly, float lz) => new Vector3(x, 0f, z) + r * new Vector3(lx, ly, lz);
            props.Box(darkWood, P(0f, 0.76f, 0f), new Vector3(1.6f, 0.05f, 0.75f), r);
            foreach (int sx in new[] { -1, 1 })
            {
                props.Box(darkWood, P(sx * 0.6f, 0.37f, 0f), new Vector3(0.36f, 0.74f, 0.7f), r);
                props.Box(brass, P(sx * 0.6f, 0.55f, -0.355f), new Vector3(0.08f, 0.02f, 0.02f), r);
                props.Box(brass, P(sx * 0.6f, 0.3f, -0.355f), new Vector3(0.08f, 0.02f, 0.02f), r);
            }
            props.Box(leather, P(0f, 0.79f, -0.05f), new Vector3(0.8f, 0.006f, 0.45f), r);
            for (int i = 0; i < 9; i++)
            {
                float a = (float)(rng.NextDouble() - 0.5) * 50f;
                props.Box(paper, P((float)(rng.NextDouble() - 0.5) * 1.1f, 0.795f + i * 0.002f, (float)(rng.NextDouble() - 0.5) * 0.4f),
                    new Vector3(0.21f, 0.002f, 0.29f), r * Quaternion.Euler(0f, a, 0f));
            }
            for (int i = 0; i < 5; i++)
            {
                props.Box(bookMats[(i + 2) % bookMats.Length], P(-0.62f, 0.81f + i * 0.05f, 0.18f), new Vector3(0.3f - i * 0.015f, 0.045f, 0.22f), r * Quaternion.Euler(0f, i * 9f - 15f, 0f));
            }
            props.Cylinder(soot, P(0.35f, 0.81f, 0.15f), 0.035f, 0.06f);
            props.Box(paper, P(0.36f, 0.87f, 0.15f), new Vector3(0.008f, 0.14f, 0.008f), r * Quaternion.Euler(0f, 0f, 20f));
            // Banker's lamp.
            props.Cylinder(brass, P(0.55f, 0.795f, 0.2f), 0.09f, 0.02f);
            props.Cylinder(brass, P(0.55f, 0.95f, 0.2f), 0.012f, 0.3f);
            props.Box(lampShade, P(0.55f, 1.1f, 0.12f), new Vector3(0.36f, 0.07f, 0.16f), r * Quaternion.Euler(-12f, 0f, 0f));
            glow.Quad(warmPool, P(0.1f, 0.8f, -0.3f), P(1.0f, 0.8f, -0.3f), P(1.0f, 0.8f, 0.4f), P(0.1f, 0.8f, 0.4f));
            Light lamp = DeadlockSceneKit.PointLight("DeskLamp", root, P(0.55f, 1.0f, 0.05f), new Color(1f, 0.75f, 0.45f), 1.1f, 3.2f);
            lamp.gameObject.AddComponent<LightFlicker>().amount = 0.04f;
            // Chair.
            Vector3 c = P(0f, 0f, -0.75f);
            props.Box(darkWood, c + Vector3.up * 0.46f, new Vector3(0.48f, 0.05f, 0.46f), r);
            props.Box(leather, c + Vector3.up * 0.5f, new Vector3(0.44f, 0.04f, 0.42f), r);
            props.Box(darkWood, c + r * new Vector3(0f, 0.85f, -0.22f), new Vector3(0.46f, 0.7f, 0.05f), r);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    props.Box(blackWood, c + r * new Vector3(sx * 0.2f, 0.22f, sz * 0.19f), new Vector3(0.04f, 0.44f, 0.04f), r);
        }

        // ---- Globe on a stand + a trunk under the front-left window ----
        {
            Vector3 g = new Vector3(-wx + 0.75f, 0f, -4.6f);
            props.Cylinder(blackWood, g + Vector3.up * 0.03f, 0.28f, 0.06f);
            props.Cylinder(darkWood, g + Vector3.up * 0.4f, 0.04f, 0.7f);
            props.Sphere(DeadlockSceneKit.Lit("Atmo_Globe", new Color(0.55f, 0.48f, 0.3f), 0.5f), g + Vector3.up * 1.0f, 0.32f);
            props.Box(brass, g + Vector3.up * 1.0f, new Vector3(0.02f, 0.72f, 0.72f), Quaternion.Euler(0f, 30f, 23f));
            Vector3 t = new Vector3(-wx + 0.45f, 0f, -3.6f);
            props.Box(leather, t + Vector3.up * 0.25f, new Vector3(0.55f, 0.5f, 0.95f), Quaternion.identity);
            props.Box(brass, t + Vector3.up * 0.5f, new Vector3(0.57f, 0.03f, 0.97f), Quaternion.identity);
            props.Box(brass, t + Vector3.up * 0.25f, new Vector3(0.57f, 0.5f, 0.04f), Quaternion.identity);
        }

        // ---- Chandelier over the ritual circle ----
        Transform chand = new GameObject("Chandelier").transform;
        chand.SetParent(root, false);
        {
            Vector3 c = new Vector3(0f, 4.7f, -0.2f);
            props.Cylinder(blackWood, new Vector3(c.x, (c.y + RoomH) * 0.5f + 0.2f, c.z), 0.015f, RoomH - c.y - 0.4f);
            props.Cylinder(brass, c + Vector3.up * 0.1f, 0.12f, 0.35f);
            props.Sphere(brass, c - Vector3.up * 0.12f, 0.1f);
            const int arms = 10;
            float ringR = 1.0f;
            for (int i = 0; i < arms; i++)
            {
                float a = i / (float)arms * 360f;
                Quaternion q = Quaternion.Euler(0f, a, 0f);
                props.Box(brass, c + q * new Vector3(0f, 0f, ringR * 0.5f), new Vector3(0.025f, 0.025f, ringR), q);
                float a2 = (i + 0.5f) / arms * 360f;
                Quaternion q2 = Quaternion.Euler(0f, a2, 0f);
                float seg = 2f * Mathf.PI * ringR / arms + 0.03f;
                props.Box(brass, c + q2 * new Vector3(0f, -0.02f, ringR * Mathf.Cos(Mathf.PI / arms)), new Vector3(seg, 0.05f, 0.04f), q2);
                Vector3 tip = c + q * new Vector3(0f, 0f, ringR);
                props.Cylinder(brass, tip + Vector3.up * 0.03f, 0.04f, 0.03f);
                props.Cylinder(wax, tip + Vector3.up * 0.13f, 0.018f, 0.18f);
                glow.Billboardish(flame, tip + Vector3.up * 0.27f, 0.04f, 0.08f);
            }
            Light l = DeadlockSceneKit.PointLight("ChandelierLight", chand, c + Vector3.up * 0.1f, new Color(1f, 0.7f, 0.42f), 1.5f, 9f);
            l.gameObject.AddComponent<LightFlicker>().amount = 0.07f;
        }

        // ---- Wall sconces (glow only, two of them real lights) ----
        // Back pillars: their front face is at BackZ - 0.5.
        foreach (float sx in new[] { -4.6f, -1.6f, 1.6f, 4.6f })
        {
            Sconce(props, glow, brass, wax, flame, warmPool, new Vector3(sx, 2.7f, BackZ - 0.5f), Quaternion.LookRotation(Vector3.back), 0.25f);
            if (Mathf.Abs(sx) > 4f)
            {
                Light l = DeadlockSceneKit.PointLight("SconceLight", root, new Vector3(sx, 2.85f, BackZ - 0.9f), new Color(1f, 0.65f, 0.38f), 0.9f, 4f);
                l.gameObject.AddComponent<LightFlicker>().amount = 0.12f;
            }
        }
        // Side walls above the bookcases (bare wall face is at +-HW up there).
        foreach (int side in new[] { -1, 1 })
        {
            foreach (float z in new[] { -1.7f, 4.05f })
            {
                Sconce(props, glow, brass, wax, flame, warmPool, new Vector3(side * HW, 3.75f, z), Quaternion.LookRotation(new Vector3(-side, 0f, 0f)), 0.45f);
            }
            // One real light per side, washing down over the long bookcases.
            Light shelfLight = DeadlockSceneKit.PointLight("ShelfLight", root, new Vector3(side * (HW - 0.9f), 3.3f, 1.1f), new Color(1f, 0.66f, 0.4f), 1.1f, 6.5f);
            shelfLight.gameObject.AddComponent<LightFlicker>().amount = 0.08f;
        }

        // ---- Moonlight shafts through the back and left windows ----
        Vector3 backDir = new Vector3(0.22f, -0.6f, -1f).normalized;
        Vector3 leftDir = new Vector3(1f, -0.62f, -0.18f).normalized;
        ShaftsFromWindow(glow, shaft, moonPool, new Vector3(0f, 1.35f, BackZ - 0.05f), Vector3.right, 2.3f, 3.7f, backDir);
        ShaftsFromWindow(glow, shaft, moonPool, new Vector3(-3.1f, 1.55f, BackZ - 0.05f), Vector3.right, 1.85f, 3.2f, backDir);
        ShaftsFromWindow(glow, shaft, moonPool, new Vector3(3.1f, 1.55f, BackZ - 0.05f), Vector3.right, 1.85f, 3.2f, backDir);
        ShaftsFromWindow(glow, shaft, moonPool, new Vector3(-HW + 0.05f, 1.65f, -4.2f), Vector3.forward, 1.7f, 2.9f, leftDir);
        ShaftsFromWindow(glow, shaft, moonPool, new Vector3(-HW + 0.05f, 1.65f, 0.8f), Vector3.forward, 1.7f, 2.9f, leftDir);

        // ---- Dust motes drifting through the room ----
        ParticleSystem motes = DeadlockSceneKit.Particles("DustMotes", root, new Vector3(0f, 2.6f, -0.5f), dust, new Color(1f, 0.92f, 0.8f, 0.6f),
            true, 26f, 120, 14f, 0.05f, 0.022f, new Vector3(12.5f, 4.8f, 12.5f), false, -0.002f);
        var noise = motes.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.05f;

        // ---- Flush merged meshes ----
        Transform statics = new GameObject("Static").transform;
        statics.SetParent(root, false);
        arch.Flush(statics, "Garage_Arch");
        props.Flush(statics, "Garage_Props");
        shelvesL.Flush(statics, "Garage_ShelvesL");
        shelvesR.Flush(statics, "Garage_ShelvesR");
        booksL.Flush(statics, "Garage_BooksL");
        booksR.Flush(statics, "Garage_BooksR");
        glow.Flush(statics, "Garage_Glow", false);
        foreach (Transform t in statics) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);

        // Night outside the windows instead of a bright pastel panel.
        SetGradient("Lobby_Window", new Color(0.08f, 0.14f, 0.17f), new Color(0.012f, 0.025f, 0.05f));
        RenderSettings.ambientEquatorColor = new Color(0.26f, 0.27f, 0.25f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.1f, 0.08f);

        // Slightly cleaner, cooler fog so the warm practicals pop.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.035f, 0.06f, 0.065f);
        RenderSettings.fogDensity = 0.028f;

        BakeProbe(root, "Garage_ReflectionProbe", new Vector3(0f, 2.2f, midZ), new Vector3(width, RoomH, depth));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Append($"Garage: {cams} cameras with post. ");
    }

    private static void Bookcase(Batch frame, Batch books, Material[] bookMats, Material wood, Material dark, Material brass,
        System.Random rng, Vector3 wallBase, Quaternion inward, float w, float h)
    {
        const float d = 0.42f;
        Vector3 P(float x, float y, float z) => wallBase + inward * new Vector3(x, y, z);

        frame.Box(dark, P(0f, h * 0.5f, 0.012f), new Vector3(w, h, 0.024f), inward);
        frame.Box(wood, P(-w * 0.5f + 0.03f, h * 0.5f, d * 0.5f), new Vector3(0.06f, h, d), inward);
        frame.Box(wood, P(w * 0.5f - 0.03f, h * 0.5f, d * 0.5f), new Vector3(0.06f, h, d), inward);
        frame.Box(wood, P(0f, 0.07f, d * 0.5f + 0.01f), new Vector3(w, 0.14f, d + 0.02f), inward);
        frame.Box(wood, P(0f, h + 0.05f, d * 0.5f + 0.03f), new Vector3(w + 0.1f, 0.1f, d + 0.06f), inward);
        frame.Box(brass, P(0f, h - 0.01f, d + 0.005f), new Vector3(w - 0.1f, 0.02f, 0.01f), inward);

        float shelf = 0.14f;
        float[] gaps = { 0.46f, 0.42f, 0.42f, 0.4f, 0.4f, 0.38f, 0.36f };
        int level = 0;
        while (shelf < h - 0.2f)
        {
            frame.Box(wood, P(0f, shelf + 0.015f, d * 0.5f), new Vector3(w - 0.12f, 0.03f, d - 0.02f), inward);
            float gap = Mathf.Min(gaps[Mathf.Min(level, gaps.Length - 1)], h - shelf - 0.03f);
            float baseY = shelf + 0.03f;

            float x = -w * 0.5f + 0.07f;
            float end = w * 0.5f - 0.07f;
            while (x < end - 0.03f)
            {
                double roll = rng.NextDouble();
                if (roll < 0.07)
                {
                    // Lying stack.
                    float sw = 0.18f + (float)rng.NextDouble() * 0.08f;
                    if (x + sw > end) break;
                    int n = 2 + rng.Next(4);
                    for (int i = 0; i < n; i++)
                    {
                        float th = 0.03f + (float)rng.NextDouble() * 0.02f;
                        books.Box(bookMats[rng.Next(bookMats.Length)], P(x + sw * 0.5f, baseY + th * 0.5f + i * 0.045f, 0.2f),
                            new Vector3(sw - i * 0.01f, th, 0.2f), inward * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 12f, 0f));
                    }
                    x += sw + 0.02f;
                    continue;
                }
                if (roll < 0.12)
                {
                    x += 0.08f + (float)rng.NextDouble() * 0.15f; // gap
                    continue;
                }

                float bw = 0.025f + (float)rng.NextDouble() * 0.035f;
                float bh = Mathf.Min(gap - 0.05f, 0.2f + (float)rng.NextDouble() * 0.16f);
                float bd = 0.17f + (float)rng.NextDouble() * 0.08f;
                if (x + bw > end) break;
                Material m = bookMats[rng.Next(bookMats.Length)];
                bool lean = rng.NextDouble() < 0.06;
                Quaternion tilt = lean ? Quaternion.Euler(0f, 0f, 14f) : Quaternion.identity;
                float cx = x + bw * 0.5f + (lean ? bh * 0.12f : 0f);
                books.Box(m, P(cx, baseY + bh * 0.5f, 0.04f + bd * 0.5f), new Vector3(bw, bh, bd), inward * tilt);
                x += bw + (lean ? bh * 0.25f : 0.002f);
            }

            shelf += gap;
            level++;
        }
    }

    /// <summary>
    /// Brass candle sconce on a wall face: <paramref name="wallPoint"/> is on the surface,
    /// <paramref name="outward"/> faces into the room. The warm pool is painted on the wall behind it.
    /// </summary>
    private static void Sconce(Batch props, Batch glow, Material brass, Material wax, Material flame, Material pool,
        Vector3 wallPoint, Quaternion outward, float poolHalfWidth)
    {
        Vector3 P(float x, float y, float z) => wallPoint + outward * new Vector3(x, y, z);
        props.Box(brass, P(0f, 0f, 0.015f), new Vector3(0.14f, 0.3f, 0.03f), outward);
        props.Box(brass, P(0f, -0.05f, 0.1f), new Vector3(0.03f, 0.03f, 0.16f), outward);
        props.Cylinder(brass, P(0f, -0.03f, 0.17f), 0.04f, 0.02f);
        props.Cylinder(wax, P(0f, 0.05f, 0.17f), 0.02f, 0.14f);
        glow.Billboardish(flame, P(0f, 0.17f, 0.17f), 0.04f, 0.08f);
        float w = poolHalfWidth;
        glow.Quad(pool, P(-w, -0.45f, 0.004f), P(w, -0.45f, 0.004f), P(w, 0.75f, 0.004f), P(-w, 0.75f, 0.004f));
    }

    private static void Armchair(Batch b, Material leather, Material legs, Material brass, Vector3 pos, Quaternion rot)
    {
        Vector3 P(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
        b.Box(leather, P(0f, 0.25f, 0f), new Vector3(0.82f, 0.3f, 0.8f), rot);
        b.Box(leather, P(0f, 0.46f, 0.05f), new Vector3(0.62f, 0.13f, 0.66f), rot);
        b.Box(leather, P(0f, 0.75f, -0.34f), new Vector3(0.82f, 0.95f, 0.16f), rot * Quaternion.Euler(-8f, 0f, 0f));
        b.Box(leather, P(0f, 1.24f, -0.38f), new Vector3(0.7f, 0.12f, 0.2f), rot * Quaternion.Euler(-8f, 0f, 0f));
        foreach (int s in new[] { -1, 1 })
        {
            b.Box(leather, P(s * 0.37f, 0.5f, 0.02f), new Vector3(0.16f, 0.32f, 0.78f), rot);
            b.Box(leather, P(s * 0.37f, 0.68f, 0.06f), new Vector3(0.2f, 0.06f, 0.78f), rot);
            for (int k = 0; k < 6; k++) b.Box(brass, P(s * 0.46f, 0.6f, -0.3f + k * 0.12f), new Vector3(0.015f, 0.02f, 0.02f), rot);
            foreach (int f in new[] { -1, 1 }) b.Box(legs, P(s * 0.34f, 0.05f, f * 0.32f), new Vector3(0.06f, 0.1f, 0.06f), rot);
        }
    }

    /// <summary>
    /// Light shaft from a window: three sheets (bottom, middle, top of the opening) swept along
    /// <paramref name="dir"/> to the floor, plus a soft pool where they land.
    /// </summary>
    private static void ShaftsFromWindow(Batch b, Material shaft, Material pool, Vector3 sillCenter, Vector3 across, float width, float height, Vector3 dir)
    {
        Vector3 half = across * (width * 0.5f);
        Vector3 floorHit0 = Vector3.zero, floorHit1 = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            float t = 0.15f + i * 0.35f;
            Vector3 a = sillCenter + Vector3.up * (height * t);
            float len = (a.y - 0.02f) / -dir.y;
            Vector3 far = dir * len;
            b.Quad(shaft, a - half, a + half, a + half + far, a - half + far);
            if (i == 0) { floorHit0 = a + far; }
            if (i == 2) { floorHit1 = a + far; }
        }
        // Vertical centre sheet (seen from the side it fills the beam).
        Vector3 top = sillCenter + Vector3.up * (height * 0.85f);
        Vector3 bottom = sillCenter + Vector3.up * (height * 0.15f);
        float l = (top.y - 0.02f) / -dir.y;
        float lb = (bottom.y - 0.02f) / -dir.y;
        b.Quad(shaft, bottom, top, top + dir * l, bottom + dir * lb);

        Vector3 c0 = new Vector3(floorHit0.x, 0.015f, floorHit0.z);
        Vector3 c1 = new Vector3(floorHit1.x, 0.015f, floorHit1.z);
        Vector3 h2 = half * 1.1f;
        b.Quad(pool, c0 - h2, c0 + h2, c1 + h2, c1 - h2);
    }

    // =====================================================================================
    // Intro rooftop
    // =====================================================================================

    private static void BuildIntro(System.Text.StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Intro.unity", OpenSceneMode.Single);
        Transform root = FreshRoot(scene);

        AddVolume(root, Profile("Intro_PostFX", 0.55f, 12f, 4f, 1.25f, 0.85f,
            new Vector4(0.94f, 0.98f, 1.06f, -0.02f), new Vector4(1.05f, 1.0f, 0.95f, 0f), 0.24f, Color.black));
        int cams = EnablePostOnCameras();

        // Night sky: deep blue zenith, lighter city-glow horizon, pale moon instead of the sun.
        Color top = new Color(0.012f, 0.02f, 0.055f);
        Color mid = new Color(0.05f, 0.08f, 0.16f);
        Color horizon = new Color(0.2f, 0.2f, 0.27f);
        Color haze = new Color(0.07f, 0.09f, 0.14f);
        SetGradient("Intro_SkyLow", horizon, mid);
        SetGradient("Intro_SkyHigh", mid, top);
        SetGradient("Intro_SkyCap", top, top);
        SetGlow("Intro_SunGlow", new Color(0.65f, 0.78f, 1f, 0.5f));
        SetGlow("Intro_Cloud", new Color(0.35f, 0.42f, 0.62f, 0.2f));
        var facade = AssetDatabase.LoadAssetAtPath<Material>($"{DeadlockSceneKit.MaterialDir}/Intro_Facade.mat");
        if (facade != null)
        {
            facade.SetColor("_EmissionColor", new Color(1.6f, 1.1f, 0.62f));
            EditorUtility.SetDirty(facade);
        }

        Transform sky = GameObject.Find("Intro/Sky")?.transform;
        if (sky != null)
        {
            Transform core = sky.Find("SunCore");
            if (core != null) core.localScale = new Vector3(70f, 70f, 1f);
            Transform glowT = sky.Find("SunGlow");
            if (glowT != null) glowT.localScale = new Vector3(360f, 360f, 1f);
        }

        foreach (Light l in Object.FindObjectsOfType<Light>(true))
        {
            if (l.type == LightType.Directional)
            {
                l.color = new Color(0.55f, 0.66f, 1f);
                l.intensity = 0.75f;
                EditorUtility.SetDirty(l);
            }
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.15f, 0.19f, 0.32f);
        RenderSettings.ambientEquatorColor = new Color(0.16f, 0.16f, 0.22f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.04f);
        RenderSettings.fogColor = haze;

        var rng = new System.Random(11);
        var props = new Batch();
        var bulbs = new Batch();
        var glow = new Batch();

        Material metal = AssetDatabase.LoadAssetAtPath<Material>($"{DeadlockSceneKit.MaterialDir}/Intro_RoofMetal.mat");
        Material wire = DeadlockSceneKit.Lit("Atmo_Wire", new Color(0.02f, 0.02f, 0.02f), 0.3f);
        Material bulbMat = DeadlockSceneKit.Lit("Atmo_BulbLit", new Color(1f, 0.85f, 0.6f), 0.8f, 0f, null, new Color(3.2f, 2.0f, 0.9f));
        Material puddle = DeadlockSceneKit.Lit("Atmo_Puddle", new Color(0.015f, 0.02f, 0.03f), 0.97f, 0.1f);
        Texture2D crateTex = WoodPlanks("Intro_CratePlanks", new Color(0.4f, 0.29f, 0.17f));
        Material crate = DeadlockSceneKit.Lit("Atmo_Crate", Color.white, 0.2f, 0f, crateTex);
        Material red = DeadlockSceneKit.Glow("Atmo_RedBlink", new Color(1f, 0.12f, 0.08f, 0.95f), 1f, 2.6f);
        Material halo = DeadlockSceneKit.Glow("Atmo_BulbHalo", new Color(1f, 0.7f, 0.4f, 0.16f), 1f);
        Material beam = DeadlockSceneKit.Shader("Atmo_SearchBeam", "Deadlock/LightShaft");
        beam.SetColor("_Color", new Color(0.75f, 0.85f, 1f, 0.17f));
        beam.SetFloat("_FarFade", 0.9f);
        beam.SetFloat("_EdgeSoftness", 0.45f);
        beam.SetFloat("_NearFade", 5f);
        Material steam = DeadlockSceneKit.Shader("Atmo_Steam", "Deadlock/GlowParticle");
        steam.SetColor("_Color", new Color(0.55f, 0.6f, 0.72f, 0.12f));
        steam.SetFloat("_Radial", 1f);

        // String lights: hut -> front corners, and across the front.
        Vector3 poleL = new Vector3(-5.55f, 0f, 4.55f), poleR = new Vector3(5.55f, 0f, 4.55f);
        foreach (Vector3 p in new[] { poleL, poleR })
        {
            props.Cylinder(metal, p + Vector3.up * 1.75f, 0.035f, 1.3f);
            props.Box(metal, p + new Vector3(0f, 1.15f, 0.18f), new Vector3(0.08f, 0.12f, 0.12f), Quaternion.identity);
        }
        Vector3 hutL = new Vector3(0.7f, 2.65f, -2.2f), hutR = new Vector3(2.3f, 2.65f, -2.2f);
        Vector3 topL = poleL + Vector3.up * 2.35f, topR = poleR + Vector3.up * 2.35f;
        Transform strings = new GameObject("StringLights").transform;
        strings.SetParent(root, false);
        StringOfLights(strings, bulbs, glow, wire, bulbMat, halo, hutL, topL, 0.45f, 16);
        StringOfLights(strings, bulbs, glow, wire, bulbMat, halo, hutR, topR, 0.45f, 16);
        StringOfLights(strings, bulbs, glow, wire, bulbMat, halo, topL, topR, 0.7f, 22);
        DeadlockSceneKit.PointLight("StringGlow_L", strings, new Vector3(-2.6f, 1.9f, 1.4f), new Color(1f, 0.72f, 0.42f), 1.8f, 7f);
        DeadlockSceneKit.PointLight("StringGlow_R", strings, new Vector3(3.6f, 1.9f, 1.4f), new Color(1f, 0.72f, 0.42f), 1.8f, 7f);
        DeadlockSceneKit.PointLight("StringGlow_Front", strings, new Vector3(0f, 1.6f, 4.0f), new Color(1f, 0.72f, 0.42f), 1.4f, 6f);

        // Antenna with a slow red beacon on the stair hut.
        Vector3 ant = new Vector3(2.25f, 2.8f, -4.3f);
        props.Cylinder(metal, ant + Vector3.up * 1.6f, 0.03f, 3.2f);
        props.Box(metal, ant + Vector3.up * 2.4f, new Vector3(0.5f, 0.02f, 0.02f), Quaternion.identity);
        props.Box(metal, ant + Vector3.up * 2.9f, new Vector3(0.35f, 0.02f, 0.02f), Quaternion.identity);
        glow.Billboardish(red, ant + Vector3.up * 3.25f, 0.35f, 0.35f);

        // Puddles (flat ovals) and crates.
        float[,] puddles = { { -2.2f, 2.9f, 1.7f, 0.9f, 20f }, { 2.8f, 1.9f, 1.1f, 0.6f, -35f }, { -0.6f, 3.9f, 1.3f, 0.55f, 5f }, { -4.2f, -0.6f, 0.9f, 0.5f, 60f } };
        for (int i = 0; i < puddles.GetLength(0); i++)
        {
            props.Mesh(puddle, BuiltinMesh("Cylinder"), Matrix4x4.TRS(new Vector3(puddles[i, 0], 0.003f, puddles[i, 1]),
                Quaternion.Euler(0f, puddles[i, 4], 0f), new Vector3(puddles[i, 2], 0.002f, puddles[i, 3])));
        }
        props.Box(crate, new Vector3(4.4f, 0.3f, 1.6f), new Vector3(0.6f, 0.6f, 0.6f), Quaternion.Euler(0f, 12f, 0f), 0.6f);
        props.Box(crate, new Vector3(4.45f, 0.85f, 1.55f), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.Euler(0f, 31f, 0f), 0.6f);
        props.Box(crate, new Vector3(3.75f, 0.25f, 2.3f), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.Euler(0f, -8f, 0f), 0.6f);
        props.Cylinder(metal, new Vector3(-5.4f, 0.22f, 0f), 0.09f, 8.5f, Quaternion.Euler(90f, 0f, 0f));
        props.Cylinder(metal, new Vector3(-5.15f, 0.15f, 0f), 0.05f, 8.5f, Quaternion.Euler(90f, 0f, 0f));

        Transform fx = new GameObject("FX").transform;
        fx.SetParent(root, false);
        ParticleSystem vent = DeadlockSceneKit.Particles("VentSteam", fx, new Vector3(-4.4f, 0.75f, 2.4f), steam, new Color(1f, 1f, 1f, 0.6f),
            true, 5f, 0, 4.5f, 0.45f, 1.1f, new Vector3(0.25f, 0.05f, 0.25f), false, -0.03f);
        var vsize = vent.sizeOverLifetime;
        vsize.enabled = true;
        vsize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.6f));
        ParticleSystem chimney = DeadlockSceneKit.Particles("ChimneySmoke", fx, new Vector3(4.6f, 2.1f, -1.5f), steam, new Color(1f, 1f, 1f, 0.5f),
            true, 3f, 0, 6f, 0.35f, 1.4f, new Vector3(0.2f, 0.05f, 0.2f), false, -0.02f);
        var csize = chimney.sizeOverLifetime;
        csize.enabled = true;
        csize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 2f));

        // Searchlights sweeping over the skyline.
        Transform lights = new GameObject("Searchlights").transform;
        lights.SetParent(root, false);
        float[,] beams = { { -140f, 0f, 330f, -58f, -12f, 0f }, { 170f, 0f, 380f, -62f, 18f, 0.35f }, { 30f, 0f, 560f, -70f, -4f, 0.7f } };
        Mesh beamMesh = BeamMesh(5f, 50f, 520f);
        for (int i = 0; i < beams.GetLength(0); i++)
        {
            GameObject b = DeadlockSceneKit.MeshObject("Searchlight", lights, new Vector3(beams[i, 0], beams[i, 1], beams[i, 2]), beamMesh, beam);
            b.transform.localRotation = Quaternion.Euler(beams[i, 3], beams[i, 4], 0f);
            b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var sweep = b.AddComponent<SlowSweep>();
            sweep.yawAmplitude = 22f;
            sweep.pitchAmplitude = 5f;
            sweep.period = 16f + i * 3f;
            sweep.phase = beams[i, 5];
        }

        Transform statics = new GameObject("Static").transform;
        statics.SetParent(root, false);
        props.Flush(statics, "Intro_Props");
        bulbs.Flush(statics, "Intro_Bulbs", false);
        glow.Flush(statics, "Intro_Glow", false);

        BakeProbe(root, "Intro_ReflectionProbe", new Vector3(0f, 1.5f, 1f), new Vector3(12f, 4f, 10f));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Append($"Intro: {cams} cameras with post. ");
    }

    private static void StringOfLights(Transform parent, Batch bulbs, Batch glow, Material wire, Material bulb, Material halo,
        Vector3 a, Vector3 b, float sag, int count)
    {
        const int segments = 24;
        var pts = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            pts[i] = Vector3.Lerp(a, b, t) - Vector3.up * (sag * 4f * t * (1f - t));
        }

        var go = new GameObject("Wire");
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = pts.Length;
        line.SetPositions(pts);
        line.widthMultiplier = 0.012f;
        line.sharedMaterial = wire;
        line.shadowCastingMode = ShadowCastingMode.Off;

        for (int i = 1; i < count; i++)
        {
            float t = i / (float)count;
            Vector3 p = Vector3.Lerp(a, b, t) - Vector3.up * (sag * 4f * t * (1f - t) + 0.07f);
            bulbs.Mesh(wire, BuiltinMesh("Cylinder"), Matrix4x4.TRS(p + Vector3.up * 0.045f, Quaternion.identity, new Vector3(0.012f, 0.03f, 0.012f)));
            bulbs.Sphere(bulb, p, 0.025f);
            glow.Billboardish(halo, p, 0.12f, 0.12f);
        }
    }

    /// <summary>Two crossed tapered ribbons along +Z, uv.y 0 at the lamp and 1 at the far end.</summary>
    private static Mesh BeamMesh(float startWidth, float endWidth, float length)
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        foreach (Vector3 axis in new[] { Vector3.right, Vector3.up })
        {
            int s = v.Count;
            Vector3 normal = Vector3.Cross(axis, Vector3.forward);
            v.Add(-axis * startWidth * 0.5f); v.Add(axis * startWidth * 0.5f);
            v.Add(axis * endWidth * 0.5f + Vector3.forward * length); v.Add(-axis * endWidth * 0.5f + Vector3.forward * length);
            for (int i = 0; i < 4; i++) n.Add(normal);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }
        var mesh = new Mesh { name = "Atmo_SearchBeam" };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return SaveMesh(mesh);
    }

    private static void SetGradient(string material, Color bottom, Color top)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{DeadlockSceneKit.MaterialDir}/{material}.mat");
        if (m == null) return;
        m.SetColor("_Bottom", bottom);
        m.SetColor("_Top", top);
        EditorUtility.SetDirty(m);
    }

    private static void SetGlow(string material, Color color)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{DeadlockSceneKit.MaterialDir}/{material}.mat");
        if (m == null) return;
        m.SetColor("_Color", color);
        EditorUtility.SetDirty(m);
    }

    // =====================================================================================
    // Spirit vision (HeroSelect)
    // =====================================================================================

    private static void BuildSpirit(System.Text.StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/HeroSelect.unity", OpenSceneMode.Single);
        Transform root = FreshRoot(scene);
        AddVolume(root, Profile("Spirit_PostFX", 0.35f, 18f, -5f, 1.3f, 0.8f,
            new Vector4(0.9f, 1.02f, 1.02f, -0.03f), new Vector4(0.98f, 1.03f, 1.0f, 0f), 0.32f, new Color(0f, 0.03f, 0.025f)));
        int cams = EnablePostOnCameras();

        // The hero column: soft volumetric falloff instead of a flat glowing cylinder.
        Material column = DeadlockSceneKit.Shader("Atmo_SpiritColumn", "Deadlock/LightShaft");
        column.SetColor("_Color", new Color(0.35f, 1f, 0.72f, 0.38f));
        column.SetFloat("_EdgeSoftness", 0.01f);
        column.SetFloat("_FarFade", 0.9f);
        column.SetFloat("_NearFade", 0.6f);
        Transform beamT = GameObject.Find("SpiritVision/Beam")?.transform;
        if (beamT != null)
        {
            beamT.GetComponent<Renderer>().sharedMaterial = column;
            EditorUtility.SetDirty(beamT.GetComponent<Renderer>());
        }

        Material mote = DeadlockSceneKit.Shader("Atmo_SpiritMote", "Deadlock/GlowParticle");
        mote.SetColor("_Color", new Color(0.4f, 1f, 0.75f, 0.7f));
        mote.SetFloat("_Radial", 1f);
        ParticleSystem ps = DeadlockSceneKit.Particles("SpiritMotes", root, new Vector3(0f, 2f, 2f), mote, new Color(1f, 1f, 1f, 0.7f),
            true, 18f, 80, 10f, 0.08f, 0.04f, new Vector3(12f, 4f, 10f), false, -0.01f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.3f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Append($"Spirit: {cams} cameras with post. ");
    }

    // =====================================================================================
    // Lane (the match): night street + curved zipline route
    // =====================================================================================

    private const float RopeY = 9.5f;
    private const float RouteHalf = 80f;
    // Steel gantries that carry the rope across the avenue (Amber half; mirrored for Sapphire).
    private static readonly float[] GantryZ = { -80f, -63f, -37f, -16f, 0f, 16f, 37f, 63f, 80f };

    [MenuItem("Deadlock/Setup/30 Lane Night + Zipline Route")]
    public static void BuildLaneFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RunLane();
    }

    public static string RunLane()
    {
        string previous = SceneManager.GetActiveScene().path;
        var log = new System.Text.StringBuilder();
        SetupPipeline(log);
        BuildLaneNight(log);
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        Debug.Log("[Atmosphere/Lane] " + log);
        return log.ToString();
    }

    /// <summary>
    /// Plan-view x of the rope at <paramref name="z"/>: straight out of Amber's base, a bend to the
    /// tower side (x = -5), straight past the tower, an S through the plaza, and point-mirrored after.
    /// </summary>
    private static float RouteX(float z)
    {
        float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        if (z < -56f) return 0f;
        if (z < -40f) return -5f * Smooth((z + 56f) / 16f);
        if (z < -16f) return -5f;
        if (z <= 16f) return -5f + 10f * Smooth((z + 16f) / 32f);
        return -RouteX(-z);
    }

    /// <summary>Rope height at <paramref name="z"/>: hangs from each gantry and sags a little in between.</summary>
    private static float RouteY(float z)
    {
        // Through the plaza it dips under the banners that cross the avenue at ~9.5 m (z = +-11).
        float dip = 1.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21f, 15f, Mathf.Abs(z)));
        for (int i = 0; i < GantryZ.Length - 1; i++)
        {
            float g0 = GantryZ[i], g1 = GantryZ[i + 1];
            if (z < g0 || z > g1) continue;
            float t = (z - g0) / (g1 - g0);
            return RopeY - dip - (g1 - g0) * 0.018f * 4f * t * (1f - t);
        }
        return RopeY - dip;
    }

    private static Vector3[] RoutePoints()
    {
        var pts = new List<Vector3>();
        for (float z = -RouteHalf; z <= RouteHalf + 0.01f; z += 1f)
        {
            pts.Add(new Vector3(RouteX(z), RouteY(z), z));
        }
        return pts.ToArray();
    }

    private static void BuildLaneNight(System.Text.StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Lane.unity", OpenSceneMode.Single);
        Transform root = FreshRoot(scene);
        Physics.SyncTransforms();

        AddVolume(root, Profile("Lane_PostFX", 0.75f, 10f, 6f, 0.7f, 1.05f,
            new Vector4(0.95f, 0.98f, 1.05f, -0.01f), new Vector4(1.04f, 1.0f, 0.95f, 0f), 0.15f, Color.black));
        int cams = EnablePostOnCameras();

        // ---- Night: sky, moon, ambient, fog ----
        Material sky = RenderSettings.skybox;
        if (sky != null && sky.shader.name == "Skybox/Procedural")
        {
            sky.SetColor("_SkyTint", new Color(0.15f, 0.2f, 0.42f));
            sky.SetColor("_GroundColor", new Color(0.03f, 0.03f, 0.05f));
            sky.SetFloat("_AtmosphereThickness", 0.35f);
            sky.SetFloat("_Exposure", 0.45f);
            sky.SetFloat("_SunDisk", 0f);
            EditorUtility.SetDirty(sky);
        }
        Light moonLight = null;
        foreach (Light l in Object.FindObjectsOfType<Light>(true))
        {
            if (l.type != LightType.Directional) continue;
            moonLight = l;
            l.color = new Color(0.62f, 0.72f, 1f);
            l.intensity = 0.95f;
            l.transform.rotation = Quaternion.Euler(38f, 160f, 0f);
            EditorUtility.SetDirty(l);
            EditorUtility.SetDirty(l.transform);
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        // Night, but readable for play: a strong blue ambient keeps every surface visible.
        RenderSettings.ambientSkyColor = new Color(0.36f, 0.41f, 0.6f);
        RenderSettings.ambientEquatorColor = new Color(0.3f, 0.3f, 0.38f);
        RenderSettings.ambientGroundColor = new Color(0.15f, 0.13f, 0.12f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.13f, 0.15f, 0.23f);
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 170f;

        var rng = new System.Random(30);
        var sky2 = new Batch();
        Material moonMat = DeadlockSceneKit.Glow("Atmo_Moon", new Color(0.85f, 0.9f, 1f, 0.95f), 1f);
        Material moonHalo = DeadlockSceneKit.Glow("Atmo_MoonHalo", new Color(0.5f, 0.6f, 0.9f, 0.18f), 1f);
        Material star = DeadlockSceneKit.Glow("Atmo_Star", new Color(0.85f, 0.9f, 1f, 0.8f), 1f);
        Vector3 moonDir = moonLight != null ? -moonLight.transform.forward : new Vector3(-0.27f, 0.62f, 0.74f).normalized;
        FacingQuad(sky2, moonMat, moonDir * 650f, 46f);
        FacingQuad(sky2, moonHalo, moonDir * 652f, 230f);
        for (int i = 0; i < 380; i++)
        {
            float az = (float)rng.NextDouble() * Mathf.PI * 2f;
            float el = Mathf.Asin(0.12f + (float)rng.NextDouble() * 0.88f);
            Vector3 d = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
            if (Vector3.Angle(d, moonDir) < 12f) continue;
            FacingQuad(sky2, star, d * 700f, 1.2f + (float)rng.NextDouble() * 2.2f);
        }

        // ---- Wet asphalt ----
        Material asphalt = FindMaterial("Map_Asphalt");
        if (asphalt != null)
        {
            asphalt.SetFloat("_Smoothness", 0.62f);
            asphalt.SetColor("_BaseColor", new Color(0.24f, 0.24f, 0.27f));
            EditorUtility.SetDirty(asphalt);
        }

        // The map's own lit windows wash out to cream under the night exposure: make them warm amber.
        Material litWindow = FindMaterial("Lane_WindowLit");
        if (litWindow != null)
        {
            litWindow.SetColor("_BaseColor", new Color(0.09f, 0.055f, 0.03f));
            litWindow.SetColor("_EmissionColor", new Color(0.42f, 0.19f, 0.05f));
            EditorUtility.SetDirty(litWindow);
        }

        // ---- Replace the old straight neon cable with the curved route ----
        int stripped = StripOverheadCable("Map/Amber_Lane/Map_Neon_Blue") + StripOverheadCable("Map/Sapphire_Lane/Map_Neon_Blue");

        Vector3[] route = RoutePoints();
        var routeGo = new GameObject("ZiplineRoute");
        routeGo.transform.SetParent(root, false);
        routeGo.AddComponent<ZiplineRoute>().SetPoints(route);

        Material steel = DeadlockSceneKit.Lit("Atmo_Steel", new Color(0.13f, 0.14f, 0.16f), 0.45f, 0.75f);
        Material cable = DeadlockSceneKit.Lit("Atmo_Cable", new Color(0.08f, 0.08f, 0.09f), 0.7f, 0.85f);
        Material concrete = DeadlockSceneKit.Lit("Atmo_Concrete", new Color(0.42f, 0.41f, 0.4f), 0.15f);
        Material amberLamp = DeadlockSceneKit.Glow("Atmo_TeamAmber", new Color(1f, 0.6f, 0.2f, 0.95f), 1f);
        Material sapphireLamp = DeadlockSceneKit.Glow("Atmo_TeamSapphire", new Color(0.3f, 0.65f, 1f, 0.95f), 1f);
        Material neutralLamp = DeadlockSceneKit.Glow("Atmo_TeamNeutral", new Color(0.9f, 0.95f, 1f, 0.9f), 1f);

        var gantries = new Batch();
        var glow = new Batch();
        foreach (float gz in GantryZ)
        {
            Material team = gz < -1f ? amberLamp : gz > 1f ? sapphireLamp : neutralLamp;
            Gantry(gantries, glow, steel, concrete, team, gz, RouteX(gz), RouteY(gz));
        }
        Tube(gantries, cable, route, 0.03f);

        // ---- Street lamps (real light under each lantern) ----
        Material iron = DeadlockSceneKit.Lit("Atmo_Iron", new Color(0.05f, 0.05f, 0.055f), 0.5f, 0.6f);
        Material lampGlass = DeadlockSceneKit.Lit("Atmo_LampGlass", new Color(1f, 0.85f, 0.6f), 0.9f, 0f, null, new Color(2.6f, 1.75f, 0.85f));
        Material halo = DeadlockSceneKit.Glow("Atmo_LampHalo", new Color(1f, 0.72f, 0.42f, 0.2f), 1f);
        Material pool = DeadlockSceneKit.Glow("Atmo_LampPool", new Color(1f, 0.65f, 0.35f, 0.13f), 1f);
        Transform lights = new GameObject("StreetLights").transform;
        lights.SetParent(root, false);
        var lamps = new Batch();
        float[] lampZ = { -67f, -58f, -45f, -31f, -21f };
        int lampCount = 0;
        foreach (int half in new[] { 1, -1 })
        {
            for (int i = 0; i < lampZ.Length; i++)
            {
                float x = (i % 2 == 0 ? -7.35f : 7.35f) * half;
                Vector3 p = FreeSpot(new Vector3(x, 0f, lampZ[i] * half));
                if (StreetLamp(lamps, glow, iron, lampGlass, halo, pool, lights, p, true)) lampCount++;
            }
            foreach (Vector2 b in new[] { new Vector2(21f, -75f), new Vector2(-21f, -75f), new Vector2(21f, -88f), new Vector2(-21f, -88f) })
            {
                Vector3 p = FreeSpot(new Vector3(b.x * half, 0f, b.y * half));
                StreetLamp(lamps, glow, iron, lampGlass, halo, pool, lights, p, b.y > -80f);
                lampCount++;
            }
        }
        foreach (int s in new[] { -1, 1 })
        {
            StreetLamp(lamps, glow, iron, lampGlass, halo, pool, lights, FreeSpot(new Vector3(s * 15.5f, 0f, -s * 1.5f)), true);
            lampCount++;
        }

        // ---- Lit windows on the facades (found by raycasting the building colliders) ----
        // Additive and multiplied by the night exposure, so keep them faint: amber, not cream.
        Material winWarm = DeadlockSceneKit.Glow("Atmo_WinWarm", new Color(1f, 0.52f, 0.2f, 0.13f));
        Material winCool = DeadlockSceneKit.Glow("Atmo_WinCool", new Color(0.4f, 0.6f, 1f, 0.09f));
        Material winAmber = DeadlockSceneKit.Glow("Atmo_WinAmber", new Color(1f, 0.4f, 0.1f, 0.17f));
        Material[] winMats = { winWarm, winWarm, winWarm, winCool, winAmber };
        var windows = new Batch();
        int windowCount = 0;
        for (float z = -68f; z <= 68f; z += 3.2f)
        {
            foreach (float y in new[] { 3.4f, 6.2f, 9.0f, 11.8f })
            {
                windowCount += TryWindow(windows, winMats, rng, new Vector3(0f, y, z), Vector3.right, 6.5f, 11f);
                windowCount += TryWindow(windows, winMats, rng, new Vector3(0f, y, z), Vector3.left, 6.5f, 11f);
                windowCount += TryWindow(windows, winMats, rng, new Vector3(24f, y, z), Vector3.right, 2.5f, 9f);
                windowCount += TryWindow(windows, winMats, rng, new Vector3(24f, y, z), Vector3.left, 2.5f, 9f);
                windowCount += TryWindow(windows, winMats, rng, new Vector3(-24f, y, z), Vector3.right, 2.5f, 9f);
                windowCount += TryWindow(windows, winMats, rng, new Vector3(-24f, y, z), Vector3.left, 2.5f, 9f);
            }
        }

        // ---- Puddles, manholes with steam, parked cars ----
        Material puddle = DeadlockSceneKit.Lit("Atmo_Puddle", new Color(0.015f, 0.02f, 0.03f), 0.97f, 0.1f);
        Material manhole = DeadlockSceneKit.Lit("Atmo_Manhole", new Color(0.06f, 0.06f, 0.06f), 0.4f, 0.7f);
        Material steam = DeadlockSceneKit.Shader("Atmo_Steam", "Deadlock/GlowParticle");
        steam.SetColor("_Color", new Color(0.55f, 0.6f, 0.72f, 0.12f));
        steam.SetFloat("_Radial", 1f);
        var street = new Batch();
        Transform fx = new GameObject("FX").transform;
        fx.SetParent(root, false);
        foreach (int half in new[] { 1, -1 })
        {
            for (int i = 0; i < 12; i++)
            {
                float px = (float)(rng.NextDouble() * 13.0 - 6.5);
                float pz = half * (float)(14.0 + rng.NextDouble() * 54.0);
                street.Mesh(puddle, BuiltinMesh("Cylinder"), Matrix4x4.TRS(new Vector3(px, 0.018f, pz),
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f), new Vector3(1.2f + (float)rng.NextDouble() * 2.2f, 0.001f, 0.6f + (float)rng.NextDouble() * 1.1f)));
            }
            foreach (Vector2 m in new[] { new Vector2(2.2f, -60f), new Vector2(-2.8f, -33f), new Vector2(1.6f, -8f) })
            {
                Vector3 p = new Vector3(m.x * half, 0f, m.y * half);
                street.Cylinder(manhole, p + Vector3.up * 0.02f, 0.45f, 0.02f);
                ParticleSystem ps = DeadlockSceneKit.Particles("ManholeSteam", fx, p + Vector3.up * 0.15f, steam, new Color(1f, 1f, 1f, 0.55f),
                    true, 4f, 0, 5f, 0.5f, 1.6f, new Vector3(0.5f, 0.05f, 0.5f), false, -0.03f);
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.35f, 1f, 1.7f));
            }
        }

        Material[] carPaint =
        {
            DeadlockSceneKit.Lit("Atmo_CarGreen", new Color(0.07f, 0.19f, 0.13f), 0.78f, 0.35f),
            DeadlockSceneKit.Lit("Atmo_CarMaroon", new Color(0.3f, 0.05f, 0.05f), 0.78f, 0.35f),
            DeadlockSceneKit.Lit("Atmo_CarCream", new Color(0.7f, 0.64f, 0.5f), 0.7f, 0.2f),
            DeadlockSceneKit.Lit("Atmo_CarBlack", new Color(0.03f, 0.03f, 0.035f), 0.82f, 0.4f),
        };
        Material chrome = DeadlockSceneKit.Lit("Atmo_Chrome", new Color(0.7f, 0.7f, 0.72f), 0.9f, 1f);
        Material tyre = DeadlockSceneKit.Lit("Atmo_Tyre", new Color(0.03f, 0.03f, 0.03f), 0.2f);
        Material carGlass = DeadlockSceneKit.Lit("Atmo_CarGlass", new Color(0.04f, 0.05f, 0.07f), 0.95f, 0.2f);
        Material tail = DeadlockSceneKit.Glow("Atmo_TailLight", new Color(1f, 0.1f, 0.06f, 0.8f), 1f);
        var cars = new Batch();
        int carCount = 0;
        float[,] carSpots = { { 12.5f, -37.2f, 90f }, { -13.5f, -36.6f, 270f }, { 20.9f, -58f, 0f }, { -20.9f, -19f, 180f }, { 29f, -27f, 0f } };
        foreach (int half in new[] { 1, -1 })
        {
            for (int i = 0; i < carSpots.GetLength(0); i++)
            {
                Vector3 p = new Vector3(carSpots[i, 0] * half, 0f, carSpots[i, 1] * half);
                if (Physics.CheckBox(p + Vector3.up * 0.8f, new Vector3(2.3f, 0.6f, 2.3f))) continue;
                Car(cars, carPaint[rng.Next(carPaint.Length)], chrome, tyre, carGlass, tail, p, Quaternion.Euler(0f, carSpots[i, 2] + (half < 0 ? 180f : 0f), 0f));
                carCount++;
            }
        }

        Transform statics = new GameObject("Static").transform;
        statics.SetParent(root, false);
        sky2.Flush(statics, "Lane_Sky", false);
        gantries.Flush(statics, "Lane_Gantries");
        lamps.Flush(statics, "Lane_Lamps");
        windows.Flush(statics, "Lane_Windows", false);
        street.Flush(statics, "Lane_Street", false);
        cars.Flush(statics, "Lane_Cars");
        glow.Flush(statics, "Lane_Glow", false);
        foreach (Transform t in statics) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);

        BakeProbe(root, "Lane_Probe_Amber", new Vector3(0f, 5f, -41f), new Vector3(16f, 12f, 60f));
        BakeProbe(root, "Lane_Probe_Sapphire", new Vector3(0f, 5f, 41f), new Vector3(16f, 12f, 60f));
        BakeProbe(root, "Lane_Probe_Plaza", new Vector3(0f, 6f, 0f), new Vector3(38f, 14f, 24f));

        // Report anything the rope passes through (signs, the plaza, placeholder objectives).
        var clashes = new List<string>();
        foreach (string group in new[] { "Map/Signs", "Map/Plaza", "Map/Structures" })
        {
            GameObject g = GameObject.Find(group);
            if (g == null) continue;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;
                if (b.size.z > 60f) continue; // merged meshes spanning the whole lane are useless here
                b.Expand(0.3f);
                foreach (Vector3 p in route)
                {
                    if (b.Contains(p)) { clashes.Add($"{r.name}@{p.z:0}"); break; }
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Append($"Lane: {cams} cams, cable tris stripped={stripped}, lamps={lampCount}, windows={windowCount}, cars={carCount}, route={route.Length} pts. Clashes: {(clashes.Count == 0 ? "none" : string.Join(", ", clashes))}");
    }

    private static Material FindMaterial(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:Material"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Material>(p);
        }
        return null;
    }

    /// <summary>Keeps only the triangles near the ground (the dashed centre line), dropping the cable at 9.6 m.</summary>
    private static int StripOverheadCable(string path)
    {
        GameObject go = GameObject.Find(path);
        if (go == null || !go.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) return 0;
        Mesh src = mf.sharedMesh;
        Vector3[] v = src.vertices;
        int[] tris = src.triangles;
        Matrix4x4 m = go.transform.localToWorldMatrix;
        var keep = new List<int>();
        int dropped = 0;
        for (int i = 0; i < tris.Length; i += 3)
        {
            bool high = m.MultiplyPoint3x4(v[tris[i]]).y > 5f || m.MultiplyPoint3x4(v[tris[i + 1]]).y > 5f || m.MultiplyPoint3x4(v[tris[i + 2]]).y > 5f;
            if (high) { dropped++; continue; }
            keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
        }
        if (dropped == 0) return 0;

        var mesh = Object.Instantiate(src);
        mesh.name = go.transform.parent.name + "_LaneLine_NoCable";
        mesh.SetTriangles(keep, 0);
        mesh.RecalculateBounds();
        mf.sharedMesh = SaveMesh(mesh);
        EditorUtility.SetDirty(mf);
        return dropped;
    }

    /// <summary>Nudges a prop along the lane until it doesn't overlap a collider (cover, kiosks...).</summary>
    private static Vector3 FreeSpot(Vector3 p)
    {
        for (int i = 0; i < 6; i++)
        {
            Vector3 q = p + Vector3.forward * (i % 2 == 0 ? i : -i) * 1.2f;
            if (!Physics.CheckSphere(q + Vector3.up * 1.2f, 0.45f)) return q;
        }
        return p;
    }

    private static void FacingQuad(Batch b, Material m, Vector3 center, float size)
    {
        Vector3 n = -center.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, n).normalized * (size * 0.5f);
        Vector3 up = Vector3.Cross(n, right).normalized * (size * 0.5f);
        b.Quad(m, center - right - up, center + right - up, center + right + up, center - right + up);
    }

    /// <summary>A lattice steel gantry across the avenue with a pulley hanger where the rope passes.</summary>
    private static void Gantry(Batch b, Batch glow, Material steel, Material concrete, Material team, float z, float ropeX, float ropeY)
    {
        const float postX = 7.55f;
        float top = ropeY + 1.0f;
        foreach (int s in new[] { -1, 1 })
        {
            float x = s * postX;
            b.Box(concrete, new Vector3(x, 0.2f, z), new Vector3(0.9f, 0.4f, 0.9f), Quaternion.identity);
            foreach (float dz in new[] { -0.25f, 0.25f })
            {
                b.Box(steel, new Vector3(x, top * 0.5f, z + dz), new Vector3(0.12f, top, 0.12f), Quaternion.identity);
            }
            for (float y = 0.4f; y < top - 0.8f; y += 0.8f)
            {
                float angle = Mathf.Atan2(0.5f, 0.8f) * Mathf.Rad2Deg * ((int)(y / 0.8f) % 2 == 0 ? 1f : -1f);
                b.Box(steel, new Vector3(x, y + 0.4f, z), new Vector3(0.05f, 0.94f, 0.05f), Quaternion.Euler(angle, 0f, 0f));
                b.Box(steel, new Vector3(x, y, z), new Vector3(0.06f, 0.05f, 0.5f), Quaternion.identity);
            }
            // Team lamp facing the lane.
            b.Box(steel, new Vector3(x - s * 0.2f, 4.2f, z), new Vector3(0.25f, 0.3f, 0.25f), Quaternion.identity);
            glow.Billboardish(team, new Vector3(x - s * 0.36f, 4.2f, z), 0.35f, 0.35f);
        }
        // Truss: two chords and zig-zag braces.
        float lo = ropeY + 0.55f, hi = top;
        b.Box(steel, new Vector3(0f, lo, z), new Vector3(postX * 2f, 0.14f, 0.18f), Quaternion.identity);
        b.Box(steel, new Vector3(0f, hi, z), new Vector3(postX * 2f, 0.14f, 0.18f), Quaternion.identity);
        float h = hi - lo, step = 0.9f;
        float diag = Mathf.Sqrt(h * h + step * step);
        float ang = Mathf.Atan2(step, h) * Mathf.Rad2Deg;
        int k = 0;
        for (float x = -postX + step * 0.5f; x < postX - step * 0.4f; x += step, k++)
        {
            b.Box(steel, new Vector3(x, (lo + hi) * 0.5f, z), new Vector3(0.05f, diag, 0.05f), Quaternion.Euler(0f, 0f, k % 2 == 0 ? ang : -ang));
        }
        // Hanger + pulley at the rope.
        b.Box(steel, new Vector3(ropeX, (ropeY + lo) * 0.5f + 0.1f, z), new Vector3(0.05f, lo - ropeY, 0.05f), Quaternion.identity);
        b.Cylinder(steel, new Vector3(ropeX, ropeY + 0.14f, z), 0.17f, 0.06f, Quaternion.Euler(0f, 0f, 90f));
        b.Box(steel, new Vector3(ropeX, ropeY + 0.14f, z), new Vector3(0.12f, 0.42f, 0.04f), Quaternion.identity);
    }

    /// <summary>A round tube through <paramref name="points"/> (the steel cable).</summary>
    private static void Tube(Batch b, Material m, Vector3[] points, float radius)
    {
        const int sides = 6;
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 a = points[i], c = points[i + 1];
            Vector3 dir = (c - a).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, dir);
            for (int s = 0; s < sides; s++)
            {
                float t0 = s / (float)sides * Mathf.PI * 2f, t1 = (s + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 o0 = (side * Mathf.Cos(t0) + up * Mathf.Sin(t0)) * radius;
                Vector3 o1 = (side * Mathf.Cos(t1) + up * Mathf.Sin(t1)) * radius;
                b.Quad(m, a + o0, a + o1, c + o1, c + o0);
            }
        }
    }

    /// <summary>
    /// Iron street lamp with a lantern on an arm reaching over the street. <paramref name="realLight"/>
    /// adds a point light; the others only glow (halo + pool), to keep the light count down.
    /// </summary>
    private static bool StreetLamp(Batch b, Batch glow, Material iron, Material glass, Material halo, Material pool,
        Transform lights, Vector3 p, bool realLight)
    {
        float toward = p.x > 0f ? -1f : 1f; // arm reaches towards the centre of the street
        b.Cylinder(iron, p + Vector3.up * 0.25f, 0.17f, 0.5f);
        b.Cylinder(iron, p + Vector3.up * 2.4f, 0.065f, 4.0f);
        b.Cylinder(iron, p + Vector3.up * 0.55f, 0.11f, 0.08f);
        b.Cylinder(iron, p + Vector3.up * 2.2f, 0.09f, 0.06f);
        b.Sphere(iron, p + Vector3.up * 4.45f, 0.1f);
        Vector3 armEnd = p + new Vector3(toward * 1.0f, 4.3f, 0f);
        b.Box(iron, p + new Vector3(toward * 0.5f, 4.3f, 0f), new Vector3(1.0f, 0.05f, 0.05f), Quaternion.identity);
        b.Box(iron, p + new Vector3(toward * 0.3f, 4.05f, 0f), new Vector3(0.05f, 0.5f, 0.05f), Quaternion.Euler(0f, 0f, toward * 50f));
        Vector3 lantern = armEnd + Vector3.down * 0.38f;
        b.Box(iron, lantern + Vector3.up * 0.25f, new Vector3(0.38f, 0.06f, 0.38f), Quaternion.Euler(0f, 45f, 0f));
        b.Box(glass, lantern, new Vector3(0.26f, 0.4f, 0.26f), Quaternion.Euler(0f, 45f, 0f));
        b.Box(iron, lantern + Vector3.down * 0.22f, new Vector3(0.2f, 0.05f, 0.2f), Quaternion.Euler(0f, 45f, 0f));
        glow.Billboardish(halo, lantern, 1.1f, 1.1f);
        Vector3 g = new Vector3(lantern.x, 0.025f, lantern.z);
        glow.Quad(pool, g + new Vector3(-3.5f, 0f, -3.5f), g + new Vector3(3.5f, 0f, -3.5f), g + new Vector3(3.5f, 0f, 3.5f), g + new Vector3(-3.5f, 0f, 3.5f));
        if (realLight)
        {
            Light l = DeadlockSceneKit.PointLight("StreetLamp", lights, lantern + Vector3.down * 0.1f, new Color(1f, 0.72f, 0.45f), 2.4f, 13f);
            l.renderMode = LightRenderMode.ForcePixel;
        }
        return true;
    }

    /// <summary>Casts from <paramref name="origin"/> along <paramref name="dir"/>; a facade hit gets a four-pane lit window (or stays dark).</summary>
    private static int TryWindow(Batch b, Material[] mats, System.Random rng, Vector3 origin, Vector3 dir, float minDist, float maxDist)
    {
        double roll = rng.NextDouble();
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, maxDist)) return 0;
        if (hit.distance < minDist || Mathf.Abs(hit.normal.x) < 0.9f) return 0;
        if (roll > 0.55) return 0; // dark window
        Material m = mats[rng.Next(mats.Length)];
        Vector3 n = hit.normal;
        Vector3 c = hit.point + n * 0.04f;
        Vector3 along = Vector3.Cross(Vector3.up, n).normalized;
        const float pw = 0.44f, ph = 0.66f, gap = 0.06f;
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sy in new[] { -1, 1 })
            {
                Vector3 pc = c + along * (sx * (pw + gap) * 0.5f) + Vector3.up * (sy * (ph + gap) * 0.5f);
                Vector3 r = along * (pw * 0.5f), u = Vector3.up * (ph * 0.5f);
                b.Quad(m, pc - r - u, pc + r - u, pc + r + u, pc - r + u);
            }
        }
        return 1;
    }

    /// <summary>A generic old sedan: body, cabin with dark glass, chrome bumpers, wheels and tail lights.</summary>
    private static void Car(Batch b, Material paint, Material chrome, Material tyre, Material glass, Material tail, Vector3 pos, Quaternion rot)
    {
        Vector3 P(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
        // Local +Z is the car's nose.
        b.Box(paint, P(0f, 0.62f, 0f), new Vector3(1.78f, 0.5f, 4.3f), rot);
        b.Box(paint, P(0f, 0.92f, 1.25f), new Vector3(1.6f, 0.16f, 1.5f), rot * Quaternion.Euler(4f, 0f, 0f));
        b.Box(glass, P(0f, 1.18f, -0.25f), new Vector3(1.56f, 0.52f, 2.0f), rot);
        b.Box(paint, P(0f, 1.47f, -0.3f), new Vector3(1.62f, 0.07f, 2.15f), rot);
        foreach (int s in new[] { -1, 1 })
        {
            b.Box(paint, P(s * 0.79f, 1.18f, 0.72f), new Vector3(0.06f, 0.52f, 0.08f), rot * Quaternion.Euler(-18f, 0f, 0f));
            b.Box(paint, P(s * 0.79f, 1.18f, -1.27f), new Vector3(0.06f, 0.52f, 0.1f), rot);
            b.Box(paint, P(s * 0.79f, 1.18f, -0.25f), new Vector3(0.06f, 0.52f, 0.08f), rot);
            b.Box(chrome, P(s * 0.92f, 0.38f, 0f), new Vector3(0.12f, 0.05f, 2.3f), rot);
            foreach (float wz in new[] { -1.42f, 1.42f })
            {
                b.Box(paint, P(s * 0.83f, 0.66f, wz), new Vector3(0.3f, 0.36f, 1.0f), rot);
                b.Mesh(tyre, BuiltinMesh("Cylinder"), Matrix4x4.TRS(P(s * 0.8f, 0.36f, wz), rot * Quaternion.Euler(0f, 0f, 90f), new Vector3(0.72f, 0.13f, 0.72f)));
                b.Mesh(chrome, BuiltinMesh("Cylinder"), Matrix4x4.TRS(P(s * 0.94f, 0.36f, wz), rot * Quaternion.Euler(0f, 0f, 90f), new Vector3(0.36f, 0.02f, 0.36f)));
            }
            b.Sphere(chrome, P(s * 0.58f, 0.85f, 2.15f), 0.13f);
            b.Box(tail, P(s * 0.62f, 0.75f, -2.16f), new Vector3(0.16f, 0.1f, 0.02f), rot);
        }
        b.Box(chrome, P(0f, 0.42f, 2.2f), new Vector3(1.86f, 0.14f, 0.12f), rot);
        b.Box(chrome, P(0f, 0.42f, -2.2f), new Vector3(1.86f, 0.14f, 0.12f), rot);
        b.Box(chrome, P(0f, 0.72f, 2.16f), new Vector3(0.7f, 0.3f, 0.04f), rot);
    }

    // =====================================================================================
    // Procedural textures
    // =====================================================================================

    private static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>Staggered planks: 16 boards across, 2 lengths per tile, grain and dark seams. Tiles seamlessly.</summary>
    private static Texture2D WoodPlanks(string name, Color baseColor)
    {
        const int size = 1024, boards = 16, len = 512;
        int bw = size / boards;
        return MakeTexture(name, size, size, (x, y) =>
        {
            int col = x / bw;
            int off = (int)(Hash(col, 3) * len);
            int yy = (y + off) % size;
            int seg = yy / len;
            int ly = yy % len;
            int lx = x % bw;
            float tone = 0.78f + Hash(col, seg + 10) * 0.4f;
            float seed = Hash(col, seg) * 50f;
            float grain = Mathf.PerlinNoise(seed + lx * 0.09f, ly * 0.004f);
            float fine = Mathf.PerlinNoise(seed * 2f + lx * 0.6f, ly * 0.02f);
            float knot = Mathf.PerlinNoise(seed + lx * 0.03f, seed + ly * 0.03f);
            float v = tone * (0.75f + grain * 0.35f + fine * 0.1f) * (knot > 0.72f ? 0.8f : 1f);
            if (lx < 2 || ly < 2) v *= 0.35f;
            else if (lx < 4 || ly < 4) v *= 0.75f;
            Color c = baseColor * v;
            c.a = 1f;
            return c;
        });
    }

    /// <summary>Dark teal wallpaper: soft stripes and a repeating diamond/fleur motif. 512 px = 2 m.</summary>
    private static Texture2D Wallpaper(string name)
    {
        const int size = 512, cell = 128;
        Color baseC = new Color(0.2f, 0.31f, 0.29f);
        Color line = new Color(0.44f, 0.43f, 0.3f);
        return MakeTexture(name, size, size, (x, y) =>
        {
            int cx = x % cell, cy = y % cell;
            float dx = Mathf.Abs(cx - cell * 0.5f), dy = Mathf.Abs(cy - cell * 0.5f);
            float diamond = dx + dy * 0.7f;
            float motif = Mathf.Max(
                1f - Mathf.Abs(diamond - 38f) / 2.2f,
                1f - Mathf.Abs(Mathf.Sqrt(dx * dx + (dy - 6f) * (dy - 6f)) - 10f) / 1.8f);
            float petals = 1f - Mathf.Clamp01(Mathf.Abs(dx * 1.6f - Mathf.Abs(cy - cell * 0.5f - 16f) * 0.5f) / 3f) * (dy < 26f ? 1f : 0f);
            motif = Mathf.Clamp01(Mathf.Max(motif, petals * 0.6f));
            float stripe = (x % 64) < 3 ? 0.12f : 0f;
            float n = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.08f;
            Color c = Color.Lerp(baseC * (0.95f + n + stripe), line, motif * 0.55f);
            c.a = 1f;
            return c;
        });
    }

    /// <summary>Raised panel wainscot: frame, bevel highlight/shadow, darker field. 512 px = 1 m.</summary>
    private static Texture2D Wainscot(string name)
    {
        const int size = 512, frame = 52, bevel = 14;
        Color wood = new Color(0.27f, 0.155f, 0.09f);
        return MakeTexture(name, size, size, (x, y) =>
        {
            float grain = Mathf.PerlinNoise(x * 0.006f, y * 0.08f) * 0.25f + Mathf.PerlinNoise(x * 0.04f, y * 0.4f) * 0.06f;
            float v = 0.85f + grain;
            int ix = Mathf.Min(x, size - 1 - x), iy = Mathf.Min(y, size - 1 - y);
            int d = Mathf.Min(ix, iy);
            if (d < 3) v *= 0.45f;
            else if (d < frame) v *= 1.05f;
            else if (d < frame + bevel)
            {
                bool lit = (iy == d && y > size / 2) || (ix == d && x < size / 2);
                v *= lit ? 1.35f : 0.6f;
            }
            else v *= 0.92f;
            Color c = wood * v;
            c.a = 1f;
            return c;
        });
    }

    /// <summary>Whole-rug texture: deep red field, central medallion, gold and navy borders, some wear.</summary>
    private static Texture2D Rug(string name)
    {
        const int w = 512, h = 608;
        Color field = new Color(0.36f, 0.06f, 0.05f);
        Color gold = new Color(0.62f, 0.45f, 0.18f);
        Color navy = new Color(0.07f, 0.08f, 0.18f);
        Color cream = new Color(0.7f, 0.6f, 0.45f);
        return MakeTexture(name, w, h, (x, y) =>
        {
            int ix = Mathf.Min(x, w - 1 - x), iy = Mathf.Min(y, h - 1 - y);
            int d = Mathf.Min(ix, iy);
            Color c;
            if (d < 6) c = cream * 0.8f;
            else if (d < 12) c = gold;
            else if (d < 42)
            {
                float t = ((ix > iy ? y : x) % 34) / 34f;
                float pattern = Mathf.Abs(t - 0.5f) * 2f;
                float band = Mathf.Abs((d - 27f) / 15f);
                c = (pattern + band * 0.6f) < 0.6f ? gold : navy;
            }
            else if (d < 48) c = gold;
            else
            {
                float u = (x - w * 0.5f) / (w * 0.5f), v = (y - h * 0.5f) / (h * 0.5f);
                float r = Mathf.Sqrt(u * u * 1.4f + v * v);
                float diamond = Mathf.Abs(u) + Mathf.Abs(v) * 0.8f;
                c = field;
                if (r < 0.32f) c = Color.Lerp(navy, field, Mathf.Clamp01(r / 0.32f) * 0.3f);
                if (Mathf.Abs(r - 0.34f) < 0.02f || Mathf.Abs(diamond - 0.55f) < 0.015f) c = gold;
                if (r < 0.1f) c = gold * 0.9f;
                float lattice = Mathf.Abs(Mathf.Sin(u * 14f) * Mathf.Sin(v * 12f));
                if (r > 0.38f && lattice > 0.93f) c = Color.Lerp(c, gold, 0.35f);
            }
            float wear = Mathf.PerlinNoise(x * 0.02f, y * 0.02f);
            float fine = Mathf.PerlinNoise(x * 0.5f, y * 0.5f);
            c *= 0.82f + wear * 0.25f + fine * 0.08f;
            c.a = 1f;
            return c;
        });
    }

    /// <summary>A small original night landscape for the painting over the mantel.</summary>
    private static Texture2D Painting(string name)
    {
        const int w = 512, h = 384;
        return MakeTexture(name, w, h, (x, y) =>
        {
            float u = x / (float)w, v = y / (float)h;
            Color sky = Color.Lerp(new Color(0.55f, 0.42f, 0.25f), new Color(0.05f, 0.12f, 0.13f), Mathf.Pow(v, 0.7f));
            float moon = Vector2.Distance(new Vector2(u * 1.33f, v), new Vector2(0.95f, 0.72f));
            if (moon < 0.06f) sky = new Color(0.92f, 0.88f, 0.7f);
            else sky += new Color(0.25f, 0.22f, 0.15f) * Mathf.Clamp01(0.25f - moon) * 2f;
            float hills = 0.32f + Mathf.PerlinNoise(u * 3f, 1.7f) * 0.18f;
            float nearHills = 0.18f + Mathf.PerlinNoise(u * 6f + 4f, 3.1f) * 0.12f;
            Color c = sky;
            if (v < hills) c = new Color(0.08f, 0.12f, 0.1f);
            if (v < nearHills) c = new Color(0.03f, 0.05f, 0.04f);
            float brush = Mathf.PerlinNoise(x * 0.08f, y * 0.3f) * 0.12f;
            c *= 0.92f + brush;
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D MakeTexture(string name, int w, int h, Func<int, int, Color> pixel)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var cols = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cols[y * w + x] = pixel(x, y);
        tex.SetPixels(cols);
        tex.Apply();

        DeadlockSceneKit.EnsureFolder(TexDir);
        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =====================================================================================
    // Mesh merging
    // =====================================================================================

    private static Mesh BuiltinMesh(string name) => Resources.GetBuiltinResource<Mesh>(name + ".fbx");

    private static Mesh SaveMesh(Mesh mesh)
    {
        DeadlockSceneKit.EnsureFolder(DeadlockSceneKit.MeshDir);
        string path = $"{DeadlockSceneKit.MeshDir}/{mesh.name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Accumulates geometry per material and writes one merged mesh per material.</summary>
    private sealed class Batch
    {
        private sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
        }

        private readonly Dictionary<Material, Part> parts = new Dictionary<Material, Part>();

        private Part Get(Material m)
        {
            if (!parts.TryGetValue(m, out Part p))
            {
                p = new Part();
                parts[m] = p;
            }
            return p;
        }

        public void Box(Material m, Vector3 center, Vector3 size, Quaternion rotation, float tile = 1f)
        {
            Part p = Get(m);
            Vector3 h = size * 0.5f;

            void Face(Vector3 normal, Vector3 right, Vector3 up, float width, float height)
            {
                int s = p.V.Count;
                Vector3 c = Vector3.Scale(normal, h);
                Vector3 r = right * (width * 0.5f);
                Vector3 u = up * (height * 0.5f);
                p.V.Add(center + rotation * (c - r - u)); p.V.Add(center + rotation * (c + r - u));
                p.V.Add(center + rotation * (c + r + u)); p.V.Add(center + rotation * (c - r + u));
                Vector3 wn = rotation * normal;
                for (int i = 0; i < 4; i++) p.N.Add(wn);
                p.UV.Add(new Vector2(0, 0)); p.UV.Add(new Vector2(width / tile, 0));
                p.UV.Add(new Vector2(width / tile, height / tile)); p.UV.Add(new Vector2(0, height / tile));
                p.T.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }

            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
        }

        public void Mesh(Material m, Mesh mesh, Matrix4x4 matrix)
        {
            Part p = Get(m);
            int s = p.V.Count;
            Vector3[] v = mesh.vertices;
            Vector3[] n = mesh.normals;
            Vector2[] uv = mesh.uv;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < v.Length; i++)
            {
                p.V.Add(matrix.MultiplyPoint3x4(v[i]));
                p.N.Add(normalMatrix.MultiplyVector(n[i]).normalized);
                p.UV.Add(i < uv.Length ? uv[i] : Vector2.zero);
            }
            foreach (int t in mesh.triangles) p.T.Add(s + t);
        }

        public void Cylinder(Material m, Vector3 center, float radius, float height, Quaternion? rotation = null)
        {
            Mesh(m, BuiltinMesh("Cylinder"), Matrix4x4.TRS(center, rotation ?? Quaternion.identity, new Vector3(radius * 2f, height * 0.5f, radius * 2f)));
        }

        public void Sphere(Material m, Vector3 center, float radius)
        {
            Mesh(m, BuiltinMesh("Sphere"), Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * radius * 2f));
        }

        /// <summary>Quad a0-a1 (uv.y 0) to b1-b0 (uv.y 1), double sided via the shader (Cull Off).</summary>
        public void Quad(Material m, Vector3 a0, Vector3 a1, Vector3 b1, Vector3 b0)
        {
            Part p = Get(m);
            int s = p.V.Count;
            Vector3 normal = Vector3.Cross(a1 - a0, b0 - a0).normalized;
            p.V.Add(a0); p.V.Add(a1); p.V.Add(b1); p.V.Add(b0);
            for (int i = 0; i < 4; i++) p.N.Add(normal);
            p.UV.Add(new Vector2(0, 0)); p.UV.Add(new Vector2(1, 0)); p.UV.Add(new Vector2(1, 1)); p.UV.Add(new Vector2(0, 1));
            p.T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }

        /// <summary>Three upright quads crossed at 60 degrees so a soft glow reads from any side.</summary>
        public void Billboardish(Material m, Vector3 center, float width, float height)
        {
            for (int i = 0; i < 3; i++)
            {
                Quaternion q = Quaternion.Euler(0f, i * 60f, 0f);
                Vector3 r = q * Vector3.right * (width * 0.5f);
                Vector3 u = Vector3.up * (height * 0.5f);
                Part p = Get(m);
                int s = p.V.Count;
                p.V.Add(center - r - u); p.V.Add(center + r - u); p.V.Add(center + r + u); p.V.Add(center - r + u);
                Vector3 n = q * Vector3.forward;
                for (int k = 0; k < 4; k++) p.N.Add(n);
                p.UV.Add(new Vector2(0, 0)); p.UV.Add(new Vector2(1, 0)); p.UV.Add(new Vector2(1, 1)); p.UV.Add(new Vector2(0, 1));
                p.T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
        }

        public void Flush(Transform parent, string prefix, bool castShadows = true)
        {
            foreach (KeyValuePair<Material, Part> kv in parts)
            {
                Part p = kv.Value;
                var mesh = new Mesh { name = $"{prefix}_{kv.Key.name}" };
                if (p.V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(p.V);
                mesh.SetNormals(p.N);
                mesh.SetUVs(0, p.UV);
                mesh.SetTriangles(p.T, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                mesh = SaveMesh(mesh);

                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            parts.Clear();
        }
    }
}
