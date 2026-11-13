using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

/// <summary>
/// Step 35: life in the streets and a city around the map. Runs after 33 (it reuses its prop
/// materials and mesh builder) and only adds, under "StreetLife":
/// <list type="bullet">
/// <item>street furniture from Blender (<c>Tools/props/deadlock_props.blend</c>): trees in planters,
/// hot-dog carts under striped umbrellas, phone booths and bollards on the pavements, parked taxis,
/// road works (cones and striped sawhorses), traffic lights at the crossings;</item>
/// <item>zebra crossings where the cross street and the tower passage meet the lane, and at each
/// base gate;</item>
/// <item>a painted arcane circle on the street under each Walker, in its team's colour, so the
/// Walker stands in an arena of its own;</item>
/// <item>billboards with lamps on the roofs along the lane, with original posters;</item>
/// <item>the bases dressed as theatres: velvet curtains on the court walls (red for Amber, teal for
/// Sapphire), candle stands around the patron and two rune obelisks inside the gate glowing in the
/// team colour;</item>
/// <item>a skyline of tall towers with lit windows all around and beyond the map, so the city goes on
/// past the playable streets and the map reads much bigger than it is to walk.</item>
/// </list>
/// The bigger props (taxis, carts, booths, planters, obelisks) get box colliders on the Ground layer
/// like the map's own; small ones (cones, bollards, candles) stay walk-through. Nothing is placed where
/// it would touch a building, another prop, the lane's middle (minion path) or an objective. Meshes
/// are merged per 32 m cell and material like step 33 (Quest, RNF-01).
/// </summary>
public static partial class DeadlockDetailPass
{
    private const string StreetRootName = "StreetLife";
    private const string StreetMeshDir = "Assets/Art/Meshes/StreetLife";
    private const string StreetTextureDir = "Assets/Art/Textures/StreetLife";

    // Lane edges and the pavement of step 33 (world metres, Amber half; Sapphire is the point reflection).
    private const float WallX = 12f, PavementX = 10.9f, KerbHeight = 0.12f, ParkX = 8.3f;
    private const float MinionLaneHalf = 5.5f;
    private const float CrossStreetZ = -48f, TowerPassageZ = -32.5f, GateZ = -91f;
    private static readonly Vector3 WalkerSpot = new Vector3(0f, 0f, -57.2f);
    private static readonly Vector3 PatronSpot = new Vector3(0f, 0f, -100.1f);
    private const float CourtFrontZ = -93.6f, CourtBackZ = -119.6f, CourtSideX = 33f;

    private static readonly List<(Vector3 centre, float radius)> keepClear = new List<(Vector3, float)>();
    private static readonly List<Bounds> placed = new List<Bounds>();
    private static Transform solids;
    private static int groundLayerIndex;
    private static Material paintWhite;
    private static readonly Dictionary<Faction, Material> rune = new Dictionary<Faction, Material>();
    private static readonly Dictionary<Faction, Material> velvet = new Dictionary<Faction, Material>();

    [MenuItem("Deadlock/Setup/35 Street Life (props, Walker circles, billboards, skyline)")]
    public static void RunStreetLife()
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        if (GameObject.Find("Map") == null)
        {
            Debug.LogError("[Deadlock Setup] 35: no Map in the Lane. Run steps 26 and 33 first.");
            return;
        }

        CreateMaterials();
        StreetMaterials();
        props.Clear();
        placed.Clear();
        groundLayerIndex = LayerMask.NameToLayer("Ground");

        GameObject old = GameObject.Find(StreetRootName);
        if (old != null) Object.DestroyImmediate(old);
        if (AssetDatabase.IsValidFolder(StreetMeshDir)) AssetDatabase.DeleteAsset(StreetMeshDir);
        AssetDatabase.CreateFolder("Assets/Art/Meshes", "StreetLife");
        if (!AssetDatabase.IsValidFolder(StreetTextureDir)) AssetDatabase.CreateFolder("Assets/Art/Textures", "StreetLife");

        Transform root = new GameObject(StreetRootName).transform;
        solids = new GameObject("Colliders").transform;
        solids.SetParent(root, false);
        Physics.SyncTransforms();
        KeepClear();

        var builder = new Builder();
        var posters = new List<(Vector3 position, Quaternion rotation, int design)>();
        int poster = 0;

