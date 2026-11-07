using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws the shop's textures from code so they need no external art: the paper of the board, one
/// icon per item (a glyph on its category's colour), the category tabs and the price tag. The
/// glyphs are our own simple shapes, not the original game's icons. Shapes are signed distance
/// functions in a 0..1 square (y up), so edges come out anti-aliased at any size.
/// </summary>
public static class DeadlockShopIcons
{
    public const string TextureDir = "Assets/Art/Textures/Shop";

    public static readonly Color WeaponColor = new Color(0.86f, 0.52f, 0.16f);
    public static readonly Color VitalityColor = new Color(0.42f, 0.68f, 0.3f);
    public static readonly Color SpiritColor = new Color(0.58f, 0.36f, 0.78f);

    private static readonly Color GlyphColor = new Color(1f, 0.96f, 0.86f);

    public static Color CategoryColor(ShopCategory category)
    {
        switch (category)
        {
            case ShopCategory.Vitality: return VitalityColor;
            case ShopCategory.Spirit: return SpiritColor;
            default: return WeaponColor;
        }
    }

    // ------------------------------------------------------------------ textures

    /// <summary>Old cream paper: mottled, darker towards the edges, a few faint stains.</summary>
    public static Texture2D Paper()
    {
        const int n = 512;
        var px = new Color[n * n];
        var rng = new System.Random(7);
        var stains = new Vector3[9];

        for (int i = 0; i < stains.Length; i++)
        {
            stains[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), 0.04f + 0.1f * (float)rng.NextDouble());
        }

        Color baseColor = new Color(0.87f, 0.8f, 0.64f);

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float noise = 0f, amp = 0.5f, freq = 6f;

                for (int o = 0; o < 4; o++)
                {
                    noise += amp * (Mathf.PerlinNoise(u * freq + 13.1f, v * freq + 7.7f) - 0.5f);
                    amp *= 0.5f;
                    freq *= 2.1f;
                }

                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float vignette = Mathf.SmoothStep(0f, 0.12f, edge);
                float stain = 0f;

