using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dresses the Lane greybox as the street under the bridge from Deadlock: stone paving, sandstone
/// cover, a sandstone ground floor along both sides with brick buildings above, a bridge over the
/// middle of the lane, warm sky and fog. Safe to re-run: it rebuilds the "Environment" root and
/// re-skins Ground and Cover without touching the rest of the scene.
/// </summary>
/// <remarks>
/// Textures are CC0 from Poly Haven (precast_stone_paving, sandstone_blocks_08, red_brick_03, 1K).
/// Every box gets its own mesh with UVs in metres, so a texture keeps the same real-world size on a
/// 2 m cover block and on a 17 m building instead of stretching with the transform scale.
/// </remarks>
public static class DeadlockLaneEnvironment
{
    private const string TextureDir = "Assets/Art/Textures/Lane";
    private const string MeshDir = "Assets/Art/Meshes/Lane";

    // Matches DeadlockLaneBuilder: the ground spans x ±7 and z ±30.
    private const float StreetHalfWidth = 7f;
    private const float StreetHalfLength = 30f;
    private const float BaseHeight = 4.5f;

    // Metres covered by one repeat of each texture.
    private const float PavingTile = 2.5f;
    private const float SandstoneTile = 2f;
    private const float BrickTile = 1.2f;

    private static Material paving, sandstone, sandstoneDark, brick, brickDark, window, windowLit, lamp;
    private static int groundLayer;

    [MenuItem("Deadlock/Setup/6 Lane Environment")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Lane.unity", OpenSceneMode.Single);
        groundLayer = LayerMask.NameToLayer(DeadlockSetupTools.GroundLayer);

        CreateMaterials();
        DressGround();
        DressCover();

        GameObject old = GameObject.Find("Environment");
        if (old != null) Object.DestroyImmediate(old);
        Transform root = new GameObject("Environment").transform;

        var random = new System.Random(7);
        BuildStreetSide(root, -1, random);
        BuildStreetSide(root, 1, random);
        BuildEndFacade(root, -1, random);
        BuildEndFacade(root, 1, random);
        BuildBridge(root);

        foreach (Transform t in root.GetComponentsInChildren<Transform>())
        {
            if (t.GetComponent<Light>() == null) t.gameObject.isStatic = true;
        }

        SetupSkyAndFog();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Lane environment built.");
    }

    private static void CreateMaterials()
    {
        ConfigureTextures();

        paving = TexturedMaterial("Lane_Ground", "precast_stone_paving", new Color(1f, 0.94f, 0.84f), 0.15f);
        sandstone = TexturedMaterial("Lane_Sandstone", "sandstone_blocks_08", Color.white, 0.1f);
        sandstoneDark = TexturedMaterial("Lane_Cover", "sandstone_blocks_08", new Color(0.78f, 0.72f, 0.66f), 0.1f);
        brick = TexturedMaterial("Lane_Brick", "red_brick_03", Color.white, 0.1f);
        brickDark = TexturedMaterial("Lane_BrickDark", "red_brick_03", new Color(0.7f, 0.62f, 0.58f), 0.1f);
        window = PlainMaterial("Lane_Window", new Color(0.07f, 0.08f, 0.1f), 0.85f, Color.black);
        windowLit = PlainMaterial("Lane_WindowLit", new Color(0.2f, 0.14f, 0.08f), 0.6f, new Color(1f, 0.62f, 0.3f) * 0.55f);
        lamp = PlainMaterial("Lane_Lamp", new Color(1f, 0.8f, 0.5f), 0.5f, new Color(1f, 0.6f, 0.28f) * 1.1f);
    }

    private static void ConfigureTextures()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.EndsWith("_nor.jpg");
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;

            if (importer.textureType == type && importer.anisoLevel == 4) continue;

