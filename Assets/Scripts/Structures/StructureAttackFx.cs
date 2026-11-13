using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// How an attacking objective looks when it fires, instead of the plain straight beam of
/// <see cref="Tower"/>. Only the looks: who gets hit, and when, is still the tower's job (on the
/// host), and this listens to <see cref="Tower.Shot"/>, which both screens raise, so the rival sees
/// the same attack.
/// <list type="bullet">
/// <item><b>FlameJet</b> (the Guardian): a stream of fire from the nozzle to the target that keeps
/// following it for a moment, a burst where it lands and fire left burning on the ground.</item>
/// <item><b>EyeBolts</b> (the Walker): its central eye glows brighter while the next shot charges,
/// then fires a short volley of bolts that burst into sparks.</item>
/// <item><b>EyeBeam</b> (base guardians, patron): the middle eye charges up and fires a thick beam
/// that holds on the target for half a second.</item>
/// </list>
/// Everything is drawn with two additive materials, a handful of particle systems emitted by hand
/// and two line renderers, built once in <c>Awake</c>: no instantiation while fighting (Quest, RNF-01).
/// </summary>
[RequireComponent(typeof(Tower))]
public class StructureAttackFx : MonoBehaviour
{
    public enum Style { FlameJet, EyeBolts, EyeBeam }

    [SerializeField] private Style style = Style.EyeBeam;

    [Tooltip("Where the attack leaves from: the nozzle or the eye.")]
    [SerializeField] private Transform origin;

    [Tooltip("Additive particle material, vertex coloured (Deadlock/GlowParticle).")]
    [SerializeField] private Material particleMaterial;

    [Tooltip("Additive beam material, vertex coloured (Deadlock/GlowBeam).")]
    [SerializeField] private Material beamMaterial;

    [SerializeField] private Color amberColor = new Color(1f, 0.55f, 0.12f, 1f);
    [SerializeField] private Color sapphireColor = new Color(0.25f, 0.7f, 1f, 1f);

    [Tooltip("Size of the glowing eye at rest; it grows while a shot charges.")]
    [SerializeField] private float eyeSize = 0.7f;

    [Tooltip("Thickness of the beam (EyeBeam) or of the flame where it leaves the nozzle (FlameJet).")]
    [SerializeField] private float thickness = 0.55f;

    [Header("Flame jet")]
    [SerializeField] private float flameSeconds = 0.75f;
    [SerializeField] private float flameSpeed = 26f;
    [SerializeField] private float flameRate = 230f;

    [Header("Eye bolts")]
    [SerializeField] private int boltCount = 4;
    [SerializeField] private float boltGap = 0.08f;
    [SerializeField] private float boltSpeed = 48f;

    [Header("Eye beam")]
    [SerializeField] private float beamSeconds = 0.55f;

    [Header("Light")]
    [Tooltip("A point light at the origin while firing. Off keeps the Quest's per-object light budget free.")]
    [SerializeField] private bool flashLight = true;

    private Tower tower;
    private Color team;
    private ParticleSystem jet, sparks, ground, glow;
    private LineRenderer beamCore, beamOuter;
    private Light flash;

    private float fireUntil = -1f;
    private float groundUntil = -1f;
    private float lastShotTime = -10f;
    private float emitDebt;
    private Vector3 target;
    private Vector3 groundPoint;

    private void Awake()
    {
        tower = GetComponent<Tower>();

        if (origin == null)
        {
            origin = transform;
        }
    }

    private void Start()
    {
        Build();
        ApplyColors();
        tower.Shot += OnShot;
    }

    /// <summary>
    /// The faction arrives from the spawner (or, on the rival's screen, from the network) after the
    /// object is built, so the colours follow it instead of being fixed once.
    /// </summary>
    private void ApplyColors()
    {
        appliedFaction = tower.Faction;
        team = appliedFaction == Faction.Enemy ? sapphireColor : amberColor;

        SetFade(jet, style == Style.FlameJet ? FlameGradient(team) : SparkGradient(team));
        SetFade(sparks, SparkGradient(team));
        SetFade(ground, FlameGradient(team));

        if (flash != null)
        {
            flash.color = Color.Lerp(team, Color.white, 0.25f);
        }
    }

    private static void SetFade(ParticleSystem system, Gradient fade)
    {
        if (system == null) return;
        var color = system.colorOverLifetime;
        color.color = fade;
    }

    private Faction appliedFaction;

    private void OnDestroy()
    {
        if (tower != null)
        {
            tower.Shot -= OnShot;
        }
    }

    // ------------------------------------------------------------------ building

