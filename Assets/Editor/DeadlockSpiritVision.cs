using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Turns the hero select into the seer's vision: a green, misty void with rune rings turning around
/// the player, floating shards and motes, and each hero standing as a hologram on a rune circle.
/// Pointing at a hero makes it glow; the trigger chooses it, its voice line plays and the solid
/// hero materialises in the middle; a floating "CONFIRMAR" sigil then starts the ritual.
/// </summary>
/// <remarks>
/// Keeps the scene's HeroSelectManager, voice source, subtitle, fader and intro overlay, so the
/// selection logic, the story intro and the scene loading are unchanged; only the flat cards and
/// the pedestal are hidden. Safe to re-run.
/// </remarks>
public static class DeadlockSpiritVision
{
    private const string ScenePath = "Assets/Scenes/HeroSelect.unity";
    private static readonly Vector3 CenterAnchor = new Vector3(0f, 0f, 2.6f);

    private static readonly string[] KeepOnCanvas = { "SelectSubtitle", "IntroOverlay", "ScreenFader" };

    [MenuItem("Deadlock/Setup/10 Spirit Vision (hero select)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        HeroSelectManager manager = Object.FindObjectOfType<HeroSelectManager>();
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();

        if (manager == null || rig == null)
        {
            Debug.LogError("[Deadlock Vision] HeroSelect needs its HeroSelectManager and OVRCameraRig.");
            return;
        }

        // The rig ships with Eye Level tracking, which puts the head on the floor; same fix as the lobby.
        if (Object.FindObjectOfType<PlayerSpawnHeight>() == null)
        {
            new GameObject("PlayerSpawnHeight").AddComponent<PlayerSpawnHeight>();
        }

        Material glowLine = DeadlockSceneKit.Glow("Glow_VisionLine", new Color(0.3f, 1f, 0.6f, 0.7f), 0f, 0.8f);
        Material glowPool = DeadlockSceneKit.Glow("Glow_VisionPool", new Color(0.1f, 0.65f, 0.4f, 0.35f), 1f);
        Material glowDisc = DeadlockSceneKit.Glow("Glow_HeroDisc", new Color(0.25f, 1f, 0.6f, 0.6f), 1f);
        Material glowShard = DeadlockSceneKit.Glow("Glow_Shard", new Color(0.3f, 1f, 0.6f, 0.5f), 0f, 0.6f);
        Material glowDot = DeadlockSceneKit.Glow("Glow_Mote", new Color(0.4f, 1f, 0.6f, 1f), 1f);
        Material glowBeam = DeadlockSceneKit.Glow("Glow_Beam", new Color(0.35f, 1f, 0.65f, 0.08f), 0f, 1.5f);
        Material runeAura = DeadlockSceneKit.Glow("Glow_RuneAura", new Color(0.15f, 0.85f, 0.45f, 0.55f), 1f);
        Material hologram = DeadlockSceneKit.Shader("Vision_Hologram", "Deadlock/Hologram");
        Material orbMat = DeadlockSceneKit.Shader("Lobby_Orb", "Deadlock/OracleOrb");
        Material floorMat = DeadlockSceneKit.Lit("Vision_Floor", new Color(0.02f, 0.05f, 0.05f), 0.8f);

        DeadlockOnlineSetup.Replace("SpiritVision");
        Transform root = new GameObject("SpiritVision").transform;

        // Ground: a dark glossy floor that disappears into the fog, with a pool of green light.
        DeadlockSceneKit.Box("Floor", root, new Vector3(0f, -0.05f, 0f), new Vector3(60f, 0.1f, 60f), floorMat, 4f);
        DeadlockSceneKit.FloorDisc("Pool", root, new Vector3(0f, 0.01f, 1.5f), 11f, glowPool);

        // Two rune rings turning slowly in opposite directions around the player.
        foreach ((float radius, float speed) in new[] { (4.2f, 6f), (4.6f, -4f) })
        {
            var ring = new GameObject($"RuneRing_{radius}");
            ring.transform.SetParent(root, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 1f);
            DeadlockSceneKit.Ring("Ring", ring.transform, Vector3.zero, radius, 0.03f, glowLine);
            DeadlockSceneKit.Pentagram("Star", ring.transform, Vector3.zero, radius, 0.015f, glowLine, radius * 10f);
            ring.AddComponent<TurntableRotator>().DegreesPerSecond = speed;
        }

        // Shards orbiting high around the vision.
        var shards = new GameObject("Shards");
        shards.transform.SetParent(root, false);
        shards.transform.localPosition = new Vector3(0f, 0f, 1.5f);
        var random = new System.Random(11);
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * Mathf.PI * 2f;
            float r = 6f + (float)random.NextDouble() * 2f;
            float y = 1f + (float)random.NextDouble() * 3.5f;
            GameObject shard = DeadlockSceneKit.Primitive(PrimitiveType.Cube, "Shard", shards.transform,
                new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), new Vector3(0.15f, 0.5f, 0.15f) * (0.6f + (float)random.NextDouble()), glowShard);
            shard.transform.localRotation = Quaternion.Euler(random.Next(0, 60), random.Next(0, 360), 45f);
        }
        shards.AddComponent<TurntableRotator>().DegreesPerSecond = 3f;

