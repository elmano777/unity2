using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The abilities that live for a while in the world: Seven's lightning ball (a travelling
/// simulation with its own look) and his storm cloud (the visuals that follow the caster), plus the
/// slows and tethers abilities apply.
/// </summary>
/// <remarks>
/// Movement is local: each screen moves its own player, and the host moves the minions. So instead
/// of sending effects over the network, every screen that shows a cast (the caster's, and the
/// rival's copy through <see cref="NetworkPlayer"/>) runs the same simulation and applies slows to
/// what it moves: its own player when the caster is their rival, and the minions when it is the
/// host or offline. Damage only comes from the caster's screen, as damage.
/// </remarks>
public static class AbilityEffects
{
    private static readonly Collider[] buffer = new Collider[64];
    private static readonly List<Vector3> targetPoints = new List<Vector3>();
    private static readonly HashSet<IDamageable> seen = new HashSet<IDamageable>();
    private static ComfortLocomotion localLocomotion;
    private static Runner runner;
    private static Material lineMaterial;

    private static Runner Host
    {
        get
        {
            if (runner == null)
            {
                var go = new GameObject("AbilityEffects");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideInHierarchy;
                runner = go.AddComponent<Runner>();
            }

            return runner;
        }
    }

    private static Material LineMaterial
    {
        get
        {
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            return lineMaterial;
        }
    }

    // ------------------------------------------------------------------ slows and tethers

