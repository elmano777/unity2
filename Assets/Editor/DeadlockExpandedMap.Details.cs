using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// The parts of the map that are not plain boxes: the sunken street under the plaza with its
/// bridge and stairs, and the dens of the neutral camps.
/// </summary>
/// <remarks>
/// Everything is still merged into the chunk meshes of step 26. The floor is no longer flat:
/// <see cref="GroundHeight"/> is the one description of it, used here, by step 30 and by anything
/// that places props on the street.
/// </remarks>
public static partial class DeadlockExpandedMap
{
    // ------------------------------------------------------------------ the sunken street

    /// <summary>How far the lane drops under the plaza, in metres.</summary>
    public const float PitDepth = 3.5f;

    /// <summary>Half width of the sunken street (world x): the lane's own width.</summary>
    public const float PitHalfWidth = LaneHalfWidth * ScaleX;

    /// <summary>World |z| where the flat bottom ends and where each ramp is back at street level.</summary>
    public const float PitFlatHalf = 11f, PitRampEnd = 27f;

    /// <summary>
    /// The bridge that crosses the sunken street east-west, high over the centre of the map: its deck
    /// is 8 m above the plaza (11.5 m above the sunken street), reached by a long ramp from each side.
    /// The zipline passes under its arch (step 30).
    /// </summary>
    public const float BridgeDeckY = 8f, BridgeHalfDepth = 2.6f, BridgeHalfSpan = 13.5f, BridgeRampRun = 23f;

    // Stairs from the plaza down to the sunken street, against its wall (world metres, +x/-z one; the
    // other is its point reflection).
    private const float StairInnerX = 9.8f, StairBottomZ = 3.2f, StairTopZ = 11f, StairLandingEndZ = 13.2f;

    // The recess in each district's outer row where a neutral camp lives (template coordinates).
    public const float CampStart = -29f, CampEnd = -21f, CampBackX = 36.5f;

    /// <summary>Height of the street at a world position: 0 everywhere but the ramps and the sunken middle of the lane.</summary>
    public static float GroundHeight(float x, float z)
    {
        if (Mathf.Abs(x) > PitHalfWidth)
        {
            return 0f;
        }

        float az = Mathf.Abs(z);
        if (az <= PitFlatHalf) return -PitDepth;
        if (az >= PitRampEnd) return 0f;
        return -PitDepth * (PitRampEnd - az) / (PitRampEnd - PitFlatHalf);
    }

    /// <summary>Where a district's neutral camp stands, in world space, and the way it faces (out of its recess).</summary>
    public static Vector3 CampPosition(int half, int side, out Quaternion facing)
    {
        var f = new Frame(half, side);
        facing = Quaternion.LookRotation(f.N(Vector3.left));
        return f.P((AlleyOuterX + CampBackX) / 2f + 0.4f, 0f, (CampStart + CampEnd) / 2f);
    }

    private struct OrientedBounds
    {
        public Vector3 Center, Size;
        public Quaternion Rotation;
    }

    private static readonly Dictionary<string, List<OrientedBounds>> orientedColliders = new Dictionary<string, List<OrientedBounds>>();

    private static Material campStone, campGlow;

    private static void CreateDetailMaterials()
    {
        campStone = Textured("Map_CampStone", "sandstone_blocks_08", new Color(0.36f, 0.4f, 0.36f), 0.1f);
        campGlow = Unlit("Map_CampGlow", new Color(0.55f, 1f, 0.35f));
        unshadowed.Add(campGlow);
    }

    // ------------------------------------------------------------------ shapes