                foreach (Vector3 s in stains)
                {
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(s.x, s.y));
                    stain += 0.05f * Mathf.Clamp01(1f - d / s.z);
                }

                float shade = 1f + noise * 0.14f - (1f - vignette) * 0.2f - stain;
                Color c = baseColor * shade;
                c.r += stain * 0.08f;
                c.a = 1f;
                px[y * n + x] = c;
            }
        }

        return Save("Shop_Paper", n, px, sprite: false);
    }

    /// <summary>Item icon: the glyph on a rounded square of the category colour.</summary>
    public static Sprite ItemIcon(string name, ShopCategory category, Func<Vector2, float> glyph)
    {
        const int n = 256;
        var px = new Color[n * n];
        Color color = CategoryColor(category);
        Color dark = color * 0.35f;
        dark.a = 1f;

        Fill(px, n, p => Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0.1f), dark);
        FillGradient(px, n, p => Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.455f, 0.455f), 0.08f), color * 1.12f, color * 0.62f);
        Fill(px, n, p => glyph(p - new Vector2(0.018f, -0.022f)), new Color(0f, 0f, 0f, 0.45f));
        Fill(px, n, glyph, GlyphColor);

        Save(name, n, px, sprite: true);
        return AssetDatabase.LoadAssetAtPath<Sprite>($"{TextureDir}/{name}.png");
    }

    /// <summary>Category tab: the glyph on the category colour, with a dark rim.</summary>
    public static Texture2D Tab(string name, ShopCategory category, Func<Vector2, float> glyph)
    {
        const int n = 128;
        var px = new Color[n * n];
        Color color = CategoryColor(category);
        Color rim = color * 0.4f;
        rim.a = 1f;

        Fill(px, n, p => Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0f), rim);
        FillGradient(px, n, p => Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.44f, 0.44f), 0.06f), color * 1.15f, color * 0.75f);
        Fill(px, n, p => glyph(p - new Vector2(0.02f, -0.025f)), new Color(0f, 0f, 0f, 0.4f));
        Fill(px, n, glyph, GlyphColor);
        return Save(name, n, px, sprite: false);
    }

    /// <summary>Dark price tag with a soul on its left; the number is text placed over the right part.</summary>
    public static Texture2D PriceTag()
    {
        const int w = 256, h = 64;
        var px = new Color[w * h];
        Color back = new Color(0.12f, 0.09f, 0.07f);
        Color gold = new Color(0.86f, 0.68f, 0.36f);

        FillRect(px, w, h, (u, v) => Box(new Vector2(u * 4f, v), new Vector2(2f, 0.5f), new Vector2(2f, 0.5f), 0.12f), gold);
        FillRect(px, w, h, (u, v) => Box(new Vector2(u * 4f, v), new Vector2(2f, 0.5f), new Vector2(1.94f, 0.42f), 0.1f), back);
        FillRect(px, w, h, (u, v) => Soul(new Vector2(u * 4f - 0.02f, v)), new Color(0.55f, 0.95f, 1f));
        return SaveRect("Shop_PriceTag", w, h, px);
    }

    // ------------------------------------------------------------------ glyphs

    /// <summary>Three cartridges standing side by side, the middle one taller: more damage per bullet.</summary>
    public static float Bullets(Vector2 p)
    {
        float d = float.MaxValue;
        float[] xs = { 0.3f, 0.5f, 0.7f };
        float[] tops = { 0.64f, 0.72f, 0.64f };

        for (int i = 0; i < 3; i++)
        {
            float body = Box(p, new Vector2(xs[i], 0.4f), new Vector2(0.065f, tops[i] - 0.4f - 0.06f), 0.01f);
            float tip = Circle(p, new Vector2(xs[i], tops[i] - 0.06f), 0.065f);
            float rim = Box(p, new Vector2(xs[i], 0.24f), new Vector2(0.08f, 0.025f), 0.005f);
            d = Mathf.Min(d, Mathf.Min(Mathf.Min(body, tip), rim));
        }

        return d;
    }

    /// <summary>A bullet flying right with speed streaks behind it: faster fire.</summary>
    public static float FastBullet(Vector2 p)
    {
        float body = Box(p, new Vector2(0.55f, 0.5f), new Vector2(0.14f, 0.075f), 0.01f);
        float tip = Circle(p, new Vector2(0.69f, 0.5f), 0.075f);
        float rim = Box(p, new Vector2(0.4f, 0.5f), new Vector2(0.025f, 0.09f), 0.005f);
        float streaks = Mathf.Min(Mathf.Min(
            Segment(p, new Vector2(0.14f, 0.62f), new Vector2(0.33f, 0.62f), 0.022f),
            Segment(p, new Vector2(0.1f, 0.5f), new Vector2(0.33f, 0.5f), 0.022f)),
            Segment(p, new Vector2(0.14f, 0.38f), new Vector2(0.33f, 0.38f), 0.022f));
        return Mathf.Min(Mathf.Min(body, tip), Mathf.Min(rim, streaks));
    }

    /// <summary>A boot with motion lines: faster on foot.</summary>
    public static float Boot(Vector2 p)
    {
        float boot = Polygon(p, new[]
        {
            new Vector2(0.42f, 0.8f), new Vector2(0.62f, 0.8f), new Vector2(0.62f, 0.46f),
            new Vector2(0.8f, 0.4f), new Vector2(0.84f, 0.3f), new Vector2(0.84f, 0.22f),
            new Vector2(0.4f, 0.22f), new Vector2(0.4f, 0.5f),
        }) - 0.015f;
        float cuff = Box(p, new Vector2(0.52f, 0.78f), new Vector2(0.12f, 0.035f), 0.01f);
        float lines = Mathf.Min(Mathf.Min(
            Segment(p, new Vector2(0.14f, 0.62f), new Vector2(0.3f, 0.62f), 0.022f),
            Segment(p, new Vector2(0.1f, 0.48f), new Vector2(0.3f, 0.48f), 0.022f)),
            Segment(p, new Vector2(0.14f, 0.34f), new Vector2(0.3f, 0.34f), 0.022f));
        return Mathf.Min(Mathf.Min(boot, cuff), lines);
    }

    /// <summary>A heart with a plus cut into it: more health.</summary>
    public static float HeartPlus(Vector2 p)
    {
        float heart = Mathf.Min(
            Mathf.Min(Circle(p, new Vector2(0.37f, 0.6f), 0.15f), Circle(p, new Vector2(0.63f, 0.6f), 0.15f)),
            Polygon(p, new[] { new Vector2(0.235f, 0.56f), new Vector2(0.5f, 0.2f), new Vector2(0.765f, 0.56f), new Vector2(0.5f, 0.62f) }));
        float plus = Mathf.Min(
            Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.035f, 0.12f), 0.005f),
            Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.12f, 0.035f), 0.005f));
        return Mathf.Max(heart, -plus);
    }

    /// <summary>A shield with a band down the middle: less damage taken.</summary>
    public static float Shield(Vector2 p)
    {
        Vector2[] outline =
        {
            new Vector2(0.26f, 0.79f), new Vector2(0.5f, 0.84f), new Vector2(0.74f, 0.79f), new Vector2(0.73f, 0.52f),
            new Vector2(0.65f, 0.34f), new Vector2(0.5f, 0.18f), new Vector2(0.35f, 0.34f), new Vector2(0.27f, 0.52f),
        };
        float shield = Polygon(p, outline) - 0.01f;
        // A thin groove down the middle, stopping short of the rim.
        float band = Box(p, new Vector2(0.5f, 0.52f), new Vector2(0.025f, 0.4f), 0f);
        float inner = Polygon(p, outline) + 0.07f;
        return Mathf.Max(shield, -Mathf.Max(band, inner));
    }

    /// <summary>Weapon tab: a crosshair.</summary>
    public static float Crosshair(Vector2 p)
    {
        Vector2 c = new Vector2(0.5f, 0.5f);
        float ring = Mathf.Abs(Circle(p, c, 0.24f)) - 0.035f;
        float ticks = Mathf.Min(
            Mathf.Min(Segment(p, new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.32f), 0.03f), Segment(p, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.88f), 0.03f)),
            Mathf.Min(Segment(p, new Vector2(0.12f, 0.5f), new Vector2(0.32f, 0.5f), 0.03f), Segment(p, new Vector2(0.68f, 0.5f), new Vector2(0.88f, 0.5f), 0.03f)));
        return Mathf.Min(Mathf.Min(ring, ticks), Circle(p, c, 0.05f));
    }

    /// <summary>Vitality tab: a thick plus.</summary>
    public static float Plus(Vector2 p)
    {
        return Mathf.Min(
            Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.09f, 0.3f), 0.03f),
            Box(p, new Vector2(0.5f, 0.5f), new Vector2(0.3f, 0.09f), 0.03f));
    }

    /// <summary>Spirit tab: a four-pointed star.</summary>
    public static float Star(Vector2 p)
    {
        return Polygon(p, new[]
        {
            new Vector2(0.5f, 0.88f), new Vector2(0.58f, 0.58f), new Vector2(0.88f, 0.5f), new Vector2(0.58f, 0.42f),
            new Vector2(0.5f, 0.12f), new Vector2(0.42f, 0.42f), new Vector2(0.12f, 0.5f), new Vector2(0.42f, 0.58f),
        });
    }

    /// <summary>A soul: a diamond with a smaller one inside, in the left quarter of a 4:1 tag.</summary>
    private static float Soul(Vector2 p)
    {
        Vector2 c = new Vector2(0.42f, 0.5f);
        Vector2 q = p - c;
        float d = (Mathf.Abs(q.x) + Mathf.Abs(q.y)) / 1.4142f;
        float outline = Mathf.Abs(d - 0.2f) - 0.025f;
        float core = d - 0.09f;
        return Mathf.Min(outline, core);
    }

    // ------------------------------------------------------------------ distance functions

    public static float Circle(Vector2 p, Vector2 c, float r) => Vector2.Distance(p, c) - r;

    public static float Box(Vector2 p, Vector2 c, Vector2 half, float round)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(round, round);
        Vector2 outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
    }

    public static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - r;
    }

    /// <summary>Signed distance to a simple polygon (negative inside).</summary>
    public static float Polygon(Vector2 p, Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;

        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i];
            Vector2 w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));

            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;

            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
            {
                s = -s;
            }
        }

        return s * Mathf.Sqrt(d);
    }

    // ------------------------------------------------------------------ painting

    private static void Fill(Color[] px, int n, Func<Vector2, float> sdf, Color color)
    {
        float aa = 1.5f / n;

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                float cover = Mathf.Clamp01(0.5f - sdf(p) / aa);
                Blend(ref px[y * n + x], color, cover);
            }
        }
    }

    private static void FillGradient(Color[] px, int n, Func<Vector2, float> sdf, Color top, Color bottom)
    {
        float aa = 1.5f / n;

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                float cover = Mathf.Clamp01(0.5f - sdf(p) / aa);
                Color c = Color.Lerp(bottom, top, p.y);
                c.a = 1f;
                Blend(ref px[y * n + x], c, cover);
            }
        }
    }

    /// <summary>Fill for a non-square texture: the function gets u in 0..1 across and v in 0..1 up.</summary>
    private static void FillRect(Color[] px, int w, int h, Func<float, float, float> sdf, Color color)
    {
        float aa = 1.5f / h;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float cover = Mathf.Clamp01(0.5f - sdf((x + 0.5f) / w, (y + 0.5f) / h) / aa);
                Blend(ref px[y * w + x], color, cover);
            }
        }
    }

    private static void Blend(ref Color dst, Color src, float cover)
    {
        float a = cover * src.a;

        if (a <= 0f)
        {
            return;
        }

        float outA = a + dst.a * (1f - a);
        Color rgb = (src * a + dst * dst.a * (1f - a)) / Mathf.Max(outA, 1e-5f);
        rgb.a = outA;
        dst = rgb;
    }

    private static Texture2D Save(string name, int n, Color[] px, bool sprite) => SaveRect(name, n, n, px, sprite);

    private static Texture2D SaveRect(string name, int w, int h, Color[] px, bool sprite = false)
    {
        DeadlockSceneKit.EnsureFolder(TextureDir);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();

        string path = $"{TextureDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        importer.spriteImportMode = sprite ? SpriteImportMode.Single : SpriteImportMode.None;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 4;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = Mathf.Max(w, h);
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
