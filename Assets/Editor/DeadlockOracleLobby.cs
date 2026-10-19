using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Turns the flat title screen into the seer's hall (Deadlock's lobby): a dark mansion room with
/// tall arched windows over the night city, a glowing ritual circle, and the hooded seer behind her
/// crystal ball. The player walks up (stick, with the comfort vignette) and strikes the orb; the
/// menu runes "Partida online" and "Mi misión" rise out of it and are chosen with the laser.
/// </summary>
/// <remarks>
/// Rebuilds only the "OracleLobby" root and re-wires the existing TitleScreenManager, fader and
/// audio settings panel, so the title logic (PlayerPrefs, scene loading) is unchanged. Safe to re-run.
/// Textures are the CC0 Poly Haven ones already in the project for the Lane.
/// </remarks>
public static class DeadlockOracleLobby
{
    private const string ScenePath = "Assets/Scenes/TitleScreen.unity";
    private const string TextureDir = "Assets/Art/Textures/Lane";

    // Room: x -7..7, z -7.5..6.5, 7 m high. The orb stands at the origin, the player starts 4.5 m back.
    private const float HalfWidth = 7f;
    private const float BackZ = 6.5f;
    private const float FrontZ = -7.5f;
    private const float Height = 7f;
    private static readonly Vector3 OrbPosition = new Vector3(0f, 1.27f, 0f);
    private static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -4.5f);

    private static readonly Color Green = new Color(0.25f, 1f, 0.55f, 1f);

    private static Material floorMat, wallMat, panelMat, trimMat, curtainMat, frameMat, stoneMat, robeMat, voidMat, skinMat, candleMat;
    private static Material windowMat, skylineMat, glowLine, glowPool, glowCrystal, glowFlame, glowEye, glowDot, orbMat, coreMat, runeAura;

    [MenuItem("Deadlock/Setup/9 Oracle Lobby (title screen)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CreateMaterials();

        DeadlockOnlineSetup.Replace("OracleLobby");
        Transform root = new GameObject("OracleLobby").transform;

        BuildRoom(root);
        BuildWindows(root);
        BuildCircle(root);
        BuildCandles(root);
        OracleOrb orb = BuildOrbAndRunes(root);
        BuildSeer(root);
        BuildTitle(root);
        SetupLighting(root);

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            bool dynamic = t.GetComponent<Light>() || t.GetComponent<ParticleSystem>() || t.GetComponentInParent<OracleOrb>()
                           || t.GetComponentInParent<SeerIdle>() || t.GetComponentInParent<FloatingRune>() || t.GetComponent<TextMeshPro>()
                           || t.GetComponent<FaceCamera>();
            t.gameObject.isStatic = !dynamic;
        }

        SetupPlayer();
        HideFlatTitle();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Lobby] Oracle hall built in TitleScreen.");
    }

    private static void CreateMaterials()
    {
        var paving = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/precast_stone_paving_diff.jpg");
        var sandstone = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/sandstone_blocks_08_diff.jpg");

        floorMat = DeadlockSceneKit.Lit("Lobby_Floor", new Color(0.32f, 0.3f, 0.3f), 0.55f, 0f, paving);
        wallMat = DeadlockSceneKit.Lit("Lobby_Wall", new Color(0.24f, 0.33f, 0.32f), 0.15f, 0f, sandstone);
        panelMat = DeadlockSceneKit.Lit("Lobby_Panel", new Color(0.13f, 0.08f, 0.06f), 0.45f);
        trimMat = DeadlockSceneKit.Lit("Lobby_Trim", new Color(0.55f, 0.42f, 0.2f), 0.6f, 0.8f);
        curtainMat = DeadlockSceneKit.Lit("Lobby_Curtain", new Color(0.05f, 0.15f, 0.12f), 0.35f);
        frameMat = DeadlockSceneKit.Lit("Lobby_Frame", new Color(0.04f, 0.05f, 0.05f), 0.3f);
        stoneMat = DeadlockSceneKit.Lit("Lobby_Pedestal", new Color(0.13f, 0.13f, 0.14f), 0.6f, 0.6f);
        robeMat = DeadlockSceneKit.Lit("Lobby_Robe", new Color(0.34f, 0.13f, 0.3f), 0.35f);
        voidMat = DeadlockSceneKit.Lit("Lobby_FaceVoid", Color.black, 0f);
        skinMat = DeadlockSceneKit.Lit("Lobby_Skin", new Color(0.62f, 0.55f, 0.5f), 0.35f);
        candleMat = DeadlockSceneKit.Lit("Lobby_Candle", new Color(0.92f, 0.86f, 0.72f), 0.2f, 0f, null, new Color(0.25f, 0.15f, 0.05f));

        windowMat = DeadlockSceneKit.Shader("Lobby_Window", "Deadlock/UnlitGradient");
        windowMat.SetColor("_Bottom", new Color(0.36f, 0.55f, 0.55f));
        windowMat.SetColor("_Top", new Color(0.04f, 0.08f, 0.14f));
        skylineMat = DeadlockSceneKit.Lit("Lobby_Skyline", new Color(0.02f, 0.04f, 0.05f), 0f, 0f, null, new Color(0.01f, 0.03f, 0.04f));

        glowLine = DeadlockSceneKit.Glow("Glow_RitualLine", new Color(0.25f, 1f, 0.5f, 0.9f), 0f, 1.2f);
        glowPool = DeadlockSceneKit.Glow("Glow_FloorPool", new Color(0.12f, 0.7f, 0.35f, 0.45f), 1f);
        glowCrystal = DeadlockSceneKit.Glow("Glow_Crystal", new Color(0.3f, 1f, 0.55f, 0.85f), 0f, 0.9f);
        glowFlame = DeadlockSceneKit.Glow("Glow_Flame", new Color(1f, 0.62f, 0.25f, 0.95f), 1f, 9f);
        glowEye = DeadlockSceneKit.Glow("Glow_SeerEye", new Color(0.4f, 1f, 0.6f, 1f));
        glowDot = DeadlockSceneKit.Glow("Glow_Mote", new Color(0.4f, 1f, 0.6f, 1f), 1f);
        coreMat = DeadlockSceneKit.Glow("Glow_OrbCore", new Color(0.7f, 1f, 0.8f, 1f), 1f, 2.5f);
        runeAura = DeadlockSceneKit.Glow("Glow_RuneAura", new Color(0.15f, 0.85f, 0.45f, 0.55f), 1f);
        orbMat = DeadlockSceneKit.Shader("Lobby_Orb", "Deadlock/OracleOrb");
    }

    private static void BuildRoom(Transform root)
    {
        Transform room = new GameObject("Room").transform;
        room.SetParent(root, false);

        float depth = BackZ - FrontZ;
        float midZ = (BackZ + FrontZ) * 0.5f;
        float width = HalfWidth * 2f;

        DeadlockSceneKit.Box("Floor", room, new Vector3(0f, -0.1f, midZ), new Vector3(width, 0.2f, depth), floorMat, 2.5f);
        DeadlockSceneKit.Box("Ceiling", room, new Vector3(0f, Height + 0.1f, midZ), new Vector3(width, 0.2f, depth), panelMat, 2f, false);

        DeadlockSceneKit.Box("Wall_Back", room, new Vector3(0f, Height * 0.5f, BackZ + 0.15f), new Vector3(width, Height, 0.3f), wallMat, 2f);
        DeadlockSceneKit.Box("Wall_Front", room, new Vector3(0f, Height * 0.5f, FrontZ - 0.15f), new Vector3(width, Height, 0.3f), wallMat, 2f);
        DeadlockSceneKit.Box("Wall_Left", room, new Vector3(-HalfWidth - 0.15f, Height * 0.5f, midZ), new Vector3(0.3f, Height, depth), wallMat, 2f);
        DeadlockSceneKit.Box("Wall_Right", room, new Vector3(HalfWidth + 0.15f, Height * 0.5f, midZ), new Vector3(0.3f, Height, depth), wallMat, 2f);

        // Dark wood wainscot and a brass rail around the lower walls, like the mansion in the reference.
        const float panelHeight = 1.15f;
        DeadlockSceneKit.Box("Panel_Back", room, new Vector3(0f, panelHeight * 0.5f, BackZ - 0.04f), new Vector3(width, panelHeight, 0.08f), panelMat, 1f, false);
        DeadlockSceneKit.Box("Panel_Front", room, new Vector3(0f, panelHeight * 0.5f, FrontZ + 0.04f), new Vector3(width, panelHeight, 0.08f), panelMat, 1f, false);
        DeadlockSceneKit.Box("Panel_Left", room, new Vector3(-HalfWidth + 0.04f, panelHeight * 0.5f, midZ), new Vector3(0.08f, panelHeight, depth), panelMat, 1f, false);
        DeadlockSceneKit.Box("Panel_Right", room, new Vector3(HalfWidth - 0.04f, panelHeight * 0.5f, midZ), new Vector3(0.08f, panelHeight, depth), panelMat, 1f, false);
        DeadlockSceneKit.Box("Rail_Back", room, new Vector3(0f, panelHeight, BackZ - 0.09f), new Vector3(width, 0.05f, 0.05f), trimMat, 1f, false);
        DeadlockSceneKit.Box("Rail_Left", room, new Vector3(-HalfWidth + 0.09f, panelHeight, midZ), new Vector3(0.05f, 0.05f, depth), trimMat, 1f, false);
        DeadlockSceneKit.Box("Rail_Right", room, new Vector3(HalfWidth - 0.09f, panelHeight, midZ), new Vector3(0.05f, 0.05f, depth), trimMat, 1f, false);

        // Pillars between the tall windows and at the corners.
        float[] pillarXs = { -6.6f, -4.6f, -1.6f, 1.6f, 4.6f, 6.6f };
        foreach (float x in pillarXs)
        {
            DeadlockSceneKit.Box("Pillar", room, new Vector3(x, Height * 0.5f, BackZ - 0.25f), new Vector3(0.55f, Height, 0.5f), wallMat, 2f);
        }

        DeadlockSceneKit.Box("Cornice", room, new Vector3(0f, Height - 0.35f, BackZ - 0.3f), new Vector3(width, 0.25f, 0.6f), trimMat, 1f, false);

        // Rug under the ritual circle.
        Material rug = DeadlockSceneKit.Lit("Lobby_Rug", new Color(0.2f, 0.05f, 0.06f), 0.1f);
        DeadlockSceneKit.Box("Rug", room, new Vector3(0f, 0.005f, -0.6f), new Vector3(6.4f, 0.01f, 7.6f), rug, 1f, false);
    }

    private static void BuildWindows(Transform root)
    {
        Transform windows = new GameObject("Windows").transform;
        windows.SetParent(root, false);

        // Back wall: a tall central window flanked by two narrower ones, curtains on each side.
        AddWindow(windows, "Window_Center", new Vector3(0f, 1.3f, BackZ - 0.02f), Quaternion.identity, 2.6f, 5.1f, true);
        AddWindow(windows, "Window_Left", new Vector3(-3.1f, 1.5f, BackZ - 0.02f), Quaternion.identity, 2f, 4.3f, false);
        AddWindow(windows, "Window_Right", new Vector3(3.1f, 1.5f, BackZ - 0.02f), Quaternion.identity, 2f, 4.3f, false);

        // Side walls: two windows each, facing into the room.
        foreach (int side in new[] { -1, 1 })
        {
            Quaternion facing = Quaternion.Euler(0f, side * 90f, 0f);
            foreach (float z in new[] { -4.2f, 0.8f })
            {
                AddWindow(windows, $"Window_Side{side}_{z}", new Vector3(side * (HalfWidth - 0.02f), 1.6f, z), facing, 1.8f, 3.9f, false);
            }
        }
    }

    private static void AddWindow(Transform parent, string name, Vector3 position, Quaternion rotation, float width, float height, bool skyline)
    {
        Transform window = new GameObject(name).transform;
        window.SetParent(parent, false);
        window.localPosition = position;
        window.localRotation = rotation;

        Mesh arch = DeadlockSceneKit.Arch($"Arch_{width:0.##}x{height:0.##}", width, height);
        DeadlockSceneKit.MeshObject("Glass", window, Vector3.zero, arch, windowMat);

        if (skyline)
        {
            // Dark towers of the city against the sky, and the tall spire in the middle.
            float[,] towers = { { -0.95f, 0.55f, 1.2f }, { -0.55f, 0.35f, 1.7f }, { -0.15f, 0.25f, 2.9f }, { 0.25f, 0.4f, 1.9f }, { 0.75f, 0.5f, 1.3f } };
            for (int i = 0; i < towers.GetLength(0); i++)
            {
                DeadlockSceneKit.Box("Tower", window, new Vector3(towers[i, 0], towers[i, 2] * 0.5f, -0.02f),
                    new Vector3(towers[i, 1], towers[i, 2], 0.01f), skylineMat, 1f, false);
            }
            DeadlockSceneKit.Box("Spire", window, new Vector3(-0.15f, 3.25f, -0.02f), new Vector3(0.06f, 0.8f, 0.01f), skylineMat, 1f, false);
        }

        // Mullions: two verticals and three transoms, slightly in front of the glass.
        float radius = width * 0.5f;
        foreach (float x in new[] { -width / 6f, width / 6f })
        {
            DeadlockSceneKit.Box("Mullion", window, new Vector3(x, (height - radius) * 0.5f + 0.2f, -0.04f), new Vector3(0.05f, height - radius + 0.4f, 0.04f), frameMat, 1f, false);
        }
        for (int i = 1; i <= 3; i++)
        {
            float y = (height - radius) * i / 4f;
            DeadlockSceneKit.Box("Transom", window, new Vector3(0f, y, -0.04f), new Vector3(width, 0.05f, 0.04f), frameMat, 1f, false);
        }
        DeadlockSceneKit.Box("Sill", window, new Vector3(0f, -0.05f, -0.1f), new Vector3(width + 0.3f, 0.1f, 0.22f), trimMat, 1f, false);

        // Heavy curtains: three narrow capsules per side read as folds of cloth.
        foreach (int side in new[] { -1, 1 })
        {
            for (int f = 0; f < 3; f++)
            {
                float x = side * (radius + 0.12f + f * 0.13f);
                GameObject fold = DeadlockSceneKit.Primitive(PrimitiveType.Capsule, "CurtainFold", window,
                    new Vector3(x, height * 0.5f + 0.05f, -0.15f - f * 0.02f), new Vector3(0.19f, height * 0.5f + 0.35f, 0.12f), curtainMat);
                fold.transform.localRotation = Quaternion.Euler(0f, 0f, side * (1.5f - f));
            }
        }
    }

    private static void BuildCircle(Transform root)
    {
        Transform circle = new GameObject("RitualCircle").transform;
        circle.SetParent(root, false);
        circle.localPosition = new Vector3(0f, 0.02f, 0f);

        DeadlockSceneKit.FloorDisc("GlowPool", circle, Vector3.zero, 6.5f, glowPool);
        DeadlockSceneKit.Ring("OuterRing", circle, Vector3.zero, 2.3f, 0.045f, glowLine);
        DeadlockSceneKit.Ring("InnerRing", circle, Vector3.zero, 2.05f, 0.025f, glowLine);
        DeadlockSceneKit.Pentagram("Star", circle, Vector3.zero, 2.05f, 0.03f, glowLine, 180f);

        // Green crystals standing on the circle, like the reference.
        for (int i = 0; i < 6; i++)
        {
            float a = (i * 60f + 30f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a) * 2.3f, 0.16f, Mathf.Sin(a) * 2.3f);
            GameObject crystal = DeadlockSceneKit.Primitive(PrimitiveType.Cube, "Crystal", circle, p, new Vector3(0.09f, 0.3f, 0.09f), glowCrystal);
            crystal.transform.localRotation = Quaternion.Euler(8f, i * 37f, 45f) * Quaternion.Euler(0f, 0f, 0f);
            DeadlockSceneKit.FloorDisc("CrystalGlow", circle, new Vector3(p.x, 0.005f, p.z), 0.7f, glowPool);
        }
    }

    private static void BuildCandles(Transform root)
    {
        Transform candles = new GameObject("Candelabras").transform;
        candles.SetParent(root, false);

        Mesh stand = DeadlockSceneKit.Lathe("CandelabraStand", new[]
        {
            new Vector2(0.22f, 0f), new Vector2(0.22f, 0.04f), new Vector2(0.06f, 0.1f), new Vector2(0.035f, 0.3f),
            new Vector2(0.05f, 0.45f), new Vector2(0.03f, 0.6f), new Vector2(0.03f, 1.15f), new Vector2(0.08f, 1.2f),
            new Vector2(0.12f, 1.24f), new Vector2(0f, 1.25f)
        });

        foreach (int side in new[] { -1, 1 })
        {
            Transform c = new GameObject($"Candelabra_{side}").transform;
            c.SetParent(candles, false);
            c.localPosition = new Vector3(side * 3.4f, 0f, 1.2f);

            GameObject standObject = DeadlockSceneKit.MeshObject("Stand", c, Vector3.zero, stand, trimMat);
            var standCollider = standObject.AddComponent<CapsuleCollider>();
            standCollider.center = new Vector3(0f, 0.62f, 0f);
            standCollider.height = 1.25f;
            standCollider.radius = 0.22f;

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.1f;
                float h = i == 1 ? 0.2f : 0.15f;
                DeadlockSceneKit.Primitive(PrimitiveType.Cylinder, "Candle", c, new Vector3(x, 1.25f + h * 0.5f, 0f), new Vector3(0.04f, h * 0.5f, 0.04f), candleMat);
                GameObject flame = DeadlockSceneKit.Primitive(PrimitiveType.Quad, "Flame", c, new Vector3(x, 1.25f + h + 0.035f, 0f), new Vector3(0.05f, 0.09f, 1f), glowFlame);
                flame.AddComponent<FaceCamera>();
            }

            DeadlockSceneKit.PointLight("CandleLight", c, new Vector3(0f, 1.6f, 0f), new Color(1f, 0.68f, 0.38f), 1.3f, 4.5f);
        }
    }

    private static OracleOrb BuildOrbAndRunes(Transform root)
    {
        Transform shrine = new GameObject("Shrine").transform;
        shrine.SetParent(root, false);

        Mesh pedestalMesh = DeadlockSceneKit.Lathe("OraclePedestal", new[]
        {
            new Vector2(0.45f, 0f), new Vector2(0.45f, 0.08f), new Vector2(0.32f, 0.13f), new Vector2(0.17f, 0.22f),
            new Vector2(0.12f, 0.55f), new Vector2(0.15f, 0.8f), new Vector2(0.26f, 0.92f), new Vector2(0.3f, 1f),
            new Vector2(0.27f, 1.05f), new Vector2(0.12f, 1.07f), new Vector2(0f, 1.08f)
        });
        GameObject pedestal = DeadlockSceneKit.MeshObject("Pedestal", shrine, Vector3.zero, pedestalMesh, stoneMat);
        var pedestalCollider = pedestal.AddComponent<CapsuleCollider>();
        pedestalCollider.center = new Vector3(0f, 0.55f, 0f);
        pedestalCollider.height = 1.1f;
        pedestalCollider.radius = 0.38f;

        // The orb itself: the clickable object, with its glow shell, a bright core, light, motes and sounds.
        GameObject orbObject = DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "Orb", shrine, OrbPosition, Vector3.one * 0.44f, orbMat, true);
        OracleOrb orb = orbObject.AddComponent<OracleOrb>();
        DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "Core", orbObject.transform, Vector3.zero, Vector3.one * 0.35f, coreMat);

        Light light = DeadlockSceneKit.PointLight("OrbLight", shrine, OrbPosition + Vector3.up * 0.1f, new Color(0.35f, 1f, 0.6f), 2.2f, 8f);
        ParticleSystem motes = DeadlockSceneKit.Particles("Motes", shrine, OrbPosition, glowDot, new Color(0.5f, 1f, 0.7f, 0.9f),
            true, 14f, 0, 3.5f, 0.15f, 0.035f, new Vector3(0.6f, 0f, 0f), true, -0.03f);
        ParticleSystem burst = DeadlockSceneKit.Particles("StrikeBurst", shrine, OrbPosition, glowDot, new Color(0.6f, 1f, 0.75f, 1f),
            false, 0f, 70, 1.1f, 2.4f, 0.06f, new Vector3(0.2f, 0f, 0f), true);

        AudioSource drone = orbObject.AddComponent<AudioSource>();
        drone.spatialBlend = 1f;
        drone.volume = 0.35f;
        drone.minDistance = 1f;
        drone.maxDistance = 12f;
        drone.rolloffMode = AudioRolloffMode.Linear;
        drone.playOnAwake = false;

        var strikeObject = new GameObject("StrikeAudio");
        strikeObject.transform.SetParent(orbObject.transform, false);
        AudioSource strike = strikeObject.AddComponent<AudioSource>();
        strike.spatialBlend = 0.6f;
        strike.volume = 0.8f;
        strike.playOnAwake = false;

        TextMeshPro hint = DeadlockSceneKit.Text("Hint", shrine, OrbPosition + new Vector3(0f, 0.75f, -0.1f), "", 0.75f,
            new Color(0.75f, 1f, 0.85f, 0.95f), new Vector2(2.4f, 0.5f));
        hint.gameObject.AddComponent<FaceCamera>();

        GameObject runes = BuildRunes(shrine, orbObject.transform);

        var so = new SerializedObject(orb);
        so.FindProperty("orbRenderer").objectReferenceValue = orbObject.GetComponent<Renderer>();
        so.FindProperty("orbLight").objectReferenceValue = light;
        so.FindProperty("strikeBurst").objectReferenceValue = burst;
        so.FindProperty("strikeSource").objectReferenceValue = strike;
        so.FindProperty("droneSource").objectReferenceValue = drone;
        so.FindProperty("revealOnStrike").objectReferenceValue = runes;
        so.FindProperty("hint").objectReferenceValue = hint;
        so.ApplyModifiedPropertiesWithoutUndo();

        motes.Play();
        return orb;
    }

    private static GameObject BuildRunes(Transform parent, Transform orb)
    {
        TitleScreenManager manager = Object.FindObjectOfType<TitleScreenManager>();
        var runes = new GameObject("MenuRunes");
        runes.transform.SetParent(parent, false);

        WorldButton online = Rune(runes.transform, orb, "Rune_Online", new Vector3(-0.62f, 1.55f, -0.55f), "PARTIDA ONLINE", "Duelo 1 contra 1", out _);
        WorldButton mission = Rune(runes.transform, orb, "Rune_Mission", new Vector3(0.62f, 1.55f, -0.55f), "MI MISIÓN", "Práctica guiada", out TextMeshPro recommended);

        if (manager != null)
        {
            UnityEventTools.AddPersistentListener(online.OnClick, new UnityAction(manager.PlayOnline));
            UnityEventTools.AddPersistentListener(mission.OnClick, new UnityAction(manager.StartMission));

            var so = new SerializedObject(manager);
            so.FindProperty("missionRecommendedLabel").objectReferenceValue = recommended;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogError("[Deadlock Lobby] No TitleScreenManager in TitleScreen; runes are not wired.");
        }

        return runes;
    }

    private static WorldButton Rune(Transform parent, Transform orb, string name, Vector3 position, string title, string subtitle, out TextMeshPro recommended)
    {
        var rune = new GameObject(name);
        rune.transform.SetParent(parent, false);
        rune.transform.localPosition = position;

        var collider = rune.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 0.5f, 0.08f);

        var visual = new GameObject("Visual").transform;
        visual.SetParent(rune.transform, false);

        GameObject aura = DeadlockSceneKit.Primitive(PrimitiveType.Quad, "Aura", visual, new Vector3(0f, 0f, 0.02f), new Vector3(1.3f, 0.75f, 1f), runeAura);
        aura.transform.localRotation = Quaternion.identity;

        // A thin glowing frame around the label.
        var frame = new[] { new Vector3(-0.48f, -0.2f, 0f), new Vector3(0.48f, -0.2f, 0f), new Vector3(0.48f, 0.2f, 0f), new Vector3(-0.48f, 0.2f, 0f) };
        LineRenderer border = DeadlockSceneKit.Line("Frame", visual, frame, 0.012f, glowLine, true, false);

        TextMeshPro label = DeadlockSceneKit.Text("Title", visual, new Vector3(0f, 0.05f, 0f), title, 0.95f, new Color(0.88f, 1f, 0.92f), new Vector2(1f, 0.2f), FontStyles.Bold);
        label.enableWordWrapping = false;
        DeadlockSceneKit.Text("Subtitle", visual, new Vector3(0f, -0.11f, 0f), subtitle, 0.6f, new Color(0.6f, 0.95f, 0.75f, 0.9f), new Vector2(1f, 0.12f));
        recommended = DeadlockSceneKit.Text("Recommended", visual, new Vector3(0f, -0.32f, 0f), "RECOMENDADO LA PRIMERA VEZ", 0.55f,
            new Color(1f, 0.72f, 0.3f), new Vector2(1.3f, 0.12f), FontStyles.Bold);
        if (name != "Rune_Mission") Object.DestroyImmediate(recommended.gameObject);

        var floating = rune.AddComponent<FloatingRune>();
        var floatSo = new SerializedObject(floating);
        floatSo.FindProperty("origin").objectReferenceValue = orb;
        floatSo.ApplyModifiedPropertiesWithoutUndo();

        WorldButton button = rune.AddComponent<WorldButton>();
        var so = new SerializedObject(button);
        so.FindProperty("scaleTarget").objectReferenceValue = visual;
        SerializedProperty glow = so.FindProperty("glowRenderers");
        glow.arraySize = 2;
        glow.GetArrayElementAtIndex(0).objectReferenceValue = aura.GetComponent<Renderer>();
        glow.GetArrayElementAtIndex(1).objectReferenceValue = border;
        so.FindProperty("glowProperty").stringValue = "_Intensity";
        so.FindProperty("normalGlow").floatValue = 1f;
        so.FindProperty("hoverGlow").floatValue = 2.4f;
        so.ApplyModifiedPropertiesWithoutUndo();

        return button;
    }

    private static void BuildSeer(Transform root)
    {
        // Hooded seer behind the orb, facing the player. Built from a lathed robe, a hood, a void
        // where the face should be with two glowing eyes, and arms reaching around the orb.
        var seer = new GameObject("Seer");
        seer.transform.SetParent(root, false);
        seer.transform.localPosition = new Vector3(0f, 0f, 0.95f);
        seer.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var capsule = seer.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.45f;

        Mesh robeMesh = DeadlockSceneKit.Lathe("SeerRobe", new[]
        {
            new Vector2(0.58f, 0f), new Vector2(0.52f, 0.12f), new Vector2(0.42f, 0.45f), new Vector2(0.34f, 0.85f),
            new Vector2(0.3f, 1.1f), new Vector2(0.31f, 1.3f), new Vector2(0.27f, 1.4f), new Vector2(0.15f, 1.47f), new Vector2(0f, 1.5f)
        }, 40);

        Transform body = DeadlockSceneKit.MeshObject("Body", seer.transform, Vector3.zero, robeMesh, robeMat).transform;
        DeadlockSceneKit.Primitive(PrimitiveType.Cylinder, "Sash", body, new Vector3(0f, 1.0f, 0f), new Vector3(0.64f, 0.025f, 0.64f), trimMat);
        DeadlockSceneKit.Primitive(PrimitiveType.Cylinder, "Hem", body, new Vector3(0f, 0.04f, 0f), new Vector3(1.17f, 0.02f, 1.17f), trimMat);

        Transform head = new GameObject("Head").transform;
        head.SetParent(seer.transform, false);
        head.localPosition = new Vector3(0f, 1.6f, 0f);
        DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "Hood", head, Vector3.zero, new Vector3(0.32f, 0.38f, 0.34f), robeMat);
        DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "FaceVoid", head, new Vector3(0f, -0.02f, 0.11f), new Vector3(0.21f, 0.26f, 0.14f), voidMat);
        Renderer leftEye = DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.045f, 0.01f, 0.175f), Vector3.one * 0.028f, glowEye).GetComponent<Renderer>();
        Renderer rightEye = DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.045f, 0.01f, 0.175f), Vector3.one * 0.028f, glowEye).GetComponent<Renderer>();

        // Orb is 0.95 m in front of her (local +Z); hands hover at its sides.
        foreach (int side in new[] { -1, 1 })
        {
            Vector3 shoulder = new Vector3(side * 0.25f, 1.38f, 0.02f);
            Vector3 hand = new Vector3(side * 0.24f, 1.32f, 0.74f);
            Vector3 mid = (shoulder + hand) * 0.5f;
            Vector3 dir = hand - shoulder;

            GameObject sleeve = DeadlockSceneKit.Primitive(PrimitiveType.Capsule, "Sleeve", seer.transform, mid,
                new Vector3(0.13f, dir.magnitude * 0.5f, 0.13f), robeMat);
            sleeve.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "Hand", seer.transform, hand + dir.normalized * 0.05f, new Vector3(0.075f, 0.06f, 0.1f), skinMat);
        }

        // Back light so her hood and shoulders separate from the dark windows behind her.
        DeadlockSceneKit.PointLight("SeerRimLight", seer.transform, new Vector3(0f, 2.2f, -0.9f), new Color(0.55f, 0.9f, 0.75f), 1.6f, 3f);

        SeerIdle idle = seer.AddComponent<SeerIdle>();
        var so = new SerializedObject(idle);
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("head").objectReferenceValue = head;
        SerializedProperty eyes = so.FindProperty("eyes");
        eyes.arraySize = 2;
        eyes.GetArrayElementAtIndex(0).objectReferenceValue = leftEye;
        eyes.GetArrayElementAtIndex(1).objectReferenceValue = rightEye;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildTitle(Transform root)
    {
        TextMeshPro title = DeadlockSceneKit.Text("GameTitle", root, new Vector3(0f, 5.9f, BackZ - 0.5f), "MINI DEADLOCK", 4.5f,
            new Color(0.85f, 0.72f, 0.45f), new Vector2(8f, 1f), FontStyles.Bold);
        title.characterSpacing = 12f;
        title.transform.localRotation = Quaternion.identity;
        TextMeshPro sub = DeadlockSceneKit.Text("GameSubtitle", root, new Vector3(0f, 5.35f, BackZ - 0.5f), "EL RITUAL", 1.8f,
            new Color(0.55f, 0.95f, 0.7f), new Vector2(6f, 0.5f));
        sub.characterSpacing = 20f;
    }

    private static void SetupLighting(Transform root)
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.3f, 0.42f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.22f, 0.28f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.1f, 0.1f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.05f, 0.09f, 0.09f);
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.skybox = null;

        var moon = new GameObject("Moonlight");
        moon.transform.SetParent(root, false);
        moon.transform.localRotation = Quaternion.Euler(28f, 180f, 0f);
        Light light = moon.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.5f, 0.66f, 0.78f);
        light.intensity = 0.45f;
        light.shadows = LightShadows.None;
    }

    private static void SetupPlayer()
    {
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();

        if (rig == null)
        {
            Debug.LogError("[Deadlock Lobby] No OVRCameraRig in TitleScreen.");
            return;
        }

        rig.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
        DeadlockSceneKit.EnsurePhysicsRaycaster(rig);

        // Walking needs real floor-level tracking; PlayerSpawnHeight sets it and lifts the head if needed.
        if (Object.FindObjectOfType<PlayerSpawnHeight>() == null)
        {
            new GameObject("PlayerSpawnHeight").AddComponent<PlayerSpawnHeight>();
        }

        DeadlockOnlineSetup.AddLocomotion(rig);
        var so = new SerializedObject(rig.GetComponent<ComfortLocomotion>());
        so.FindProperty("laneX").vector2Value = new Vector2(-HalfWidth + 0.5f, HalfWidth - 0.5f);
        so.FindProperty("laneZ").vector2Value = new Vector2(FrontZ + 0.5f, BackZ - 0.7f);
        so.ApplyModifiedPropertiesWithoutUndo();

        DeadlockOnlineSetup.AddVignette(rig, DeadlockOnlineSetup.CreateVignetteMaterial());

        // The camera clears to the fog colour so nothing outside the room ever shows as skybox.
        foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
        }
    }

    /// <summary>
    /// The old flat title stays for its fader and audio settings: the title, subtitle and buttons are
    /// hidden, and the canvas is moved to the right of the spawn as a floating settings plaque.
    /// </summary>
    private static void HideFlatTitle()
    {
        GameObject canvas = GameObject.Find("TitleCanvas");
        if (canvas == null) return;

        foreach (Transform child in canvas.transform)
        {
            child.gameObject.SetActive(child.name == "AudioSettings");
        }

        Vector3 plaque = new Vector3(3.3f, 1.45f, -2.4f);
        Vector3 away = plaque - (SpawnPosition + Vector3.up * 1.6f);
        away.y = 0f;
        canvas.transform.SetPositionAndRotation(plaque, Quaternion.LookRotation(away));

        Transform settings = canvas.transform.Find("AudioSettings");
        if (settings is RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