    /// <summary>A flat four-sided face through any four points, wound so it faces <paramref name="outward"/>.</summary>
    private static void QuadFacing(string chunk, Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float tile = 0f)
    {
        Vector3 n = Vector3.Cross(d - a, b - a).normalized;

        if (Vector3.Dot(n, outward) < 0f)
        {
            Vector3 swap = b;
            b = d;
            d = swap;
            n = -n;
        }

        MeshData m = Data(chunk, material);
        Vector3 u = (b - a).normalized;
        Vector3 v = Vector3.Cross(u, n);
        Vector3[] corners = { a, b, c, d };
        Vector2[] unit = { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        int start = m.Vertices.Count;

        for (int i = 0; i < 4; i++)
        {
            m.Vertices.Add(corners[i]);
            m.Normals.Add(n);
            m.Uvs.Add(tile > 0f ? new Vector2(Vector3.Dot(corners[i], u) / tile, Vector3.Dot(corners[i], v) / tile) : unit[i]);
        }

        m.Triangles.AddRange(new[] { start, start + 3, start + 2, start, start + 2, start + 1 });
    }

    /// <summary>A triangle wound so it faces <paramref name="outward"/>.</summary>
    private static void TriFacing(string chunk, Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float tile = 0f)
    {
        Vector3 n = Vector3.Cross(c - a, b - a).normalized;

        if (Vector3.Dot(n, outward) < 0f)
        {
            Vector3 swap = b;
            b = c;
            c = swap;
            n = -n;
        }

        MeshData m = Data(chunk, material);
        Vector3 u = (b - a).normalized;
        Vector3 v = Vector3.Cross(u, n);
        int start = m.Vertices.Count;

        foreach (Vector3 p in new[] { a, b, c })
        {
            m.Vertices.Add(p);
            m.Normals.Add(n);
            m.Uvs.Add(tile > 0f ? new Vector2(Vector3.Dot(p, u) / tile, Vector3.Dot(p, v) / tile) : new Vector2(Vector3.Dot(p - a, u), Vector3.Dot(p - a, v)));
        }

        m.Triangles.AddRange(new[] { start, start + 2, start + 1 });
    }

    /// <summary>A box at any rotation, merged into the chunk like <see cref="Box(string, Material, Vector3, Vector3, float, bool)"/>.</summary>
    private static void OBox(string chunk, Material material, Vector3 center, Quaternion rotation, Vector3 size, float tile = 0f, bool collider = false)
    {
        MeshData d = Data(chunk, material);
        Vector3 h = size / 2f;
        Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;

        d.Face(center + x * h.x, x, y, h.z, h.y, tile);
        d.Face(center - x * h.x, -x, y, h.z, h.y, tile);
        d.Face(center + z * h.z, z, y, h.x, h.y, tile);
        d.Face(center - z * h.z, -z, y, h.x, h.y, tile);
        d.Face(center + y * h.y, y, z, h.x, h.z, tile);
        d.Face(center - y * h.y, -y, z, h.x, h.z, tile);

        if (collider)
        {
            OrientedCollider(chunk, center, size, rotation);
        }
    }

    /// <summary>A box-shaped collider at any rotation (ramps, stairs, sloped parapets).</summary>
    private static void OrientedCollider(string chunk, Vector3 center, Vector3 size, Quaternion rotation)
    {
        if (!orientedColliders.TryGetValue(chunk, out List<OrientedBounds> list))
        {
            list = new List<OrientedBounds>();
            orientedColliders[chunk] = list;
        }

        list.Add(new OrientedBounds { Center = center, Size = size, Rotation = rotation });
    }

    /// <summary>A box from <paramref name="from"/> to <paramref name="to"/> (a rail, a brace, a ladder), <paramref name="up"/> being the side its thickness is measured on.</summary>
    private static void Beam(string chunk, Material material, Vector3 from, Vector3 to, float width, float thickness, Vector3 up)
    {
        Vector3 d = to - from;
        if (d.sqrMagnitude < 0.0001f) return;
        OBox(chunk, material, (from + to) / 2f, Quaternion.LookRotation(d, up), new Vector3(width, thickness, d.magnitude));
    }

    /// <summary>An upright round column or cone standing on <paramref name="foot"/>, with smooth sides.</summary>
    private static void Cyl(string chunk, Material material, Vector3 foot, float radius, float height, int sides = 10, float topRadius = -1f, float tile = 0f)
    {
        if (topRadius < 0f) topRadius = radius;
        MeshData m = Data(chunk, material);
        float slope = (radius - topRadius) / Mathf.Max(0.001f, height);

        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 n0 = (d0 + Vector3.up * slope).normalized, n1 = (d1 + Vector3.up * slope).normalized;
            int start = m.Vertices.Count;

            m.Vertices.Add(foot + d0 * radius);
            m.Vertices.Add(foot + d1 * radius);
            m.Vertices.Add(foot + d1 * topRadius + Vector3.up * height);
            m.Vertices.Add(foot + d0 * topRadius + Vector3.up * height);
            m.Normals.Add(n0);
            m.Normals.Add(n1);
            m.Normals.Add(n1);
            m.Normals.Add(n0);

            float scale = tile > 0f ? 1f / tile : 0f;
            float u0 = tile > 0f ? a0 * radius * scale : i / (float)sides, u1 = tile > 0f ? a1 * radius * scale : (i + 1) / (float)sides;
            float v1 = tile > 0f ? height * scale : 1f;
            m.Uvs.Add(new Vector2(u0, 0f));
            m.Uvs.Add(new Vector2(u1, 0f));
            m.Uvs.Add(new Vector2(u1, v1));
            m.Uvs.Add(new Vector2(u0, v1));
            m.Triangles.AddRange(new[] { start, start + 3, start + 2, start, start + 2, start + 1 });

            if (topRadius > 0.02f)
            {
                // Cap, as a fan round the axis.
                int cap = m.Vertices.Count;
                Vector3 top = foot + Vector3.up * height;
                m.Vertices.Add(top);
                m.Vertices.Add(top + d1 * topRadius);
                m.Vertices.Add(top + d0 * topRadius);

                for (int k = 0; k < 3; k++)
                {
                    m.Normals.Add(Vector3.up);
                }

                m.Uvs.Add(new Vector2(0.5f, 0.5f));
                m.Uvs.Add(new Vector2(0.5f + d1.x * 0.5f, 0.5f + d1.z * 0.5f));
                m.Uvs.Add(new Vector2(0.5f + d0.x * 0.5f, 0.5f + d0.z * 0.5f));
                m.Triangles.AddRange(new[] { cap, cap + 1, cap + 2 });
            }
        }
    }

