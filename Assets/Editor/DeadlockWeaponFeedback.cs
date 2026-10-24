using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gives the hero weapons something to see and hear (RF-06) and the minions a health bar, all
/// built here without external assets: shot and hit sounds synthesised into WAV files, glow
/// particles for the muzzle flash, the travelling bullet and the impact, a line tracer, and a
/// two-quad bar over every minion. Safe to re-run: each piece replaces the one it made last time.
/// </summary>
public static class DeadlockWeaponFeedback
{
    private const string MaterialDir = "Assets/Art/Materials/Combat";
    private const string AudioDir = "Assets/Audio/Combat";
    private const string EffectDir = "Assets/Prefabs/Effects";
    private const string ImpactPath = EffectDir + "/BulletImpact.prefab";
    private const int SampleRate = 44100;

    private static readonly string[] WeaponPrefabs =
    {
        "Assets/Prefabs/Weapons/Seven_Weapon.prefab",
        "Assets/Prefabs/Weapons/Vindicta_Weapon.prefab",
    };

    private static readonly string[] MinionPrefabs =
    {
        "Assets/Prefabs/Minions/Amber_Melee.prefab",
        "Assets/Prefabs/Minions/Amber_Trooper.prefab",
        "Assets/Prefabs/Minions/Amber_Medic.prefab",
        "Assets/Prefabs/Minions/Sapphire_Melee.prefab",
        "Assets/Prefabs/Minions/Sapphire_Trooper.prefab",
        "Assets/Prefabs/Minions/Sapphire_Medic.prefab",
    };