    private void Build()
    {
        var root = new GameObject("AttackFx").transform;
        root.SetParent(transform, false);

        // Colours are set right after by ApplyColors.
        jet = MakeSystem(root, "Jet", 400, Steady(), sizeGrowth: style == Style.FlameJet ? 2.4f : 1f, noise: style == Style.FlameJet ? 0.7f : 0f);
        sparks = MakeSystem(root, "Sparks", 160, Steady(), sizeGrowth: 0.2f, noise: 0f);
        glow = MakeSystem(root, "EyeGlow", 16, Steady(), sizeGrowth: 1f, noise: 0f);

        if (style == Style.FlameJet)
        {
            ground = MakeSystem(root, "GroundFire", 200, Steady(), sizeGrowth: 0.4f, noise: 0.5f);
        }

        if (style == Style.EyeBolts)
        {
            // Bolts are single particles with a short trail behind them.
            var trails = jet.trails;
            trails.enabled = true;
            trails.lifetime = 0.35f;
            trails.minVertexDistance = 0.25f;
            trails.widthOverTrail = 0.6f;
            trails.dieWithParticles = false;
            trails.inheritParticleColor = true;
            jet.GetComponent<ParticleSystemRenderer>().trailMaterial = particleMaterial;
        }

        if (style == Style.EyeBeam)
        {
            beamOuter = Line(root, "BeamOuter");
            beamCore = Line(root, "BeamCore");
        }

        if (flashLight)
        {
            flash = new GameObject("Flash").AddComponent<Light>();
            flash.transform.SetParent(root, false);
            flash.type = LightType.Point;
            flash.range = style == Style.FlameJet ? 9f : 7f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }
    }