            importer.textureType = type;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }

    private static Material TexturedMaterial(string name, string texture, Color tint, float smoothness)
    {
        Material material = LoadOrCreate(name);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_diff.jpg"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_nor.jpg"));
        material.EnableKeyword("_NORMALMAP");
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);
        material.SetTextureScale("_BaseMap", Vector2.one);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material PlainMaterial(string name, Color color, float smoothness, Color emission)
    {
        Material material = LoadOrCreate(name);
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

    private static Material LoadOrCreate(string name)
    {
        string path = $"Assets/Art/Materials/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    /// <summary>The ground was a unit cube scaled to 14×1×60; give it a real-size mesh so the paving tiles.</summary>
    private static void DressGround()
    {
        GameObject ground = GameObject.Find("Ground");
        if (ground == null) return;

        Vector3 size = new Vector3(StreetHalfWidth * 2f, 1f, StreetHalfLength * 2f);
        ApplyBox(ground, size, PavingTile, paving);
    }

    private static void DressCover()
    {
        GameObject cover = GameObject.Find("Cover");
        if (cover == null) return;

        foreach (Transform block in cover.transform)
        {
            // First run: size comes from the old scale. Later runs: scale is 1 and the collider holds it.
            Vector3 size = block.localScale == Vector3.one && block.TryGetComponent(out BoxCollider c) ? c.size : block.localScale;
            ApplyBox(block.gameObject, size, SandstoneTile, sandstoneDark);
            block.gameObject.isStatic = true;
        }
    }

    private static void ApplyBox(GameObject go, Vector3 size, float tile, Material material)
    {
        go.transform.localScale = Vector3.one;
        go.GetComponent<MeshFilter>().sharedMesh = BoxMesh(size, tile);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (go.TryGetComponent(out BoxCollider collider))
        {
            collider.size = size;
            collider.center = Vector3.zero;
        }
    }

    /// <summary>
    /// One side of the street: per building, a sandstone ground floor with pilasters and lamps
    /// facing the lane, and a brick upper part with a grid of windows.
    /// </summary>
    private static void BuildStreetSide(Transform root, int side, System.Random random)
    {
        Transform parent = new GameObject(side < 0 ? "Street_Left" : "Street_Right").transform;
        parent.SetParent(root, false);

        float[] widths = side < 0 ? new[] { 8f, 7f, 9f, 6f, 8f, 7f, 8f, 7f } : new[] { 7f, 9f, 6f, 8f, 8f, 7f, 6f, 9f };
        const float depth = 9f;
        float faceX = side * StreetHalfWidth;
        float z = -StreetHalfLength;

        for (int i = 0; i < widths.Length; i++)
        {
            float w = widths[i];
            float centerZ = z + w / 2f;
            float height = 10f + (float)random.NextDouble() * 7f;
            float centerX = faceX + side * depth / 2f;

            GameObject building = new GameObject($"Building_{i}");
            building.transform.SetParent(parent, false);

            Box(building.transform, "Base", new Vector3(centerX, BaseHeight / 2f, centerZ),
                new Vector3(depth, BaseHeight, w), SandstoneTile, sandstone, true);

            // Upper floors step back a little so the stone base reads as a plinth.
            Box(building.transform, "Upper", new Vector3(centerX + side * 0.3f, BaseHeight + (height - BaseHeight) / 2f, centerZ),
                new Vector3(depth, height - BaseHeight, w - 0.2f), BrickTile, i % 3 == 1 ? brickDark : brick, false);

            // Cornice between stone and brick.
            Box(building.transform, "Cornice", new Vector3(faceX + side * 0.05f, BaseHeight + 0.15f, centerZ),
                new Vector3(0.5f, 0.3f, w), SandstoneTile, sandstone, false);

            Windows(building.transform, faceX + side * 0.3f, side, centerZ, w, height, random);

            // Pilasters at the building edges, with a lamp between them.
            Box(building.transform, "Pilaster", new Vector3(faceX - side * 0.17f, BaseHeight / 2f, z + 0.35f),
                new Vector3(0.35f, BaseHeight, 0.7f), SandstoneTile, sandstone, true);

            Doorway(building.transform, faceX, side, centerZ);

            if (i % 2 == 0)
            {
                Lamp(building.transform, new Vector3(faceX - side * 0.25f, 3.6f, centerZ + 1.6f));
            }

            z += w;
        }
    }

    /// <summary>A dark door opening with a stone frame, so the stone ground floor is not one flat wall.</summary>
    private static void Doorway(Transform parent, float faceX, int side, float centerZ)
    {
        const float doorW = 1.6f, doorH = 2.7f;

        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Quad);
        door.name = "Door";
        Object.DestroyImmediate(door.GetComponent<Collider>());
        door.transform.SetParent(parent, false);
        door.transform.SetPositionAndRotation(new Vector3(faceX - side * 0.02f, doorH / 2f, centerZ),
            Quaternion.LookRotation(new Vector3(side, 0f, 0f)));
        door.transform.localScale = new Vector3(doorW, doorH, 1f);
        door.GetComponent<MeshRenderer>().sharedMaterial = window;
        door.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        float frameX = faceX - side * 0.1f;
        Box(parent, "DoorLintel", new Vector3(frameX, doorH + 0.2f, centerZ), new Vector3(0.2f, 0.4f, doorW + 0.6f), SandstoneTile, sandstoneDark, false);
        Box(parent, "DoorJamb", new Vector3(frameX, doorH / 2f, centerZ - doorW / 2f - 0.15f), new Vector3(0.2f, doorH, 0.3f), SandstoneTile, sandstoneDark, false);
        Box(parent, "DoorJamb", new Vector3(frameX, doorH / 2f, centerZ + doorW / 2f + 0.15f), new Vector3(0.2f, doorH, 0.3f), SandstoneTile, sandstoneDark, false);
    }

    private static void Windows(Transform parent, float faceX, int side, float centerZ, float width, float height, System.Random random)
    {
        const float spacingZ = 2.4f, spacingY = 3f, winW = 1.1f, winH = 1.7f;
        int cols = Mathf.Max(1, Mathf.FloorToInt((width - 1f) / spacingZ));
        float startZ = centerZ - (cols - 1) * spacingZ / 2f;
        Quaternion facing = Quaternion.LookRotation(new Vector3(side, 0f, 0f));

        for (float y = BaseHeight + 1.9f; y + winH / 2f < height - 0.6f; y += spacingY)
        {
            for (int c = 0; c < cols; c++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Window";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(parent, false);
                // A quad faces -Z; LookRotation(side) points that face back at the street.
                quad.transform.SetPositionAndRotation(new Vector3(faceX - side * 0.02f, y, startZ + c * spacingZ), facing);
                quad.transform.localScale = new Vector3(winW, winH, 1f);
                quad.GetComponent<MeshRenderer>().sharedMaterial = random.NextDouble() < 0.3 ? windowLit : window;
                quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    /// <summary>Buildings across each end of the street so the lane does not open onto the void.</summary>
    private static void BuildEndFacade(Transform root, int end, System.Random random)
    {
        Transform parent = new GameObject(end < 0 ? "Street_EndPlayer" : "Street_EndRival").transform;
        parent.SetParent(root, false);

        const float depth = 8f;
        float faceZ = end * StreetHalfLength;
        float[] widths = { 10f, 12f, 10f };
        float x = -17f;

        foreach (float w in widths)
        {
            float height = 12f + (float)random.NextDouble() * 6f;
            float centerX = x + w / 2f;
            float centerZ = faceZ + end * depth / 2f;

            Box(parent, "Base", new Vector3(centerX, BaseHeight / 2f, centerZ), new Vector3(w, BaseHeight, depth), SandstoneTile, sandstone, true);
            Box(parent, "Upper", new Vector3(centerX, BaseHeight + (height - BaseHeight) / 2f, centerZ + end * 0.3f),
                new Vector3(w - 0.2f, height - BaseHeight, depth), BrickTile, brickDark, false);
            Box(parent, "Cornice", new Vector3(centerX, BaseHeight + 0.15f, faceZ + end * 0.05f), new Vector3(w, 0.3f, 0.5f), SandstoneTile, sandstone, false);

            EndWindows(parent, faceZ + end * 0.3f, end, centerX, w, height, random);
            x += w;
        }
    }

    private static void EndWindows(Transform parent, float faceZ, int end, float centerX, float width, float height, System.Random random)
    {
        const float spacingX = 2.4f, spacingY = 3f;
        int cols = Mathf.FloorToInt((width - 1f) / spacingX);
        float startX = centerX - (cols - 1) * spacingX / 2f;
        Quaternion facing = Quaternion.LookRotation(new Vector3(0f, 0f, end));

        for (float y = BaseHeight + 1.9f; y + 0.85f < height - 0.6f; y += spacingY)
        {
            for (int c = 0; c < cols; c++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Window";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(parent, false);
                quad.transform.SetPositionAndRotation(new Vector3(startX + c * spacingX, y, faceZ - end * 0.02f), facing);
                quad.transform.localScale = new Vector3(1.1f, 1.7f, 1f);
                quad.GetComponent<MeshRenderer>().sharedMaterial = random.NextDouble() < 0.3 ? windowLit : window;
                quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    /// <summary>
    /// The bridge over the middle of the lane, as in the reference shot: a sandstone deck resting
    /// on the stone ground floors, with a thick beam on each edge and lamps hanging underneath.
    /// </summary>
    private static void BuildBridge(Transform root)
    {
        Transform parent = new GameObject("Bridge").transform;
        parent.SetParent(root, false);

        const float underside = 5.6f, thickness = 1.1f, span = StreetHalfWidth * 2f + 1f, deckDepth = 7f;

        Box(parent, "Deck", new Vector3(0f, underside + thickness / 2f, 0f), new Vector3(span, thickness, deckDepth), SandstoneTile, sandstone, true);
        Box(parent, "Parapet_Player", new Vector3(0f, underside + thickness + 0.5f, -deckDepth / 2f + 0.2f), new Vector3(span, 1f, 0.4f), SandstoneTile, sandstone, false);
        Box(parent, "Parapet_Rival", new Vector3(0f, underside + thickness + 0.5f, deckDepth / 2f - 0.2f), new Vector3(span, 1f, 0.4f), SandstoneTile, sandstone, false);

        foreach (int edge in new[] { -1, 1 })
        {
            float z = edge * (deckDepth / 2f - 0.4f);
            Box(parent, "Beam", new Vector3(0f, underside - 0.35f, z), new Vector3(span, 0.7f, 0.8f), SandstoneTile, sandstoneDark, false);

            // Thick piers against the street walls carry the beams.
            foreach (int side in new[] { -1, 1 })
            {
                Box(parent, "Pier", new Vector3(side * (StreetHalfWidth - 0.4f), underside / 2f, z),
                    new Vector3(0.8f, underside, 1.2f), SandstoneTile, sandstoneDark, true);
            }
        }

        foreach (float x in new[] { -3f, 3f })
        {
            Lamp(parent, new Vector3(x, underside - 0.9f, 0f));
        }

        // Two warm lights so the shade under the deck reads as lamp-lit rather than black.
        foreach (float z in new[] { -1.5f, 1.5f })
        {
            GameObject lightGo = new GameObject("BridgeLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.position = new Vector3(0f, underside - 1.2f, z);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.45f);
            light.range = 9f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
        }
    }

    private static void Lamp(Transform parent, Vector3 position)
    {
        GameObject globe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        globe.name = "Lamp";
        Object.DestroyImmediate(globe.GetComponent<Collider>());
        globe.transform.SetParent(parent, false);
        globe.transform.position = position;
        globe.transform.localScale = Vector3.one * 0.35f;
        MeshRenderer renderer = globe.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = lamp;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void SetupSkyAndFog()
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Lane_Sky.mat");
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, "Assets/Art/Materials/Lane_Sky.mat");
        }

        // Late-afternoon haze like the reference: warm horizon, muted sky.
        sky.SetFloat("_SunSize", 0.03f);
        sky.SetFloat("_AtmosphereThickness", 1.6f);
        sky.SetColor("_SkyTint", new Color(0.62f, 0.58f, 0.55f));
        sky.SetColor("_GroundColor", new Color(0.35f, 0.3f, 0.27f));
        sky.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.66f, 0.6f, 0.53f);
        RenderSettings.fogStartDistance = 25f;
        RenderSettings.fogEndDistance = 110f;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, float tile, Material material, bool collider)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.layer = groundLayer;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, tile);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;

        if (collider)
        {
            go.AddComponent<BoxCollider>().size = size;
        }

        return go;
    }

    private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

    /// <summary>A box of the given size whose UVs are in units of <paramref name="tile"/> metres. Saved as an asset so the scene keeps it.</summary>
    private static Mesh BoxMesh(Vector3 size, float tile)
    {
        string name = $"Box_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_t{tile:0.##}";
        string path = $"{MeshDir}/{name}.asset";

        if (meshCache.TryGetValue(path, out Mesh cached) && cached != null) return cached;

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            meshCache[path] = existing;
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Art/Meshes")) AssetDatabase.CreateFolder("Assets/Art", "Meshes");
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Art/Meshes", "Lane");

        Vector3 h = size / 2f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // Each face: normal, and the two in-plane axes used for position and UV (u, v).
        void Face(Vector3 n, Vector3 u, Vector3 v, float uLen, float vLen)
        {
            // Callers pass u × v = n; flipping u makes u read left-to-right and v upwards
            // for someone looking at the face, so textures are not mirrored.
            u = -u;
            int start = vertices.Count;
            Vector3 center = Vector3.Scale(n, h);
            Vector3 hu = u * (uLen / 2f), hv = v * (vLen / 2f);

            vertices.Add(center - hu - hv); uvs.Add(new Vector2(0f, 0f));
            vertices.Add(center + hu - hv); uvs.Add(new Vector2(uLen / tile, 0f));
            vertices.Add(center + hu + hv); uvs.Add(new Vector2(uLen / tile, vLen / tile));
            vertices.Add(center - hu + hv); uvs.Add(new Vector2(0f, vLen / tile));
            for (int k = 0; k < 4; k++) normals.Add(n);

            // Seen from outside: 0 bottom-left, 1 bottom-right, 2 top-right, 3 top-left. Unity front faces wind clockwise.
            triangles.AddRange(new[] { start, start + 3, start + 2, start, start + 2, start + 1 });
        }

        Face(Vector3.right, Vector3.back, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.up, Vector3.right, Vector3.back, size.x, size.z);
        Face(Vector3.down, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.left, Vector3.up, size.x, size.y);

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        AssetDatabase.CreateAsset(mesh, path);
        meshCache[path] = mesh;
        return mesh;
    }
}
