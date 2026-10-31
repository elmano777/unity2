using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// Grows the Lane into a "mini Broadway": the middle lane of Deadlock's map (The Cursed Apple)
/// with its full chain of objectives per side (Guardian → Walker → two Base Guardians → two
/// Shrines → Patron), a Times Square plaza in the middle and the four districts of the real map
/// (Haunted Lot, Chinatown, Plaza, Theater) as alleys on both flanks. About 190 × 80 m, against
/// the 60 × 14 m street of steps 2 and 6.
/// </summary>
/// <remarks>
/// <para>Replaces Ground, Cover and Environment and moves the lane path, the wave spawners, the
/// Guardian spawners, the spawn points and the player rig. It leaves the network, managers, hint
/// board, mission and sound alone, so no other step needs re-running. It must run after 2, 6 and 18,
/// which put their own street and positions back.</para>
/// <para>Only the lane has minions, as in a 1v1 the districts are side routes for flanking and
/// cover rather than a jungle: no neutral camps, since the project has no AI enemies besides the
/// lane minions. The floor is flat because <see cref="ComfortLocomotion"/> never changes height.</para>
/// <para>The alleys sit 18-30 m from the lane centre on purpose: minions acquire targets within
/// 12 m and towers within 14 m without a line-of-sight check, so anything closer would let them
/// chase or shoot the player through the buildings.</para>
/// <para>The Walker, Shrines and Patron are placeholders built from primitives until their models
/// are extracted from Deadlock like the Guardian's; Base Guardians reuse the Guardian model. None of
/// them fight yet: the tower sequence is RF-12, deliverable 4.</para>
/// <para>Buildings are merged into one mesh per area and material (a few dozen draw calls in total)
/// to keep the Quest at 72 FPS (RNF-01), with UVs in world metres so textures line up across boxes.</para>
/// <para>Neon signs are a dark board, a neon tube outline, glowing TextMeshPro text and an additive
/// halo on the wall behind (<c>Deadlock/Glow</c>): the Lane has no bloom, which costs too much on the
/// Quest, so the halo stands in for it. District names at their entrances double as wayfinding (RNF-05).</para>
/// </remarks>
public static class DeadlockExpandedMap
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string MeshDir = "Assets/Art/Meshes/Map";
    private const string MaterialDir = "Assets/Art/Materials/Map";
    private const string TextureDir = "Assets/Art/Textures/Lane";

    // Layout, in metres. Broadway runs along Z and Amber (the host) owns negative Z. Everything is
    // written once for Amber's half and point-reflected, (x, z) → (-x, -z), for Sapphire's.
    private const float MapHalfLength = 96f;
    private const float MapHalfWidth = 40f;
    private const float LaneHalfWidth = 8f;
    private const float BlockOuterX = 18f;
    private const float AlleyOuterX = 30f;
    private const float CourtyardBackX = 36f;
    private const float BaseFrontZ = -70f;
    private const float BaseBackZ = -92f;
    private const float BaseHalfWidth = 24f;
    private const float PlazaEdgeZ = -12f;
    private const float CrossStreetStart = -40f, CrossStreetEnd = -34f;
    private const float CourtyardStart = -54f, CourtyardEnd = -44f;
    private const float BaseHeight = 4.5f;

    private static readonly Vector3 GuardianPos = new Vector3(-5f, 0f, -24f);
    private static readonly Vector3 WalkerPos = new Vector3(4.5f, 0f, -46f);
    private static readonly Vector3 BaseGuardianPos = new Vector3(6f, 0f, -66f);
    private static readonly Vector3 ShrinePos = new Vector3(13f, 0f, -80f);
    private static readonly Vector3 PatronPos = new Vector3(0f, 0f, -86f);

    // Heights of the flat markings over the ground: far enough apart not to flicker on the Quest's depth buffer.
    private const float FloorY = 0.015f, MarkingY = 0.03f;
    private static readonly Vector3 PlayerSpawnPos = new Vector3(0f, 0f, -76f);
    private static readonly Vector3 TeleportBasePos = new Vector3(-17f, 0f, -75f);
    private static readonly Vector3 TeleportFrontPos = new Vector3(-13f, 0f, -37f);
    private static readonly float[] WaypointZ = { -71f, -58f, -44f, -30f, -16f, 0f, 16f, 30f, 44f, 58f, 71f };

    // Metres covered by one repeat of each texture.
    private const float PavingTile = 2.5f;
    private const float SandstoneTile = 2f;
    private const float BrickTile = 1.2f;

    private enum District { Chinatown, HauntedLot, Plaza, Theater }

    [Flags]
    private enum Faces { None = 0, MinusX = 1, PlusX = 2, MinusZ = 4, PlusZ = 8 }

    /// <summary>
    /// Maps Amber-half template coordinates to the world. <see cref="Half"/> reflects through the
    /// centre for Sapphire; <see cref="Side"/> mirrors across the lane for the other flank.
    /// </summary>
    private readonly struct Frame
    {
        public readonly int Half;
        public readonly int Side;

        public Frame(int half, int side)
        {
            Half = half;
            Side = side;
        }

        public Vector3 P(float x, float y, float z) => new Vector3(Half * Side * x, y, Half * z);
        public Vector3 P(Vector3 p) => P(p.x, p.y, p.z);
        public Vector3 N(Vector3 n) => new Vector3(Half * Side * n.x, n.y, Half * n.z);
        public string HalfName => Half > 0 ? "Amber" : "Sapphire";
        public Faction Faction => Half > 0 ? Faction.Player : Faction.Enemy;

        public District District => Half > 0
            ? (Side > 0 ? District.Chinatown : District.HauntedLot)
            : (Side > 0 ? District.Plaza : District.Theater);
    }

    private sealed class MeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        /// <summary>One rectangle facing <paramref name="n"/>. <paramref name="tile"/> 0 maps the texture once over it.</summary>
        public void Face(Vector3 center, Vector3 n, Vector3 v, float halfU, float halfV, float tile)
        {
            // u is the viewer's right when looking at the face from outside.
            Vector3 u = Vector3.Cross(n, v);
            Vector3 hu = u * halfU, hv = v * halfV;
            Vector3[] corners = { center - hu - hv, center + hu - hv, center + hu + hv, center - hu + hv };
            Vector2[] unit = { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            int start = Vertices.Count;

            for (int i = 0; i < 4; i++)
            {
                Vertices.Add(corners[i]);
                Normals.Add(n);
                // World-anchored UVs keep the texture continuous from one box to the next.
                Uvs.Add(tile > 0f ? new Vector2(Vector3.Dot(corners[i], u) / tile, Vector3.Dot(corners[i], v) / tile) : unit[i]);
            }

            // Bottom-left, top-left, top-right, bottom-right: clockwise from outside, Unity's front face.
            Triangles.AddRange(new[] { start, start + 3, start + 2, start, start + 2, start + 1 });
        }
    }

    private static readonly Dictionary<string, Dictionary<Material, MeshData>> meshes = new Dictionary<string, Dictionary<Material, MeshData>>();
    private static readonly Dictionary<string, List<Bounds>> colliders = new Dictionary<string, List<Bounds>>();
    private static readonly HashSet<Material> unshadowed = new HashSet<Material>();

    private static Material paving, sandstone, sandstoneDark, brick, brickDark, window, windowLit, lamp;
    private static Material asphalt, concrete, wood, metal, lacquer, jade, bronze, fence;
    private static Material neonBlue, neonPink, neonCyan, neonYellow, neonGreen, neonRed, neonPurple, white;
    private static Material teleportRing, signBoard;
    private static readonly Dictionary<Faction, Material> teamStone = new Dictionary<Faction, Material>();
    private static readonly Dictionary<Faction, Material> teamGlow = new Dictionary<Faction, Material>();
    private static readonly Dictionary<Faction, Material> teamFloor = new Dictionary<Faction, Material>();
    private static int groundLayer;

    /// <summary>One neon colour: the tube (unlit), the halo it throws on the wall, and the glowing text.</summary>
    private sealed class Neon
    {
        public Color Color;
        public Material Tube, Halo, Text;
    }

    private static Neon blue, pink, cyan, yellow, green, red, purple;
    private static readonly Dictionary<Faction, Neon> teamNeon = new Dictionary<Faction, Neon>();
    private static Transform signs;

    [MenuItem("Deadlock/Setup/26 Expanded Map (mini Broadway)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        groundLayer = LayerMask.NameToLayer(DeadlockSetupTools.GroundLayer);
        meshes.Clear();
        colliders.Clear();
        unshadowed.Clear();

        if (!CreateMaterials())
        {
            return;
        }

        foreach (string name in new[] { "Environment", "Cover", "Map" })
        {
            GameObject old = GameObject.Find(name);
            if (old != null) Object.DestroyImmediate(old);
        }

        // Meshes of an earlier run: every one is rebuilt below.
        if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
        if (!AssetDatabase.IsValidFolder("Assets/Art/Meshes")) AssetDatabase.CreateFolder("Assets/Art", "Meshes");
        AssetDatabase.CreateFolder("Assets/Art/Meshes", "Map");

        Transform root = new GameObject("Map").transform;
        signs = new GameObject("Signs").transform;
        signs.SetParent(root, false);
        var random = new Random(23);

        foreach (int half in new[] { 1, -1 })
        {
            BuildLane(new Frame(half, 1));
            BuildBase(new Frame(half, 1), random);

            foreach (int side in new[] { 1, -1 })
            {
                var frame = new Frame(half, side);
                BuildLaneBlocks(frame, random);
                BuildEdge(frame, random);
                BuildDistrict(frame);
            }
        }

        BuildPlaza();
        FlushMeshes(root);
        SetStatic(root);

        Transform structures = new GameObject("Structures").transform;
        structures.SetParent(root, false);
        Transform teleporters = new GameObject("Teleporters").transform;
        teleporters.SetParent(root, false);

        foreach (int half in new[] { 1, -1 })
        {
            var frame = new Frame(half, 1);
            BuildStructureMarkers(frame, structures);
            BuildTeleporters(frame, teleporters);
        }

        RebuildGround();
        RewireScene();
        SetupFog();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Expanded map built: mini Broadway, 4 districts, objective markers and teleporters.");
    }

    // ------------------------------------------------------------------ materials

    private static bool CreateMaterials()
    {
        paving = Existing("Lane_Ground");
        sandstone = Existing("Lane_Sandstone");
        sandstoneDark = Existing("Lane_Cover");
        brick = Existing("Lane_Brick");
        brickDark = Existing("Lane_BrickDark");
        window = Existing("Lane_Window");
        windowLit = Existing("Lane_WindowLit");
        lamp = Existing("Lane_Lamp");

        if (paving == null || sandstone == null || brick == null || window == null || lamp == null)
        {
            Debug.LogError("[Deadlock Setup] Lane materials missing. Run step 6 (Lane Environment) first.");
            return false;
        }

        if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/Art/Materials", "Map");

        asphalt = Textured("Map_Asphalt", "precast_stone_paving", new Color(0.32f, 0.31f, 0.33f), 0.25f);
        concrete = Textured("Map_Concrete", "sandstone_blocks_08", new Color(0.62f, 0.62f, 0.64f), 0.1f);
        wood = Textured("Map_Wood", "sandstone_blocks_08", new Color(0.55f, 0.36f, 0.22f), 0.15f);
        metal = Plain("Map_Metal", new Color(0.18f, 0.19f, 0.21f), 0.6f, Color.black);
        lacquer = Plain("Map_RedLacquer", new Color(0.55f, 0.07f, 0.05f), 0.55f, Color.black);
        jade = Plain("Map_Jade", new Color(0.12f, 0.35f, 0.28f), 0.4f, Color.black);
        bronze = Plain("Map_Bronze", new Color(0.3f, 0.42f, 0.36f), 0.5f, Color.black);
        fence = Plain("Map_Fence", new Color(0.25f, 0.26f, 0.27f), 0.3f, Color.black);

        // Neon is unlit: it reads as glowing at night and costs less than lit + emission.
        neonBlue = Unlit("Map_Neon_Blue", new Color(0.2f, 0.55f, 1f));
        neonPink = Unlit("Map_Neon_Pink", new Color(1f, 0.25f, 0.65f));
        neonCyan = Unlit("Map_Neon_Cyan", new Color(0.3f, 1f, 0.95f));
        neonYellow = Unlit("Map_Neon_Yellow", new Color(1f, 0.85f, 0.3f));
        neonGreen = Unlit("Map_Neon_Green", new Color(0.45f, 1f, 0.4f));
        neonRed = Unlit("Map_Neon_Red", new Color(1f, 0.22f, 0.15f));
        neonPurple = Unlit("Map_Neon_Purple", new Color(0.7f, 0.35f, 1f));
        white = Unlit("Map_Paint_White", new Color(0.85f, 0.84f, 0.8f));
        teleportRing = Unlit("Map_TeleportRing", new Color(0.1f, 0.55f, 0.75f));

        teamStone[Faction.Player] = Textured("Map_Stone_Amber", "sandstone_blocks_08", new Color(0.85f, 0.62f, 0.4f), 0.1f);
        teamStone[Faction.Enemy] = Textured("Map_Stone_Sapphire", "sandstone_blocks_08", new Color(0.5f, 0.6f, 0.78f), 0.1f);
        teamGlow[Faction.Player] = Unlit("Map_Glow_Amber", new Color(1f, 0.55f, 0.15f));
        teamGlow[Faction.Enemy] = Unlit("Map_Glow_Sapphire", new Color(0.3f, 0.7f, 1f));
        teamFloor[Faction.Player] = Textured("Map_Floor_Amber", "precast_stone_paving", new Color(0.9f, 0.7f, 0.5f), 0.15f);
        teamFloor[Faction.Enemy] = Textured("Map_Floor_Sapphire", "precast_stone_paving", new Color(0.62f, 0.7f, 0.85f), 0.15f);

        foreach (Material m in new[] { window, windowLit, lamp, neonBlue, neonPink, neonCyan, neonYellow, neonGreen, neonRed, neonPurple, white, teleportRing,
                     teamGlow[Faction.Player], teamGlow[Faction.Enemy] })
        {
            unshadowed.Add(m);
        }

        signBoard = Plain("Map_SignBoard", new Color(0.04f, 0.04f, 0.05f), 0.5f, Color.black);
        blue = NeonSet("Blue", neonBlue);
        pink = NeonSet("Pink", neonPink);
        cyan = NeonSet("Cyan", neonCyan);
        yellow = NeonSet("Yellow", neonYellow);
        green = NeonSet("Green", neonGreen);
        red = NeonSet("Red", neonRed);
        purple = NeonSet("Purple", neonPurple);
        teamNeon[Faction.Player] = NeonSet("Amber", teamGlow[Faction.Player]);
        teamNeon[Faction.Enemy] = NeonSet("Sapphire", teamGlow[Faction.Enemy]);

        return true;
    }

    private static Neon NeonSet(string name, Material tube)
    {
        Color color = tube.GetColor("_BaseColor");

        // A soft additive pool of the sign's colour, brightest behind the board and fading out.
        Material halo = LoadOrCreate($"Map_Halo_{name}", "Deadlock/Glow");
        halo.SetColor("_Color", new Color(color.r, color.g, color.b, 0.5f));
        halo.SetFloat("_Radial", 1f);
        EditorUtility.SetDirty(halo);
        unshadowed.Add(halo);

        return new Neon { Color = color, Tube = tube, Halo = halo, Text = TextMaterial(name, color) };
    }

    /// <summary>The default TMP font with its glow turned on in the sign's colour, like the menus of step 12.</summary>
    private static Material TextMaterial(string name, Color color)
    {
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        string path = $"{MaterialDir}/Map_Text_{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            m = new Material(font.material) { name = $"Map_Text_{name}" };
            AssetDatabase.CreateAsset(m, path);
        }

        // The mobile SDF shader the font ships with has no glow; the full one does.
        m.shader = Shader.Find("TextMeshPro/Distance Field");
        m.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
        m.EnableKeyword(ShaderUtilities.Keyword_Glow);
        m.SetColor(ShaderUtilities.ID_GlowColor, new Color(color.r, color.g, color.b, 0.75f));
        m.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
        m.SetFloat(ShaderUtilities.ID_GlowInner, 0.1f);
        m.SetFloat(ShaderUtilities.ID_GlowOuter, 0.6f);
        m.SetFloat(ShaderUtilities.ID_GlowPower, 0.75f);
        m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
        ShaderUtilities.UpdateShaderRatios(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/{name}.mat");

    private static Material LoadOrCreate(string name, string shader)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    private static Material Textured(string name, string texture, Color tint, float smoothness)
    {
        Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_diff.jpg"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_nor.jpg"));
        material.EnableKeyword("_NORMALMAP");
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Plain(string name, Color color, float smoothness, Color emission)
    {
        Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);

        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Unlit(string name, Color color)
    {
        Material material = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ geometry primitives

    private static MeshData Data(string chunk, Material material)
    {
        if (!meshes.TryGetValue(chunk, out var byMaterial))
        {
            byMaterial = new Dictionary<Material, MeshData>();
            meshes[chunk] = byMaterial;
        }

        if (!byMaterial.TryGetValue(material, out MeshData data))
        {
            data = new MeshData();
            byMaterial[material] = data;
        }

        return data;
    }

    /// <summary>An axis-aligned box in world space, merged into the chunk's mesh for that material.</summary>
    private static void Box(string chunk, Material material, Vector3 center, Vector3 size, float tile, bool collider)
    {
        MeshData d = Data(chunk, material);
        Vector3 h = size / 2f;

        d.Face(center + Vector3.right * h.x, Vector3.right, Vector3.up, h.z, h.y, tile);
        d.Face(center + Vector3.left * h.x, Vector3.left, Vector3.up, h.z, h.y, tile);
        d.Face(center + Vector3.forward * h.z, Vector3.forward, Vector3.up, h.x, h.y, tile);
        d.Face(center + Vector3.back * h.z, Vector3.back, Vector3.up, h.x, h.y, tile);
        d.Face(center + Vector3.up * h.y, Vector3.up, Vector3.forward, h.x, h.z, tile);

        // Boxes standing on the ground never show their underside.
        if (center.y - h.y > 0.05f)
        {
            d.Face(center + Vector3.down * h.y, Vector3.down, Vector3.forward, h.x, h.z, tile);
        }

        if (collider)
        {
            Collider(chunk, center, size);
        }
    }

    /// <summary>A box given in template coordinates; the frame's reflection keeps it axis-aligned.</summary>
    private static void Box(string chunk, Frame f, Material material, Vector3 center, Vector3 size, float tile, bool collider) =>
        Box(chunk, material, f.P(center), size, tile, collider);

    /// <summary>A single flat rectangle (window, sign, floor marking).</summary>
    private static void Quad(string chunk, Material material, Vector3 center, Vector3 normal, float width, float height, float tile = 0f)
    {
        Vector3 v = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
        Data(chunk, material).Face(center, normal, v, width / 2f, height / 2f, tile);
    }

    private static void Collider(string chunk, Vector3 center, Vector3 size)
    {
        if (!colliders.TryGetValue(chunk, out List<Bounds> list))
        {
            list = new List<Bounds>();
            colliders[chunk] = list;
        }

        list.Add(new Bounds(center, size));
    }

    // ------------------------------------------------------------------ signs

    /// <summary>
    /// A neon sign on a wall: dark board, tube outline, glowing text and a halo on the wall behind.
    /// <paramref name="center"/> is the board's centre and <paramref name="normal"/> the axis it faces, in world space.
    /// </summary>
    private static void Sign(string chunk, Vector3 center, Vector3 normal, float width, float height, Neon neon, string text, bool halo = true)
    {
        Board(chunk, center, normal, width, height, neon, false);

        if (halo)
        {
            // Just behind the board's face but in front of the wall it hangs on.
            Quad(chunk, neon.Halo, center - normal * 0.04f, normal, width * 1.6f + 1f, height * 1.9f + 1f);
        }

        Text(center + normal * 0.1f, normal, width * 0.86f, height * 0.78f, neon, text);
    }

    /// <summary>A blade sign standing out from a facade, lettered on both faces so it reads up and down the street.</summary>
    private static void BladeSign(string chunk, Vector3 center, Vector3 normal, float width, float height, Neon neon, string text)
    {
        Board(chunk, center, normal, width, height, neon, true);
        Text(center + normal * 0.1f, normal, width * 0.8f, height * 0.88f, neon, text);
        Text(center - normal * 0.1f, -normal, width * 0.8f, height * 0.88f, neon, text);
    }

    /// <summary>The dark board with a neon tube running round its edge, on one face or both.</summary>
    private static void Board(string chunk, Vector3 center, Vector3 normal, float width, float height, Neon neon, bool bothFaces)
    {
        const float tube = 0.07f;
        Vector3 along = Abs(Vector3.Cross(Vector3.up, normal));
        Vector3 depth = Abs(normal);
        Box(chunk, signBoard, center, along * width + Vector3.up * height + depth * 0.1f, 0f, false);

        foreach (int face in bothFaces ? new[] { 1, -1 } : new[] { 1 })
        {
            Vector3 front = center + normal * (face * 0.07f);
            Vector3 thin = depth * 0.04f;
            Vector3 horizontal = along * (width - 0.1f) + Vector3.up * tube + thin;
            Vector3 vertical = along * tube + Vector3.up * (height - 0.1f) + thin;

            Box(chunk, neon.Tube, front + Vector3.up * (height / 2f - tube), horizontal, 0f, false);
            Box(chunk, neon.Tube, front - Vector3.up * (height / 2f - tube), horizontal, 0f, false);
            Box(chunk, neon.Tube, front + along * (width / 2f - tube), vertical, 0f, false);
            Box(chunk, neon.Tube, front - along * (width / 2f - tube), vertical, 0f, false);
        }
    }

    /// <summary>Glowing TextMeshPro text sized to fit the board. Not batched: one renderer per sign face.</summary>
    private static void Text(Vector3 position, Vector3 normal, float width, float height, Neon neon, string content)
    {
        var go = new GameObject($"Sign {content.Replace('\n', ' ')}");
        go.transform.SetParent(signs, false);
        // TMP text faces its local -Z, so the transform looks into the board.
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-normal));

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = content;
        text.rectTransform.sizeDelta = new Vector2(width, height);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.5f;
        text.fontSizeMax = 40f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.Lerp(neon.Color, Color.white, 0.35f);
        text.fontSharedMaterial = neon.Text;
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>One letter per line, for tall narrow blade signs.</summary>
    private static string Stacked(string word) => string.Join("\n", word.ToCharArray());

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // ------------------------------------------------------------------ buildings

    /// <summary>
    /// One building on footprint x0..x1 × z0..z1 (template): a sandstone ground floor, a brick or
    /// concrete upper part stepped back a little, a cornice, and windows plus a door on the faces
    /// the player can see.
    /// </summary>
    private static void Building(string chunk, Frame f, float x0, float x1, float z0, float z1, float height, Material upper, Faces faces, Random random)
    {
        float dx = x1 - x0, dz = z1 - z0;
        float mx = (x0 + x1) / 2f, mz = (z0 + z1) / 2f;

        Box(chunk, f, sandstone, new Vector3(mx, BaseHeight / 2f, mz), new Vector3(dx, BaseHeight, dz), SandstoneTile, false);
        Box(chunk, f, upper, new Vector3(mx, BaseHeight + (height - BaseHeight) / 2f, mz), new Vector3(dx - 0.2f, height - BaseHeight, dz - 0.2f), BrickTile, false);
        Box(chunk, f, sandstone, new Vector3(mx, BaseHeight + 0.15f, mz), new Vector3(dx + 0.3f, 0.3f, dz + 0.3f), SandstoneTile, false);
        Collider(chunk, f.P(mx, height / 2f, mz), new Vector3(dx, height, dz));

        if ((faces & Faces.MinusX) != 0) Facade(chunk, f, new Vector3(x0, 0f, mz), Vector3.left, Vector3.forward, dz, height, random);
        if ((faces & Faces.PlusX) != 0) Facade(chunk, f, new Vector3(x1, 0f, mz), Vector3.right, Vector3.forward, dz, height, random);
        if ((faces & Faces.MinusZ) != 0) Facade(chunk, f, new Vector3(mx, 0f, z0), Vector3.back, Vector3.right, dx, height, random);
        if ((faces & Faces.PlusZ) != 0) Facade(chunk, f, new Vector3(mx, 0f, z1), Vector3.forward, Vector3.right, dx, height, random);
    }

    /// <summary>Windows on the upper floors and a dark door in the stone base of one face.</summary>
    private static void Facade(string chunk, Frame f, Vector3 faceCenter, Vector3 normal, Vector3 along, float width, float height, Random random)
    {
        const float spacing = 2.4f, rowSpacing = 3f, winW = 1.1f, winH = 1.7f;
        Vector3 n = f.N(normal);
        int cols = Mathf.Max(1, Mathf.FloorToInt((width - 1f) / spacing));
        float start = -(cols - 1) * spacing / 2f;

        // The upper part is stepped back 0.1 m, the windows sit just proud of it.
        for (float y = BaseHeight + 1.9f; y + winH / 2f < height - 0.6f; y += rowSpacing)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector3 p = faceCenter + along * (start + c * spacing) - normal * 0.08f + Vector3.up * y;
                Quad(chunk, random.NextDouble() < 0.3 ? windowLit : window, f.P(p), n, winW, winH);
            }
        }

        if (width >= 4f)
        {
            Quad(chunk, window, f.P(faceCenter + normal * 0.02f + Vector3.up * 1.35f), n, 1.6f, 2.7f);
        }

        if (width >= 6f && random.NextDouble() < 0.6)
        {
            Lamp(chunk, f.P(faceCenter + normal * 0.25f + along * 1.8f + Vector3.up * 3.6f), 0.35f, lamp);
        }
    }

    /// <summary>Buildings side by side along Z from <paramref name="zStart"/> to <paramref name="zEnd"/>.</summary>
    private static void Row(string chunk, Frame f, float x0, float x1, float zStart, float zEnd, Faces sides, Faces first, Faces last,
        float minHeight, float maxHeight, Random random)
    {
        float z = zStart;

        while (z < zEnd - 0.01f)
        {
            float w = 6f + (float)random.NextDouble() * 3f;
            if (zEnd - (z + w) < 4f) w = zEnd - z;

            Faces faces = sides;
            if (Mathf.Approximately(z, zStart)) faces |= first;
            if (Mathf.Approximately(z + w, zEnd)) faces |= last;

            float height = minHeight + (float)random.NextDouble() * (maxHeight - minHeight);
            double pick = random.NextDouble();
            Material upper = pick < 0.45 ? brick : pick < 0.75 ? brickDark : concrete;

            Building(chunk, f, x0, x1, z, z + w, height, upper, faces, random);
            z += w;
        }
    }

    private static void Lamp(string chunk, Vector3 position, float size, Material material)
    {
        // A low-poly globe: a box reads the same at street distance and merges into the batch.
        Box(chunk, material, position, Vector3.one * size, 0f, false);
    }

    // ------------------------------------------------------------------ areas

    /// <summary>Broadway itself: asphalt, a dashed blue centre line, cover, the zipline and a bridge.</summary>
    private static void BuildLane(Frame f)
    {
        string chunk = $"{f.HalfName}_Lane";

        float length = PlazaEdgeZ - BaseFrontZ;
        Quad(chunk, asphalt, f.P(0f, FloorY, (BaseFrontZ + PlazaEdgeZ) / 2f), Vector3.up, LaneHalfWidth * 2f, length, PavingTile);

        for (float z = BaseFrontZ + 2f; z < PlazaEdgeZ - 2f; z += 6f)
        {
            Quad(chunk, neonBlue, f.P(0f, MarkingY, z + 1.5f), Vector3.up, 0.2f, 3f);
        }

        // Cover staggered left and right, clear of the minions' path down the middle (RF-15).
        Vector3[] cover = { new Vector3(-3.6f, 0f, -60f), new Vector3(3.6f, 0f, -56f), new Vector3(-3.6f, 0f, -40f),
            new Vector3(3.4f, 0f, -31f), new Vector3(-3f, 0f, -16f), new Vector3(3f, 0f, -6f) };

        foreach (Vector3 c in cover)
        {
            Box(chunk, f, sandstoneDark, c + Vector3.up * 0.9f, new Vector3(2.2f, 1.8f, 1.2f), SandstoneTile, true);
        }

        // This side's zipline, run overhead from its own gate to the enemy's, in the lane's blue.
        const float cableX = -7f, cableY = 9.6f;
        Box(chunk, f, neonBlue, new Vector3(cableX, cableY, 0f), new Vector3(0.08f, 0.08f, -2f * BaseFrontZ), 0f, false);

        foreach (float z in new[] { -68f, -40f, -12f, 16f, 44f })
        {
            Box(chunk, f, metal, new Vector3(-7.6f, cableY / 2f, z), new Vector3(0.3f, cableY, 0.3f), 0f, true);
            Box(chunk, f, metal, new Vector3(-7.3f, cableY, z), new Vector3(0.9f, 0.2f, 0.2f), 0f, false);
        }

        // A bridge resting on the lane buildings, as in the reference street.
        const float bridgeZ = -52f, underside = 5.6f, span = LaneHalfWidth * 2f + 1f, deckDepth = 6f;
        Box(chunk, f, sandstone, new Vector3(0f, underside + 0.55f, bridgeZ), new Vector3(span, 1.1f, deckDepth), SandstoneTile, false);
        Box(chunk, f, sandstone, new Vector3(0f, underside + 1.6f, bridgeZ - deckDepth / 2f + 0.2f), new Vector3(span, 1f, 0.4f), SandstoneTile, false);
        Box(chunk, f, sandstone, new Vector3(0f, underside + 1.6f, bridgeZ + deckDepth / 2f - 0.2f), new Vector3(span, 1f, 0.4f), SandstoneTile, false);

        foreach (int edge in new[] { -1, 1 })
        {
            float z = bridgeZ + edge * (deckDepth / 2f - 0.4f);
            Box(chunk, f, sandstoneDark, new Vector3(0f, underside - 0.35f, z), new Vector3(span, 0.7f, 0.8f), SandstoneTile, false);

            foreach (int side in new[] { -1, 1 })
            {
                Box(chunk, f, sandstoneDark, new Vector3(side * (LaneHalfWidth - 0.4f), underside / 2f, z), new Vector3(0.8f, underside, 1.2f), SandstoneTile, true);
            }
        }

        foreach (float x in new[] { -3f, 3f })
        {
            Lamp(chunk, f.P(x, underside - 0.9f, bridgeZ), 0.35f, lamp);
        }
    }

    /// <summary>The two blocks of buildings between the lane and the district alley, split by a cross street.</summary>
    private static void BuildLaneBlocks(Frame f, Random random)
    {
        string chunk = $"{f.HalfName}_{f.District}";

        Row(chunk, f, LaneHalfWidth, BlockOuterX, BaseFrontZ, CrossStreetStart, Faces.MinusX | Faces.PlusX, Faces.None, Faces.PlusZ, 10f, 16f, random);
        Row(chunk, f, LaneHalfWidth, BlockOuterX, CrossStreetEnd, PlazaEdgeZ, Faces.MinusX | Faces.PlusX, Faces.MinusZ, Faces.PlusZ, 12f, 20f, random);

        // Blade signs standing out from the lane facades near Times Square, readable down the street.
        Neon[] colors = { pink, cyan, yellow };
        string[] words = f.Half > 0
            ? (f.Side > 0 ? new[] { "HOTEL", "JAZZ", "DINER" } : new[] { "BAR", "DELI", "PAWN" })
            : (f.Side > 0 ? new[] { "CAFE", "OPEN", "MOTEL" } : new[] { "LIVE", "SOULS", "SHOP" });

        for (int i = 0; i < 3; i++)
        {
            Neon neon = colors[(i + (f.Half > 0 ? 0 : 1) + (f.Side > 0 ? 0 : 2)) % 3];
            BladeSign(chunk, f.P(LaneHalfWidth - 0.7f, 7.5f, -30f + i * 6f), f.N(Vector3.forward), 1.4f, 5f, neon, Stacked(words[i]));
        }
    }

    /// <summary>The buildings along the map edge, facing the district alley, with a courtyard recess.</summary>
    private static void BuildEdge(Frame f, Random random)
    {
        string chunk = $"{f.HalfName}_{f.District}";

        Row(chunk, f, AlleyOuterX, MapHalfWidth, BaseFrontZ, CourtyardStart, Faces.MinusX, Faces.None, Faces.PlusZ, 9f, 15f, random);
        Row(chunk, f, CourtyardBackX, MapHalfWidth, CourtyardStart, CourtyardEnd, Faces.MinusX, Faces.None, Faces.None, 8f, 10f, random);
        Row(chunk, f, AlleyOuterX, MapHalfWidth, CourtyardEnd, 0f, Faces.MinusX, Faces.MinusZ, Faces.None, 10f, 17f, random);
    }

    /// <summary>One team's base: walls, the court facade behind the Patron, and a tinted floor.</summary>
    private static void BuildBase(Frame f, Random random)
    {
        string chunk = $"{f.HalfName}_Base";
        Material stone = teamStone[f.Faction];

        Building(chunk, f, -BaseHalfWidth, BaseHalfWidth, -MapHalfLength, BaseBackZ, 16f, brickDark, Faces.PlusZ, random);
        Quad(chunk, teamFloor[f.Faction], f.P(0f, FloorY, (BaseBackZ + BaseFrontZ) / 2f), Vector3.up, 44f, BaseFrontZ - BaseBackZ, PavingTile);

        foreach (int side in new[] { -1, 1 })
        {
            var s = new Frame(f.Half, side);

            // Side wall, front wall up to the gate, and the solid corner beyond the alley's end.
            Box(chunk, s, stone, new Vector3(BaseHalfWidth - 1f, 3f, (BaseBackZ + BaseFrontZ) / 2f), new Vector3(2f, 6f, BaseFrontZ - BaseBackZ), SandstoneTile, true);
            Box(chunk, s, stone, new Vector3((LaneHalfWidth + BaseHalfWidth) / 2f, 3f, BaseFrontZ - 1f), new Vector3(BaseHalfWidth - LaneHalfWidth, 6f, 2f), SandstoneTile, true);
            Building(chunk, s, BaseHalfWidth, MapHalfWidth, -MapHalfLength, BaseFrontZ, 14f, brick, Faces.PlusZ, random);

            // Gate posts with the team's light, so each side reads at a glance from the plaza.
            Box(chunk, s, stone, new Vector3(LaneHalfWidth + 0.6f, 4f, BaseFrontZ), new Vector3(1.2f, 8f, 1.2f), SandstoneTile, true);
            Lamp(chunk, s.P(LaneHalfWidth + 0.6f, 9.6f, BaseFrontZ), 0.8f, teamGlow[f.Faction]);
        }

        // An arch over the gate with the base's name from the real map, on both faces: the rival
        // reads it coming up the lane, the owner every time they respawn.
        string name = f.Faction == Faction.Player ? "HIDDEN KING'S COURT" : "THE GUIDED HAND";
        Box(chunk, f, stone, new Vector3(0f, 8.6f, BaseFrontZ), new Vector3(LaneHalfWidth * 2f + 2.4f, 1.2f, 1.2f), SandstoneTile, false);
        Sign(chunk, f.P(0f, 8.6f, BaseFrontZ + 0.66f), f.N(Vector3.forward), 12f, 1f, teamNeon[f.Faction], name, false);
        Sign(chunk, f.P(0f, 8.6f, BaseFrontZ - 0.66f), f.N(Vector3.back), 12f, 1f, teamNeon[f.Faction], name, false);
    }

    /// <summary>Times Square: neon signs on the blocks facing the plaza, kiosks and banners over the lane.</summary>
    private static void BuildPlaza()
    {
        const string chunk = "Plaza";
        Neon[] colors = { blue, pink, purple, cyan };
        string[] screens = { "TIMES SQ", "BROADWAY", "THE RITUAL", "DEADLOCK" };
        string[] strips = { "NOW PLAYING", "OPEN LATE", "HOT COFFEE", "NEWS 24H" };
        string[] kiosks = { "NEWS", "SNACKS", "TICKETS", "SOUVENIRS" };
        int i = 0;

        foreach (int half in new[] { 1, -1 })
        {
            foreach (int side in new[] { 1, -1 })
            {
                var f = new Frame(half, side);

                // Big signs on the block ends facing the square, with a smaller strip below.
                Sign(chunk, f.P(13f, 9f, PlazaEdgeZ + 0.15f), f.N(Vector3.forward), 7f, 3.4f, colors[i % 4], screens[i]);
                Sign(chunk, f.P(13f, 5.6f, PlazaEdgeZ + 0.15f), f.N(Vector3.forward), 5f, 1.1f, colors[(i + 1) % 4], strips[i]);

                // A kiosk in the open square: cover, with its sign on the side facing the lane.
                Box(chunk, f, metal, new Vector3(12f, 1.2f, -5f), new Vector3(2.4f, 2.4f, 2.4f), 0f, true);
                Box(chunk, f, metal, new Vector3(12f, 2.55f, -5f), new Vector3(2.8f, 0.3f, 2.8f), 0f, false);
                Sign(chunk, f.P(10.74f, 1.75f, -5f), f.N(Vector3.left), 2f, 0.7f, colors[(i + 2) % 4], kiosks[i], false);
                i++;
            }

            // Banner over the lane, high enough to walk under, lettered on both faces since it is
            // seen from the lane on the way in and from the square on the way out.
            var h = new Frame(half, 1);
            Box(chunk, h, metal, new Vector3(0f, 9.5f, PlazaEdgeZ + 1f), new Vector3(LaneHalfWidth * 2f + 1f, 2.6f, 0.3f), 0f, false);
            Sign(chunk, h.P(0f, 9.5f, PlazaEdgeZ + 1f - 0.21f), h.N(Vector3.back), LaneHalfWidth * 2f - 1f, 2f, blue, "TIMES SQUARE", false);
            Sign(chunk, h.P(0f, 9.5f, PlazaEdgeZ + 1f + 0.21f), h.N(Vector3.forward), LaneHalfWidth * 2f - 1f, 2f, purple, "BROADWAY", false);
        }

        // Crossing stripes where Broadway enters the square.
        foreach (int half in new[] { 1, -1 })
        {
            var f = new Frame(half, 1);
            for (float x = -6.5f; x <= 6.5f; x += 1.6f)
            {
                Quad(chunk, white, f.P(x, MarkingY, PlazaEdgeZ - 1.5f), Vector3.up, 0.8f, 2.4f);
            }
        }
    }

    /// <summary>The district alley: themed cover and props down its length and a landmark in the courtyard.</summary>
    private static void BuildDistrict(Frame f)
    {
        string chunk = $"{f.HalfName}_{f.District}";
        District d = f.District;
        const float midX = (BlockOuterX + AlleyOuterX) / 2f;
        Vector3 court = new Vector3((AlleyOuterX + CourtyardBackX) / 2f, 0f, (CourtyardStart + CourtyardEnd) / 2f);

        Material coverMaterial = d == District.Chinatown ? wood : d == District.HauntedLot ? metal : d == District.Plaza ? concrete : sandstoneDark;
        Vector3[] cover = { new Vector3(21f, 0f, -63f), new Vector3(27f, 0f, -56f), new Vector3(21f, 0f, -47f),
            new Vector3(27f, 0f, -30f), new Vector3(21f, 0f, -22f), new Vector3(27f, 0f, -15f) };

        foreach (Vector3 c in cover)
        {
            Box(chunk, f, coverMaterial, c + Vector3.up * 0.7f, new Vector3(2f, 1.4f, 1.4f), SandstoneTile, true);
        }

        // The district's name where the player turns into it: from the square and from the cross street.
        Neon neon = d == District.Chinatown ? red : d == District.HauntedLot ? green : d == District.Plaza ? purple : yellow;
        string name = d == District.Chinatown ? "CHINATOWN" : d == District.HauntedLot ? "HAUNTED LOT" : d == District.Plaza ? "FAIRFAX PLAZA" : "THEATER DISTRICT";

        foreach (float z in new[] { -16f, -37f })
        {
            Sign(chunk, f.P(AlleyOuterX - 0.06f, 5.2f, z), f.N(Vector3.left), 6f, 1.3f, neon, name);
        }

        switch (d)
        {
            case District.Chinatown:
                // Lantern strings across the alley, red awnings, a gate to the square and the bell tower.
                for (float z = -66f; z <= -18f; z += 8f)
                {
                    Box(chunk, f, metal, new Vector3(midX, 4.8f, z), new Vector3(AlleyOuterX - BlockOuterX, 0.04f, 0.04f), 0f, false);

                    foreach (float x in new[] { 21f, 24f, 27f })
                    {
                        // A red lantern with dark caps, so it reads as a lantern rather than a red cube.
                        Lamp(chunk, f.P(x, 4.4f, z), 0.55f, neonRed);
                        Box(chunk, f, metal, new Vector3(x, 4.71f, z), new Vector3(0.36f, 0.08f, 0.36f), 0f, false);
                        Box(chunk, f, metal, new Vector3(x, 4.09f, z), new Vector3(0.36f, 0.08f, 0.36f), 0f, false);
                    }
                }

                for (float z = -68f; z < -16f; z += 9f)
                {
                    if (z > CourtyardStart - 2f && z < CourtyardEnd) continue;
                    Box(chunk, f, lacquer, new Vector3(AlleyOuterX - 0.6f, 3.2f, z + 2f), new Vector3(1.2f, 0.15f, 4f), 0f, false);
                }

                Box(chunk, f, lacquer, new Vector3(BlockOuterX + 0.6f, 2.6f, -13.5f), new Vector3(0.7f, 5.2f, 0.7f), 0f, true);
                Box(chunk, f, lacquer, new Vector3(AlleyOuterX - 0.6f, 2.6f, -13.5f), new Vector3(0.7f, 5.2f, 0.7f), 0f, true);
                Box(chunk, f, lacquer, new Vector3(midX, 5.4f, -13.5f), new Vector3(12.4f, 0.6f, 0.8f), 0f, false);
                Box(chunk, f, jade, new Vector3(midX, 5.85f, -13.5f), new Vector3(13.2f, 0.3f, 1.6f), 0f, false);
                BladeSign(chunk, f.P(midX, 6.85f, -13.5f), f.N(Vector3.forward), 5.5f, 1.1f, red, "CHINATOWN");

                Box(chunk, f, brickDark, court + new Vector3(0f, 6f, 0f), new Vector3(3f, 12f, 3f), BrickTile, true);
                Box(chunk, f, jade, court + new Vector3(0f, 12.3f, 0f), new Vector3(4f, 0.6f, 4f), 0f, false);
                Lamp(chunk, f.P(court + new Vector3(0f, 10.6f, 0f)), 1.2f, neonYellow);
                break;

            case District.HauntedLot:
                // Green street lights, chain-link fences and a basketball court in the courtyard.
                for (float z = -66f; z <= -16f; z += 10f)
                {
                    Box(chunk, f, metal, new Vector3(AlleyOuterX - 0.5f, 2f, z), new Vector3(0.15f, 4f, 0.15f), 0f, true);
                    Lamp(chunk, f.P(AlleyOuterX - 0.9f, 4.1f, z), 0.45f, neonGreen);
                }

                // Kept clear of the cross street (z -40..-34), which they would otherwise half close.
                foreach (float z in new[] { -62f, -28f, -20f })
                {
                    Box(chunk, f, fence, new Vector3(BlockOuterX + 0.4f, 1.2f, z), new Vector3(0.05f, 2.4f, 4f), 0f, true);
                }

                Box(chunk, f, metal, court + new Vector3(2.4f, 1.8f, 0f), new Vector3(0.2f, 3.6f, 0.2f), 0f, true);
                Box(chunk, f, white, court + new Vector3(2.2f, 3.4f, 0f), new Vector3(0.1f, 1.1f, 1.8f), 0f, false);
                Lamp(chunk, f.P(court + new Vector3(1.8f, 3.05f, 0f)), 0.45f, neonRed);
                Quad(chunk, white, f.P(court + new Vector3(-0.5f, MarkingY, 0f)), Vector3.up, 0.12f, 8f);
                Quad(chunk, white, f.P(court + new Vector3(0.5f, MarkingY, 0f)), Vector3.up, 5f, 0.12f);
                break;

            case District.Plaza:
                // The Fairfax statue on its pedestal, under purple lamps.
                Box(chunk, f, concrete, court + new Vector3(0f, 1.1f, 0f), new Vector3(2.6f, 2.2f, 2.6f), SandstoneTile, true);
                Box(chunk, f, bronze, court + new Vector3(0f, 3.4f, 0f), new Vector3(0.9f, 2.4f, 0.7f), 0f, false);
                Box(chunk, f, bronze, court + new Vector3(0f, 4.85f, 0f), new Vector3(0.5f, 0.5f, 0.5f), 0f, false);
                Box(chunk, f, bronze, court + new Vector3(0.5f, 4.6f, 0f), new Vector3(0.2f, 1.2f, 0.2f), 0f, false);
                Sign(chunk, f.P(court + new Vector3(-1.36f, 1.5f, 0f)), f.N(Vector3.left), 1.9f, 0.55f, purple, "FAIRFAX", false);

                for (float z = -66f; z <= -16f; z += 10f)
                {
                    Box(chunk, f, metal, new Vector3(BlockOuterX + 0.5f, 2f, z), new Vector3(0.15f, 4f, 0.15f), 0f, true);
                    Lamp(chunk, f.P(BlockOuterX + 0.9f, 4.1f, z), 0.45f, neonPurple);
                }
                break;

            case District.Theater:
                // A marquee over the courtyard with a row of bulbs, posters and a red carpet.
                Box(chunk, f, metal, new Vector3(CourtyardBackX - 0.3f, 5f, court.z), new Vector3(0.6f, 2.2f, 8f), 0f, false);
                Sign(chunk, f.P(CourtyardBackX - 0.66f, 5f, court.z), f.N(Vector3.left), 7.4f, 1.6f, yellow, "TONIGHT: THE RITUAL", false);

                for (float z = court.z - 3.6f; z <= court.z + 3.6f; z += 0.9f)
                {
                    Lamp(chunk, f.P(CourtyardBackX - 0.7f, 6.25f, z), 0.18f, neonYellow);
                }

                Quad(chunk, neonRed, f.P((AlleyOuterX + CourtyardBackX) / 2f, MarkingY, court.z), Vector3.up, CourtyardBackX - AlleyOuterX, 2f);

                // Show posters for the two heroes, on the edge buildings only, not over the open
                // courtyard, and beside the doors (which sit mid-building) rather than over them.
                float[] posterZ = { -61.8f, -37.8f, -25.8f, -18.8f };
                string[] posters = { "SEVEN", "VINDICTA", "THE\nRITUAL", "SOLD\nOUT" };

                for (int p = 0; p < posterZ.Length; p++)
                {
                    Sign(chunk, f.P(AlleyOuterX - 0.06f, 2.2f, posterZ[p]), f.N(Vector3.left), 1.4f, 1.9f, pink, posters[p]);
                    Lamp(chunk, f.P(AlleyOuterX - 0.4f, 3.7f, posterZ[p]), 0.35f, lamp);
                }
                break;
        }
    }

    // ------------------------------------------------------------------ objectives and teleporters

    /// <summary>Placeholders for the objectives of one side, standing where the real ones will go.</summary>
    private static void BuildStructureMarkers(Frame f, Transform parent)
    {
        Transform side = new GameObject(f.HalfName).transform;
        side.SetParent(parent, false);
        Quaternion forward = Quaternion.LookRotation(f.N(Vector3.forward));

        Walker(side, f.P(WalkerPos), forward, f.Faction);

        foreach (int s in new[] { -1, 1 })
        {
            var mirrored = new Frame(f.Half, s);
            BaseGuardian(side, mirrored.P(BaseGuardianPos), forward, s);
            Shrine(side, mirrored.P(ShrinePos), forward, f.Faction, s);
        }

        Patron(side, f.P(PatronPos), forward, f.Faction);
    }

    private static Transform Marker(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(position, rotation);
        return t;
    }

    private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider, Vector3 euler = default)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = groundLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (unshadowed.Contains(material))
        {
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        if (!collider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        return go;
    }

    /// <summary>Walker placeholder: a four-legged automaton about 9 m tall with a glowing eye.</summary>
    private static void Walker(Transform parent, Vector3 position, Quaternion rotation, Faction faction)
    {
        Transform t = Marker(parent, "Walker (placeholder)", position, rotation);
        Material stone = teamStone[faction], glow = teamGlow[faction];

        foreach (int x in new[] { -1, 1 })
        {
            foreach (int z in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "Leg", t, new Vector3(x * 2f, 3f, z * 1.5f), new Vector3(0.7f, 6f, 0.7f), metal, true);
                Prim(PrimitiveType.Cube, "Foot", t, new Vector3(x * 2.2f, 0.25f, z * 1.7f), new Vector3(1.3f, 0.5f, 1.3f), stone, false);
            }
        }

        Prim(PrimitiveType.Cube, "Body", t, new Vector3(0f, 7f, 0f), new Vector3(4.6f, 2.4f, 3.6f), stone, true);
        Prim(PrimitiveType.Cube, "Plate", t, new Vector3(0f, 6.4f, 1.85f), new Vector3(4f, 1.6f, 0.3f), metal, false);
        Prim(PrimitiveType.Sphere, "Head", t, new Vector3(0f, 8.7f, 1.2f), Vector3.one * 1.6f, metal, false);
        Prim(PrimitiveType.Sphere, "Eye", t, new Vector3(0f, 8.8f, 1.95f), Vector3.one * 0.7f, glow, false);
        Prim(PrimitiveType.Cube, "Arm", t, new Vector3(2.7f, 7.2f, 1.2f), new Vector3(0.6f, 0.6f, 3f), metal, false);
        Prim(PrimitiveType.Sphere, "ArmEye", t, new Vector3(2.7f, 7.2f, 2.9f), Vector3.one * 0.8f, glow, false);
    }

    /// <summary>Base Guardian: the Guardian model with everything but its looks stripped, so it does not fight yet.</summary>
    private static void BaseGuardian(Transform parent, Vector3 position, Quaternion rotation, int side)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Structures/Guardian_Tower.prefab");

        if (prefab == null)
        {
            Debug.LogWarning("[Deadlock Setup] Guardian_Tower.prefab missing; Base Guardians skipped.");
            return;
        }

        GameObject guardian = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(guardian, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        guardian.name = $"BaseGuardian_{(side > 0 ? "R" : "L")} (inactive)";
        guardian.transform.SetPositionAndRotation(position, rotation);
        guardian.transform.localScale = Vector3.one * 0.85f;

        // Keep only geometry. Last component first: a component is added after the ones it
        // requires, so this removes dependents before what they depend on.
        foreach (Transform t in guardian.GetComponentsInChildren<Transform>(true))
        {
            Component[] components = t.GetComponents<Component>();

            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component c = components[i];
                if (c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer || c is UnityEngine.Collider) continue;
                Object.DestroyImmediate(c);
            }
        }
    }

    /// <summary>Shrine placeholder: a hexagon of candles around a pillar with the team's light.</summary>
    private static void Shrine(Transform parent, Vector3 position, Quaternion rotation, Faction faction, int side)
    {
        Transform t = Marker(parent, $"Shrine_{(side > 0 ? "R" : "L")} (placeholder)", position, rotation);
        Material stone = teamStone[faction], glow = teamGlow[faction];

        Prim(PrimitiveType.Cylinder, "Platform", t, new Vector3(0f, 0.05f, 0f), new Vector3(7f, 0.05f, 7f), stone, false);
        Prim(PrimitiveType.Cube, "Pillar", t, new Vector3(0f, 1.8f, 0f), new Vector3(0.9f, 3.6f, 0.9f), stone, true);
        Prim(PrimitiveType.Sphere, "Core", t, new Vector3(0f, 4.2f, 0f), Vector3.one * 1.1f, glow, false);

        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.8f;
            Prim(PrimitiveType.Cylinder, "Candle", t, p + Vector3.up * 0.35f, new Vector3(0.25f, 0.35f, 0.25f), sandstone, false);
            Prim(PrimitiveType.Sphere, "Flame", t, p + Vector3.up * 0.8f, Vector3.one * 0.2f, lamp, false);
        }

        if (faction == Faction.Player)
        {
            // The Hidden King's shrine is a speaker tower.
            Prim(PrimitiveType.Cube, "Speaker", t, new Vector3(-0.75f, 2.6f, 0f), new Vector3(0.6f, 1.2f, 0.8f), metal, false);
            Prim(PrimitiveType.Cube, "Speaker", t, new Vector3(0.75f, 2.6f, 0f), new Vector3(0.6f, 1.2f, 0.8f), metal, false);
        }
        else
        {
            // The Archmother's holds a glowing tome on a stand.
            Prim(PrimitiveType.Cube, "Lectern", t, new Vector3(0f, 0.55f, 1.5f), new Vector3(0.6f, 1.1f, 0.5f), wood, false);
            Prim(PrimitiveType.Cube, "Tome", t, new Vector3(0f, 1.15f, 1.5f), new Vector3(0.7f, 0.08f, 0.5f), glow, false, new Vector3(-20f, 0f, 0f));
        }
    }

    /// <summary>Patron placeholder: an effigy bound to a frame, the Hidden King with antlers, the Archmother crowned.</summary>
    private static void Patron(Transform parent, Vector3 position, Quaternion rotation, Faction faction)
    {
        bool king = faction == Faction.Player;
        Transform t = Marker(parent, $"Patron_{(king ? "HiddenKing" : "Archmother")} (placeholder)", position, rotation);
        Material stone = teamStone[faction], glow = teamGlow[faction];

        Prim(PrimitiveType.Cylinder, "Dais", t, new Vector3(0f, 0.05f, 0f), new Vector3(12f, 0.05f, 12f), stone, false);
        Prim(PrimitiveType.Cube, "Post", t, new Vector3(-3.2f, 5f, -0.5f), new Vector3(0.7f, 10f, 0.7f), king ? wood : stone, true);
        Prim(PrimitiveType.Cube, "Post", t, new Vector3(3.2f, 5f, -0.5f), new Vector3(0.7f, 10f, 0.7f), king ? wood : stone, true);
        Prim(PrimitiveType.Cube, "Beam", t, new Vector3(0f, 9f, -0.5f), new Vector3(7.2f, 0.7f, 0.7f), king ? wood : stone, false);
        Prim(PrimitiveType.Capsule, "Body", t, new Vector3(0f, 4.6f, 0f), new Vector3(2.6f, 3.4f, 2.6f), king ? wood : stone, true);
        Prim(PrimitiveType.Sphere, "Head", t, new Vector3(0f, 8.6f, 0f), Vector3.one * 2f, metal, false);
        Prim(PrimitiveType.Sphere, "Eye", t, new Vector3(-0.4f, 8.8f, 0.9f), Vector3.one * 0.3f, glow, false);
        Prim(PrimitiveType.Sphere, "Eye", t, new Vector3(0.4f, 8.8f, 0.9f), Vector3.one * 0.3f, glow, false);

        if (king)
        {
            foreach (int s in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "Antler", t, new Vector3(s * 1f, 10.4f, 0f), new Vector3(0.25f, 2.6f, 0.25f), wood, false, new Vector3(0f, 0f, -s * 35f));
                Prim(PrimitiveType.Cube, "Tine", t, new Vector3(s * 1.7f, 11f, 0f), new Vector3(0.18f, 1.2f, 0.18f), wood, false, new Vector3(0f, 0f, s * 20f));
            }
        }
        else
        {
            for (int i = -2; i <= 2; i++)
            {
                Prim(PrimitiveType.Cube, "CrownSpike", t, new Vector3(i * 0.45f, 10f, 0f), new Vector3(0.18f, 1.4f, 0.18f), glow, false, new Vector3(0f, 0f, -i * 18f));
            }

            Prim(PrimitiveType.Cube, "Crystal", t, new Vector3(0f, 2.4f, 1.4f), Vector3.one * 1.6f, glow, false, new Vector3(45f, 45f, 0f));
        }

        // The orb it collapses into in its second phase, floating above.
        Prim(PrimitiveType.Sphere, "Orb", t, new Vector3(0f, 13f, 0f), Vector3.one * 1.8f, glow, false);
    }

    /// <summary>
    /// A linked pair per side: inside the base and in this side's cross street near the Guardian.
    /// Each faces where the player should look on arrival: the front one toward the lane, the
    /// home one toward the base gate.
    /// </summary>
    private static void BuildTeleporters(Frame f, Transform parent)
    {
        TeleportPad home = Pad(parent, $"Teleporter_{f.HalfName}_Base", f.P(TeleportBasePos), f.N(Vector3.forward));
        TeleportPad front = Pad(parent, $"Teleporter_{f.HalfName}_Front", f.P(TeleportFrontPos), f.N(Vector3.right));
        Link(home, front);
        Link(front, home);
    }

    private static TeleportPad Pad(Transform parent, string name, Vector3 position, Vector3 facing)
    {
        Transform t = Marker(parent, name, position, Quaternion.LookRotation(facing));
        Prim(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, 0.03f, 0f), new Vector3(2.4f, 0.03f, 2.4f), metal, false);
        GameObject ring = Prim(PrimitiveType.Cylinder, "Ring", t, new Vector3(0f, 0.04f, 0f), new Vector3(2.2f, 0.02f, 2.2f), teleportRing, false);
        Prim(PrimitiveType.Cylinder, "Inner", t, new Vector3(0f, 0.05f, 0f), new Vector3(1.4f, 0.02f, 1.4f), metal, false);

        // A halo at head height so the pad can be spotted from down the street, not only underfoot.
        Prim(PrimitiveType.Cylinder, "Halo", t, new Vector3(0f, 2.5f, 0f), new Vector3(1.8f, 0.01f, 1.8f), neonCyan, false);
        Prim(PrimitiveType.Cylinder, "HaloHole", t, new Vector3(0f, 2.5f, 0f), new Vector3(1.5f, 0.012f, 1.5f), metal, false);

        TeleportPad pad = t.gameObject.AddComponent<TeleportPad>();
        var so = new SerializedObject(pad);
        so.FindProperty("ring").objectReferenceValue = ring.GetComponent<MeshRenderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return pad;
    }

    private static void Link(TeleportPad from, TeleportPad to)
    {
        var so = new SerializedObject(from);
        so.FindProperty("destination").objectReferenceValue = to;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ scene

    private static void FlushMeshes(Transform root)
    {
        foreach (var chunk in meshes)
        {
            Transform chunkRoot = new GameObject(chunk.Key).transform;
            chunkRoot.SetParent(root, false);

            foreach (var entry in chunk.Value)
            {
                MeshData d = entry.Value;
                var mesh = new Mesh { name = $"{chunk.Key}_{entry.Key.name}" };
                if (d.Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(d.Vertices);
                mesh.SetNormals(d.Normals);
                mesh.SetUVs(0, d.Uvs);
                mesh.SetTriangles(d.Triangles, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{MeshDir}/{mesh.name}.asset");

                GameObject go = new GameObject(entry.Key.name);
                go.layer = groundLayer;
                go.transform.SetParent(chunkRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = entry.Key;
                if (unshadowed.Contains(entry.Key)) renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            if (!colliders.TryGetValue(chunk.Key, out List<Bounds> boxes)) continue;

            Transform colliderRoot = new GameObject("Colliders").transform;
            colliderRoot.SetParent(chunkRoot, false);

            foreach (Bounds b in boxes)
            {
                GameObject go = new GameObject("Box");
                go.layer = groundLayer;
                go.transform.SetParent(colliderRoot, false);
                go.transform.position = b.center;
                go.AddComponent<BoxCollider>().size = b.size;
            }
        }
    }

    private static void SetStatic(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = true;
        }
    }

    /// <summary>The ground keeps its name and layer (minions and step 6 look for it) but covers the whole map.</summary>
    private static void RebuildGround()
    {
        GameObject ground = GameObject.Find("Ground");

        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
        }

        ground.layer = groundLayer;
        ground.isStatic = true;
        ground.transform.SetPositionAndRotation(new Vector3(0f, -0.5f, 0f), Quaternion.identity);
        ground.transform.localScale = Vector3.one;

        Vector3 size = new Vector3(MapHalfWidth * 2f, 1f, MapHalfLength * 2f);
        var data = new MeshData();
        data.Face(new Vector3(0f, 0.5f, 0f), Vector3.up, Vector3.forward, size.x / 2f, size.z / 2f, PavingTile);

        var mesh = new Mesh { name = "Map_Ground" };
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetUVs(0, data.Uvs);
        mesh.SetTriangles(data.Triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{MeshDir}/Map_Ground.asset");

        ground.GetComponent<MeshFilter>().sharedMesh = mesh;
        ground.GetComponent<MeshRenderer>().sharedMaterial = paving;
        if (!ground.TryGetComponent(out BoxCollider collider)) collider = ground.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = size;
    }

    /// <summary>Moves what other steps placed for the old 60 m street onto the new layout.</summary>
    private static void RewireScene()
    {
        // The lane path, base gate to base gate down the middle.
        GameObject laneObject = GameObject.Find("LanePath");
        if (laneObject != null)
        {
            for (int i = laneObject.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(laneObject.transform.GetChild(i).gameObject);
            }

            var so = new SerializedObject(laneObject.GetComponent<LanePath>());
            SerializedProperty array = so.FindProperty("waypoints");
            array.arraySize = WaypointZ.Length;

            for (int i = 0; i < WaypointZ.Length; i++)
            {
                GameObject point = new GameObject($"Waypoint_{i}");
                point.transform.SetParent(laneObject.transform, false);
                point.transform.position = new Vector3(0f, 0f, WaypointZ[i]);
                array.GetArrayElementAtIndex(i).objectReferenceValue = point.transform;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        float gate = WaypointZ[WaypointZ.Length - 1];

        // Waves take about a minute to cross now, so allow a third one on the field.
        Place("Spawner_Amber", new Vector3(0f, 0f, -gate), Quaternion.identity, AllowThreeWaves);
        Place("Spawner_Sapphire", new Vector3(0f, 0f, gate), Quaternion.Euler(0f, 180f, 0f), AllowThreeWaves);

        var amber = new Frame(1, 1);
        var sapphire = new Frame(-1, 1);
        Place("TowerSpawn_Amber", amber.P(GuardianPos), Quaternion.identity);
        Place("TowerSpawn_Sapphire", sapphire.P(GuardianPos), Quaternion.Euler(0f, 180f, 0f));
        Place("SpawnPoint_Amber", amber.P(PlayerSpawnPos), Quaternion.identity);
        Place("SpawnPoint_Sapphire", sapphire.P(PlayerSpawnPos), Quaternion.Euler(0f, 180f, 0f));

        // Offline ("Mi misión") the rig starts where it stands, so it starts at Amber's spawn.
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null)
        {
            rig.transform.SetPositionAndRotation(amber.P(PlayerSpawnPos), Quaternion.identity);

            if (rig.TryGetComponent(out ComfortLocomotion locomotion))
            {
                var so = new SerializedObject(locomotion);
                so.FindProperty("laneX").vector2Value = new Vector2(-MapHalfWidth + 0.7f, MapHalfWidth - 0.7f);
                so.FindProperty("laneZ").vector2Value = new Vector2(-MapHalfLength + 0.7f, MapHalfLength - 0.7f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void AllowThreeWaves(GameObject spawner)
    {
        var so = new SerializedObject(spawner.GetComponent<WaveSpawner>());
        so.FindProperty("maxAlive").intValue = 18;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Place(string name, Vector3 position, Quaternion rotation, Action<GameObject> extra = null)
    {
        GameObject go = GameObject.Find(name);

        if (go == null)
        {
            Debug.LogWarning($"[Deadlock Setup] '{name}' not found in the Lane; run its setup step first.");
            return;
        }

        go.transform.SetPositionAndRotation(position, rotation);
        extra?.Invoke(go);
    }

    private static void SetupFog()
    {
        // Farther than the old street so the plaza reads from the gate, close enough to hide the map edge.
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 150f;
    }
}