    private ParticleSystem MakeSystem(Transform parent, string name, int max, Gradient fade, float sizeGrowth, float noise)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startSpeed = 0f;
        main.startLifetime = 1f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = false;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, sizeGrowth));

        if (noise > 0f)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = noise;
            n.frequency = 1.4f;
            n.scrollSpeed = 1.5f;
            n.quality = ParticleSystemNoiseQuality.Low;
        }

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 4f;

        ps.Play();
        return ps;
    }

    private LineRenderer Line(Transform parent, string name)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false);
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 4;
        line.sharedMaterial = beamMaterial;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private static Gradient FlameGradient(Color team)
    {
        // Hot white-yellow at the nozzle, the team colour in the body, dark ember at the tip.
        Color hot = Color.Lerp(team, Color.white, 0.7f);
        Color deep = new Color(team.r * 0.9f, team.g * 0.55f, team.b * 0.5f);
        Color ember = new Color(team.r * 0.45f, team.g * 0.2f, team.b * 0.25f);
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(hot, 0f), new GradientColorKey(team, 0.25f), new GradientColorKey(deep, 0.6f), new GradientColorKey(ember, 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0.45f, 0.7f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    private static Gradient SparkGradient(Color team)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.Lerp(team, Color.white, 0.4f), 0.25f), new GradientColorKey(team, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    private static Gradient Steady()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // ------------------------------------------------------------------ firing

    private void OnShot(Vector3 point)
    {
        if (!enabled || jet == null)
        {
            return;
        }

        target = point;
        lastShotTime = Time.time;

        switch (style)
        {
            case Style.FlameJet:
                fireUntil = Time.time + flameSeconds;
                groundPoint = GroundBelow(point);
                groundUntil = Time.time + flameSeconds + 1.4f;
                Burst(point, 18, 3.5f, 0.45f, 0.5f);
                break;

            case Style.EyeBolts:
                fireUntil = Time.time + boltGap * boltCount + 0.1f;
                StartCoroutine(Volley(point));
                break;

            case Style.EyeBeam:
                fireUntil = Time.time + beamSeconds;
                Burst(origin.position, 10, 2f, 0.3f, eyeSize * 0.6f);
                Burst(point, 22, 4.5f, 0.5f, 0.45f);
                break;
        }
    }

    private IEnumerator Volley(Vector3 point)
    {
        for (int i = 0; i < boltCount; i++)
        {
            Vector3 from = origin.position;
            Vector3 aim = (tower.HasAimPoint ? tower.AimPoint : point) + Random.insideUnitSphere * 0.6f;
            float distance = Vector3.Distance(from, aim);
            float travel = distance / boltSpeed;

            Emit(jet, from, (aim - from).normalized * boltSpeed, travel, thickness);
            Burst(from, 5, 2f, 0.2f, eyeSize * 0.5f);
            StartCoroutine(ImpactAfter(aim, travel));
            yield return new WaitForSeconds(boltGap);
        }
    }

    private IEnumerator ImpactAfter(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);
        Burst(point, 12, 4f, 0.4f, 0.35f);
    }

    private void Update()
    {
        if (jet == null)
        {
            return;
        }

        if (tower.Faction != appliedFaction)
        {
            ApplyColors();
        }

        bool alive = tower.IsStanding && !MatchManager.IsOver;
        bool firing = alive && Time.time < fireUntil;

        if (tower.HasAimPoint && firing)
        {
            // Keep following the target while the attack lasts.
            target = tower.AimPoint;
        }

        DrawEye(alive);

        switch (style)
        {
            case Style.FlameJet:
                UpdateFlame(firing);
                break;

            case Style.EyeBeam:
                UpdateBeam(firing);
                break;
        }

        if (flash != null)
        {
            flash.enabled = firing;

            if (firing)
            {
                flash.transform.position = origin.position;
                flash.intensity = 2.2f + Mathf.PerlinNoise(Time.time * 18f, 0.3f) * 2.5f;
            }
        }
    }

    /// <summary>The eye (or pilot flame) glows at rest and swells as the next shot gets close.</summary>
    private void DrawEye(bool alive)
    {
        if (!alive)
        {
            return;
        }

        float interval = Mathf.Max(0.3f, tower.AttackInterval);
        float charge = tower.HasAimPoint ? Mathf.Clamp01((Time.time - lastShotTime) / interval) : 0f;
        float size = eyeSize * (1f + charge * charge * 0.9f) * (0.92f + 0.08f * Mathf.Sin(Time.time * 7f));
        Color c = Color.Lerp(team, Color.white, 0.35f + 0.4f * charge);
        c.a = 0.55f + 0.45f * charge;

        // A one-frame billboard re-emitted every frame: a steady glow without a separate mesh.
        var p = new ParticleSystem.EmitParams
        {
            position = origin.position,
            velocity = Vector3.zero,
            startLifetime = Mathf.Max(0.05f, Time.deltaTime * 1.8f),
            startSize = size,
            startColor = c,
            applyShapeToPosition = false,
        };
        glow.Emit(p, 1);
    }

    private void UpdateFlame(bool firing)
    {
        if (firing)
        {
            Vector3 from = origin.position;
            Vector3 to = target;
            Vector3 dir = (to - from).normalized;
            float distance = Vector3.Distance(from, to);
            float life = distance / flameSpeed;

            emitDebt += flameRate * Time.deltaTime;
            int count = Mathf.FloorToInt(emitDebt);
            emitDebt -= count;

            for (int i = 0; i < count; i++)
            {
                Vector3 spread = Random.insideUnitSphere * 0.08f;
                Vector3 velocity = (dir + spread) * flameSpeed * Random.Range(0.92f, 1.08f);
                // Spread the spawn along the last frame's travel so the stream has no gaps.
                Vector3 start = from + velocity * (Random.value * Time.deltaTime);
                Emit(jet, start, velocity, life * Random.Range(0.95f, 1.2f), thickness * Random.Range(0.8f, 1.2f));
            }
        }

        if (ground != null && Time.time < groundUntil)
        {
            for (int i = 0; i < 2; i++)
            {
                Vector2 disk = Random.insideUnitCircle * 0.9f;
                Vector3 at = groundPoint + new Vector3(disk.x, 0.05f, disk.y);
                Emit(ground, at, Vector3.up * Random.Range(1.2f, 2.4f), Random.Range(0.45f, 0.8f), Random.Range(0.35f, 0.7f));
            }
        }
    }

    private void UpdateBeam(bool firing)
    {
        if (beamCore == null)
        {
            return;
        }

        beamCore.enabled = firing;
        beamOuter.enabled = firing;

        if (!firing)
        {
            return;
        }

        float t = 1f - (fireUntil - Time.time) / beamSeconds;
        // Fat at once, thinning out at the end, with a fast flicker.
        float width = thickness * Mathf.Lerp(1f, 0.25f, t * t) * (0.85f + 0.15f * Mathf.Sin(Time.time * 60f));
        Vector3 from = origin.position;

        beamOuter.SetPosition(0, from);
        beamOuter.SetPosition(1, target);
        beamOuter.widthMultiplier = width;
        Color outer = team;
        outer.a = 0.85f;
        beamOuter.startColor = outer;
        beamOuter.endColor = outer;

        beamCore.SetPosition(0, from);
        beamCore.SetPosition(1, target);
        beamCore.widthMultiplier = width * 0.35f;
        beamCore.startColor = Color.white;
        beamCore.endColor = Color.Lerp(team, Color.white, 0.7f);

        // A few sparks off the impact for as long as the beam holds.
        if (Random.value < 0.5f)
        {
            Burst(target, 2, 3f, 0.35f, 0.3f);
        }
    }

    // ------------------------------------------------------------------ helpers

    private void Burst(Vector3 at, int count, float speed, float life, float size)
    {
        for (int i = 0; i < count; i++)
        {
            Emit(sparks, at, Random.onUnitSphere * speed * Random.Range(0.4f, 1f) + Vector3.up * speed * 0.3f, life * Random.Range(0.6f, 1.2f), size * Random.Range(0.6f, 1.3f));
        }
    }

    private static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float life, float size)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startLifetime = life,
            startSize = size,
            startColor = Color.white,
            rotation = Random.Range(0f, 360f),
            applyShapeToPosition = false,
        };
        system.Emit(p, 1);
    }

    private static Vector3 GroundBelow(Vector3 point)
    {
        int mask = LayerMask.GetMask("Ground");

        if (mask != 0 && Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, mask, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        return new Vector3(point.x, point.y - 1f, point.z);
    }
}