    /// <summary>A low-poly globe: lamps, lanterns, finials.</summary>
    private static void Ball(string chunk, Material material, Vector3 center, float radius, int around = 8, int rings = 5, float squash = 1f)
    {
        MeshData m = Data(chunk, material);

        for (int j = 0; j < rings; j++)
        {
            float e0 = Mathf.Lerp(-90f, 90f, j / (float)rings) * Mathf.Deg2Rad, e1 = Mathf.Lerp(-90f, 90f, (j + 1) / (float)rings) * Mathf.Deg2Rad;

            for (int i = 0; i < around; i++)
            {
                float a0 = i / (float)around * Mathf.PI * 2f, a1 = (i + 1) / (float)around * Mathf.PI * 2f;
                Vector3[] n =
                {
                    Dir(a0, e0), Dir(a1, e0), Dir(a1, e1), Dir(a0, e1),
                };
                int start = m.Vertices.Count;

                for (int k = 0; k < 4; k++)
                {
                    m.Vertices.Add(center + new Vector3(n[k].x, n[k].y * squash, n[k].z) * radius);
                    m.Normals.Add(n[k]);
                    m.Uvs.Add(new Vector2(k == 1 || k == 2 ? 1f : 0f, k >= 2 ? 1f : 0f));
                }

                m.Triangles.AddRange(new[] { start, start + 3, start + 2, start, start + 2, start + 1 });
            }
        }
    }

    private static Vector3 Dir(float azimuth, float elevation) =>
        new Vector3(Mathf.Cos(elevation) * Mathf.Cos(azimuth), Mathf.Sin(elevation), Mathf.Cos(elevation) * Mathf.Sin(azimuth));

    /// <summary>A patch of street lying on the ground wherever it is, ramps included.</summary>
    private static void FloorPatch(string chunk, Material material, float x, float z, float sizeX, float sizeZ, float lift, float tile = 0f)
    {
        float x0 = x - sizeX / 2f, x1 = x + sizeX / 2f, z0 = z - sizeZ / 2f, z1 = z + sizeZ / 2f;
        QuadFacing(chunk, material,
            new Vector3(x0, GroundHeight(x0, z0) + lift, z0), new Vector3(x1, GroundHeight(x1, z0) + lift, z0),
            new Vector3(x1, GroundHeight(x1, z1) + lift, z1), new Vector3(x0, GroundHeight(x0, z1) + lift, z1), Vector3.up, tile);
    }

    private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    // ------------------------------------------------------------------ the pit, its bridge and stairs

