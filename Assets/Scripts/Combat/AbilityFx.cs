using System.Collections;
using UnityEngine;

/// <summary>
/// Draws an ability going off: an orb that flies to the aimed point and bursts, a ring around the
/// caster, or a heavy beam. Purely visual, so the same call shows the local player's casts and,
/// through <see cref="NetworkPlayer"/>, the rival's.
/// </summary>
/// <remarks>
/// Built from primitives at runtime (no particle assets), like the other placeholder effects, and
/// tinted per ability. One per scene; the caster and the network player find it through
/// <see cref="Instance"/>.
/// </remarks>
public class AbilityFx : MonoBehaviour
{
    public static AbilityFx Instance { get; private set; }

    [Tooltip("Additive or transparent material the orbs, bursts and beams are drawn with. Tinted per ability.")]
    [SerializeField] private Material effectMaterial;

    [SerializeField] private float orbSize = 0.35f;
    [SerializeField] private float burstSeconds = 0.45f;
    [SerializeField] private float beamWidth = 0.12f;
    [SerializeField] private float beamSeconds = 0.25f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (effectMaterial == null)
        {
            effectMaterial = new Material(Shader.Find("Sprites/Default"));
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Shows <paramref name="ability"/> cast from <paramref name="from"/> towards <paramref name="to"/>.</summary>
    public static void Play(AbilitySO ability, Vector3 from, Vector3 to)
    {
        if (ability == null || Instance == null)
        {
            return;
        }

        Instance.StartCoroutine(Instance.Run(ability, from, to));
    }

    private IEnumerator Run(AbilitySO ability, Vector3 from, Vector3 to)
    {
        if (ability.castClip != null)
        {
            AudioSource.PlayClipAtPoint(ability.castClip, from, 0.8f);
        }

        switch (ability.shape)
        {
            case AbilityShape.Line:
                yield return Beam(ability, from, to);
                break;

            case AbilityShape.AreaAtAim:
                if (ability.travelTime > 0f)
                {
                    yield return Orb(ability, from, to);
                }

                yield return Burst(ability, to, ability.radius);
                break;

            default:
                yield return Burst(ability, from, ability.radius);
                break;
        }
    }

    private IEnumerator Orb(AbilitySO ability, Vector3 from, Vector3 to)
    {
        GameObject orb = Shape(PrimitiveType.Sphere, "AbilityOrb", ability.tint);
        orb.transform.localScale = Vector3.one * orbSize;

        for (float t = 0f; t < ability.travelTime; t += Time.deltaTime)
        {
            float k = t / ability.travelTime;

            // A shallow arc reads as thrown rather than shot.
            Vector3 position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.6f);
            orb.transform.position = position;
            yield return null;
        }

        Destroy(orb);
    }

    private IEnumerator Burst(AbilitySO ability, Vector3 centre, float radius)
    {
        if (ability.impactClip != null)
        {
            AudioSource.PlayClipAtPoint(ability.impactClip, centre);
        }

        if (ability.effectPrefab != null)
        {
            Destroy(Instantiate(ability.effectPrefab, centre, Quaternion.identity), 3f);
        }

        GameObject sphere = Shape(PrimitiveType.Sphere, "AbilityBurst", ability.tint);
        GameObject ring = Shape(PrimitiveType.Cylinder, "AbilityRing", ability.tint);
        sphere.transform.position = centre;
        ring.transform.position = centre + Vector3.up * 0.05f;

        Material sphereMaterial = sphere.GetComponent<Renderer>().material;
        Material ringMaterial = ring.GetComponent<Renderer>().material;

        for (float t = 0f; t < burstSeconds; t += Time.deltaTime)
        {
            float k = t / burstSeconds;
            float size = Mathf.Lerp(0.2f, radius * 2f, 1f - (1f - k) * (1f - k));
            sphere.transform.localScale = Vector3.one * size;
            ring.transform.localScale = new Vector3(size, 0.01f, size);

            Color faded = ability.tint;
            faded.a = ability.tint.a * (1f - k);
            sphereMaterial.color = faded * new Color(1f, 1f, 1f, 0.6f);
            ringMaterial.color = faded;
            yield return null;
        }

        Destroy(sphere);
        Destroy(ring);
    }

    private IEnumerator Beam(AbilitySO ability, Vector3 from, Vector3 to)
    {
        var holder = new GameObject("AbilityBeam");
        LineRenderer line = holder.AddComponent<LineRenderer>();
        line.material = effectMaterial;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (ability.impactClip != null)
        {
            AudioSource.PlayClipAtPoint(ability.impactClip, to);
        }

        for (float t = 0f; t < beamSeconds; t += Time.deltaTime)
        {
            float k = t / beamSeconds;
            Color faded = ability.tint;
            faded.a = ability.tint.a * (1f - k);
            line.startColor = line.endColor = faded;
            line.widthMultiplier = beamWidth * (1f - 0.6f * k);
            yield return null;
        }

        Destroy(holder);
    }

    private GameObject Shape(PrimitiveType type, string name, Color tint)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        // Immediately, or shots fired this frame could stop on the effect.
        DestroyImmediate(go.GetComponent<Collider>());

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.material = effectMaterial;
        renderer.material.color = tint;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
