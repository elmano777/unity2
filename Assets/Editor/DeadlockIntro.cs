using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Intro scene, the game's opening like Deadlock's "press any key" screen: the player
/// stands on a rooftop over the city at dusk (towers against an orange sky, lit windows, haze), the
/// wheel logo burns into the sky with DEADLOCK under it, and any button goes on to the title screen.
/// </summary>
/// <remarks>
/// The scene is a copy of TitleScreen with the lobby stripped out, so it keeps the same camera rig,
/// laser, fader and event system. Re-run after changing the title screen rig. Title textures come
/// from Tools/make_intro_tex.py (logo from the game's wheel icon, wordmark drawn in Georgia Bold).
/// The city is one generated mesh (Assets/Art/Meshes/Menus/IntroCity.asset). Safe to re-run.
/// </remarks>
public static class DeadlockIntro
{
    private const string TitlePath = "Assets/Scenes/TitleScreen.unity";
    private const string ScenePath = "Assets/Scenes/Intro.unity";
    private const string TextureDir = "Assets/Art/Textures/Intro";

    private const float StreetY = -60f;     // the rooftop is at y = 0, the streets far below
    private const float FacadeTile = 14f;    // metres per repeat of the window texture
    private static readonly Vector3 LogoPosition = new Vector3(0f, 10.4f, 21f);
    private static readonly Vector3 WordmarkPosition = new Vector3(0f, 2.5f, 21f);

    private static readonly Color SkyTop = new Color(0.07f, 0.09f, 0.2f);
    private static readonly Color SkyMid = new Color(0.62f, 0.38f, 0.4f);
    private static readonly Color SkyHorizon = new Color(1f, 0.6f, 0.36f);
    private static readonly Color Haze = new Color(0.46f, 0.3f, 0.32f);

    [MenuItem("Deadlock/Setup/11 Intro (city and logo)")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
        AssetDatabase.CopyAsset(TitlePath, ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        StripTitleScreen();
        ConfigureTextures();

        Transform root = new GameObject("Intro").transform;
        BuildSky(root);
        BuildCity(root);
        BuildRooftop(root);
        IntroScreen intro = BuildTitleArt(root);
        SetupLighting(root);
        SetupPlayer(intro);

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            bool dynamic = t.GetComponent<Light>() || t.GetComponent<ParticleSystem>() || t.GetComponent<TextMeshPro>()
                           || t.GetComponent<IntroScreen>() || t.name == "Logo" || t.name == "Wordmark";
            t.gameObject.isStatic = !dynamic;
        }

        DeadlockMenuPolish.ApplyToOpenScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettingsFirst();
        DeadlockPlayFromTitle.Apply();
        Debug.Log("[Deadlock Intro] Intro scene built.");
    }