    [MenuItem("Deadlock/Setup/14 Weapon Feedback And Health Bars")]
    public static void Run()
    {
        DeadlockSceneKit.EnsureFolder(MaterialDir);
        DeadlockSceneKit.EnsureFolder(AudioDir);
        DeadlockSceneKit.EnsureFolder(EffectDir);

        AudioClip shot = WriteClip("Shot", ShotSamples());
        AudioClip hit = WriteClip("Hit", HitSamples());

        Material flash = Glow("Glow_MuzzleFlash", new Color(1f, 0.72f, 0.3f, 1f), 1f);
        Material bullet = Glow("Glow_Bullet", new Color(1f, 0.85f, 0.45f, 1f), 1f);
        Material spark = Glow("Glow_Spark", new Color(1f, 0.6f, 0.2f, 1f), 1f);
        Material tracer = Glow("Glow_Tracer", new Color(1f, 0.85f, 0.5f, 0.35f), 0f);

        GameObject impact = BuildImpact(spark, flash, hit);

        foreach (string path in WeaponPrefabs)
        {
            SetUpWeapon(path, flash, bullet, tracer, impact, shot);
        }

        Material barBack = Unlit("Bar_Back", new Color(0.05f, 0.05f, 0.06f, 1f));
        Material barAmber = Unlit("Bar_Amber", new Color(1f, 0.62f, 0.15f, 1f));
        Material barSapphire = Unlit("Bar_Sapphire", new Color(0.3f, 0.6f, 1f, 1f));

        foreach (string path in MinionPrefabs)
        {
            AddHealthBar(path, barBack, path.Contains("Sapphire") ? barSapphire : barAmber);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Weapon feedback and minion health bars done.");
    }

    // ---------- Weapons ----------

    private static void SetUpWeapon(string path, Material flash, Material bullet, Material tracerMat, GameObject impact, AudioClip shot)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        Weapon weapon = root.GetComponent<Weapon>();
        Transform muzzle = DeadlockSceneKit.FindDeep(root.transform, "Muzzle");

        if (weapon == null || muzzle == null)
        {
            Debug.LogWarning($"[Deadlock Setup] {path}: no Weapon or Muzzle, skipped.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        RemoveChild(muzzle, "MuzzleFlash");
        RemoveChild(muzzle, "BulletStreak");
        RemoveChild(muzzle, "Tracer");

        // Short enough (0.05 s) to have finished before the next shot, since Play() is ignored on a
        // system that is still playing.
        ParticleSystem muzzleFlash = NewParticles("MuzzleFlash", muzzle, flash, false);
        var main = muzzleFlash.main;
        main.duration = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        Burst(muzzleFlash, 6);
        var flashShape = muzzleFlash.shape;
        flashShape.enabled = true;
        flashShape.shapeType = ParticleSystemShapeType.Cone;
        flashShape.angle = 18f;
        flashShape.radius = 0.01f;

        // Never emits on its own: Weapon.Emit()s one particle per shot with the right lifetime.
        ParticleSystem streak = NewParticles("BulletStreak", muzzle, bullet, true);
        var streakMain = streak.main;
        streakMain.startLifetime = 1f;
        streakMain.startSpeed = 0f;
        streakMain.startSize = 0.035f;
        streakMain.maxParticles = 64;
        streakMain.simulationSpace = ParticleSystemSimulationSpace.World;
        var streakEmission = streak.emission;
        streakEmission.rateOverTime = 0f;
        var streakRenderer = streak.GetComponent<ParticleSystemRenderer>();
        streakRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        streakRenderer.lengthScale = 1f;
        streakRenderer.velocityScale = 0.006f;

        var tracerGo = new GameObject("Tracer");
        tracerGo.transform.SetParent(muzzle, false);
        LineRenderer tracer = tracerGo.AddComponent<LineRenderer>();
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.widthCurve = new AnimationCurve(new Keyframe(0f, 0.012f), new Keyframe(1f, 0.004f));
        tracer.sharedMaterial = tracerMat;
        tracer.shadowCastingMode = ShadowCastingMode.Off;
        tracer.receiveShadows = false;
        tracer.enabled = false;

        AudioSource audio = root.GetComponent<AudioSource>();
        if (audio == null) audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0.6f;

        var so = new SerializedObject(weapon);
        so.FindProperty("muzzleFlash").objectReferenceValue = muzzleFlash;
        so.FindProperty("bulletStreak").objectReferenceValue = streak;
        so.FindProperty("tracer").objectReferenceValue = tracer;
        so.FindProperty("tracerDuration").floatValue = 0.03f;
        so.FindProperty("impactEffect").objectReferenceValue = impact;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        so.FindProperty("fireClip").objectReferenceValue = shot;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    /// <summary>Sparks thrown back along the surface normal, a quick flash and the hit tick.</summary>
    private static GameObject BuildImpact(Material spark, Material flash, AudioClip hit)
    {
        var root = new GameObject("BulletImpact");

        ParticleSystem sparks = NewParticles("Sparks", root.transform, spark, false);
        var main = sparks.main;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
        main.gravityModifier = 1.2f;
        Burst(sparks, 12);
        var shape = sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.02f;
        var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.velocityScale = 0.03f;
        sparkRenderer.lengthScale = 1f;

        ParticleSystem puff = NewParticles("Flash", root.transform, flash, false);
        var puffMain = puff.main;
        puffMain.duration = 0.1f;
        puffMain.startLifetime = 0.07f;
        puffMain.startSpeed = 0f;
        puffMain.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
        Burst(puff, 1);
        var puffShape = puff.shape;
        puffShape.enabled = false;

        AudioSource audio = root.AddComponent<AudioSource>();
        audio.clip = hit;
        audio.playOnAwake = true;
        audio.spatialBlend = 1f;
        audio.minDistance = 2f;
        audio.maxDistance = 40f;
        audio.volume = 0.8f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ImpactPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static ParticleSystem NewParticles(string name, Transform parent, Material material, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = loop;
        // The impact plays the moment it is spawned; the muzzle flash waits for Weapon.Play().
        main.playOnAwake = loop || parent.GetComponentInParent<Weapon>() == null;
        main.startColor = Color.white;
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.None;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static void Burst(ParticleSystem ps, short count)
    {
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
    }

    // ---------- Health bars ----------

    private static void AddHealthBar(string path, Material back, Material fill)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        RemoveChild(root.transform, "HealthBar");

        // Just above the head: the top of the capsule the prefab already uses for hits.
        float top = 2f;
        if (root.TryGetComponent(out CapsuleCollider capsule))
        {
            top = capsule.center.y + capsule.height * 0.5f;
        }

        var bar = new GameObject("HealthBar");
        bar.transform.SetParent(root.transform, false);
        bar.transform.localPosition = new Vector3(0f, top + 0.3f, 0f);

        var visual = new GameObject("Visual");
        visual.transform.SetParent(bar.transform, false);

        // The bar's forward points away from the viewer, so +Z is behind the fill.
        Quad("Back", visual.transform, new Vector3(0f, 0f, 0.01f), new Vector3(1.04f, 0.14f, 1f), back);

        var pivot = new GameObject("FillPivot");
        pivot.transform.SetParent(visual.transform, false);
        pivot.transform.localPosition = new Vector3(-0.5f, 0f, 0f);
        Quad("Fill", pivot.transform, new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.1f, 1f), fill);

        visual.SetActive(false);

        HealthBar healthBar = bar.AddComponent<HealthBar>();
        var so = new SerializedObject(healthBar);
        so.FindProperty("health").objectReferenceValue = root.GetComponent<Health>();
        so.FindProperty("fillPivot").objectReferenceValue = pivot.transform;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void Quad(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        // No collider: the bar must not block shots or show up in the minions' target scans.
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = scale;

        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---------- Materials ----------

    private static Material Glow(string name, Color color, float radial)
    {
        Material m = LoadOrCreate(name, "Deadlock/GlowParticle");
        m.SetColor("_Color", color);
        m.SetFloat("_Radial", radial);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material Unlit(string name, Color color)
    {
        Material m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LoadOrCreate(string name, string shaderName)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            m = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(m, path);
        }
        else
        {
            m.shader = Shader.Find(shaderName);
        }

        return m;
    }

    // ---------- Audio ----------

    /// <summary>A gunshot: a filtered noise crack over a falling low thump, ~0.3 s.</summary>
    private static float[] ShotSamples()
    {
        var random = new System.Random(7);
        int length = (int)(SampleRate * 0.3f);
        var data = new float[length];
        float filtered = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            filtered += (noise - filtered) * 0.35f;

            float crack = filtered * Mathf.Exp(-t * 30f);
            float thump = Mathf.Sin(2f * Mathf.PI * (140f * t - 180f * t * t)) * Mathf.Exp(-t * 20f);
            float attack = Mathf.Clamp01(t / 0.001f);
            data[i] = (crack * 0.8f + thump * 0.6f) * attack * 0.7f;
        }

        return data;
    }

    /// <summary>A short metallic tick that confirms the shot connected, ~0.12 s.</summary>
    private static float[] HitSamples()
    {
        var random = new System.Random(11);
        int length = (int)(SampleRate * 0.12f);
        var data = new float[length];

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            float ring = Mathf.Sin(2f * Mathf.PI * 1850f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 2790f * t) * 0.25f;
            data[i] = (ring * Mathf.Exp(-t * 38f) + noise * Mathf.Exp(-t * 110f) * 0.4f) * 0.6f;
        }

        return data;
    }

    /// <summary>Writes 16-bit mono PCM so the clip is a normal imported asset the prefabs can keep.</summary>
    private static AudioClip WriteClip(string name, float[] samples)
    {
        string path = $"{AudioDir}/{name}.wav";

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            int dataBytes = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            foreach (float s in samples)
            {
                writer.Write((short)(Mathf.Clamp(s, -1f, 1f) * short.MaxValue));
            }
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private static void RemoveChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
    }
}