    /// <summary>
    /// The middle of the lane sinks under the plaza: a ramp down from each side, a flat stretch at
    /// the bottom, and over it a stone bridge from one half of the plaza to the other. From the
    /// bridge the street below can be shot at; under it there is cover from above.
    /// </summary>
    private static void BuildPit()
    {
        const string chunk = "Pit";
        float w = PitHalfWidth, flat = PitFlatHalf, end = PitRampEnd, depth = PitDepth;

        // Floor and ramps. The colliders are slabs a metre thick under each surface.
        FloorPatch(chunk, asphalt, 0f, 0f, w * 2f, flat * 2f, 0f, PavingTile);
        Collider(chunk, V(0f, -depth - 0.5f, 0f), V(w * 2f, 1f, flat * 2f + 0.4f));

        foreach (int s in new[] { -1, 1 })
        {
            float mid = s * (flat + end) / 2f, run = end - flat;
            FloorPatch(chunk, asphalt, 0f, mid, w * 2f, run, 0f, PavingTile);

            Vector3 uphill = V(0f, depth, s * run).normalized;
            Quaternion slope = Quaternion.LookRotation(uphill, Vector3.up);
            Vector3 normal = slope * Vector3.up;
            OrientedCollider(chunk, V(0f, -depth / 2f, mid) - normal * 0.5f, V(w * 2f, 1f, Mathf.Sqrt(run * run + depth * depth) + 0.3f), slope);

            // A drain grate across the foot of each ramp and a painted stop line at its top.
            FloorPatch(chunk, metal, 0f, s * (flat + 0.3f), w * 2f - 1f, 0.5f, 0.012f);
            FloorPatch(chunk, white, 0f, s * (end - 0.8f), w * 2f - 3f, 0.35f, MarkingY);
        }

        // Retaining walls: dark brick with a stone footing, the whole length of the cut.
        foreach (int side in new[] { -1, 1 })
        {
            Box(chunk, brickDark, V(side * (w + 0.5f), -depth / 2f, 0f), V(1f, depth, end * 2f), BrickTile, true);
            Box(chunk, sandstoneDark, V(side * (w - 0.08f), -depth + 0.25f, 0f), V(0.16f, 0.5f, flat * 2f), SandstoneTile, false);

            // Buttresses every few metres break the long wall and give a sliver of cover.
            for (float z = -flat + 1.5f; z <= flat - 1.4f; z += 4.5f)
            {
                if (Mathf.Abs(z) < BridgeHalfDepth + 0.6f) continue;
                bool stairs = side * z < -StairBottomZ + 0.5f; // the wall the stairs climb
                if (stairs) continue;
                QuadFacing(chunk, sandstoneDark, V(side * w, -depth, z - 0.35f), V(side * w, -depth, z + 0.35f),
                    V(side * w, -0.1f, z + 0.35f), V(side * w, -0.1f, z - 0.35f), V(-side, 0f, 0f), SandstoneTile);
                QuadFacing(chunk, sandstoneDark, V(side * (w - 0.7f), -depth, z - 0.35f), V(side * (w - 0.7f), -depth, z + 0.35f),
                    V(side * w, -0.1f, z + 0.35f), V(side * w, -0.1f, z - 0.35f), V(-side, 0.3f, 0f), SandstoneTile);

                foreach (int e in new[] { -1, 1 })
                {
                    TriFacing(chunk, sandstoneDark, V(side * (w - 0.7f), -depth, z + e * 0.35f), V(side * w, -depth, z + e * 0.35f),
                        V(side * w, -0.1f, z + e * 0.35f), V(0f, 0f, e), SandstoneTile);
                }
            }
        }

        BuildPitRailings(chunk);
        BuildBridge(chunk);

        foreach (int s in new[] { 1, -1 })
        {
            BuildPitStairs(chunk, s);
        }

        // Lamps under the bridge and along the walls: the cut is the darkest part of the map.
        foreach (int side in new[] { -1, 1 })
        {
            foreach (float z in new[] { -8f, 8f })
            {
                if (side * z < -StairBottomZ) continue;
                Box(chunk, metal, V(side * (w - 0.2f), -0.9f, z), V(0.4f, 0.08f, 0.08f), 0f, false);
                Ball(chunk, lamp, V(side * (w - 0.45f), -1.05f, z), 0.2f, 6, 4);
            }
        }
    }

