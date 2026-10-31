using UnityEngine;

/// <summary>
/// A glowing healing orb in flight: a billboarded quad that lobs from where it was thrown to a
/// target (a moving transform or a fixed point) and then disappears, calling back on arrival. It moves
/// on its own, so it keeps flying after the medic that threw it has faded away.
/// </summary>
public class HealOrb : MonoBehaviour
{
    private Vector3 from;
    private Vector3 fixedEnd;
    private Transform follow;
    private Vector3 followOffset;
    private float flight;
    private float size;
    private float arc;
    private float elapsed;
    private System.Action onArrive;

    /// <summary>Creates an orb at <paramref name="from"/> and sends it to the target.</summary>
    /// <param name="target">Transform to fly to; its position is followed while the orb is in the air. May be null.</param>
    /// <param name="fallback">Where to fly to when there is no transform.</param>
    /// <param name="onArrive">Runs once when the orb lands (a heal, usually).</param>
    public static HealOrb Launch(Vector3 from, Transform target, Vector3 fallback, Vector3 targetOffset,
        float flightTime, Material material, float orbSize, float arcHeight, System.Action onArrive)
    {
        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(orb.GetComponent<Collider>());
        orb.name = "HealOrb";

        var renderer = orb.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // The glow shader multiplies by vertex colour and a bare quad has none.
        Mesh quad = orb.GetComponent<MeshFilter>().mesh;
        var white = new Color[quad.vertexCount];

        for (int i = 0; i < white.Length; i++)
        {
            white[i] = Color.white;
        }

        quad.colors = white;

        var flying = orb.AddComponent<HealOrb>();
        flying.from = from;
        flying.follow = target;
        flying.followOffset = targetOffset;
        flying.fixedEnd = fallback;
        flying.flight = Mathf.Max(0.05f, flightTime);
        flying.size = orbSize;
        flying.arc = arcHeight;
        flying.onArrive = onArrive;
        flying.Place(0f);
        return flying;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / flight);
        Place(t);

        if (t >= 1f)
        {
            System.Action arrived = onArrive;
            onArrive = null;
            arrived?.Invoke();
            Destroy(gameObject);
        }
    }

    private void Place(float t)
    {
        Vector3 end = follow != null ? follow.position + followOffset : fixedEnd;

        // A lob: straight line plus a bump in the middle, growing a little as it lands.
        transform.position = Vector3.Lerp(from, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arc);
        transform.localScale = Vector3.one * size * (0.7f + 0.5f * t);

        Camera viewer = Camera.main;

        if (viewer != null)
        {
            transform.rotation = viewer.transform.rotation;
        }
    }
}