        DeadlockSceneKit.Particles("Motes", root, new Vector3(0f, 1.5f, 1.5f), glowDot, new Color(0.5f, 1f, 0.7f, 0.8f),
            true, 25f, 0, 6f, 0.12f, 0.04f, new Vector3(10f, 3f, 10f), false, -0.02f).Play();

        // Header floating over the vision.
        TextMeshPro header = DeadlockSceneKit.Text("Header", root, new Vector3(0f, 3.1f, 4.6f), "ELIGE TU HÉROE", 3f,
            new Color(0.8f, 1f, 0.88f), new Vector2(6f, 0.7f), FontStyles.Bold);
        header.characterSpacing = 10f;
        DeadlockSceneKit.Text("SubHeader", root, new Vector3(0f, 2.65f, 4.6f), "La vidente te muestra a quienes pueden pelear el ritual", 1.1f,
            new Color(0.55f, 0.95f, 0.72f, 0.9f), new Vector2(6f, 0.4f));

        // Where the chosen hero materialises: a beam of light, and a small orb while nobody is chosen.
        var center = new GameObject("VisionAnchor");
        center.transform.SetParent(root, false);
        center.transform.localPosition = CenterAnchor;
        center.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        DeadlockSceneKit.FloorDisc("CenterDisc", root, CenterAnchor + Vector3.up * 0.015f, 1.6f, glowDisc);
        DeadlockSceneKit.Ring("CenterRing", root, CenterAnchor + Vector3.up * 0.02f, 0.75f, 0.025f, glowLine);
        GameObject beam = DeadlockSceneKit.Primitive(PrimitiveType.Cylinder, "Beam", root, CenterAnchor + Vector3.up * 2.5f, new Vector3(0.9f, 2.5f, 0.9f), glowBeam);
        beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObject placeholder = DeadlockSceneKit.Primitive(PrimitiveType.Sphere, "PlaceholderOrb", root, CenterAnchor + Vector3.up * 1.2f, Vector3.one * 0.3f, orbMat);
        DeadlockSceneKit.PointLight("HeroLight", root, CenterAnchor + new Vector3(0f, 2.6f, -1.2f), new Color(0.85f, 1f, 0.9f), 2.2f, 6f);

        BuildSpirits(root, manager, hologram, glowDisc, glowLine);
        GameObject confirm = BuildConfirm(root, manager, center.transform, runeAura, glowLine);