    /// <summary>Stone parapets with an iron rail along the open edges of the cut, leaving the gaps for the bridge and the stairs.</summary>
    private static void BuildPitRailings(string chunk)
    {
        float w = PitHalfWidth, plazaEdge = -PlazaEdgeZ * ScaleZ;

        foreach (int side in new[] { -1, 1 })
        {
            // The stairs come up through this side's wall between StairTopZ and StairLandingEndZ, on the side * -z half.
            var spans = new List<Vector2>();
            float gapA = StairTopZ, gapB = StairLandingEndZ;

            // Negative-z half and positive-z half of this wall, as |z| ranges.
            foreach (int half in new[] { -1, 1 })
            {
                bool hasStairs = half == -side;

                if (hasStairs)
                {
                    spans.Add(new Vector2(half * BridgeHalfDepth, half * gapA));
                    spans.Add(new Vector2(half * gapB, half * plazaEdge));
                }
                else
                {
                    spans.Add(new Vector2(half * BridgeHalfDepth, half * plazaEdge));
                }
            }

            foreach (Vector2 span in spans)
            {
                float z0 = Mathf.Min(span.x, span.y), z1 = Mathf.Max(span.x, span.y);
                Parapet(chunk, V(side * (w + 0.25f), 0f, z0), V(side * (w + 0.25f), 0f, z1), 0f);
            }
        }
    }