    /// <summary>Slows and/or tethers whatever is hostile to <paramref name="caster"/> within <paramref name="radius"/> of <paramref name="centre"/>.</summary>
    public static void ApplyMovement(AbilitySO ability, Faction caster, Vector3 centre, float radius)
    {
        if (ability == null || !ability.HasMovementEffect)
        {
            return;
        }

        // This screen's own player, when the caster is their rival.
        if (FactionUtility.IsHostile(caster, MatchNet.LocalFaction) && caster != Faction.Neutral)
        {
            if (localLocomotion == null)
            {
                localLocomotion = Object.FindObjectOfType<ComfortLocomotion>();
            }

            Camera cam = Camera.main;

            if (localLocomotion != null && cam != null && Flat(cam.transform.position - centre) <= radius)
            {
                if (ability.slow > 0f) localLocomotion.ApplySlow(ability.slow, ability.slowSeconds);
                if (ability.tetherSeconds > 0f) localLocomotion.Tether(centre, ability.tetherLength, ability.tetherSeconds);
                HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.5f, 0.15f);
                HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.5f, 0.15f);
            }
        }

        // Minions walk on the host (or offline).
        if (!MatchNet.IsAuthority)
        {
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(centre, radius, buffer, ~0, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            MinionAgent minion = buffer[i].GetComponentInParent<MinionAgent>();
            Health health = minion != null ? minion.GetComponent<Health>() : null;

            if (minion == null || health == null || !health.IsAlive || !FactionUtility.IsHostile(caster, health.Faction))
            {
                continue;
            }

            if (ability.slow > 0f) minion.ApplySlow(ability.slow, ability.slowSeconds);
            if (ability.tetherSeconds > 0f) minion.Tether(centre, ability.tetherLength, ability.tetherSeconds);
        }
    }

    // ------------------------------------------------------------------ lightning ball

    /// <summary>
    /// Runs a lightning ball from <paramref name="from"/> along <paramref name="direction"/> for the
    /// ability's lifetime (Duration). It crawls at <see cref="AbilitySO.slowedSpeed"/> while any enemy
    /// is inside its radius and goes back to full speed when they leave; at <paramref name="maxDistance"/>
    /// (where it met the world) it stops for good. Every tick it slows what it touches and, on the
    /// caster's screen, <paramref name="onTick"/> deals the damage.
    /// </summary>
    public static void RunProjectile(AbilitySO ability, Faction caster, Vector3 from, Vector3 direction, float maxDistance,
        float radius, System.Action<Vector3, float> onTick)
    {
        Host.StartCoroutine(Projectile(ability, caster, from, direction.normalized, maxDistance, radius, onTick));
    }

    /// <summary>How far a ball can travel in its lifetime at full speed.</summary>
    public static float MaxTravel(AbilitySO ability) => ability.travelSpeed * Mathf.Max(0.5f, ability.duration);

    private static IEnumerator Projectile(AbilitySO ability, Faction caster, Vector3 from, Vector3 direction, float maxDistance,
        float radius, System.Action<Vector3, float> onTick)
    {
        float life = Mathf.Max(0.5f, ability.duration);
        float interval = Mathf.Max(0.1f, ability.tickInterval);
        float travelled = 0f;
        float nextTick = 0f;
        var look = new BallLook(ability.tint, radius);

        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            Vector3 at = from + direction * travelled;
            bool touching = FindHostiles(caster, at, radius);
            float speed = ability.travelSpeed * (touching ? ability.slowedSpeed : 1f);
            travelled = Mathf.Min(maxDistance, travelled + speed * Time.deltaTime);

            if (t >= nextTick)
            {
                nextTick += interval;
                ApplyMovement(ability, caster, at, radius);
                onTick?.Invoke(at, ability.damagePerSecond * interval);
            }

            // Fades in over the first moment and out over the last half second.
            float fade = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((life - t) / 0.5f);
            look.Show(at, targetPoints, fade, Time.time);
            yield return null;
        }

        look.Destroy();
    }

    /// <summary>Fills <see cref="targetPoints"/> with the enemies of <paramref name="caster"/> inside the radius; true if any.</summary>
    private static bool FindHostiles(Faction caster, Vector3 at, float radius)
    {
        targetPoints.Clear();
        seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(at, radius, buffer, ~0, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = buffer[i].GetComponentInParent<IDamageable>();

            if (target == null || !target.IsAlive || target.Faction == Faction.Neutral || !seen.Add(target)
                || !FactionUtility.IsHostile(caster, target.Faction))
            {
                continue;
            }

            targetPoints.Add(buffer[i].bounds.center);
        }

        // This screen's own player, when the ball is the rival's.
        Camera cam = Camera.main;

        if (cam != null && caster != Faction.Neutral && FactionUtility.IsHostile(caster, MatchNet.LocalFaction)
            && Vector3.Distance(cam.transform.position + Vector3.down * 0.5f, at) <= radius)
        {
            targetPoints.Add(cam.transform.position + Vector3.down * 0.5f);
        }

        return targetPoints.Count > 0;
    }

    /// <summary>
    /// The ball: a hot yellow-white core in a soft glow, with static crackling around it (short
    /// jagged arcs, redrawn many times a second) and longer arcs reaching for whatever it is shocking.
    /// </summary>
    private sealed class BallLook
    {
        private const int StaticArcs = 7, TargetArcs = 4, ArcPoints = 7;

        private readonly GameObject root;
        private readonly Transform core, glow;
        private readonly Material coreMaterial, glowMaterial;
        private readonly LineRenderer[] arcs = new LineRenderer[StaticArcs];
        private readonly LineRenderer[] reach = new LineRenderer[TargetArcs];
        private readonly Light light;
        private readonly Color tint;
        private readonly float radius;
        private float nextCrackle;

        public BallLook(Color tint, float radius)
        {
            this.tint = tint;
            this.radius = radius;
            root = new GameObject("LightningBall");

            core = Sphere("Core", out coreMaterial).transform;
            core.localScale = Vector3.one * 0.32f;
            glow = Sphere("Glow", out glowMaterial).transform;
            glow.localScale = Vector3.one * 0.95f;

            for (int i = 0; i < StaticArcs; i++) arcs[i] = Arc("Static" + i, 0.025f);
            for (int i = 0; i < TargetArcs; i++) reach[i] = Arc("Reach" + i, 0.04f);

            light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = Color.Lerp(tint, Color.white, 0.3f);
            light.range = radius + 1.5f;
            light.shadows = LightShadows.None;
        }

        public void Show(Vector3 at, List<Vector3> targets, float fade, float time)
        {
            root.transform.position = at;
            float flicker = 0.8f + 0.4f * Mathf.PerlinNoise(time * 23f, 0.7f);

            core.localScale = Vector3.one * 0.32f * flicker;
            coreMaterial.color = new Color(1f, 0.97f, 0.8f, fade);
            glow.localScale = Vector3.one * (0.85f + 0.25f * flicker);
            glowMaterial.color = new Color(tint.r, tint.g, tint.b, 0.28f * fade);
            light.intensity = 2.2f * flicker * fade;

            // Static is redrawn ~25 times a second: it reads as crackling rather than moving.
            if (time >= nextCrackle)
            {
                nextCrackle = time + 0.04f;

                for (int i = 0; i < StaticArcs; i++)
                {
                    Vector3 end = at + Random.onUnitSphere * Random.Range(0.35f, Mathf.Min(1.4f, radius * 0.45f));
                    Jag(arcs[i], at, end, 0.12f, fade * Random.Range(0.5f, 1f));
                }

                for (int i = 0; i < TargetArcs; i++)
                {
                    if (i < targets.Count)
                    {
                        Jag(reach[i], at, targets[i], 0.3f, fade);
                    }
                    else
                    {
                        reach[i].enabled = false;
                    }
                }
            }
        }

        public void Destroy()
        {
            Object.Destroy(root);
        }

        private GameObject Sphere(string name, out Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            Object.Destroy(go.GetComponent<Collider>());
            Renderer r = go.GetComponent<Renderer>();
            material = new Material(LineMaterial);
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private LineRenderer Arc(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = LineMaterial;
            line.positionCount = ArcPoints;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private void Jag(LineRenderer line, Vector3 from, Vector3 to, float jitter, float alpha)
        {
            line.enabled = alpha > 0.01f;
            Vector3 side = Vector3.Cross(to - from, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();

            for (int p = 0; p < ArcPoints; p++)
            {
                float k = p / (float)(ArcPoints - 1);
                Vector3 point = Vector3.Lerp(from, to, k);

                if (p > 0 && p < ArcPoints - 1)
                {
                    point += Random.insideUnitSphere * jitter + side * Random.Range(-jitter, jitter);
                }

                line.SetPosition(p, point);
            }

            Color hot = new Color(1f, 0.98f, 0.85f, alpha);
            Color edge = new Color(tint.r, tint.g, tint.b, alpha * 0.7f);
            line.startColor = hot;
            line.endColor = edge;
        }
    }

    // ------------------------------------------------------------------ stake

    /// <summary>
    /// Vindicta's stake where it landed: a dark stake driven into the ground, a violet ring at its
    /// capture radius, and a ghostly tether from the stake to every enemy it caught, for as long as
    /// the tether lasts. Each screen draws the enemies it sees, so both players see the same tethers.
    /// </summary>
    public static void RunStake(AbilitySO ability, Faction caster, Vector3 point, float radius)
    {
        Host.StartCoroutine(Stake(ability, caster, point, radius));
    }

    private static IEnumerator Stake(AbilitySO ability, Faction caster, Vector3 point, float radius)
    {
        var root = new GameObject("Stake");
        root.transform.position = point;
        Color tint = ability.tint;

        // The stake itself: a dark shaft with a sharpened tip sunk in the ground and a pale band.
        Material wood = new Material(LineMaterial) { color = new Color(0.16f, 0.1f, 0.08f, 1f) };
        Material band = new Material(LineMaterial) { color = Color.Lerp(tint, Color.white, 0.4f) };
        Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.09f, 0.45f, 0.09f), wood, Quaternion.Euler(8f, 0f, 6f));
        Part(root.transform, PrimitiveType.Cylinder, new Vector3(0.03f, 0.78f, 0.04f), new Vector3(0.11f, 0.03f, 0.11f), band, Quaternion.Euler(8f, 0f, 6f));
        Vector3 top = point + new Vector3(0.06f, 0.85f, 0.08f);

        // The captured enemies: the colliders of what is hostile in reach, and this screen's own
        // player when the stake is the rival's.
        var held = new List<Transform>();
        var heldSet = new HashSet<IDamageable>();
        int count = Physics.OverlapSphereNonAlloc(point, radius, buffer, ~0, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count && held.Count < 6; i++)
        {
            IDamageable target = buffer[i].GetComponentInParent<IDamageable>();

            if (target != null && target.IsAlive && target.Faction != Faction.Neutral && heldSet.Add(target)
                && FactionUtility.IsHostile(caster, target.Faction))
            {
                held.Add(buffer[i].transform);
            }
        }

        Camera cam = Camera.main;

        if (cam != null && caster != Faction.Neutral && FactionUtility.IsHostile(caster, MatchNet.LocalFaction)
            && Flat(cam.transform.position - point) <= radius)
        {
            held.Add(cam.transform);
        }

        var tethers = new LineRenderer[held.Count];

        for (int i = 0; i < held.Count; i++)
        {
            tethers[i] = new GameObject("Tether").AddComponent<LineRenderer>();
            tethers[i].transform.SetParent(root.transform, false);
            tethers[i].sharedMaterial = LineMaterial;
            tethers[i].positionCount = 8;
            tethers[i].useWorldSpace = true;
            tethers[i].widthMultiplier = 0.035f;
        }

        LineRenderer ring = new GameObject("Ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(root.transform, false);
        ring.sharedMaterial = LineMaterial;
        ring.loop = true;
        ring.positionCount = 40;
        ring.useWorldSpace = true;
        ring.widthMultiplier = 0.05f;

        float hold = Mathf.Max(0.5f, ability.tetherSeconds);
        float life = hold + 0.6f;

        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            float fade = Mathf.Clamp01((life - t) / 0.6f);

            // The ring flashes out to the capture radius, then settles faint.
            float r = radius * Mathf.Min(1f, t / 0.15f);
            Color ringColor = tint;
            ringColor.a = Mathf.Lerp(0.9f, 0.3f, Mathf.Clamp01(t / 0.5f)) * fade;
            ring.startColor = ring.endColor = ringColor;

            for (int i = 0; i < ring.positionCount; i++)
            {
                float a = i / (float)ring.positionCount * Mathf.PI * 2f;
                ring.SetPosition(i, point + new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r));
            }

            for (int i = 0; i < tethers.Length; i++)
            {
                bool on = t < hold && held[i] != null;
                tethers[i].enabled = on;

                if (!on)
                {
                    continue;
                }

                Vector3 end = held[i] == (cam != null ? cam.transform : null)
                    ? held[i].position + Vector3.down * 0.6f
                    : held[i].position + Vector3.up * 0.9f;

                // A taut ghostly cord with a slight wave running along it.
                for (int p = 0; p < 8; p++)
                {
                    float k = p / 7f;
                    Vector3 pos = Vector3.Lerp(top, end, k);
                    pos += Vector3.up * Mathf.Sin(k * Mathf.PI) * (0.15f + 0.08f * Mathf.Sin(t * 14f + k * 6f));
                    tethers[i].SetPosition(p, pos);
                }

                Color c = Color.Lerp(tint, Color.white, 0.25f);
                c.a = 0.85f;
                tethers[i].startColor = c;
                c.a = 0.45f;
                tethers[i].endColor = c;
            }

            wood.color = new Color(0.16f, 0.1f, 0.08f, fade);
            band.color = new Color(band.color.r, band.color.g, band.color.b, fade);
            yield return null;
        }

        Object.Destroy(root);
    }

    private static void Part(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material material, Quaternion rotation)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        Object.Destroy(go.GetComponent<Collider>());
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------------ storm cloud

    /// <summary>
    /// The storm cloud's look for <paramref name="seconds"/>, following <paramref name="follow"/>
    /// (the caster's head): dark clouds just above the head, so it never fills the caster's own view,
    /// lightning striking all over the area, and a ring on the ground at the radius that grows from
    /// the ability's Radius to its Radius End.
    /// </summary>
    public static void RunStorm(AbilitySO ability, Transform follow, float seconds, float radiusScale)
    {
        if (follow != null)
        {
            Host.StartCoroutine(Storm(ability, follow, seconds, radiusScale));
        }
    }

    /// <summary>The storm's radius <paramref name="t"/> seconds into the channel.</summary>
    public static float StormRadius(AbilitySO ability, float t, float radiusScale) =>
        Mathf.Min(ability.radiusEnd, ability.radius + ability.growSpeed * t) * radiusScale;

    private static IEnumerator Storm(AbilitySO ability, Transform follow, float seconds, float radiusScale)
    {
        int ground = LayerMask.GetMask("Ground");
        var root = new GameObject("StormCloud");
        var puffs = new List<(Transform t, Material m, Vector3 offset, float size)>();

        for (int i = 0; i < 9; i++)
        {
            float angle = i / 9f * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            float dist = i == 0 ? 0f : Random.Range(1.2f, 2.8f);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, Random.Range(1.6f, 2.3f), Mathf.Sin(angle) * dist);
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "Cloud";
            puff.transform.SetParent(root.transform, false);
            Object.Destroy(puff.GetComponent<Collider>());
            Renderer r = puff.GetComponent<Renderer>();
            var m = new Material(LineMaterial);
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            puffs.Add((puff.transform, m, offset, Random.Range(1.6f, 2.6f)));
        }

        LineRenderer ring = new GameObject("Ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(root.transform, false);
        ring.sharedMaterial = LineMaterial;
        ring.loop = true;
        ring.positionCount = 48;
        ring.widthMultiplier = 0.08f;
        ring.useWorldSpace = true;

        var bolts = new List<(LineRenderer line, float until)>();
        Light flash = new GameObject("Flash").AddComponent<Light>();
        flash.transform.SetParent(root.transform, false);
        flash.type = LightType.Point;
        flash.range = 10f;
        flash.color = new Color(0.75f, 0.85f, 1f);
        flash.shadows = LightShadows.None;
        float nextBolt = 0f;

        for (float t = 0f; t < seconds && follow != null; t += Time.deltaTime)
        {
            float fade = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((seconds - t) / 0.6f);
            float radius = StormRadius(ability, t, radiusScale);
            Vector3 centre = follow.position;
            float floorY = Physics.Raycast(centre, Vector3.down, out RaycastHit floor, 12f, ground, QueryTriggerInteraction.Ignore)
                ? floor.point.y : centre.y - 1.7f;

            // Clouds churn slowly over the head.
            foreach (var p in puffs)
            {
                float churn = Mathf.Sin(t * 0.9f + p.offset.x) * 0.25f;
                p.t.position = centre + p.offset + new Vector3(churn, Mathf.Sin(t * 1.3f + p.offset.z) * 0.12f, -churn);
                p.t.localScale = new Vector3(p.size * 1.3f, p.size * 0.6f, p.size * 1.3f);
                p.m.color = new Color(0.16f, 0.17f, 0.24f, 0.88f * fade);
            }

            for (int i = 0; i < ring.positionCount; i++)
            {
                float a = i / (float)ring.positionCount * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(centre.x + Mathf.Cos(a) * radius, floorY + 0.06f, centre.z + Mathf.Sin(a) * radius));
            }

            Color ringColor = Color.Lerp(ability.tint, Color.white, 0.2f);
            ringColor.a = 0.75f * fade;
            ring.startColor = ring.endColor = ringColor;

            // Lightning everywhere: a new bolt every few hundredths of a second.
            if (t >= nextBolt && fade > 0.2f)
            {
                nextBolt = t + Random.Range(0.04f, 0.1f);
                int burst = Random.Range(1, 4);

                for (int b = 0; b < burst; b++)
                {
                    var p = puffs[Random.Range(0, puffs.Count)];
                    Vector2 spot = Random.insideUnitCircle * radius;
                    Vector3 strike = new Vector3(centre.x + spot.x, floorY, centre.z + spot.y);
                    bolts.Add((Bolt(root.transform, p.t.position, strike, ability.tint), t + Random.Range(0.1f, 0.2f)));
                    flash.transform.position = strike + Vector3.up * 1.5f;
                }

                flash.intensity = 5f;
            }

            flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, 40f * Time.deltaTime);

            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                if (t >= bolts[i].until)
                {
                    Object.Destroy(bolts[i].line.gameObject);
                    bolts.RemoveAt(i);
                }
            }

            yield return null;
        }

        Object.Destroy(root);
    }

    private static LineRenderer Bolt(Transform parent, Vector3 from, Vector3 to, Color tint)
    {
        const int points = 10;
        var go = new GameObject("Bolt");
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = LineMaterial;
        line.positionCount = points;
        line.widthMultiplier = Random.Range(0.08f, 0.14f);
        line.useWorldSpace = true;

        for (int i = 0; i < points; i++)
        {
            float k = i / (float)(points - 1);
            Vector3 point = Vector3.Lerp(from, to, k);
            if (i > 0 && i < points - 1) point += new Vector3(Random.Range(-0.45f, 0.45f), 0f, Random.Range(-0.45f, 0.45f));
            line.SetPosition(i, point);
        }

        line.startColor = new Color(1f, 1f, 1f, 1f);
        line.endColor = Color.Lerp(tint, Color.white, 0.5f);
        return line;
    }

    private static float Flat(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    private sealed class Runner : MonoBehaviour
    {
    }
}