    private static void StripTitleScreen()
    {
        foreach (string name in new[] { "OracleLobby", "TitleScreenManager", "TitleCanvas", "InvisibleFloor" })
        {
            DeadlockOnlineSetup.Replace(name);
        }

        // The player just stands and looks: no walking here.
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null)
        {
            Object.DestroyImmediate(rig.GetComponent<ComfortLocomotion>());
        }
        DeadlockOnlineSetup.Replace("ComfortVignette");
    }

    private static void ConfigureTextures()
    {
        foreach (string name in new[] { "IntroLogo", "IntroWordmark", "IntroFacadeLights" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath($"{TextureDir}/{name}.png");
            bool facade = name == "IntroFacadeLights";
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = facade;          // the title art stores masks, not colours
            importer.wrapMode = facade ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }

    // ---------- Sky ----------

    private static void BuildSky(Transform root)
    {
        Transform sky = new GameObject("Sky").transform;
        sky.SetParent(root, false);

        // Two stacked open cylinders around the player: haze-orange horizon up to a pink band, then
        // up into the night blue. The camera clears to the top colour, so looking straight up matches.
        Material low = DeadlockSceneKit.Shader("Intro_SkyLow", "Deadlock/UnlitGradient");
        low.SetColor("_Bottom", SkyHorizon);
        low.SetColor("_Top", SkyMid);
        Material high = DeadlockSceneKit.Shader("Intro_SkyHigh", "Deadlock/UnlitGradient");
        high.SetColor("_Bottom", SkyMid);
        high.SetColor("_Top", SkyTop);

        DeadlockSceneKit.MeshObject("SkyLow", sky, Vector3.zero, SkyBand("IntroSkyLow", 900f, StreetY - 40f, 140f), low);
        DeadlockSceneKit.MeshObject("SkyHigh", sky, Vector3.zero, SkyBand("IntroSkyHigh", 900f, 140f, 1100f), high);
        Material cap = DeadlockSceneKit.Shader("Intro_SkyCap", "Deadlock/UnlitGradient");
        cap.SetColor("_Bottom", SkyTop);
        cap.SetColor("_Top", SkyTop);
        GameObject lid = Quad("SkyCap", sky, Vector3.up * 1100f, new Vector2(1900f, 1900f), cap);
        lid.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        // The setting sun behind the downtown towers, and a few warm streaks of cloud.
        Material sun = DeadlockSceneKit.Glow("Intro_SunGlow", new Color(1f, 0.72f, 0.42f, 0.8f), 1f);
        Material cloud = DeadlockSceneKit.Glow("Intro_Cloud", new Color(1f, 0.62f, 0.5f, 0.22f), 1f);
        Quad("SunGlow", sky, new Vector3(30f, 25f, 820f), new Vector2(520f, 300f), sun);
        Quad("SunCore", sky, new Vector3(30f, 20f, 815f), new Vector2(120f, 90f), sun);

        var rng = new System.Random(11);
        for (int i = 0; i < 14; i++)
        {
            float angle = (float)(rng.NextDouble() * 220 - 110) * Mathf.Deg2Rad;
            float distance = 760f + (float)rng.NextDouble() * 80f;
            var p = new Vector3(Mathf.Sin(angle) * distance, 70f + (float)rng.NextDouble() * 170f, Mathf.Cos(angle) * distance);
            GameObject streak = Quad("Cloud", sky, p, new Vector2(260f + (float)rng.NextDouble() * 260f, 18f + (float)rng.NextDouble() * 22f), cloud);
            streak.transform.localRotation = Quaternion.LookRotation(p.normalized * -1f) * Quaternion.Euler(0f, 180f, 0f);
        }
    }

    private static Mesh SkyBand(string name, float radius, float bottom, float top)
    {
        const int segments = 64;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        for (int s = 0; s <= segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
            vertices.Add(dir + Vector3.up * bottom); uvs.Add(new Vector2(s / (float)segments, 0f));
            vertices.Add(dir + Vector3.up * top); uvs.Add(new Vector2(s / (float)segments, 1f));
            if (s == segments) break;
            int i = s * 2;
            triangles.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 3f);
        return SaveMesh(mesh);
    }

    // ---------- City ----------

    private static void BuildCity(Transform root)
    {
        Transform city = new GameObject("City").transform;
        city.SetParent(root, false);

        var lights = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/IntroFacadeLights.png");
        Material facade = DeadlockSceneKit.Lit("Intro_Facade", new Color(0.035f, 0.04f, 0.06f), 0.35f, 0f, null, new Color(1.1f, 0.8f, 0.5f));
        facade.SetTexture("_EmissionMap", lights);
        EditorUtility.SetDirty(facade);

        var builder = new CityMesh();
        var rng = new System.Random(2026);
        float R() => (float)rng.NextDouble();

        // Blocks in all directions, so turning around still shows the city. Taller towards the
        // downtown cluster in front; nothing near the rooftop rises above the parapet line.
        Vector2 downtown = new Vector2(0f, 300f);
        for (int i = 0; i < 1100; i++)
        {
            float angle = R() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(45f, 620f, Mathf.Sqrt(R()));
            var p = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * distance;
            p.x = Mathf.Round(p.x / 18f) * 18f;
            p.y = Mathf.Round(p.y / 18f) * 18f;
            if (Mathf.Abs(p.x) < 30f && Mathf.Abs(p.y) < 30f) continue;

            float width = 9f + R() * 7f, depth = 9f + R() * 7f;
            float core = Mathf.Exp(-(p - downtown).sqrMagnitude / (170f * 170f));
            float height = 25f + R() * 40f + core * (40f + R() * 130f);
            if (distance < 90f) height = Mathf.Min(height, -StreetY - 8f - R() * 15f);
            builder.Box(new Vector3(p.x, StreetY + height * 0.5f, p.y), new Vector3(width, height, depth), StreetY);
        }

        // Landmark skyscrapers: stepped towers with crowns and spires, like the skyline in the game.
        Landmark(builder, new Vector3(0f, StreetY, 330f), 22f, 230f, city, 0);
        Landmark(builder, new Vector3(-150f, StreetY, 270f), 18f, 170f, city, 1);
        Landmark(builder, new Vector3(165f, StreetY, 360f), 19f, 195f, city, 2);
        Landmark(builder, new Vector3(-60f, StreetY, 470f), 16f, 160f, city, 3);
        Landmark(builder, new Vector3(90f, StreetY, 200f), 14f, 130f, city, 4);

        DeadlockSceneKit.MeshObject("Buildings", city, Vector3.zero, builder.Build("IntroCity"), facade);

        Material ground = DeadlockSceneKit.Lit("Intro_Street", new Color(0.05f, 0.05f, 0.06f), 0.1f);
        DeadlockSceneKit.Box("Streets", city, new Vector3(0f, StreetY - 0.5f, 0f), new Vector3(1400f, 1f, 1400f), ground, 50f, false);

        // Street glow rising between the blocks, so the city doesn't end in a black floor.
        Material streetGlow = DeadlockSceneKit.Glow("Intro_StreetGlow", new Color(1f, 0.6f, 0.3f, 0.18f), 1f);
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3((R() - 0.5f) * 700f, StreetY + 2f, 80f + R() * 500f);
            DeadlockSceneKit.FloorDisc("StreetGlow", city, p, 120f + R() * 80f, streetGlow);
        }
    }

    private static void Landmark(CityMesh builder, Vector3 basePoint, float halfWidth, float height, Transform city, int seed)
    {
        // Three setbacks, then a crown and a needle spire.
        float y = basePoint.y;
        float[] fractions = { 0.55f, 0.25f, 0.12f };
        float w = halfWidth * 2f;
        foreach (float f in fractions)
        {
            float h = height * f;
            builder.Box(new Vector3(basePoint.x, y + h * 0.5f, basePoint.z), new Vector3(w, h, w), y);
            y += h;
            w *= 0.72f;
        }

        Material dark = DeadlockSceneKit.Lit("Intro_Spire", new Color(0.06f, 0.06f, 0.08f), 0.5f, 0.4f);
        Mesh crown = DeadlockSceneKit.Lathe("IntroCrown", new[]
        {
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0.15f), new Vector2(0.32f, 0.5f), new Vector2(0.12f, 0.85f), new Vector2(0.03f, 1f), new Vector2(0f, 1f)
        }, 4);
        GameObject top = DeadlockSceneKit.MeshObject($"Crown_{seed}", city, new Vector3(basePoint.x, y, basePoint.z), crown, dark);
        top.transform.localScale = new Vector3(w * 1.4f, height * 0.12f, w * 1.4f);
        top.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);

        DeadlockSceneKit.Box($"Spire_{seed}", city, new Vector3(basePoint.x, y + height * 0.12f + 10f, basePoint.z), new Vector3(0.8f, 22f, 0.8f), dark, 1f, false);

        // A warm beacon on the crown.
        Material beacon = DeadlockSceneKit.Glow("Intro_Beacon", new Color(1f, 0.75f, 0.45f, 0.9f), 1f);
        GameObject glow = Quad($"Beacon_{seed}", city, new Vector3(basePoint.x, y + height * 0.06f, basePoint.z - w), new Vector2(w * 1.6f, w * 1.2f), beacon);
        glow.AddComponent<FaceCamera>();
    }

    /// <summary>All the city blocks as one mesh: walls get window UVs in metres from the street up.</summary>
    private class CityMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public void Box(Vector3 center, Vector3 size, float floorY)
        {
            Vector3 h = size * 0.5f;
            Wall(center, h, Vector3.forward, Vector3.left, size.x, floorY);
            Wall(center, h, Vector3.back, Vector3.right, size.x, floorY);
            Wall(center, h, Vector3.right, Vector3.forward, size.z, floorY);
            Wall(center, h, Vector3.left, Vector3.back, size.z, floorY);

            // Roof: mapped to the dark margin of the window texture.
            int s = vertices.Count;
            float y = center.y + h.y;
            vertices.Add(new Vector3(center.x - h.x, y, center.z - h.z));
            vertices.Add(new Vector3(center.x + h.x, y, center.z - h.z));
            vertices.Add(new Vector3(center.x + h.x, y, center.z + h.z));
            vertices.Add(new Vector3(center.x - h.x, y, center.z + h.z));
            for (int i = 0; i < 4; i++) { normals.Add(Vector3.up); uvs.Add(new Vector2(0.01f, 0.01f)); }
            triangles.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }

        private void Wall(Vector3 center, Vector3 h, Vector3 normal, Vector3 right, float width, float floorY)
        {
            int s = vertices.Count;
            Vector3 c = center + Vector3.Scale(normal, h);
            Vector3 r = right * (width * 0.5f);
            float bottom = center.y - h.y, top = center.y + h.y;
            vertices.Add(new Vector3(c.x, bottom, c.z) - r);
            vertices.Add(new Vector3(c.x, bottom, c.z) + r);
            vertices.Add(new Vector3(c.x, top, c.z) + r);
            vertices.Add(new Vector3(c.x, top, c.z) - r);
            for (int i = 0; i < 4; i++) normals.Add(normal);

            // Offset each building's texture so neighbouring towers don't share the same lit windows.
            float u0 = Mathf.Repeat(c.x * 0.37f + c.z * 0.61f, 1f);
            float v0 = (bottom - floorY) / FacadeTile, v1 = (top - floorY) / FacadeTile;
            uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0 + width / FacadeTile, v0));
            uvs.Add(new Vector2(u0 + width / FacadeTile, v1)); uvs.Add(new Vector2(u0, v1));
            triangles.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return SaveMesh(mesh);
        }
    }

    // ---------- Rooftop ----------

    private static void BuildRooftop(Transform root)
    {
        Transform roof = new GameObject("Rooftop").transform;
        roof.SetParent(root, false);

        var paving = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Lane/precast_stone_paving_diff.jpg");
        var brick = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Lane/sandstone_blocks_08_diff.jpg");
        Material floor = DeadlockSceneKit.Lit("Intro_RoofFloor", new Color(0.36f, 0.32f, 0.32f), 0.35f, 0f, paving);
        Material stone = DeadlockSceneKit.Lit("Intro_RoofStone", new Color(0.42f, 0.36f, 0.34f), 0.2f, 0f, brick);
        Material metal = DeadlockSceneKit.Lit("Intro_RoofMetal", new Color(0.12f, 0.11f, 0.1f), 0.5f, 0.6f);
        Material lamp = DeadlockSceneKit.Glow("Intro_Lamp", new Color(1f, 0.75f, 0.45f, 0.95f), 1f);

        // The building the player stands on: 12 x 10 m roof, walls dropping to the street.
        DeadlockSceneKit.Box("RoofFloor", roof, new Vector3(0f, -0.1f, 0f), new Vector3(12f, 0.2f, 10f), floor, 2.5f);
        DeadlockSceneKit.Box("Building", roof, new Vector3(0f, StreetY * 0.5f - 0.2f, 0f), new Vector3(12.2f, -StreetY, 10.2f), stone, 3f, false);

        // Parapet in front (waist high, so the city opens up beyond it) and on the sides.
        DeadlockSceneKit.Box("Parapet_Front", roof, new Vector3(0f, 0.5f, 4.8f), new Vector3(12f, 1f, 0.4f), stone, 2f);
        DeadlockSceneKit.Box("Coping_Front", roof, new Vector3(0f, 1.04f, 4.8f), new Vector3(12.3f, 0.08f, 0.6f), floor, 2f, false);
        foreach (int side in new[] { -1, 1 })
        {
            DeadlockSceneKit.Box($"Parapet_{side}", roof, new Vector3(side * 5.8f, 0.5f, 0f), new Vector3(0.4f, 1f, 10f), stone, 2f);
            DeadlockSceneKit.Box($"Coping_{side}", roof, new Vector3(side * 5.8f, 1.04f, 0f), new Vector3(0.6f, 0.08f, 10.3f), floor, 2f, false);
        }

        // Stair hut behind the player with a lamp over its door, a water tower and a chimney.
        DeadlockSceneKit.Box("StairHut", roof, new Vector3(1.5f, 1.4f, -3.6f), new Vector3(4f, 2.8f, 2.6f), stone, 2f);
        DeadlockSceneKit.Box("HutDoor", roof, new Vector3(1.5f, 1.05f, -2.29f), new Vector3(1f, 2.1f, 0.04f), metal, 1f, false);
        Quad("DoorLamp", roof, new Vector3(1.5f, 2.35f, -2.25f), new Vector2(0.35f, 0.35f), lamp).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        DeadlockSceneKit.PointLight("DoorLight", roof, new Vector3(1.5f, 2.3f, -1.9f), new Color(1f, 0.7f, 0.42f), 1.6f, 7f);
        // Soft fill from behind, so the parapet in front isn't a black band against the sunset.
        DeadlockSceneKit.PointLight("RoofFill", roof, new Vector3(0f, 1.6f, 2.2f), new Color(1f, 0.72f, 0.5f), 2.5f, 6f);

        Mesh tank = DeadlockSceneKit.Lathe("IntroWaterTank", new[]
        {
            new Vector2(0.9f, 0f), new Vector2(0.9f, 1.6f), new Vector2(0.95f, 1.65f), new Vector2(0.55f, 2.1f), new Vector2(0f, 2.3f)
        }, 20);
        Transform tower = new GameObject("WaterTower").transform;
        tower.SetParent(roof, false);
        tower.localPosition = new Vector3(-3.8f, 0f, -3f);
        DeadlockSceneKit.MeshObject("Tank", tower, new Vector3(0f, 1.6f, 0f), tank, metal);
        foreach (Vector2 leg in new[] { new Vector2(-0.6f, -0.6f), new Vector2(0.6f, -0.6f), new Vector2(-0.6f, 0.6f), new Vector2(0.6f, 0.6f) })
        {
            DeadlockSceneKit.Box("Leg", tower, new Vector3(leg.x, 0.8f, leg.y), new Vector3(0.1f, 1.6f, 0.1f), metal, 1f, false);
        }

        DeadlockSceneKit.Box("Chimney", roof, new Vector3(4.6f, 0.9f, -1.5f), new Vector3(0.7f, 1.8f, 0.7f), stone, 1f);
        DeadlockSceneKit.Box("Vent", roof, new Vector3(-4.4f, 0.3f, 2.4f), new Vector3(0.9f, 0.6f, 0.6f), metal, 1f);
    }

    // ---------- Title art ----------

    private static IntroScreen BuildTitleArt(Transform root)
    {
        Transform title = new GameObject("TitleArt").transform;
        title.SetParent(root, false);

        Material logoMat = LogoMaterial("Intro_Logo", "IntroLogo");
        Material wordMat = LogoMaterial("Intro_Wordmark", "IntroWordmark");

        // Logo 14 m across at 21 m, the wordmark under it at about eye level, like the game's screen.
        GameObject logo = Quad("Logo", title, LogoPosition, new Vector2(14f, 14f), logoMat);
        GameObject word = Quad("Wordmark", title, WordmarkPosition, new Vector2(26f, 6.5f), wordMat);

        // The prompt hangs at the wordmark's depth, just under it: nearer, it slid over the title for
        // players whose eyes are lower or higher than the 1.65 m it was framed for (parallax).
        TextMeshPro prompt = DeadlockSceneKit.Text("Prompt", title, new Vector3(0f, 0.1f, WordmarkPosition.z), "PULSA CUALQUIER BOTÓN PARA EMPEZAR", 8.5f,
            new Color(0.92f, 0.95f, 0.88f), new Vector2(28f, 1.5f), FontStyles.Bold);
        prompt.characterSpacing = 8f;
        prompt.enableWordWrapping = false;
        prompt.alpha = 0f;

        Light light = DeadlockSceneKit.PointLight("LogoLight", title, new Vector3(0f, 5f, 12f), new Color(0.3f, 1f, 0.75f), 2.5f, 16f);

        Material mote = DeadlockSceneKit.Glow("Glow_Mote", new Color(0.4f, 1f, 0.6f, 1f), 1f);
        ParticleSystem embers = DeadlockSceneKit.Particles("Embers", title, LogoPosition + Vector3.down * 3f, mote, new Color(0.45f, 1f, 0.8f, 0.8f),
            true, 18f, 0, 6f, 0.5f, 0.18f, new Vector3(24f, 14f, 6f), false, -0.02f);
        embers.Play();
        ParticleSystem burst = DeadlockSceneKit.Particles("StartBurst", title, LogoPosition, mote, new Color(0.6f, 1f, 0.85f, 1f),
            false, 0f, 120, 1.6f, 9f, 0.35f, new Vector3(4.5f, 0f, 0f), true);

        GameObject director = new GameObject("IntroScreen");
        director.transform.SetParent(root, false);
        IntroScreen intro = director.AddComponent<IntroScreen>();

        AudioSource ambience = director.AddComponent<AudioSource>();
        ambience.playOnAwake = false;
        ambience.volume = 0.7f;
        ambience.spatialBlend = 0f;
        AudioSource sfx = director.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;

        var so = new SerializedObject(intro);
        so.FindProperty("logo").objectReferenceValue = logo.GetComponent<Renderer>();
        so.FindProperty("wordmark").objectReferenceValue = word.GetComponent<Renderer>();
        so.FindProperty("prompt").objectReferenceValue = prompt;
        so.FindProperty("logoLight").objectReferenceValue = light;
        so.FindProperty("startBurst").objectReferenceValue = burst;
        so.FindProperty("ambience").objectReferenceValue = ambience;
        so.FindProperty("music").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/music_title_155bpm.mp3");
        so.FindProperty("sfx").objectReferenceValue = sfx;
        so.ApplyModifiedPropertiesWithoutUndo();
        return intro;
    }

    private static Material LogoMaterial(string name, string texture)
    {
        Material m = DeadlockSceneKit.Shader(name, "Deadlock/IntroLogo");
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}.png"));
        m.SetColor("_Color", new Color(0.78f, 1f, 0.93f));
        m.SetColor("_GlowColor", new Color(0.12f, 0.9f, 0.68f));
        m.SetFloat("_Reveal", 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- Lighting and player ----------

    private static void SetupLighting(Transform root)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.22f, 0.22f, 0.34f);
        RenderSettings.ambientEquatorColor = new Color(0.3f, 0.22f, 0.22f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Haze;
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 1000f;

        // Low sun in front of the player: the towers stand as dark silhouettes with warm rims.
        var sun = new GameObject("Sun");
        sun.transform.SetParent(root, false);
        sun.transform.localRotation = Quaternion.Euler(10f, 182f, 0f);
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.62f, 0.38f);
        light.intensity = 0.9f;
        light.shadows = LightShadows.None;
    }

    private static void SetupPlayer(IntroScreen intro)
    {
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig == null)
        {
            Debug.LogError("[Deadlock Intro] No OVRCameraRig in the copied scene.");
            return;
        }

        rig.transform.SetPositionAndRotation(new Vector3(0f, 0f, 1.2f), Quaternion.identity);

        foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyTop;
            cam.farClipPlane = 2000f;
        }

        ScreenFader fader = rig.GetComponentInChildren<ScreenFader>(true);
        var so = new SerializedObject(intro);
        so.FindProperty("fader").objectReferenceValue = fader;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (fader != null)
        {
            var faderSo = new SerializedObject(fader);
            faderSo.FindProperty("defaultDuration").floatValue = 1.5f;
            faderSo.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AddToBuildSettingsFirst()
    {
        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s.path != ScenePath) scenes.Add(s);
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ---------- Helpers ----------

    private static GameObject Quad(string name, Transform parent, Vector3 position, Vector2 size, Material material)
    {
        GameObject quad = DeadlockSceneKit.Primitive(PrimitiveType.Quad, name, parent, position, new Vector3(size.x, size.y, 1f), material);
        quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return quad;
    }

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
}