        foreach (int half in new[] { 1, -1 })
        {
            Faction team = half > 0 ? Faction.Player : Faction.Enemy;
            var random = new Random(half > 0 ? 35 : 53);

            Crossings(builder, half);
            Pavements(builder, half, random);
            Parking(builder, half);
            RoadWorks(builder, half);
            TrafficLights(builder, half);
            Billboards(builder, half, posters, ref poster);
            BaseDressing(builder, half, team);
            WalkerCircle(root, half, team);
        }

        builder.Flush(root, StreetMeshDir);
        Posters(root, posters);

        var skyline = new Builder();
        Skyline(skyline);
        Transform sky = new GameObject("Skyline").transform;
        sky.SetParent(root, false);
        skyline.Flush(sky, StreetMeshDir);

        foreach (Renderer r in sky.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = true;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Deadlock Setup] 35 Street life: {placed.Count} props, {posters.Count} billboards, {builder.Triangles + skyline.Triangles} triangles ({skyline.Triangles} skyline).");
    }

    // ------------------------------------------------------------------ materials

    private static void StreetMaterials()
    {
        paintWhite = Plain("Map_PaintWhite", new Color(0.8f, 0.8f, 0.76f), 0.3f);
        Material chrome = Plain("Map_Chrome", new Color(0.72f, 0.72f, 0.75f), 0.8f);
        chrome.SetFloat("_Metallic", 1f);

        propMaterials["PaintOrange"] = Plain("Map_PaintOrange", new Color(0.85f, 0.3f, 0.05f), 0.4f);
        propMaterials["PaintWhite"] = paintWhite;
        propMaterials["PaintYellow"] = Plain("Map_TaxiYellow", new Color(0.95f, 0.64f, 0.07f), 0.55f);
        propMaterials["Rubber"] = Plain("Map_Rubber", new Color(0.04f, 0.04f, 0.04f), 0.1f);
        propMaterials["Chrome"] = chrome;
        propMaterials["LightRed"] = Plain("Map_LightRed", new Color(1f, 0.12f, 0.05f), 0.5f, new Color(3f, 0.25f, 0.1f));
        propMaterials["LightGreen"] = Plain("Map_LightGreen", new Color(0.1f, 1f, 0.4f), 0.5f, new Color(0.12f, 0.8f, 0.3f));
        propMaterials["StoneDark"] = Plain("Map_StoneDark", new Color(0.17f, 0.17f, 0.19f), 0.2f);
        propMaterials["Leaves"] = Plain("Map_Leaves", new Color(0.07f, 0.17f, 0.07f), 0.1f);
        propMaterials["Wax"] = Plain("Map_Wax", new Color(0.9f, 0.86f, 0.76f), 0.3f, new Color(0.18f, 0.12f, 0.05f));
        propMaterials["Flame"] = Plain("Map_Flame", new Color(1f, 0.6f, 0.15f), 0.2f, new Color(4f, 2.2f, 0.6f));

        rune[Faction.Player] = Plain("Map_Rune_Amber", new Color(1f, 0.6f, 0.2f), 0.3f, new Color(4f, 2f, 0.5f));
        rune[Faction.Enemy] = Plain("Map_Rune_Sapphire", new Color(0.3f, 0.75f, 1f), 0.3f, new Color(0.5f, 2.2f, 4f));
        velvet[Faction.Player] = Plain("Map_Velvet_Amber", new Color(0.42f, 0.04f, 0.05f), 0.05f);
        velvet[Faction.Enemy] = Plain("Map_Velvet_Sapphire", new Color(0.04f, 0.2f, 0.24f), 0.05f);
    }

    private static System.Func<string, Material> TeamMaterials(Faction team)
    {
        return name => name == "Rune" ? rune[team] : name == "Velvet" ? velvet[team] : PropMaterial(name);
    }

    // ------------------------------------------------------------------ placement

    /// <summary>Objectives and other spots that must stay free (both halves).</summary>
    private static void KeepClear()
    {
        keepClear.Clear();
        var amber = new List<(Vector3, float)>
        {
            (new Vector3(-7.5f, 0f, -31.2f), 5.5f),     // Guardian
            (WalkerSpot, 10f),                          // Walker
            (new Vector3(9f, 0f, -85.8f), 4f),          // base guardians
            (new Vector3(-9f, 0f, -85.8f), 4f),
            (PatronSpot, 6.5f),                         // patron
            (new Vector3(17.25f, 0f, -100.1f), 4.5f),   // shrines
            (new Vector3(-17.25f, 0f, -100.1f), 4.5f),
        };

        foreach (var (c, r) in amber)
        {
            keepClear.Add((c, r));
            keepClear.Add((new Vector3(-c.x, 0f, -c.z), r));
        }

        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name.StartsWith("Kiosk_") && !t.name.EndsWith("_Board")) keepClear.Add((t.position, 4.5f));
            else if (t.name.Contains("TeleportPad") || t.name.StartsWith("Teleport_")) keepClear.Add((t.position, 3f));
            else if (t.name.StartsWith("NeutralCamp") && t.parent != null && t.parent.name == "NeutralCamps") keepClear.Add((t.position, 6f));
        }
    }

    /// <summary>Amber-half world point to this half.</summary>
    private static Vector3 H(int half, float x, float z) => new Vector3(half * x, 0f, half * z);

    private static Quaternion Yaw(int half, float degrees) => Quaternion.Euler(0f, degrees + (half < 0 ? 180f : 0f), 0f);

    private static bool GroundAt(Vector3 p, out float y, float from = 6f)
    {
        y = 0f;

        if (Physics.Raycast(new Vector3(p.x, p.y + from, p.z), Vector3.down, out RaycastHit hit, from + 10f, 1 << groundLayerIndex, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Places a Blender prop at <paramref name="p"/> (x, z; y found on the ground unless
    /// <paramref name="fixedY"/>) if its footprint (<paramref name="half"/> extents, metres) is clear.
    /// </summary>
    private static bool Put(Builder b, string prop, Vector3 p, Quaternion rotation, Vector3 extents, bool solid,
        System.Func<string, Material> map = null, bool onPavement = false, float? fixedY = null, bool laneOk = false)
    {
        GameObject prefab = Prop(prop);

        if (prefab == null)
        {
            return false;
        }

        if (!laneOk && Mathf.Abs(p.x) < MinionLaneHalf && Mathf.Abs(p.z) < 92f)
        {
            return false;
        }

        foreach (var (c, r) in keepClear)
        {
            if (new Vector2(p.x - c.x, p.z - c.z).magnitude < r + Mathf.Max(extents.x, extents.z))
            {
                return false;
            }
        }

        float y;

        if (fixedY.HasValue)
        {
            y = fixedY.Value;
        }
        else if (!GroundAt(p, out y))
        {
            return false;
        }

        // On step 33's pavement (visual only, 12 cm): stand on it.
        if (onPavement && Mathf.Abs(p.x) > 9.9f && Mathf.Abs(p.x) < WallX && Mathf.Abs(y) < 0.05f)
        {
            y += KerbHeight;
        }

        Vector3 foot = new Vector3(p.x, y, p.z);
        Vector3 centre = foot + Vector3.up * (extents.y + 0.2f);
        Vector3 check = new Vector3(extents.x, extents.y, extents.z);

        if (Physics.CheckBox(centre, check, rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        var bounds = new Bounds(centre, rotation * (check * 2f));
        bounds.size = new Vector3(Mathf.Abs(bounds.size.x), Mathf.Abs(bounds.size.y), Mathf.Abs(bounds.size.z));

        foreach (Bounds other in placed)
        {
            if (other.Intersects(bounds))
            {
                return false;
            }
        }

        placed.Add(bounds);
        b.SetCell(foot);
        b.Model(prefab, Matrix4x4.TRS(foot, rotation, Vector3.one), map ?? PropMaterial);

        if (solid)
        {
            var go = new GameObject(prop);
            go.transform.SetParent(solids, false);
            go.transform.SetPositionAndRotation(foot + Vector3.up * extents.y, rotation);
            go.layer = groundLayerIndex;
            go.AddComponent<BoxCollider>().size = extents * 2f;
            Physics.SyncTransforms();
        }

        return true;
    }

    // ------------------------------------------------------------------ the street

    private static void Crossings(Builder b, int half)
    {
        foreach (float z in new[] { CrossStreetZ, TowerPassageZ, GateZ })
        {
            Vector3 c = H(half, 0f, z);

            if (!GroundAt(c, out float y) || Mathf.Abs(y) > 0.05f)
            {
                continue;
            }

            b.SetCell(c);

            for (float x = -10.45f; x <= 10.5f; x += 1.1f)
            {
                b.Box(paintWhite, new Vector3(x, y + 0.036f, c.z), new Vector3(0.55f, 0.012f, 3.4f), 1f);
            }

            // Stop lines either side.
            foreach (float dz in new[] { -2.3f, 2.3f })
            {
                b.Box(paintWhite, new Vector3(0f, y + 0.036f, c.z + dz), new Vector3(21.6f, 0.012f, 0.25f), 1f);
            }
        }
    }

    private static void Pavements(Builder b, int half, Random random)
    {
        // Every 5.5 m along both pavements: a tree, a hot-dog cart, a phone booth, a pair of bollards or nothing.
        for (float z = -89f; z <= -24f; z += 5.5f)
        {
            foreach (int side in new[] { 1, -1 })
            {
                Vector3 p = H(half, side * PavementX, z + (float)random.NextDouble() * 1.5f);
                Quaternion face = Quaternion.Euler(0f, p.x > 0f ? -90f : 90f, 0f);
                double roll = random.NextDouble();

                if (roll < 0.34) Put(b, "StreetTree", p, face, new Vector3(0.65f, 1.6f, 0.65f), solid: true, onPavement: true);
                else if (roll < 0.47) Put(b, "VendorCart", p, face, new Vector3(0.85f, 0.7f, 1.0f), solid: true, onPavement: true);
                else if (roll < 0.56) Put(b, "PhoneBooth", p, face, new Vector3(0.53f, 1.25f, 0.53f), solid: true, onPavement: true);
                else if (roll < 0.72)
                {
                    foreach (float dz in new[] { -0.9f, 0.9f })
                    {
                        Vector3 q = p + new Vector3(-Mathf.Sign(p.x) * 0.55f, 0f, dz);
                        Put(b, "Bollard", q, face, new Vector3(0.12f, 0.45f, 0.12f), solid: false, onPavement: true);
                    }
                }
            }
        }
    }

    private static void Parking(Builder b, int half)
    {
        var spots = new (float x, float z, float yaw)[]
        {
            (ParkX, -78f, 0f), (-ParkX, -67.5f, 180f), (ParkX, -40.5f, 0f), (-ParkX, -39f, 180f), (ParkX, -71f, 0f),
        };

        foreach (var (x, z, yaw) in spots)
        {
            Put(b, "Taxi", H(half, x, z), Yaw(half, yaw), new Vector3(0.98f, 0.8f, 2.35f), solid: true);
        }
    }

    private static void RoadWorks(Builder b, int half)
    {
        // A dug-up corner on the road edge: two sawhorses and a ring of cones.
        var sites = new (float x, float z)[] { (-8.4f, -75f), (8.6f, -26.8f) };

        foreach (var (x, z) in sites)
        {
            if (!Put(b, "Sawhorse", H(half, x, z), Yaw(half, 90f), new Vector3(0.95f, 0.65f, 0.35f), solid: false))
            {
                continue;
            }

            Put(b, "Sawhorse", H(half, x + Mathf.Sign(x) * -1.4f, z + 1.6f), Yaw(half, 0f), new Vector3(0.95f, 0.65f, 0.35f), solid: false);

            for (int i = 0; i < 5; i++)
            {
                float a = -60f + i * 30f;
                Vector3 c = H(half, x - Mathf.Sign(x) * (1.1f + Mathf.Cos(a * Mathf.Deg2Rad) * 0.9f), z - 1.6f + Mathf.Sin(a * Mathf.Deg2Rad) * 1.1f);
                Put(b, "TrafficCone", c, Quaternion.Euler(0f, i * 37f, 0f), new Vector3(0.2f, 0.38f, 0.2f), solid: false);
            }
        }

        // Cones narrowing the cross street's mouth.
        for (int i = 0; i < 4; i++)
        {
            Put(b, "TrafficCone", H(half, 13.2f + i * 0.9f, CrossStreetZ - 1.8f + i * 0.5f), Quaternion.Euler(0f, i * 53f, 0f), new Vector3(0.2f, 0.38f, 0.2f), solid: false);
        }
    }

    private static void TrafficLights(Builder b, int half)
    {
        // At each crossing corner, arm reaching over the road (+X side arm points -X, so turn the other side round).
        var corners = new (float x, float z, float yaw)[]
        {
            (PavementX, CrossStreetZ - 3.6f, 0f), (-PavementX, CrossStreetZ + 3.6f, 180f),
            (PavementX, TowerPassageZ + 4.6f, 0f), (-PavementX, TowerPassageZ - 4.6f, 180f),
        };

        foreach (var (x, z, yaw) in corners)
        {
            Put(b, "TrafficLight", H(half, x, z), Yaw(half, yaw), new Vector3(0.2f, 1.5f, 0.2f), solid: false, onPavement: true);
        }
    }

    private static void Billboards(Builder b, int half, List<(Vector3, Quaternion, int)> posters, ref int design)
    {
        foreach (float z in new[] { -79f, -64f, -41f, -24f })
        {
            foreach (int side in new[] { 1, -1 })
            {
                // Right at the roof edge (the facade is at 12 m), so the board rises above the cornice
                // where it can be seen from the street; the catwalk overhangs the facade a little.
                Vector3 p = H(half, side * 12.75f, z);

                if (!GroundAt(H(half, side * 13.6f, z), out float roof, 80f) || roof < 6f)
                {
                    continue;
                }

                Quaternion face = Quaternion.Euler(0f, p.x > 0f ? -90f : 90f, 0f);

                if (Put(b, "Billboard", p, face, new Vector3(3.2f, 3.9f, 0.6f), solid: false, fixedY: roof))
                {
                    posters.Add((new Vector3(p.x, roof, p.z), face, design++ % PosterDesigns.Length));
                }
            }
        }
    }

    private static void BaseDressing(Builder b, int half, Faction team)
    {
        System.Func<string, Material> map = TeamMaterials(team);

        // Curtains on the back wall either side of the spawn room, and down both side walls.
        foreach (float x in new[] { 15.5f, 21.5f, 27.5f, -15.5f, -21.5f, -27.5f })
        {
            Put(b, "Curtain", H(half, x, CourtBackZ + 0.4f), Yaw(half, 0f), new Vector3(1.75f, 3.5f, 0.15f), solid: false, map: map, laneOk: true);
        }

        foreach (float z in new[] { -97.5f, -104f, -110.5f, -116.5f })
        {
            foreach (int side in new[] { 1, -1 })
            {
                Put(b, "Curtain", H(half, side * (CourtSideX - 0.4f), z), Yaw(half, side > 0 ? -90f : 90f), new Vector3(1.75f, 3.5f, 0.15f), solid: false, map: map, laneOk: true);
            }
        }

        // Two rune obelisks just inside the gate.
        foreach (int side in new[] { 1, -1 })
        {
            Put(b, "RuneObelisk", H(half, side * 9.5f, CourtFrontZ - 1.6f), Yaw(half, 0f), new Vector3(1.0f, 3.1f, 1.0f), solid: true, map: map, laneOk: true);
        }

        // Candle stands in a ring round the patron (not on the lane axis, where minions and players pass).
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            float x = Mathf.Cos(a) * 8f, z = PatronSpot.z + Mathf.Sin(a) * 8f;

            if (Mathf.Abs(x) < 3f)
            {
                continue;
            }

            Put(b, "Candles", H(half, x, z), Quaternion.Euler(0f, i * 41f, 0f), new Vector3(0.45f, 0.45f, 0.45f), solid: false, map: map, laneOk: true);
        }
    }

    // ------------------------------------------------------------------ Walker circle

    private static void WalkerCircle(Transform root, int half, Faction team)
    {
        Vector3 c = H(half, WalkerSpot.x, WalkerSpot.z);

        if (!GroundAt(c, out float y))
        {
            return;
        }

        Texture2D texture = CircleTexture();
        string name = team == Faction.Player ? "Map_WalkerCircle_Amber" : "Map_WalkerCircle_Sapphire";
        Color tint = team == Faction.Player ? new Color(1f, 0.72f, 0.4f, 0.75f) : new Color(0.55f, 0.85f, 1f, 0.75f);
        Material material = TransparentUnlit(name, texture, tint);

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "WalkerCircle_" + (team == Faction.Player ? "Amber" : "Sapphire");
        quad.transform.SetParent(root, false);
        quad.transform.SetPositionAndRotation(new Vector3(c.x, y + 0.04f, c.z), Quaternion.Euler(90f, half > 0 ? 0f : 180f, 0f));
        quad.transform.localScale = new Vector3(19f, 19f, 1f);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>An original arcane circle: double rim, ticks, eight small rings, a nine-point star and a hub.</summary>
    private static Texture2D CircleTexture()
    {
        string path = $"{StreetTextureDir}/WalkerCircle.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (existing != null)
        {
            return existing;
        }

        const int size = 1024;
        var pixels = new Color32[size * size];
        var star = new Vector2[9];

        for (int i = 0; i < 9; i++)
        {
            float a = Mathf.PI / 2f + i * 2f * Mathf.PI / 9f;
            star[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.6f;
        }

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                var p = new Vector2((px + 0.5f) / size * 2f - 1f, (py + 0.5f) / size * 2f - 1f);
                float r = p.magnitude;
                float ang = Mathf.Atan2(p.y, p.x);
                float ink = 0f;

                ink = Mathf.Max(ink, Band(r, 0.965f, 0.012f));
                ink = Mathf.Max(ink, Band(r, 0.915f, 0.006f));
                ink = Mathf.Max(ink, Band(r, 0.62f, 0.008f));
                ink = Mathf.Max(ink, Band(r, 0.13f, 0.01f));
                ink = Mathf.Max(ink, Band(r, 0.06f, 0.025f) * 0.8f);

                // 36 ticks between the rims, every third one longer.
                float tick = Mathf.Repeat(ang / (2f * Mathf.PI) * 36f, 1f);
                float tickIndex = Mathf.Floor(Mathf.Repeat(ang / (2f * Mathf.PI) * 36f, 36f));
                float inner = tickIndex % 3 == 0 ? 0.8f : 0.85f;
                if (r > inner && r < 0.9f && Mathf.Abs(tick - 0.5f) < 0.06f / Mathf.Max(r, 0.1f)) ink = Mathf.Max(ink, 1f);

                // Eight small rings on r = 0.75.
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f + Mathf.PI / 8f;
                    var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.755f;
                    ink = Mathf.Max(ink, Band((p - c).magnitude, 0.045f, 0.006f));
                    if ((p - c).magnitude < 0.014f) ink = 1f;
                }

                // Nine-point star {9/4}.
                for (int i = 0; i < 9; i++)
                {
                    ink = Mathf.Max(ink, Segment(p, star[i], star[(i + 4) % 9], 0.0055f));
                }

                // Faint wash inside the inner ring so the circle reads as a floor, not just lines.
                float wash = r < 0.62f ? 0.08f : r < 0.965f ? 0.04f : 0f;
                float a8 = Mathf.Clamp01(Mathf.Max(ink, wash));
                pixels[py * size + px] = new Color32(255, 255, 255, (byte)(a8 * 255f));
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.SetPixels32(pixels);
        texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static float Band(float value, float centre, float halfWidth)
    {
        float d = Mathf.Abs(value - centre);
        return Mathf.Clamp01(1f - (d - halfWidth) / 0.002f);
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        float d = (p - (a + ab * t)).magnitude;
        return Mathf.Clamp01(1f - (d - halfWidth) / 0.002f);
    }

    private static Material TransparentUnlit(string name, Texture texture, Color color)
    {
        Material material = Load(name);
        material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent - 10;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material OpaqueUnlit(string name, Texture texture, Color color)
    {
        Material material = Load(name);
        material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ billboards

    private static readonly (string title, string subtitle, Color top, Color bottom, Color ink, int motif)[] PosterDesigns =
    {
        ("EL RITUAL", "ESTA NOCHE · 23H", new Color(0.32f, 0.08f, 0.38f), new Color(0.75f, 0.12f, 0.16f), new Color(1f, 0.92f, 0.75f), 0),
        ("CAFÉ MEDIANOCHE", "ABIERTO HASTA EL AMANECER", new Color(0.04f, 0.3f, 0.32f), new Color(0.02f, 0.1f, 0.14f), new Color(0.95f, 0.85f, 0.6f), 1),
        ("ALMAS & CÍA.", "PRÉSTAMOS SIN PREGUNTAS", new Color(0.85f, 0.62f, 0.12f), new Color(0.6f, 0.38f, 0.06f), new Color(0.08f, 0.05f, 0.04f), 2),
        ("TEATRO LUNA", "LA VIDENTE · FUNCIÓN FINAL", new Color(0.45f, 0.03f, 0.05f), new Color(0.12f, 0.01f, 0.02f), new Color(1f, 0.85f, 0.45f), 3),
        ("RADIO ÉTER 1440", "LA VOZ DE LA CIUDAD", new Color(0.1f, 0.12f, 0.35f), new Color(0.35f, 0.5f, 0.75f), new Color(1f, 1f, 1f), 1),
    };

    private static void Posters(Transform root, List<(Vector3 position, Quaternion rotation, int design)> posters)
    {
        Transform parent = new GameObject("Posters").transform;
        parent.SetParent(root, false);

        foreach (var (position, rotation, design) in posters)
        {
            var d = PosterDesigns[design];
            Material material = OpaqueUnlit($"Map_Poster_{design}", PosterTexture(design), new Color(0.9f, 0.9f, 0.9f));

            // The frame's front face sits at z -0.01 of the prop; quads face -Z, so they are turned round.
            Quaternion back = rotation * Quaternion.Euler(0f, 180f, 0f);
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Poster_" + d.title;
            quad.transform.SetParent(parent, false);
            quad.transform.SetPositionAndRotation(position + rotation * new Vector3(0f, 5.5f, 0.01f), back);
            quad.transform.localScale = new Vector3(6f, 3f, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            AddText(parent, d.title, position + rotation * new Vector3(-0.2f, 5.85f, 0.04f), back, new Vector2(5f, 1.2f), 6f, d.ink, FontStyles.Bold);
            AddText(parent, d.subtitle, position + rotation * new Vector3(-0.2f, 4.75f, 0.04f), back, new Vector2(5f, 0.5f), 2.4f, d.ink, FontStyles.Normal);
        }
    }

    private static void AddText(Transform parent, string text, Vector3 position, Quaternion rotation, Vector2 box, float maxSize, Color color, FontStyles style)
    {
        var go = new GameObject("Text_" + text);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.5f;
        tmp.fontSizeMax = maxSize;
        tmp.characterSpacing = 3f;
        tmp.color = color;
        tmp.rectTransform.sizeDelta = box;
    }

    /// <summary>Background art for a poster (the words are TextMeshPro on top): gradient plus a motif.</summary>
    private static Texture2D PosterTexture(int design)
    {
        string path = $"{StreetTextureDir}/Poster_{design}.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (existing != null)
        {
            return existing;
        }

        var d = PosterDesigns[design];
        const int w = 512, h = 256;
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                Color c = Color.Lerp(d.bottom, d.top, v);
                Vector2 p = new Vector2((u - 0.5f) * 2f, v - 0.5f);

                switch (d.motif)
                {
                    case 0: // a big moon low on the right with a halo
                        float m = (new Vector2(u, v * 0.5f) - new Vector2(0.8f, 0.2f)).magnitude;
                        if (m < 0.13f) c = Color.Lerp(c, new Color(1f, 0.9f, 0.7f), 0.85f);
                        else c += new Color(0.25f, 0.12f, 0.08f) * Mathf.Clamp01(1f - (m - 0.13f) * 5f);
                        break;
                    case 1: // horizontal bands, art-deco
                        if (Mathf.Repeat(v * 14f, 1f) < 0.12f && (u < 0.08f || u > 0.92f)) c = Color.Lerp(c, d.ink, 0.6f);
                        if (v < 0.1f || v > 0.92f) c = Color.Lerp(c, d.ink, 0.25f);
                        break;
                    case 2: // black diagonal hazard stripes at the ends
                        if ((u < 0.12f || u > 0.88f) && Mathf.Repeat((u + v * 0.5f) * 10f, 1f) < 0.5f) c = d.ink;
                        break;
                    case 3: // two spotlight beams from below
                        float beam = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(p.x + 0.55f - p.y * 0.9f) * 4f), Mathf.Clamp01(1f - Mathf.Abs(p.x - 0.55f + p.y * 0.9f) * 4f));
                        c += new Color(0.5f, 0.35f, 0.15f) * beam * (0.5f - v * 0.4f);
                        break;
                }

                // Edge darkening, like paper under lamps.
                float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1f - u) * 8f, Mathf.Min(v, 1f - v) * 6f));
                c *= 0.65f + 0.35f * edge;
                c.a = 1f;
                pixels[y * w + x] = c;
            }
        }

        var texture = new Texture2D(w, h, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ skyline

    /// <summary>
    /// Two rows of tall towers beyond the map on all four sides (the map is about ±60 x ±128 m): the
    /// inner row 25-60 m, the outer 45-110 m, with lit windows from one tiling texture (16 x 16 windows\n    /// over 48 m: a 3 m grid like the map's own facades, about one in five lit).
    /// </summary>
    private static void Skyline(Builder b)
    {
        Material facade = OpaqueUnlit("Map_SkylineFacade", SkylineTexture(), Color.white);
        Material beacon = Plain("Map_Beacon", new Color(1f, 0.1f, 0.05f), 0.3f, new Color(4f, 0.3f, 0.15f));
        var random = new Random(1440);

        void Tower(float x, float z, float width, float depth, float height)
        {
            b.SetCell(new Vector3(x, 0f, z));
            b.Box(facade, new Vector3(x, height / 2f - 2f, z), new Vector3(width, height + 4f, depth), 48f);

            // A setback crown on some, and a red beacon on the tallest.
            if (random.NextDouble() < 0.45)
            {
                float crown = height * (0.12f + (float)random.NextDouble() * 0.12f);
                b.Box(facade, new Vector3(x, height + crown / 2f, z), new Vector3(width * 0.6f, crown, depth * 0.6f), 48f);
                height += crown;
            }

            if (height > 70f)
            {
                b.Box(beacon, new Vector3(x, height + 1.5f, z), new Vector3(0.6f, 3f, 0.6f), 1f);
            }
        }

        float R(float a, float c) => a + (float)random.NextDouble() * (c - a);

        // Along both long sides.
        foreach (int side in new[] { 1, -1 })
        {
            for (float z = -230f; z <= 230f; z += R(16f, 24f))
            {
                Tower(side * R(74f, 88f), z, R(12f, 18f), R(12f, 18f), R(25f, 60f));
                Tower(side * R(104f, 135f), z + R(-6f, 6f), R(16f, 26f), R(16f, 26f), R(45f, 110f));
            }
        }

        // Beyond both ends.
        foreach (int end in new[] { 1, -1 })
        {
            for (float x = -66f; x <= 66f; x += R(16f, 24f))
            {
                Tower(x, end * R(150f, 165f), R(12f, 18f), R(12f, 18f), R(30f, 65f));
                Tower(x + R(-6f, 6f), end * R(185f, 215f), R(16f, 26f), R(16f, 26f), R(50f, 115f));
            }
        }
    }

    private static Texture2D SkylineTexture()
    {
        string path = $"{StreetTextureDir}/SkylineFacade.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (existing != null)
        {
            return existing;
        }

        const int size = 512, cells = 16, cell = size / cells;
        var random = new Random(7);
        var pixels = new Color[size * size];
        var wall = new Color(0.055f, 0.06f, 0.085f);
        var band = new Color(0.075f, 0.08f, 0.11f);

        for (int i = 0; i < pixels.Length; i++) pixels[i] = wall;

        for (int cy = 0; cy < cells; cy++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                double roll = random.NextDouble();
                Color glass = roll < 0.17 ? new Color(1f, 0.74f, 0.42f) * (0.45f + (float)random.NextDouble() * 0.4f)
                    : roll < 0.21 ? new Color(0.55f, 0.75f, 1f) * 0.6f
                    : new Color(0.07f, 0.08f, 0.11f);

                for (int y = 6; y < cell - 6; y++)
                {
                    for (int x = 9; x < cell - 9; x++)
                    {
                        pixels[(cy * cell + y) * size + cx * cell + x] = glass;
                    }
                }

                // Floor band under each row.
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        pixels[(cy * cell + y) * size + cx * cell + x] = band;
                    }
                }
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        texture.Apply();
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
