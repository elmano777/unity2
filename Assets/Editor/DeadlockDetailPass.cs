using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

/// <summary>
/// Step 33: takes the boxes out of the city. The expanded map (step 26) builds every building as a
/// box with flat window quads; this pass reads where those buildings and the cover blocks stand (their
/// colliders) and dresses them, without touching the map itself:
/// <list type="bullet">
/// <item>roofs: a two-tier cornice, a parapet and gravel, and on top water towers on legs, stair
/// bulkheads, air-conditioning units, chimneys and vent pipes, so the skyline stops being flat;</item>
/// <item>facades the player can see: a stone sill and lintel on every window, iron fire escapes with
/// railings and stairs, canvas awnings over the doors, bins and hydrants on the pavement;</item>
/// <item>cover: each stone block becomes a banded wooden crate with a cross brace and a rusty barrel.</item>
/// </list>
/// Textures are Poly Haven CC0 (wood_planks, rusty_metal_02, gravel_concrete_03). Everything goes
/// under "MapDetail", merged by material, static, with no colliders of its own (the map's stay), so
/// running it again just rebuilds the dressing. It must run after step 26.
/// </summary>
public static class DeadlockDetailPass
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string MeshDir = "Assets/Art/Meshes/Detail";
    private const string MaterialDir = "Assets/Art/Materials/Map";
    private const string TextureDir = "Assets/Art/Textures/Lane";
    private const string RootName = "MapDetail";

    // Must match the expanded map's layout (DeadlockExpandedMap): template → world scale, the stone
    // ground floor, and the window grid of Facade().
    private const float ScaleX = DeadlockExpandedMap.ScaleX, ScaleZ = DeadlockExpandedMap.ScaleZ;
    private const float BaseHeight = 4.5f;
    private const float WinSpacing = 2.4f, RowSpacing = 3f, WinW = 1.1f, WinH = 1.7f;

    private static Material sandstone, concrete, brickDark, planks, planksDark, rust, gravel, hydrantRed;
    private static Material[] canvas;

    // Props modelled in Blender (Assets/Models/Props/*.glb, front = +Z, origin on the wall plane or the
    // ground). Their meshes are merged into the same per-material batches as the boxes, with the glTF
    // material names mapped onto the project's textured materials. Missing props fall back to boxes.
    private const string PropDir = "Assets/Models/Props";
    private static readonly Dictionary<string, GameObject> props = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, Material> propMaterials = new Dictionary<string, Material>();
    private static readonly List<Vector3> lampSpots = new List<Vector3>();
    private static readonly List<(Vector3 position, Quaternion rotation, string text)> signTexts = new List<(Vector3, Quaternion, string)>();
    private static readonly string[] MarqueeTitles = { "EL RITUAL  ·  ESTRENO", "HIDDEN KING  vs  ARCHMOTHER", "SOLO ESTA NOCHE", "LA VIDENTE  ·  FUNCIÓN 23H" };

    private static GameObject Prop(string name)
    {
        if (!props.TryGetValue(name, out GameObject prefab))
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{name}.glb");
            props[name] = prefab;
        }

        return prefab;
    }

    private static Material PropMaterial(string name)
    {
        return propMaterials.TryGetValue(name, out Material m) ? m : concrete;
    }

    /// <summary>Places a prop if it exists; returns false so the caller can draw its fallback.</summary>
    private static bool Place(Builder b, string prop, Vector3 position, Quaternion rotation, Vector3? scale = null)
    {
        GameObject prefab = Prop(prop);

        if (prefab == null)
        {
            return false;
        }

        b.Model(prefab, Matrix4x4.TRS(position, rotation, scale ?? Vector3.one), PropMaterial);
        return true;
    }

    [MenuItem("Deadlock/Setup/33 Detail Pass (less boxy)")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        GameObject map = GameObject.Find("Map");

        if (map == null)
        {
            Debug.LogError("[Deadlock Setup] 33: no Map in the Lane. Run step 26 first.");
            return;
        }

        PrepareTextures();
        CreateMaterials();
        props.Clear();
        signTexts.Clear();
        lampSpots.Clear();

        // The lane already has lit street lamps (step 30): modelled lamps only go where none stands.
        foreach (Light light in Object.FindObjectsOfType<Light>(true))
        {
            if (light.name == "StreetLamp")
            {
                lampSpots.Add(light.transform.position);
            }
        }

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
        AssetDatabase.CreateFolder("Assets/Art/Meshes", "Detail");

        var buildings = new List<Bounds>();
        var covers = new List<Bounds>();

        foreach (BoxCollider box in map.GetComponentsInChildren<BoxCollider>(true))
        {
            if (box.transform.rotation != Quaternion.identity || box.gameObject.name != "Box")
            {
                continue;
            }

            Bounds b = new Bounds(box.transform.TransformPoint(box.center), Vector3.Scale(box.size, box.transform.lossyScale));
            Vector3 s = b.size;
            float small = Mathf.Min(s.x, s.z), large = Mathf.Max(s.x, s.z);

            if (s.y >= 7f && small >= 3f && b.min.y < 1.5f)
            {
                buildings.Add(b);
            }
            else if (s.y > 1.4f && s.y < 2.3f && small >= 1f && large <= 4f && b.min.y > -4f && b.min.y < 1f)
            {
                covers.Add(b);
            }
        }

        // The modelled covers replace the stone blocks: take the blocks out of the map's meshes (their
        // colliders stay, so cover works exactly as before).
        if (Prop("CoverSandbags") != null)
        {
            HideCoverBlocks(map, covers);
        }

        var builder = new Builder();

        GroundMaterials();
        Physics.SyncTransforms();

        foreach (Bounds b in buildings)
        {
            var random = new Random(Mathf.RoundToInt(b.center.x * 37f + b.center.z * 101f));
            builder.SetCell(b.center);
            Sidewalks(builder, b, buildings);
            Roof(builder, b, random);

            foreach (Vector3 normal in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Facade(builder, b, normal, buildings, random);
            }
        }

        string[] coverModels = { "CoverSandbags", "CoverCrates", "CoverBarrier" };

        foreach (Bounds c in covers)
        {
            var random = new Random(Mathf.RoundToInt(c.center.x * 53f + c.center.z * 17f));
            builder.SetCell(c.center);

            // The modelled covers are 3.3 x 1.56 x 1.8 along +X: each block takes one, stretched to its
            // collider exactly so what you hide behind is what stops the bullets.
            string model = coverModels[random.Next(coverModels.Length)];
            bool longX = c.size.x >= c.size.z;
            Quaternion yaw = Quaternion.Euler(0f, (longX ? 0f : 90f) + (random.NextDouble() < 0.5 ? 180f : 0f), 0f);
            float length = longX ? c.size.x : c.size.z, depth = longX ? c.size.z : c.size.x;
            Vector3 fit = new Vector3(length / 3.3f, c.size.y / 1.8f, depth / 1.56f);

            if (!Place(builder, model, new Vector3(c.center.x, c.min.y, c.center.z), yaw, fit))
            {
                Crate(builder, c, random);
            }
        }

        Transform root = new GameObject(RootName).transform;
        builder.Flush(root, MeshDir);
        MarqueeTexts(root);

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = true;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Deadlock Setup] 33 Detail pass: {buildings.Count} buildings and {covers.Count} cover blocks dressed, {builder.Triangles} triangles.");
    }

    // ------------------------------------------------------------------ roofs

    private static void Roof(Builder b, Bounds building, Random random)
    {
        Vector3 c = building.center;
        Vector3 s = building.size;
        float top = building.max.y;

        if (Prop("CorniceSegment") != null)
        {
            // The modelled cornice (frieze, dentils, modillions, corona) run along every face in
            // roughly metre-long pieces, with a stone block filling each corner where two runs meet.
            foreach (Vector3 normal in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                bool alongZ = Mathf.Abs(normal.x) > 0.5f;
                Vector3 along = alongZ ? Vector3.forward : Vector3.right;
                float width = alongZ ? s.z : s.x;
                float depth = alongZ ? building.extents.x : building.extents.z;
                // The modelled piece is 2 m long.
                int count = Mathf.Max(1, Mathf.RoundToInt(width / 2f));
                float piece = width / count;
                Quaternion facing = Quaternion.LookRotation(normal, Vector3.up);

                for (int i = 0; i < count; i++)
                {
                    Vector3 p = new Vector3(c.x, top, c.z) + normal * depth + along * (-width / 2f + (i + 0.5f) * piece);
                    Place(b, "CorniceSegment", p, facing, new Vector3(piece / 2f, 1f, 1f));
                }
            }

            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                b.Box(sandstone, new Vector3(c.x + sx * (s.x / 2f + 0.33f), top - 0.33f, c.z + sz * (s.z / 2f + 0.33f)), new Vector3(0.66f, 0.66f, 0.66f), 2f);
            }
        }
        else
        {
            // A two-tier cornice: a dentil band and a wider cap.
            b.Box(sandstone, new Vector3(c.x, top - 0.38f, c.z), new Vector3(s.x + 0.32f, 0.2f, s.z + 0.32f), 2f);
            b.Box(sandstone, new Vector3(c.x, top - 0.1f, c.z), new Vector3(s.x + 0.6f, 0.3f, s.z + 0.6f), 2f);
        }

        // A parapet round the roof, capped, with gravel inside.
        const float wall = 0.28f, rise = 0.85f;
        float y = top + 0.05f + rise / 2f;
        b.Box(concrete, new Vector3(c.x, y, building.max.z - wall / 2f + 0.25f), new Vector3(s.x + 0.5f, rise, wall), 2f);
        b.Box(concrete, new Vector3(c.x, y, building.min.z + wall / 2f - 0.25f), new Vector3(s.x + 0.5f, rise, wall), 2f);
        b.Box(concrete, new Vector3(building.max.x - wall / 2f + 0.25f, y, c.z), new Vector3(wall, rise, s.z + 0.5f), 2f);
        b.Box(concrete, new Vector3(building.min.x + wall / 2f - 0.25f, y, c.z), new Vector3(wall, rise, s.z + 0.5f), 2f);
        b.Box(gravel, new Vector3(c.x, top + 0.06f, c.z), new Vector3(s.x, 0.02f, s.z), 3f);

        // Rooftop clutter, kept inside the parapet.
        float roof = top + 0.07f;
        float marginX = Mathf.Max(0.5f, s.x / 2f - 1.6f), marginZ = Mathf.Max(0.5f, s.z / 2f - 1.6f);
        System.Func<Vector3> spot = () => new Vector3(
            c.x + Lerp(-marginX, marginX, random), roof, c.z + Lerp(-marginZ, marginZ, random));

        if (s.x >= 6f && s.z >= 6f && random.NextDouble() < 0.45)
        {
            Vector3 p = spot();

            if (!Place(b, "WaterTower", p, Quaternion.Euler(0f, random.Next(0, 4) * 90f + Lerp(-10f, 10f, random), 0f)))
            {
                WaterTower(b, p, random);
            }
        }

        if (random.NextDouble() < 0.55)
        {
            Vector3 p = spot();
            b.Box(concrete, p + Vector3.up * 1.2f, new Vector3(2.2f, 2.4f, 2.6f), 2f);
            b.Box(sandstone, p + Vector3.up * 2.45f, new Vector3(2.5f, 0.12f, 2.9f), 2f);
            b.Box(brickDark, p + new Vector3(0f, 1f, 1.31f), new Vector3(0.9f, 2f, 0.03f), 1f);
        }

        int ac = random.Next(0, 3);
        for (int i = 0; i < ac; i++)
        {
            Vector3 p = spot();
            b.Box(rust, p + Vector3.up * 0.45f, new Vector3(1.2f, 0.9f, 1f), 1.5f);
            b.Cylinder(rust, p + Vector3.up * 0.92f, 0.36f, 0.06f, 12, 1.5f, true);
        }

        int chimneys = random.Next(0, 3);
        for (int i = 0; i < chimneys; i++)
        {
            Vector3 p = spot();
            b.Box(brickDark, p + Vector3.up * 0.8f, new Vector3(0.7f, 1.6f, 0.7f), 1.2f);
            b.Box(concrete, p + Vector3.up * 1.65f, new Vector3(0.85f, 0.1f, 0.85f), 2f);
        }

        int vents = random.Next(1, 4);
        for (int i = 0; i < vents; i++)
        {
            Vector3 p = spot();
            b.Cylinder(rust, p + Vector3.up * 0.45f, 0.14f, 0.9f, 10, 1.5f, true);
            b.Cylinder(rust, p + Vector3.up * 0.95f, 0.22f, 0.1f, 10, 1.5f, true);
        }
    }

    /// <summary>The city's wooden water tank: four legs, a deck, a banded barrel and a conical lid.</summary>
    private static void WaterTower(Builder b, Vector3 p, Random random)
    {
        const float legH = 2.4f, radius = 1.35f, tank = 2.6f;

        foreach (Vector2 corner in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            b.Box(rust, p + new Vector3(corner.x * 0.95f, legH / 2f, corner.y * 0.95f), new Vector3(0.16f, legH, 0.16f), 1.5f);
        }

        // Cross bracing between the legs on two sides.
        b.Box(rust, p + new Vector3(0f, legH * 0.5f, 0.95f), new Vector3(1.9f, 0.08f, 0.08f), 1.5f, Quaternion.Euler(0f, 0f, 45f));
        b.Box(rust, p + new Vector3(0f, legH * 0.5f, -0.95f), new Vector3(1.9f, 0.08f, 0.08f), 1.5f, Quaternion.Euler(0f, 0f, -45f));
        b.Box(planksDark, p + Vector3.up * (legH + 0.06f), new Vector3(2.8f, 0.12f, 2.8f), 1.2f);

        Vector3 tankBase = p + Vector3.up * (legH + 0.12f);
        b.Cylinder(random.NextDouble() < 0.5 ? planks : planksDark, tankBase + Vector3.up * tank / 2f, radius, tank, 16, 1.2f, false);
        b.Cylinder(rust, tankBase + Vector3.up * 0.5f, radius + 0.03f, 0.08f, 16, 1.5f, false);
        b.Cylinder(rust, tankBase + Vector3.up * (tank - 0.5f), radius + 0.03f, 0.08f, 16, 1.5f, false);
        b.Cone(rust, tankBase + Vector3.up * tank, radius + 0.15f, 1f, 16, 1.5f);
    }

    // ------------------------------------------------------------------ facades

    private static void Facade(Builder b, Bounds building, Vector3 normal, List<Bounds> all, Random random)
    {
        bool alongZ = Mathf.Abs(normal.x) > 0.5f;
        Vector3 along = alongZ ? Vector3.forward : Vector3.right;
        float worldWidth = alongZ ? building.size.z : building.size.x;
        float scale = alongZ ? ScaleZ : ScaleX;
        float templateWidth = worldWidth / scale;
        float halfDepth = alongZ ? building.extents.x : building.extents.z;

        // The upper floors stand 0.1 template-metres back from the stone ground floor.
        float stepBack = 0.1f * (alongZ ? ScaleX : ScaleZ);
        Vector3 faceCentre = new Vector3(building.center.x, building.min.y, building.center.z) + normal * halfDepth;
        Vector3 upperFace = faceCentre - normal * stepBack;
        float top = building.max.y;

        // Only faces with open air in front get dressed (the others are against a neighbour).
        if (Blocked(faceCentre + normal * 0.6f + Vector3.up * 6f, building, all))
        {
            return;
        }

        int cols = Mathf.Max(1, Mathf.FloorToInt((templateWidth - 1f) / WinSpacing));
        float start = -(cols - 1) * WinSpacing / 2f;
        var rows = new List<float>();

        for (float y = BaseHeight + 1.9f; y + WinH / 2f < (top - building.min.y) - 0.6f; y += RowSpacing)
        {
            rows.Add(building.min.y + y);
        }

        Quaternion facing = Quaternion.LookRotation(normal, Vector3.up);

        // Fire escape over one column of windows on wide walls (chosen first: no balconies on it).
        int escapeColumn = rows.Count >= 2 && templateWidth >= 6.5f && random.NextDouble() < 0.4 ? random.Next(0, cols) : -1;

        // Window surrounds: the modelled frame (sill, casings, lintel, keystone), now and then a balcony.
        for (int c = 0; c < cols; c++)
        {
            Vector3 x = along * ((start + c * WinSpacing) * scale);

            for (int r = 0; r < rows.Count; r++)
            {
                float y = rows[r];
                Vector3 p = upperFace + x;

                if (Blocked(p + normal * 0.4f + Vector3.up * (y - building.min.y), building, all))
                {
                    continue;
                }

                // The full frame on the floors seen up close; the plain sill and lintel higher up,
                // where nobody can tell and the triangles count most on the headset.
                if (r >= 3 || !Place(b, "WindowFrame", Point(p, y), facing))
                {
                    b.Box(sandstone, Point(p, y - WinH / 2f - 0.06f) + normal * 0.11f, Size(along, normal, WinW + 0.3f, 0.12f, 0.22f), 2f);
                    b.Box(sandstone, Point(p, y + WinH / 2f + 0.08f) + normal * 0.07f, Size(along, normal, WinW + 0.22f, 0.16f, 0.14f), 2f);
                }

                if (c != escapeColumn && r >= 1 && random.NextDouble() < 0.12)
                {
                    Place(b, "Balcony", Point(p, y), facing);
                }
            }
        }

        // A floor band between the stone base and the brick: breaks the tall flat wall.
        b.Box(concrete, Point(upperFace, building.min.y + BaseHeight + 0.55f) + normal * 0.05f, Size(along, normal, worldWidth - 0.1f, 0.14f, 0.12f), 2f);

        if (escapeColumn >= 0)
        {
            Vector3 column = upperFace + along * ((start + escapeColumn * WinSpacing) * scale);

            if (Prop("FireEscape") != null && Prop("FireEscapeTop") != null)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    Place(b, r + 1 < rows.Count ? "FireEscape" : "FireEscapeTop", Point(column, rows[r] - WinH / 2f - 0.2f), facing);
                }
            }
            else
            {
                FireEscape(b, column, normal, along, rows);
            }
        }

        // Ground floor: a theatre marquee on a few street fronts, otherwise an awning over the door.
        if (templateWidth >= 4f)
        {
            if (worldWidth >= 7f && random.NextDouble() < 0.14 && Place(b, "TheaterMarquee", faceCentre + Vector3.up * 3f, facing))
            {
                string title = MarqueeTitles[random.Next(MarqueeTitles.Length)];
                signTexts.Add((faceCentre + Vector3.up * 3.3f + normal * 1.53f, Quaternion.LookRotation(-normal, Vector3.up), title));

                // "TEATRO" down both faces of the blade sign above the canopy.
                Vector3 blade = faceCentre + Vector3.up * 5.8f + normal * 0.55f;
                foreach (float side in new[] { -1f, 1f })
                {
                    signTexts.Add((blade + along * (side * 0.135f), Quaternion.LookRotation(-along * side, Vector3.up), "T\nE\nA\nT\nR\nO"));
                }
            }
            else
            {
                Material cloth = canvas[random.Next(canvas.Length)];
                Vector3 door = faceCentre + Vector3.up * 3.05f + normal * 0.62f;
                b.Box(cloth, door, Size(along, normal, 2.4f, 0.05f, 1.25f), 1f, Quaternion.AngleAxis(alongZ ? -18f * normal.x : 18f * normal.z, along));
                b.Box(rust, door + Vector3.up * 0.18f - normal * 0.5f, Size(along, normal, 2.5f, 0.06f, 0.06f), 1.5f);
            }
        }

        // A lit arrow sign sticking out near a corner of some walls.
        if (worldWidth >= 6f && random.NextDouble() < 0.12)
        {
            float side = random.NextDouble() < 0.5 ? -1f : 1f;
            Place(b, "ArrowSign", Point(upperFace + along * (side * (worldWidth / 2f - 0.9f)), building.min.y + BaseHeight + 2.6f), facing);
        }

        // Street furniture on the pavement in front (standing on the raised slabs where they were laid).
        double furniture = random.NextDouble();
        float slide = worldWidth * 0.3f * (random.NextDouble() < 0.5 ? -1f : 1f);
        Vector3 lift = Vector3.up * PavementLift(faceCentre + normal * 1f + along * slide, building.min.y);
        faceCentre += lift;
        Vector3 kerb = faceCentre + normal * 0.9f + along * slide;
        Quaternion kerbFacing = Quaternion.LookRotation(normal, Vector3.up);

        if (furniture < 0.25)
        {
            if (!Place(b, "TrashCan", kerb, Quaternion.Euler(0f, Lerp(0f, 360f, random), 0f)))
            {
                b.Cylinder(rust, kerb + Vector3.up * 0.45f, 0.3f, 0.9f, 12, 1.5f, true);
                b.Cylinder(rust, kerb + Vector3.up * 0.93f, 0.33f, 0.06f, 12, 1.5f, true);
            }
        }
        else if (furniture < 0.42)
        {
            if (!Place(b, "Hydrant", kerb, kerbFacing))
            {
                b.Cylinder(hydrantRed, kerb + Vector3.up * 0.32f, 0.14f, 0.64f, 10, 1f, true);
                b.Cylinder(hydrantRed, kerb + Vector3.up * 0.68f, 0.1f, 0.12f, 10, 1f, true);
                b.Box(hydrantRed, kerb + Vector3.up * 0.42f, Size(along, normal, 0.42f, 0.09f, 0.09f), 1f);
            }
        }
        else if (furniture < 0.6)
        {
            // A bench against the wall, facing the street.
            Place(b, "Bench", faceCentre + normal * 0.45f + along * slide, kerbFacing);
        }
        else if (furniture < 0.72)
        {
            Place(b, "Mailbox", kerb, kerbFacing);
            Place(b, "NewsBox", kerb + along * 0.6f, kerbFacing);
        }

        // A modelled street lamp at the kerb where the lane's lit lamps don't already stand.
        if (worldWidth >= 6f && random.NextDouble() < 0.45)
        {
            Vector3 lamp = faceCentre + normal * 1.6f - along * slide * 0.8f;

            if (!lampSpots.Exists(spot => (new Vector3(spot.x, 0f, spot.z) - new Vector3(lamp.x, 0f, lamp.z)).sqrMagnitude < 81f)
                && Place(b, "StreetLamp", lamp, kerbFacing))
            {
                lampSpots.Add(lamp);
            }
        }
    }

    // ------------------------------------------------------------------ ground

    private static Material pavement, curb, gutter;

    /// <summary>The pavement's height (12 cm) where solid ground at the building's foot lies under <paramref name="p"/>, else 0.</summary>
    private static float PavementLift(Vector3 p, float foot)
    {
        bool level = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
                     && Mathf.Abs(hit.point.y - foot) <= 0.3f;
        return level ? 0.12f : 0f;
    }

    /// <summary>
    /// Real surfaces instead of one paving tile everywhere: wet cracked asphalt on the streets
    /// (Map_Asphalt), cobbles on the districts and plazas (Lane_Ground), and puddles that read as water
    /// rather than holes. Poly Haven CC0: asphalt_02, cobblestone_floor_08, concrete_pavement.
    /// </summary>
    private static void GroundMaterials()
    {
        foreach (string name in new[] { "asphalt_02", "cobblestone_floor_08", "concrete_pavement" })
        {
            string path = $"{TextureDir}/{name}_nor.jpg";

            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        Material asphalt = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/Map_Asphalt.mat");
        if (asphalt != null)
        {
            Retexture(asphalt, "asphalt_02", new Color(0.62f, 0.62f, 0.68f), 0.58f, new Vector2(0.55f, 0.55f));
        }

        Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Lane_Ground.mat");
        if (ground != null)
        {
            Retexture(ground, "cobblestone_floor_08", new Color(0.78f, 0.74f, 0.7f), 0.32f, new Vector2(1.4f, 1.4f));
        }

        pavement = Textured("Map_Pavement", "concrete_pavement", new Color(0.72f, 0.7f, 0.68f), 0.25f, 0f);
        curb = Textured("Map_Curb", "concrete_pavement", new Color(0.55f, 0.55f, 0.56f), 0.2f, 0f);
        gutter = Plain("Map_Gutter", new Color(0.05f, 0.055f, 0.06f), 0.92f);

        // Lane puddles get their own material (the Intro keeps Atmo_Puddle): a see-through glossy film,
        // so the asphalt shows under the reflection instead of a black hole in the street.
        Material puddle = Plain("Map_Puddle", new Color(0.05f, 0.06f, 0.08f, 0.55f), 1f);
        puddle.SetFloat("_Surface", 1f);
        puddle.SetFloat("_Blend", 0f);
        puddle.SetOverrideTag("RenderType", "Transparent");
        puddle.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        puddle.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        puddle.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        puddle.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        puddle.SetFloat("_ZWrite", 0f);
        puddle.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        puddle.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(puddle);

        foreach (MeshRenderer r in Object.FindObjectsOfType<MeshRenderer>(true))
        {
            if (r.sharedMaterial != null && r.sharedMaterial.name == "Atmo_Puddle")
            {
                r.sharedMaterial = puddle;
            }
        }
    }

    private static void Retexture(Material material, string texture, Color tint, float smoothness, Vector2 scale)
    {
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_diff.jpg"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_nor.jpg"));
        material.EnableKeyword("_NORMALMAP");
        material.SetTextureScale("_BaseMap", scale);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
    }

    /// <summary>
    /// A raised pavement in front of every open face: concrete slabs 12 cm up, a stone kerb on the
    /// street edge, a dark wet gutter and a drain now and then. Laid in 2 m runs, each only where the
    /// ground under it is level with the building's foot (none over the sunken street). Visual only:
    /// no colliders, so walking, minions and cover are exactly as before.
    /// </summary>
    private static void Sidewalks(Builder b, Bounds building, List<Bounds> all)
    {
        const float width = 2.1f, height = 0.12f, kerb = 0.24f, run = 2f;
        int groundMask = LayerMask.GetMask("Ground");
        float foot = building.min.y;

        foreach (Vector3 normal in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            bool alongZ = Mathf.Abs(normal.x) > 0.5f;
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            float faceWidth = alongZ ? building.size.z : building.size.x;
            float depth = alongZ ? building.extents.x : building.extents.z;
            Vector3 face = new Vector3(building.center.x, foot, building.center.z) + normal * depth;

            if (Blocked(face + normal * 1.2f + Vector3.up * 1f, building, all))
            {
                continue;
            }

            // Faces along X carry the corners (so the two runs never overlap there).
            float extend = alongZ ? 0f : width;
            float from = -faceWidth / 2f - extend, to = faceWidth / 2f + extend;
            int count = Mathf.Max(1, Mathf.RoundToInt((to - from) / run));
            float piece = (to - from) / count;

            for (int i = 0; i < count; i++)
            {
                Vector3 mid = face + along * (from + (i + 0.5f) * piece) + normal * (width / 2f);

                // Solid ground at the building's foot under this run, and no other building there.
                if (!Physics.Raycast(mid + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, groundMask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(hit.point.y - foot) > 0.3f || Blocked(mid + Vector3.up * 0.5f, building, all))
                {
                    continue;
                }

                float y = hit.point.y;
                b.Box(pavement, new Vector3(mid.x, y + height / 2f, mid.z) - normal * (kerb / 2f), Size(along, normal, piece, height, width - kerb), 1.5f);
                b.Box(curb, new Vector3(mid.x, y + 0.075f, mid.z) + normal * (width / 2f - kerb / 2f), Size(along, normal, piece, 0.15f, kerb), 1.5f);
                b.Box(gutter, new Vector3(mid.x, y + 0.006f, mid.z) + normal * (width / 2f + 0.18f), Size(along, normal, piece, 0.012f, 0.36f), 1f);

                if (i % 6 == 3)
                {
                    b.Box(rust, new Vector3(mid.x, y + 0.014f, mid.z) + normal * (width / 2f + 0.2f), Size(along, normal, 0.62f, 0.012f, 0.32f), 1f);
                }
            }
        }
    }

    private const string MapMeshDir = "Assets/Art/Meshes/Map";

    /// <summary>
    /// Swaps each map mesh drawn with the cover-block material for a copy without the triangles that lie
    /// inside a cover block. The copy is rebuilt from the step-26 mesh every run, so this can run again.
    /// </summary>
    private static void HideCoverBlocks(GameObject map, List<Bounds> covers)
    {
        foreach (MeshFilter filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();

            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.name != "Lane_Cover" || filter.transform.parent == null)
            {
                continue;
            }

            // Step 26 names its meshes "<chunk>_<material>" and puts them under the chunk's object.
            string baseName = $"{filter.transform.parent.name}_{renderer.sharedMaterial.name}";
            Mesh original = AssetDatabase.LoadAssetAtPath<Mesh>($"{MapMeshDir}/{baseName}.asset");

            if (original == null)
            {
                continue;
            }

            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            Vector3[] vertices = original.vertices;
            int[] triangles = original.triangles;
            var kept = new List<int>(triangles.Length);

            for (int t = 0; t < triangles.Length; t += 3)
            {
                bool inside = false;

                foreach (Bounds cover in covers)
                {
                    Bounds grown = cover;
                    grown.Expand(0.1f);

                    if (grown.Contains(toWorld.MultiplyPoint3x4(vertices[triangles[t]]))
                        && grown.Contains(toWorld.MultiplyPoint3x4(vertices[triangles[t + 1]]))
                        && grown.Contains(toWorld.MultiplyPoint3x4(vertices[triangles[t + 2]])))
                    {
                        inside = true;
                        break;
                    }
                }

                if (!inside)
                {
                    kept.Add(triangles[t]);
                    kept.Add(triangles[t + 1]);
                    kept.Add(triangles[t + 2]);
                }
            }

            if (kept.Count == triangles.Length)
            {
                filter.sharedMesh = original;
                continue;
            }

            Mesh copy = Object.Instantiate(original);
            copy.name = baseName + "_NoCover";
            copy.SetTriangles(kept, 0);
            copy.RecalculateBounds();
            AssetDatabase.CreateAsset(copy, $"{MeshDir}/{copy.name}.asset");
            filter.sharedMesh = copy;
        }
    }

    /// <summary>The titles on the theatre marquees, as glowing text on their boards.</summary>
    private static void MarqueeTexts(Transform root)
    {
        if (signTexts.Count == 0)
        {
            return;
        }

        Transform parent = new GameObject("MarqueeTitles").transform;
        parent.SetParent(root, false);

        foreach (var (position, rotation, text) in signTexts)
        {
            var go = new GameObject("Marquee_" + text);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);

            bool vertical = text.Contains("\n");
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.text = text;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            tmp.characterSpacing = vertical ? 0f : 4f;
            tmp.lineSpacing = vertical ? -20f : 0f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.8f;
            tmp.fontSizeMax = vertical ? 5f : 3.4f;
            tmp.color = new Color(0.22f, 0.04f, 0.03f, 1f);
            tmp.rectTransform.sizeDelta = vertical ? new Vector2(0.6f, 2.7f) : new Vector2(4.2f, 0.42f);
        }
    }

    /// <summary>Iron landings in front of a window column, railings and a slanted stair between floors.</summary>
    private static void FireEscape(Builder b, Vector3 face, Vector3 normal, Vector3 along, List<float> rows)
    {
        const float width = 3f, depth = 1.05f, rail = 0.95f;
        // The stair leans within the wall's plane: it turns about the wall's normal.
        Quaternion stairTilt = Quaternion.AngleAxis(Mathf.Atan2(RowSpacing, width * 0.8f) * Mathf.Rad2Deg, normal);

        for (int i = 0; i < rows.Count; i++)
        {
            float floor = rows[i] - WinH / 2f - 0.2f;
            Vector3 landing = Point(face, floor) + normal * (depth / 2f + 0.05f);

            b.Box(rust, landing, Size(along, normal, width, 0.06f, depth), 1.5f);
            b.Box(rust, landing + Vector3.up * rail + normal * (depth / 2f), Size(along, normal, width, 0.05f, 0.05f), 1.5f);
            b.Box(rust, landing + Vector3.up * (rail * 0.5f) + normal * (depth / 2f), Size(along, normal, width, 0.03f, 0.03f), 1.5f);

            foreach (float end in new[] { -1f, 1f })
            {
                Vector3 side = landing + along * (end * width / 2f);
                b.Box(rust, side + Vector3.up * rail, Size(along, normal, 0.05f, 0.05f, depth), 1.5f);
                b.Box(rust, side + Vector3.up * (rail / 2f) + normal * (depth / 2f), new Vector3(0.05f, rail, 0.05f), 1.5f);
            }

            for (float t = -width / 2f + 0.5f; t < width / 2f; t += 0.5f)
            {
                b.Box(rust, landing + along * t + Vector3.up * (rail / 2f) + normal * (depth / 2f), new Vector3(0.025f, rail, 0.025f), 1.5f);
            }

            // Brackets into the wall.
            b.Box(rust, landing - Vector3.up * 0.25f - normal * 0.1f + along * (width / 2f - 0.2f), Size(along, normal, 0.06f, 0.5f, 0.06f), 1.5f, Quaternion.AngleAxis(35f, along));
            b.Box(rust, landing - Vector3.up * 0.25f - normal * 0.1f - along * (width / 2f - 0.2f), Size(along, normal, 0.06f, 0.5f, 0.06f), 1.5f, Quaternion.AngleAxis(35f, along));

            if (i + 1 < rows.Count)
            {
                // A stair climbing from this landing to the next, along the wall.
                float length = Mathf.Sqrt(RowSpacing * RowSpacing + width * 0.8f * width * 0.8f);
                Vector3 mid = landing + Vector3.up * (RowSpacing / 2f) + normal * 0.08f;
                b.Box(rust, mid, Size(along, normal, length, 0.05f, 0.6f), 1.5f, stairTilt);
            }
        }

        // The drop ladder under the lowest landing.
        float lowest = rows[0] - WinH / 2f - 0.2f;
        Vector3 ladder = Point(face, lowest - 1.2f) + normal * (depth - 0.1f) + along * (width / 2f - 0.3f);
        b.Box(rust, ladder - along * 0.2f, new Vector3(0.04f, 2.4f, 0.04f), 1.5f);
        b.Box(rust, ladder + along * 0.2f, new Vector3(0.04f, 2.4f, 0.04f), 1.5f);

        for (float r = -1f; r <= 1.01f; r += 0.4f)
        {
            b.Box(rust, ladder + Vector3.up * r, Size(along, normal, 0.4f, 0.03f, 0.03f), 1.5f);
        }
    }

    // ------------------------------------------------------------------ cover

    /// <summary>A stone cover block dressed as a big banded crate, with a barrel beside it.</summary>
    private static void Crate(Builder b, Bounds block, Random random)
    {
        Vector3 c = block.center;
        Vector3 s = block.size + new Vector3(0.06f, 0.03f, 0.06f);
        c.y += 0.015f;

        b.Box(planks, c, s, 1.2f);

        // Frame strips on every edge and a cross brace on the two long faces.
        const float strip = 0.12f;
        Vector3 h = s / 2f;

        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            b.Box(planksDark, c + new Vector3(sx * (h.x - strip / 2f + 0.02f), 0f, sz * (h.z - strip / 2f + 0.02f)), new Vector3(strip, s.y, strip), 1.2f);
        }

        foreach (float sy in new[] { -1f, 1f })
        {
            b.Box(planksDark, c + new Vector3(0f, sy * (h.y - strip / 2f), h.z + 0.01f), new Vector3(s.x, strip, 0.04f), 1.2f);
            b.Box(planksDark, c + new Vector3(0f, sy * (h.y - strip / 2f), -h.z - 0.01f), new Vector3(s.x, strip, 0.04f), 1.2f);
            b.Box(planksDark, c + new Vector3(h.x + 0.01f, sy * (h.y - strip / 2f), 0f), new Vector3(0.04f, strip, s.z), 1.2f);
            b.Box(planksDark, c + new Vector3(-h.x - 0.01f, sy * (h.y - strip / 2f), 0f), new Vector3(0.04f, strip, s.z), 1.2f);
        }

        bool longX = s.x >= s.z;
        float span = longX ? s.x : s.z;
        float angle = Mathf.Atan2(s.y - strip * 2f, span - strip * 2f) * Mathf.Rad2Deg;
        float brace = Mathf.Sqrt((span - strip * 2f) * (span - strip * 2f) + (s.y - strip * 2f) * (s.y - strip * 2f));

        foreach (float side in new[] { -1f, 1f })
        {
            if (longX)
            {
                b.Box(planksDark, c + new Vector3(0f, 0f, side * (h.z + 0.02f)), new Vector3(brace, strip, 0.04f), 1.2f, Quaternion.Euler(0f, 0f, angle * side));
            }
            else
            {
                b.Box(planksDark, c + new Vector3(side * (h.x + 0.02f), 0f, 0f), new Vector3(0.04f, strip, brace), 1.2f, Quaternion.Euler(angle * side, 0f, 0f));
            }
        }

        // Metal corner caps.
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            b.Box(rust, c + new Vector3(sx * (h.x - 0.06f), h.y - 0.06f, sz * (h.z - 0.06f)), Vector3.one * 0.16f, 1.5f);
        }

        // A barrel at one end, standing on the ground.
        float end = random.NextDouble() < 0.5 ? -1f : 1f;
        Vector3 barrel = new Vector3(c.x, block.min.y, c.z) + (longX ? new Vector3(end * (h.x + 0.45f), 0f, 0f) : new Vector3(0f, 0f, end * (h.z + 0.45f)));
        b.Cylinder(rust, barrel + Vector3.up * 0.48f, 0.34f, 0.96f, 14, 1.5f, true);
        b.Cylinder(planksDark, barrel + Vector3.up * 0.3f, 0.355f, 0.05f, 14, 1.2f, false);
        b.Cylinder(planksDark, barrel + Vector3.up * 0.68f, 0.355f, 0.05f, 14, 1.2f, false);
    }

    // ------------------------------------------------------------------ helpers

    private static bool Blocked(Vector3 point, Bounds self, List<Bounds> all)
    {
        foreach (Bounds other in all)
        {
            if (other != self && other.Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3 Point(Vector3 onFace, float y) => new Vector3(onFace.x, y, onFace.z);

    /// <summary>A box size given as width along the wall, height, and depth out of the wall.</summary>
    private static Vector3 Size(Vector3 along, Vector3 normal, float width, float height, float depth)
    {
        Vector3 a = new Vector3(Mathf.Abs(along.x), 0f, Mathf.Abs(along.z)) * width;
        Vector3 n = new Vector3(Mathf.Abs(normal.x), 0f, Mathf.Abs(normal.z)) * depth;
        return a + n + Vector3.up * height;
    }

    private static float Lerp(float a, float b, Random random) => a + (b - a) * (float)random.NextDouble();

    // ------------------------------------------------------------------ materials

    private static void PrepareTextures()
    {
        foreach (string name in new[] { "wood_planks", "rusty_metal_02", "gravel_concrete_03" })
        {
            string path = $"{TextureDir}/{name}_nor.jpg";

            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }
    }

    private static void CreateMaterials()
    {
        sandstone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Lane_Sandstone.mat");
        brickDark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Lane_BrickDark.mat");
        concrete = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/Map_Concrete.mat") ?? sandstone;

        planks = Textured("Map_Planks", "wood_planks", new Color(0.86f, 0.74f, 0.62f), 0.2f, 0f);
        planksDark = Textured("Map_PlanksDark", "wood_planks", new Color(0.42f, 0.32f, 0.25f), 0.2f, 0f);
        rust = Textured("Map_RustyIron", "rusty_metal_02", new Color(0.62f, 0.6f, 0.6f), 0.35f, 0.55f);
        gravel = Textured("Map_RoofGravel", "gravel_concrete_03", new Color(0.5f, 0.5f, 0.54f), 0.05f, 0f);
        hydrantRed = Plain("Map_HydrantRed", new Color(0.62f, 0.08f, 0.06f), 0.45f);
        canvas = new[]
        {
            Plain("Map_Canvas_Red", new Color(0.5f, 0.08f, 0.07f), 0.15f),
            Plain("Map_Canvas_Green", new Color(0.08f, 0.32f, 0.18f), 0.15f),
            Plain("Map_Canvas_Blue", new Color(0.08f, 0.16f, 0.38f), 0.15f),
            Plain("Map_Canvas_Ochre", new Color(0.55f, 0.38f, 0.1f), 0.15f),
        };

        // The Blender props' material names onto the project's materials.
        Material lampGlass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Lane_Lamp.mat");
        Material lacquer = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/Map_RedLacquer.mat") ?? hydrantRed;
        Material bulb = Plain("Map_Bulb", new Color(1f, 0.85f, 0.55f), 0.3f, new Color(2.4f, 1.8f, 0.9f));
        propMaterials.Clear();
        propMaterials["Wood"] = planks;
        propMaterials["WoodDark"] = planksDark;
        propMaterials["Iron"] = rust;
        propMaterials["Stone"] = sandstone;
        propMaterials["Concrete"] = concrete;
        propMaterials["Brick"] = brickDark;
        propMaterials["Sandbag"] = Plain("Map_Sandbag", new Color(0.5f, 0.43f, 0.3f), 0.05f);
        propMaterials["PaintRed"] = hydrantRed;
        propMaterials["PaintBlue"] = Plain("Map_PaintBlue", new Color(0.06f, 0.14f, 0.38f), 0.45f);
        propMaterials["PaintGreen"] = Plain("Map_PaintGreen", new Color(0.05f, 0.24f, 0.13f), 0.45f);
        propMaterials["GlassDark"] = Plain("Map_GlassDark", new Color(0.03f, 0.04f, 0.05f), 0.9f);
        propMaterials["LampGlass"] = lampGlass != null ? lampGlass : bulb;
        propMaterials["Bulb"] = bulb;
        propMaterials["Marquee"] = Plain("Map_MarqueeBoard", new Color(0.95f, 0.9f, 0.78f), 0.4f, new Color(0.9f, 0.82f, 0.62f));
        propMaterials["SignRed"] = lacquer;
    }

    private static Material Load(string name)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    private static Material Textured(string name, string texture, Color tint, float smoothness, float metallic)
    {
        Material material = Load(name);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_diff.jpg"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{texture}_nor.jpg"));
        material.EnableKeyword("_NORMALMAP");
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Plain(string name, Color color, float smoothness, Color? emission = null)
    {
        Material material = Load(name);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ mesh building

    /// <summary>Collects boxes, cylinders and cones per material and writes one mesh per material.</summary>
    private sealed class Builder
    {
        private sealed class Data
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        // Batches per map cell and material, so the headset only draws the cells in view.
        private readonly Dictionary<(string cell, Material material), Data> byMaterial = new Dictionary<(string, Material), Data>();

        private const float CellSize = 32f;

        /// <summary>The cell everything added next goes into (set from a building's or cover's position).</summary>
        public string Cell { get; private set; } = "0_0";

        public void SetCell(Vector3 position)
        {
            Cell = $"{Mathf.FloorToInt(position.x / CellSize)}_{Mathf.FloorToInt(position.z / CellSize)}";
        }

        public int Triangles { get; private set; }

        private Data For(Material material)
        {
            if (!byMaterial.TryGetValue((Cell, material), out Data data))
            {
                data = new Data();
                byMaterial[(Cell, material)] = data;
            }

            return data;
        }

        /// <summary>A box, optionally rotated about its centre; UVs are world-projected per face.</summary>
        public void Box(Material material, Vector3 centre, Vector3 size, float tile, Quaternion? rotation = null)
        {
            Quaternion r = rotation ?? Quaternion.identity;
            Vector3 h = size / 2f;
            Data d = For(material);

            Face(d, centre, r, Vector3.right, Vector3.up, Vector3.forward, h.x, h.y, h.z, tile);
            Face(d, centre, r, Vector3.left, Vector3.up, Vector3.back, h.x, h.y, h.z, tile);
            Face(d, centre, r, Vector3.forward, Vector3.up, Vector3.left, h.z, h.y, h.x, tile);
            Face(d, centre, r, Vector3.back, Vector3.up, Vector3.right, h.z, h.y, h.x, tile);
            Face(d, centre, r, Vector3.up, Vector3.forward, Vector3.right, h.y, h.z, h.x, tile);
            Face(d, centre, r, Vector3.down, Vector3.forward, Vector3.left, h.y, h.z, h.x, tile);
        }

        private void Face(Data d, Vector3 centre, Quaternion r, Vector3 normal, Vector3 up, Vector3 right, float offset, float halfUp, float halfRight, float tile)
        {
            int i = d.Vertices.Count;
            Vector3 n = r * normal;
            Vector3 u = r * up;
            Vector3 ri = r * right;
            Vector3 c = centre + n * offset;
            float scale = tile > 0f ? 1f / tile : 1f;

            Vector3[] corners =
            {
                c - ri * halfRight - u * halfUp,
                c + ri * halfRight - u * halfUp,
                c + ri * halfRight + u * halfUp,
                c - ri * halfRight + u * halfUp,
            };

            Vector2[] uv =
            {
                new Vector2(-halfRight, -halfUp) * scale,
                new Vector2(halfRight, -halfUp) * scale,
                new Vector2(halfRight, halfUp) * scale,
                new Vector2(-halfRight, halfUp) * scale,
            };

            for (int k = 0; k < 4; k++)
            {
                d.Vertices.Add(corners[k]);
                d.Normals.Add(n);
                d.Uvs.Add(uv[k] + new Vector2(Vector3.Dot(c, ri), Vector3.Dot(c, u)) * scale);
            }

            // Wound so the face is seen from the side its normal points to.
            d.Triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            Triangles += 2;
        }

        /// <summary>An upright cylinder centred on <paramref name="centre"/>, optionally with a top cap.</summary>
        public void Cylinder(Material material, Vector3 centre, float radius, float height, int segments, float tile, bool cap)
        {
            Data d = For(material);
            float scale = 1f / tile;
            float circumference = 2f * Mathf.PI * radius;
            int start = d.Vertices.Count;

            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float u = s / (float)segments * circumference * scale;

                d.Vertices.Add(centre + dir * radius - Vector3.up * height / 2f);
                d.Normals.Add(dir);
                d.Uvs.Add(new Vector2(u, 0f));
                d.Vertices.Add(centre + dir * radius + Vector3.up * height / 2f);
                d.Normals.Add(dir);
                d.Uvs.Add(new Vector2(u, height * scale));
            }

            for (int s = 0; s < segments; s++)
            {
                int i = start + s * 2;
                d.Triangles.AddRange(new[] { i, i + 1, i + 3, i, i + 3, i + 2 });
                Triangles += 2;
            }

            if (cap)
            {
                Disc(d, centre + Vector3.up * height / 2f, radius, segments, scale);
            }
        }

        private void Disc(Data d, Vector3 centre, float radius, int segments, float scale)
        {
            int middle = d.Vertices.Count;
            d.Vertices.Add(centre);
            d.Normals.Add(Vector3.up);
            d.Uvs.Add(Vector2.zero);

            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                d.Vertices.Add(centre + dir * radius);
                d.Normals.Add(Vector3.up);
                d.Uvs.Add(new Vector2(dir.x, dir.z) * radius * scale);
            }

            for (int s = 0; s < segments; s++)
            {
                d.Triangles.AddRange(new[] { middle, middle + s + 2, middle + s + 1 });
                Triangles += 1;
            }
        }

        /// <summary>A cone standing on <paramref name="baseCentre"/> (a tank's lid).</summary>
        public void Cone(Material material, Vector3 baseCentre, float radius, float height, int segments, float tile)
        {
            Data d = For(material);
            float scale = 1f / tile;
            Vector3 apex = baseCentre + Vector3.up * height;
            float slope = Mathf.Atan2(radius, height);

            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments, a1 = (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 n0 = (d0 * Mathf.Cos(slope) + Vector3.up * Mathf.Sin(slope)).normalized;
                Vector3 n1 = (d1 * Mathf.Cos(slope) + Vector3.up * Mathf.Sin(slope)).normalized;
                int i = d.Vertices.Count;

                d.Vertices.Add(baseCentre + d0 * radius);
                d.Vertices.Add(apex);
                d.Vertices.Add(baseCentre + d1 * radius);
                d.Normals.Add(n0);
                d.Normals.Add(((n0 + n1) / 2f).normalized);
                d.Normals.Add(n1);
                d.Uvs.Add(new Vector2(s * scale, 0f));
                d.Uvs.Add(new Vector2((s + 0.5f) * scale, height * scale));
                d.Uvs.Add(new Vector2((s + 1) * scale, 0f));
                d.Triangles.AddRange(new[] { i, i + 1, i + 2 });
                Triangles += 1;
            }
        }

        /// <summary>
        /// Merges a modelled prop into the batches: every mesh under <paramref name="prefab"/>, placed by
        /// <paramref name="placement"/>, each submesh into the batch of its mapped material.
        /// </summary>
        public void Model(GameObject prefab, Matrix4x4 placement, System.Func<string, Material> map)
        {
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();

                if (mesh == null || renderer == null)
                {
                    continue;
                }

                // The asset's root sits at the origin, so this is the part's place inside the prop.
                Matrix4x4 m = placement * filter.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = m.inverse.transpose;
                bool mirrored = m.determinant < 0f;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                Material[] materials = renderer.sharedMaterials;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material source = materials.Length > 0 ? materials[Mathf.Min(sub, materials.Length - 1)] : null;
                    Data d = For(map(source != null ? source.name : string.Empty));
                    int baseIndex = d.Vertices.Count;

                    for (int i = 0; i < vertices.Length; i++)
                    {
                        d.Vertices.Add(m.MultiplyPoint3x4(vertices[i]));
                        d.Normals.Add(normals.Length > i ? normalMatrix.MultiplyVector(normals[i]).normalized : Vector3.up);
                        d.Uvs.Add(uvs.Length > i ? uvs[i] : Vector2.zero);
                    }

                    int[] triangles = mesh.GetTriangles(sub);

                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        d.Triangles.Add(baseIndex + triangles[t]);
                        d.Triangles.Add(baseIndex + (mirrored ? triangles[t + 2] : triangles[t + 1]));
                        d.Triangles.Add(baseIndex + (mirrored ? triangles[t + 1] : triangles[t + 2]));
                        Triangles += 1;
                    }
                }
            }
        }

        public void Flush(Transform root, string folder)
        {
            var cells = new Dictionary<string, Transform>();

            foreach (var entry in byMaterial)
            {
                Data d = entry.Value;
                string cell = entry.Key.cell;
                Material material = entry.Key.material;

                if (!cells.TryGetValue(cell, out Transform parent))
                {
                    parent = new GameObject("Cell_" + cell).transform;
                    parent.SetParent(root, false);
                    cells[cell] = parent;
                }

                var mesh = new Mesh { name = $"Detail_{cell}_{material.name}" };
                if (d.Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(d.Vertices);
                mesh.SetNormals(d.Normals);
                mesh.SetUVs(0, d.Uvs);
                mesh.SetTriangles(d.Triangles, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{folder}/{mesh.name}.asset");

                var go = new GameObject(material.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
        }
    }
}