        var so = new SerializedObject(manager);
        so.FindProperty("pedestalAnchor").objectReferenceValue = center.transform;
        so.FindProperty("pedestalPlaceholder").objectReferenceValue = placeholder;
        so.FindProperty("confirmObject").objectReferenceValue = confirm;
        so.FindProperty("confirmButton").objectReferenceValue = null;
        so.FindProperty("heroCards").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        HideFlatSelect();
        SetupAtmosphere(rig);
        DeadlockSceneKit.EnsurePhysicsRaycaster(rig);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Vision] Spirit vision built in HeroSelect.");
    }

    private static void BuildSpirits(Transform root, HeroSelectManager manager, Material hologram, Material disc, Material line)
    {
        SerializedProperty heroes = new SerializedObject(manager).FindProperty("availableHeroes");
        int count = heroes.arraySize;

        for (int i = 0; i < count; i++)
        {
            var hero = heroes.GetArrayElementAtIndex(i).objectReferenceValue as HeroDefinitionSO;
            if (hero == null) continue;

            // Spread the heroes in an arc in front of the player, either side of the centre.
            float t = count == 1 ? 0f : i / (float)(count - 1) * 2f - 1f;
            Vector3 position = new Vector3(t * 1.9f, 0f, 2.9f - Mathf.Abs(t) * 0.3f);

            var spirit = new GameObject($"Spirit_{hero.heroName}");
            spirit.transform.SetParent(root, false);
            spirit.transform.localPosition = position;
            Vector3 toPlayer = new Vector3(0f, 0f, 0f) - position;
            toPlayer.y = 0f;
            spirit.transform.localRotation = Quaternion.LookRotation(toPlayer);

            GameObject circle = DeadlockSceneKit.FloorDisc("RuneDisc", spirit.transform, new Vector3(0f, 0.015f, 0f), 1.4f, disc);
            DeadlockSceneKit.Ring("RuneRing", spirit.transform, new Vector3(0f, 0.02f, 0f), 0.6f, 0.02f, line);
            TextMeshPro label = DeadlockSceneKit.Text("Name", spirit.transform, new Vector3(0f, 2.15f, 0f), hero.heroName.ToUpperInvariant(), 1.4f,
                new Color(0.8f, 1f, 0.88f), new Vector2(2f, 0.3f), FontStyles.Bold);
            label.gameObject.AddComponent<FaceCamera>();

            SpiritHero component = spirit.AddComponent<SpiritHero>();
            var so = new SerializedObject(component);
            so.FindProperty("manager").objectReferenceValue = manager;
            so.FindProperty("hero").objectReferenceValue = hero;
            so.FindProperty("heroIndex").intValue = i;
            so.FindProperty("hologramMaterial").objectReferenceValue = hologram;
            so.FindProperty("runeCircle").objectReferenceValue = circle.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static GameObject BuildConfirm(Transform root, HeroSelectManager manager, Transform origin, Material aura, Material line)
    {
        var sigil = new GameObject("ConfirmSigil");
        sigil.transform.SetParent(root, false);
        sigil.transform.localPosition = new Vector3(0f, 0.75f, 1.45f);

        var collider = sigil.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.9f, 0.32f, 0.08f);

        var visual = new GameObject("Visual").transform;
        visual.SetParent(sigil.transform, false);
        GameObject glow = DeadlockSceneKit.Primitive(PrimitiveType.Quad, "Aura", visual, new Vector3(0f, 0f, 0.02f), new Vector3(1.2f, 0.6f, 1f), aura);
        var frame = new[] { new Vector3(-0.42f, -0.14f, 0f), new Vector3(0.42f, -0.14f, 0f), new Vector3(0.42f, 0.14f, 0f), new Vector3(-0.42f, 0.14f, 0f) };
        LineRenderer border = DeadlockSceneKit.Line("Frame", visual, frame, 0.012f, line, true, false);
        DeadlockSceneKit.Text("Label", visual, Vector3.zero, "CONFIRMAR", 1.2f, new Color(0.9f, 1f, 0.93f), new Vector2(0.9f, 0.25f), FontStyles.Bold);

        var floating = sigil.AddComponent<FloatingRune>();
        var floatSo = new SerializedObject(floating);
        floatSo.FindProperty("origin").objectReferenceValue = origin;
        floatSo.ApplyModifiedPropertiesWithoutUndo();

        WorldButton button = sigil.AddComponent<WorldButton>();
        var so = new SerializedObject(button);
        so.FindProperty("scaleTarget").objectReferenceValue = visual;
        SerializedProperty glowRenderers = so.FindProperty("glowRenderers");
        glowRenderers.arraySize = 2;
        glowRenderers.GetArrayElementAtIndex(0).objectReferenceValue = glow.GetComponent<Renderer>();
        glowRenderers.GetArrayElementAtIndex(1).objectReferenceValue = border;
        so.FindProperty("hoverGlow").floatValue = 2.4f;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(button.OnClick, new UnityAction(manager.ConfirmSelection));
        return sigil;
    }

    /// <summary>Hides the flat cards, header and pedestal; keeps the subtitle, fader and intro overlay.</summary>
    private static void HideFlatSelect()
    {
        GameObject canvas = GameObject.Find("HeroSelectCanvas");

        if (canvas != null)
        {
            foreach (Transform child in canvas.transform)
            {
                child.gameObject.SetActive(System.Array.IndexOf(KeepOnCanvas, child.name) >= 0);
            }
        }

        GameObject pedestal = GameObject.Find("HeroPedestal");
        if (pedestal != null) pedestal.SetActive(false);
    }

    private static void SetupAtmosphere(OVRCameraRig rig)
    {
        Color fog = new Color(0.03f, 0.1f, 0.09f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.32f, 0.3f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fog;
        RenderSettings.fogDensity = 0.07f;
        RenderSettings.skybox = null;

        GameObject sun = GameObject.Find("Directional Light");
        if (sun != null)
        {
            Light light = sun.GetComponent<Light>();
            light.color = new Color(0.6f, 0.85f, 0.8f);
            light.intensity = 0.5f;
        }

        foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = fog;
        }
    }
}
