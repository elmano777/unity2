using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Building blocks shared by the lobby and spirit-vision scene builders: meshes generated in code
/// (boxes with UVs in metres, surfaces of revolution, arched windows), glow and lit materials,
/// rune circles drawn with line renderers, 3D text and particle presets. Generated meshes are saved
/// as assets under <see cref="MeshDir"/> so the scenes keep them.
/// </summary>
public static class DeadlockSceneKit
{
    public const string MeshDir = "Assets/Art/Meshes/Menus";
    public const string MaterialDir = "Assets/Art/Materials/Menus";

    // ---------- Materials ----------

    public static Material Glow(string name, Color color, float radial = 0f, float pulse = 0f)
    {
        Material m = LoadOrCreate(name, "Deadlock/Glow");
        m.SetColor("_Color", color);
        m.SetFloat("_Radial", radial);
        m.SetFloat("_Pulse", pulse);
        EditorUtility.SetDirty(m);
        return m;
    }

    public static Material Shader(string name, string shader)
    {
        return LoadOrCreate(name, shader);
    }

    public static Material Lit(string name, Color color, float smoothness = 0.3f, float metallic = 0f, Texture texture = null, Color? emission = null)
    {
        Material m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        m.SetTexture("_BaseMap", texture);

        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LoadOrCreate(string name, string shaderName)
    {
        EnsureFolder(MaterialDir);
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        UnityEngine.Shader shader = UnityEngine.Shader.Find(shaderName);

        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader)
        {
            m.shader = shader;
        }

        return m;
    }

    // ---------- Objects ----------