    /// <summary>A low stone wall with posts and an iron rail from <paramref name="a"/> to <paramref name="b"/> (level, axis-aligned), with its collider.</summary>
    private static void Parapet(string chunk, Vector3 a, Vector3 b, float baseY)
    {
        Vector3 d = b - a;
        float length = d.magnitude;
        if (length < 0.2f) return;
        Vector3 along = d / length, mid = (a + b) / 2f;
        Vector3 axis = Abs(along), across = new Vector3(axis.z, 0f, axis.x);

        Box(chunk, sandstone, mid + Vector3.up * (baseY + 0.3f), axis * length + Vector3.up * 0.6f + across * 0.45f, SandstoneTile, false);
        Box(chunk, sandstoneDark, mid + Vector3.up * (baseY + 0.65f), axis * length + Vector3.up * 0.1f + across * 0.6f, SandstoneTile, false);
        Box(chunk, metal, mid + Vector3.up * (baseY + 1.15f), axis * length + Vector3.up * 0.06f + across * 0.06f, 0f, false);

        int posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.2f) + 1);

        for (int i = 0; i < posts; i++)
        {
            Vector3 p = a + along * (length * i / (posts - 1));
            Box(chunk, metal, p + Vector3.up * (baseY + 0.92f), new Vector3(0.07f, 0.46f, 0.07f), 0f, false);
        }

        Collider(chunk, mid + Vector3.up * (baseY + 0.9f), axis * length + Vector3.up * 1.8f + across * 0.5f);
    }

    /// <summary>
    /// The bridge over the cut: a stone deck on a shallow arch, a ramp up from the plaza at each end,
    /// parapets with a lamp at every corner. It is the high ground of the middle of the map.
    /// </summary>
    private static void BuildBridge(string chunk)
    {
        float w = PitHalfWidth, span = BridgeHalfSpan, depth = BridgeHalfDepth, top = BridgeDeckY;
        const float slab = 0.5f;

        // Deck (walkable: its collider is the floor up there) and the abutments it rests on.
        Box(chunk, sandstone, V(0f, top - slab / 2f, 0f), V(span * 2f, slab, depth * 2f), SandstoneTile, true);
        FloorPatchAt(chunk, paving, 0f, 0f, span * 2f - 0.2f, depth * 2f - 1f, top + 0.012f, PavingTile);

        foreach (int side in new[] { -1, 1 })
        {
            Box(chunk, sandstoneDark, V(side * (w + (span - w) / 2f + 0.05f), (top - slab) / 2f, 0f), V(span - w + 0.1f, top - slab, depth * 2f), SandstoneTile, true);

            // Approach ramp: a sloped slab with a stone skirt down to the plaza on both faces.
            float x0 = side * span, x1 = side * (span + BridgeRampRun);
            Vector3 high = V(x0, top, 0f), low = V(x1, 0f, 0f);
            Quaternion slope = Quaternion.LookRotation(high - low, Vector3.up);
            Vector3 normal = slope * Vector3.up;
            float length = (high - low).magnitude;
            OBox(chunk, sandstone, (high + low) / 2f - normal * 0.2f, slope, V(depth * 2f, 0.4f, length), SandstoneTile);
            OrientedCollider(chunk, (high + low) / 2f - normal * 0.5f, V(depth * 2f, 1f, length + 0.4f), slope);
            QuadFacing(chunk, paving, V(x0, top + 0.012f, -depth + 0.5f), V(x0, top + 0.012f, depth - 0.5f),
                V(x1, 0.014f, depth - 0.5f), V(x1, 0.014f, -depth + 0.5f), Vector3.up, PavingTile);

            // The space under the ramp is closed: stepped walls along both skirts, each no taller than
            // the ramp over it at its high end (the parapet hides what pokes above at the low end).
            const int wallSteps = 10;

            for (int k = 0; k < wallSteps; k++)
            {
                float a0 = Mathf.Lerp(x0, x1, k / (float)wallSteps), a1 = Mathf.Lerp(x0, x1, (k + 1) / (float)wallSteps);
                float h = top * (BridgeRampRun - Mathf.Abs(Mathf.Min(Mathf.Abs(a0), Mathf.Abs(a1)) - span)) / BridgeRampRun;

                foreach (int face in new[] { -1, 1 })
                {
                    Collider(chunk, V((a0 + a1) / 2f, h / 2f, face * (depth - 0.15f)), V(Mathf.Abs(a1 - a0) + 0.02f, h, 0.3f));
                }
            }

            foreach (int face in new[] { -1, 1 })
            {
                float z = face * depth;
                TriFacing(chunk, sandstoneDark, V(x0, 0f, z), V(x1, 0f, z), V(x0, top, z), V(0f, 0f, face), SandstoneTile);

                // Sloped parapet along the ramp, and its collider.
                Vector3 a = V(x0, top, face * (depth - 0.2f)), b = V(x1, 0f, face * (depth - 0.2f));
                Beam(chunk, sandstone, a + Vector3.up * 0.35f, b + Vector3.up * 0.35f, 0.4f, 0.7f, Vector3.up);
                Beam(chunk, sandstoneDark, a + Vector3.up * 0.75f, b + Vector3.up * 0.75f, 0.55f, 0.1f, Vector3.up);
                Beam(chunk, metal, a + Vector3.up * 1.15f, b + Vector3.up * 1.15f, 0.06f, 0.06f, Vector3.up);
                OrientedCollider(chunk, (a + b) / 2f + Vector3.up * 0.9f, V(0.45f, 1.9f, length), Quaternion.LookRotation(a - b, Vector3.up));

                // Newel post with a globe lamp where the ramp meets the plaza and where it meets the deck.
                foreach (Vector3 foot in new[] { V(x1, 0f, face * (depth - 0.2f)), V(x0, top, face * (depth - 0.2f)) })
                {
                    Cyl(chunk, sandstoneDark, foot, 0.32f, 1.25f, 8, 0.26f, SandstoneTile);
                    Cyl(chunk, metal, foot + Vector3.up * 1.25f, 0.06f, 1.5f, 6);
                    Ball(chunk, lamp, foot + Vector3.up * 2.9f, 0.26f);
                    Cyl(chunk, metal, foot + Vector3.up * 3.1f, 0.2f, 0.12f, 6, 0.02f);
                }
            }
        }

        // Parapets along the deck.
        foreach (int face in new[] { -1, 1 })
        {
            Parapet(chunk, V(-span, 0f, face * (depth - 0.2f)), V(span, 0f, face * (depth - 0.2f)), top);
        }

        // The arch: a ring of voussoirs under the deck and the spandrel wall between them on each face.
        // It springs from the top of the cut's walls, so the whole street stays open underneath.
        const float rise = 6f, springY = 0.6f, ring = 0.45f;
        float radius = (w * w + rise * rise) / (2f * rise);
        float centreY = springY + rise - radius;
        const int segments = 10;
        Vector3 Arc(float x) => V(x, centreY + Mathf.Sqrt(radius * radius - x * x), 0f);

        for (int i = 0; i < segments; i++)
        {
            float xa = Mathf.Lerp(-w, w, i / (float)segments), xb = Mathf.Lerp(-w, w, (i + 1) / (float)segments);
            Vector3 a = Arc(xa), b = Arc(xb);
            Vector3 up = Vector3.Cross(b - a, Vector3.forward).normalized;
            if (up.y < 0f) up = -up;
            OBox(chunk, i % 2 == 0 ? sandstoneDark : sandstone, (a + b) / 2f - up * (ring / 2f), Quaternion.LookRotation(b - a, up),
                V(depth * 2f + 0.3f, ring, (b - a).magnitude + 0.02f), SandstoneTile);

            foreach (int face in new[] { -1, 1 })
            {
                Vector3 off = Vector3.forward * (face * depth);
                QuadFacing(chunk, sandstone, a + off, b + off, V(xb, top - slab, face * depth), V(xa, top - slab, face * depth), V(0f, 0f, face), SandstoneTile);
            }
        }

        // Keystone and a team-neutral lantern hanging under the crown.
        foreach (int face in new[] { -1, 1 })
        {
            Box(chunk, sandstoneDark, V(0f, springY + rise - 0.05f, face * (depth + 0.12f)), V(0.9f, 1.1f, 0.3f), SandstoneTile, false);
        }

        // Two lanterns hanging from the soffit, either side of the zipline that runs under the crown.
        foreach (int e in new[] { -1, 1 })
        {
            float lx = e * 6f, soffit = Arc(lx).y - ring;
            Cyl(chunk, metal, V(lx, soffit - 0.75f, 0f), 0.03f, 0.75f, 5);
            Ball(chunk, lamp, V(lx, soffit - 0.95f, 0f), 0.24f);
        }
    }

    /// <summary>A level patch at a given height (the deck's paving), unlike <see cref="FloorPatch"/> which follows the street.</summary>
    private static void FloorPatchAt(string chunk, Material material, float x, float z, float sizeX, float sizeZ, float y, float tile)
    {
        Quad(chunk, material, V(x, y, z), Vector3.up, sizeX, sizeZ, tile);
    }

    /// <summary>
    /// Stairs against the wall of the cut, from its floor up to the plaza: the way in and out of
    /// the sunken street without walking the length of a ramp. <paramref name="s"/> = 1 builds the
    /// one on the +x wall climbing towards -z; -1 its point reflection.
    /// </summary>
    private static void BuildPitStairs(string chunk, int s)
    {
        float w = PitHalfWidth, inner = StairInnerX, depth = PitDepth;
        float width = w - inner, run = StairTopZ - StairBottomZ;
        const int steps = 14;
        float rise = depth / steps, tread = run / steps;
        float xMid = s * (inner + w) / 2f;

        for (int i = 0; i < steps; i++)
        {
            float z = -s * (StairBottomZ + tread * (i + 0.5f));
            float h = rise * (i + 1);
            Box(chunk, i % 2 == 0 ? sandstone : sandstoneDark, V(xMid, -depth + h / 2f, z), V(width, h, tread + 0.01f), SandstoneTile, false);
        }

        // Walked as a ramp: one sloped slab, deep enough that its side closes the stairs' flank.
        Vector3 foot = V(xMid, -depth, -s * StairBottomZ), head = V(xMid, 0f, -s * StairTopZ);
        Quaternion slope = Quaternion.LookRotation(head - foot, Vector3.up);
        Vector3 normal = slope * Vector3.up;
        float length = (head - foot).magnitude;
        OrientedCollider(chunk, (foot + head) / 2f - normal * 2f, V(width, 4f, length), slope);

        // Landing at plaza level, standing on the ramp below, open towards the plaza.
        float landingMid = -s * (StairTopZ + StairLandingEndZ) / 2f, landing = StairLandingEndZ - StairTopZ;
        Box(chunk, sandstone, V(xMid, -depth / 2f, landingMid), V(width, depth, landing), SandstoneTile, true);

        // Iron handrail down the open side, and rails round the landing's two open edges.
        float railX = s * (inner + 0.06f);
        Vector3 a = V(railX, -depth, -s * StairBottomZ), b = V(railX, 0f, -s * StairTopZ);
        Beam(chunk, metal, a + Vector3.up * 1.05f, b + Vector3.up * 1.05f, 0.06f, 0.06f, Vector3.up);
        Beam(chunk, metal, a + Vector3.up * 0.55f, b + Vector3.up * 0.55f, 0.04f, 0.04f, Vector3.up);

        for (int i = 0; i <= 4; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / 4f);
            Box(chunk, metal, p + Vector3.up * 0.53f, V(0.06f, 1.06f, 0.06f), 0f, false);
        }

        OrientedCollider(chunk, (a + b) / 2f + Vector3.up * 0.9f, V(0.2f, 1.9f, length), slope);
        Parapet(chunk, V(s * (inner + 0.25f), 0f, -s * StairTopZ), V(s * (inner + 0.25f), 0f, -s * StairLandingEndZ), 0f);
        Parapet(chunk, V(s * (inner + 0.25f), 0f, -s * (StairLandingEndZ - 0.25f)), V(s * w, 0f, -s * (StairLandingEndZ - 0.25f)), 0f);

        // A lamp over the foot of the stairs so they can be found from below.
        Box(chunk, metal, V(s * (w - 0.2f), -1.2f, -s * (StairBottomZ - 0.6f)), V(0.4f, 0.08f, 0.08f), 0f, false);
        Ball(chunk, lamp, V(s * (w - 0.45f), -1.35f, -s * (StairBottomZ - 0.6f)), 0.2f, 6, 4);
    }

    // ------------------------------------------------------------------ neutral camps

    /// <summary>
    /// The den of a neutral camp in its recess: a worn ritual ring on the floor, standing stones
    /// round the back and two green lanterns at the mouth, so it is spotted from the alley.
    /// </summary>
    private static void BuildCampDen(string chunk, Frame f)
    {
        Vector3 c = CampPosition(f.Half, f.Side, out Quaternion facing);
        Vector3 fwd = facing * Vector3.forward, right = facing * Vector3.right;

        // Floor: a darker court with a broken glowing ring.
        Vector3 mouth = f.P(AlleyOuterX, 0f, (CampStart + CampEnd) / 2f);
        Vector3 court = (c + mouth) / 2f + fwd * 0.2f;
        Quad(chunk, campStone, V(court.x, FloorY, court.z), Vector3.up, (CampBackX - AlleyOuterX) * ScaleX, (CampEnd - CampStart) * ScaleZ, SandstoneTile);

        const int segments = 18;
        const float ringRadius = 3.6f;

        for (int i = 0; i < segments; i++)
        {
            if (i % 6 == 5) continue;
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 0.8f) / segments * Mathf.PI * 2f;
            Vector3 d0 = V(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = V(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            QuadFacing(chunk, campGlow, c + d0 * (ringRadius - 0.09f) + Vector3.up * MarkingY, c + d1 * (ringRadius - 0.09f) + Vector3.up * MarkingY,
                c + d1 * (ringRadius + 0.09f) + Vector3.up * MarkingY, c + d0 * (ringRadius + 0.09f) + Vector3.up * MarkingY, Vector3.up);
        }

        // Standing stones round the back half, leaning a little, each with a glowing mark.
        var rng = new Random(f.Half * 53 + f.Side * 17 + 400);

        for (int i = 0; i < 5; i++)
        {
            float angle = Mathf.Lerp(-78f, 78f, i / 4f) * Mathf.Deg2Rad;
            Vector3 p = c - fwd * (Mathf.Cos(angle) * 3.5f) + right * (Mathf.Sin(angle) * 4.2f);
            float h = 2.2f + (float)rng.NextDouble() * 1.6f;
            Quaternion lean = Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 10f - 5f);
            OBox(chunk, campStone, p + Vector3.up * (h / 2f - 0.1f), lean, V(0.9f, h, 0.55f), SandstoneTile);
            Ball(chunk, campGlow, p + Vector3.up * (h * 0.62f) + (c - p).normalized * 0.3f, 0.09f, 6, 3);
        }

        // Lanterns on iron posts at the mouth of the recess.
        foreach (int e in new[] { -1, 1 })
        {
            Vector3 foot = mouth + right * (e * ((CampEnd - CampStart) * ScaleZ / 2f - 0.5f)) - fwd * 0.6f;
            Cyl(chunk, metal, foot, 0.09f, 3.1f, 6, 0.06f);
            Cyl(chunk, metal, foot, 0.22f, 0.25f, 6, 0.12f);
            Beam(chunk, metal, foot + Vector3.up * 3.05f, foot + Vector3.up * 3.05f + fwd * 0.7f, 0.05f, 0.05f, Vector3.up);
            Cyl(chunk, metal, foot + fwd * 0.7f + Vector3.up * 2.92f, 0.16f, 0.1f, 6, 0.04f);
            Ball(chunk, campGlow, foot + fwd * 0.7f + Vector3.up * 2.7f, 0.2f, 6, 4, 1.3f);
        }

        // A brazier in front of the stones.
        Vector3 fire = c - fwd * 1.9f;
        Cyl(chunk, metal, fire, 0.12f, 0.8f, 6);
        Cyl(chunk, metal, fire + Vector3.up * 0.8f, 0.2f, 0.3f, 8, 0.55f);
        Ball(chunk, campGlow, fire + Vector3.up * 1.2f, 0.3f, 6, 4, 1.4f);
    }
}