    /// <summary>A box of the given size centred at <paramref name="center"/>, UVs in metres / tile.</summary>
    public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material, float tile = 1f, bool collider = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, tile);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;

        if (collider)
        {
            go.AddComponent<BoxCollider>().size = size;
        }

        return go;
    }

    public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        return go;
    }

    public static GameObject MeshObject(string name, Transform parent, Vector3 position, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>Flat quad lying on the floor (facing up), for glow pools and rune discs.</summary>
    public static GameObject FloorDisc(string name, Transform parent, Vector3 position, float diameter, Material material)
    {
        GameObject quad = Primitive(PrimitiveType.Quad, name, parent, position, new Vector3(diameter, diameter, 1f), material);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return quad;
    }

    public static LineRenderer Ring(string name, Transform parent, Vector3 center, float radius, float width, Material material, int segments = 96)
    {
        var points = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            points[i] = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        return Line(name, parent, points, width, material, true);
    }

    /// <summary>Five-pointed star inscribed in a circle, drawn as one closed line (0-2-4-1-3).</summary>
    public static LineRenderer Pentagram(string name, Transform parent, Vector3 center, float radius, float width, Material material, float rotationDegrees = 0f)
    {
        var points = new Vector3[5];

        for (int i = 0; i < 5; i++)
        {
            float a = (90f + rotationDegrees + i * 144f) * Mathf.Deg2Rad;
            points[i] = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        return Line(name, parent, points, width, material, true);
    }

    /// <summary>
    /// Line through <paramref name="points"/>. With <paramref name="lieFlat"/> the points are in the
    /// XZ plane and the ribbon lies on the floor; otherwise they are in the XY plane facing -Z (a sign).
    /// </summary>
    public static LineRenderer Line(string name, Transform parent, Vector3[] points, float width, Material material, bool loop, bool lieFlat = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.widthMultiplier = width;
        line.sharedMaterial = material;
        line.alignment = LineAlignment.TransformZ;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCornerVertices = 2;

        var local = new Vector3[points.Length];

        if (lieFlat)
        {
            // TransformZ makes the ribbon face the object's Z: tip the object over so the ribbon
            // faces up, and rotate the points the opposite way so they keep their floor layout.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < points.Length; i++) local[i] = new Vector3(points[i].x, points[i].z, -points[i].y);
        }
        else
        {
            points.CopyTo(local, 0);
        }

        line.positionCount = local.Length;
        line.SetPositions(local);
        return line;
    }

    public static TextMeshPro Text(string name, Transform parent, Vector3 position, string text, float fontSize, Color color, Vector2 size, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.rectTransform.sizeDelta = size;
        return tmp;
    }

    public static Light PointLight(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        return light;
    }

    /// <summary>Soft glowing dots (Glow material with radial falloff on billboards).</summary>
    public static ParticleSystem Particles(string name, Transform parent, Vector3 position, Material material, Color color,
        bool loop, float rate, int burst, float lifetime, float speed, float size, Vector3 boxOrRadius, bool sphere, float gravity = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = loop;
        main.playOnAwake = loop;
        main.duration = loop ? 5f : 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : new ParticleSystem.Burst[0]);

        var shape = ps.shape;
        shape.shapeType = sphere ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Box;
        if (sphere) shape.radius = boxOrRadius.x; else shape.scale = boxOrRadius;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    // ---------- Meshes ----------

    public static Mesh BoxMesh(Vector3 size, float tile)
    {
        string key = $"Box_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_t{tile:0.##}";
        Mesh cached = LoadMesh(key);
        if (cached != null) return cached;

        Vector3 h = size * 0.5f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        void Face(Vector3 normal, Vector3 right, Vector3 up, float width, float height)
        {
            int start = vertices.Count;
            Vector3 c = Vector3.Scale(normal, h);
            Vector3 r = right * (width * 0.5f);
            Vector3 u = up * (height * 0.5f);
            vertices.Add(c - r - u); vertices.Add(c + r - u); vertices.Add(c + r + u); vertices.Add(c - r + u);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(width / tile, 0));
            uvs.Add(new Vector2(width / tile, height / tile)); uvs.Add(new Vector2(0, height / tile));
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);

        var mesh = new Mesh { name = key };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return SaveMesh(mesh);
    }

    /// <summary>
    /// Surface of revolution around Y from a profile of (radius, height) points, bottom to top.
    /// Used for the pedestal, the candle stands and the seer's robe.
    /// </summary>
    public static Mesh Lathe(string name, Vector2[] profile, int segments = 32)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        int rows = profile.Length;

        for (int s = 0; s <= segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);

            for (int r = 0; r < rows; r++)
            {
                vertices.Add(new Vector3(cos * profile[r].x, profile[r].y, sin * profile[r].x));
                uvs.Add(new Vector2(s / (float)segments, r / (float)(rows - 1)));
            }
        }

        for (int s = 0; s < segments; s++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                int a = s * rows + r, b = (s + 1) * rows + r;
                triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return SaveMesh(mesh);
    }

    /// <summary>Upright window pane: a rectangle with a half-circle on top, facing -Z, uv.y 0..1 bottom to top.</summary>
    public static Mesh Arch(string name, float width, float height, int arcSegments = 16)
    {
        float radius = width * 0.5f;
        float straight = height - radius;
        var outline = new List<Vector3> { new Vector3(-radius, 0f, 0f), new Vector3(radius, 0f, 0f) };

        for (int i = 0; i <= arcSegments; i++)
        {
            float a = i / (float)arcSegments * Mathf.PI;
            outline.Add(new Vector3(Mathf.Cos(a) * radius, straight + Mathf.Sin(a) * radius, 0f));
        }

        // Fan from the centre of the straight part's top edge.
        var vertices = new List<Vector3> { new Vector3(0f, straight * 0.5f, 0f) };
        vertices.AddRange(outline);
        var triangles = new List<int>();

        for (int i = 1; i < vertices.Count; i++)
        {
            int next = i + 1 < vertices.Count ? i + 1 : 1;
            triangles.AddRange(new[] { 0, next, i });
        }

        var uvs = new List<Vector2>();
        foreach (Vector3 v in vertices) uvs.Add(new Vector2(v.x / width + 0.5f, v.y / height));

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return SaveMesh(mesh);
    }

    private static Mesh LoadMesh(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{name}.asset");
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        EnsureFolder(MeshDir);
        string path = $"{MeshDir}/{mesh.name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }

        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    public static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    public static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }

        return null;
    }

    /// <summary>The laser (OVRInputModule) only reaches 3D colliders through a physics raycaster on the rig.</summary>
    public static void EnsurePhysicsRaycaster(OVRCameraRig rig)
    {
        if (rig.GetComponent<UnityEngine.EventSystems.OVRPhysicsRaycaster>() == null)
        {
            rig.gameObject.AddComponent<UnityEngine.EventSystems.OVRPhysicsRaycaster>();
        }
    }
}
