using UnityEngine;

/// <summary>
/// The path of the lane zipline as a polyline in world space, from Amber's patron to Sapphire's
/// (straight out of the base, a bend towards the tower side, straight past the tower, a bend
/// through the plaza, and the same mirrored on the other half). Built by setup step 30
/// (<c>DeadlockAtmosphere.BuildLaneNight</c>); <see cref="LaneZipline"/> rides along it and falls
/// back to a straight rope between the patrons when the scene has no route.
/// </summary>
public class ZiplineRoute : MonoBehaviour
{
    [Tooltip("Rope points in world space, densely sampled (curves already smoothed), Amber end first.")]
    [SerializeField] private Vector3[] points = new Vector3[0];

    private float[] cumulative;

    public bool IsValid => points != null && points.Length >= 2;

    public int Count => points.Length;

    public float Length
    {
        get
        {
            EnsureCache();
            return cumulative[cumulative.Length - 1];
        }
    }

    public void SetPoints(Vector3[] newPoints)
    {
        points = newPoints;
        cumulative = null;
    }

    public Vector3 PointAtIndex(int i) => points[i];

    /// <summary>Point at <paramref name="distance"/> metres along the rope from the Amber end.</summary>
    public Vector3 PointAt(float distance)
    {
        EnsureCache();
        distance = Mathf.Clamp(distance, 0f, Length);
        int i = Segment(distance);
        float segLength = cumulative[i + 1] - cumulative[i];
        float t = segLength > 1e-4f ? (distance - cumulative[i]) / segLength : 0f;
        return Vector3.Lerp(points[i], points[i + 1], t);
    }

    /// <summary>Distance along the rope of the point closest to <paramref name="p"/>, measured on the ground plane.</summary>
    public float Project(Vector3 p)
    {
        EnsureCache();
        float best = float.MaxValue;
        float along = 0f;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 a = points[i], b = points[i + 1];
            a.y = b.y = p.y = 0f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            float d = (a + ab * t - p).sqrMagnitude;

            if (d < best)
            {
                best = d;
                along = cumulative[i] + t * (cumulative[i + 1] - cumulative[i]);
            }
        }

        return along;
    }

    /// <summary>
    /// Where along [<paramref name="from"/>, <paramref name="to"/>] the ray passes closest in angle.
    /// Returns that angle in degrees (180 when the ray points away from every segment).
    /// </summary>
    public float ClosestToRay(Ray ray, float from, float to, out float along)
    {
        EnsureCache();
        along = from;
        float bestAngle = 180f;

        for (int i = 0; i < points.Length - 1; i++)
        {
            float s0 = cumulative[i], s1 = cumulative[i + 1];
            if (s1 < from || s0 > to) continue;

            float a0 = Mathf.Max(s0, from), a1 = Mathf.Min(s1, to);
            Vector3 a = PointAt(a0), b = PointAt(a1);
            Vector3 d2 = b - a;
            float segLen = d2.magnitude;
            if (segLen < 1e-4f) continue;
            d2 /= segLen;

            // Closest point on the segment to the ray (same math as two infinite lines, clamped).
            Vector3 d1 = ray.direction;
            Vector3 r = ray.origin - a;
            float dd = Vector3.Dot(d1, d2);
            float denom = 1f - dd * dd;
            float t2 = denom > 0.0001f ? (Vector3.Dot(d1, r) * dd - Vector3.Dot(d2, r)) / denom : 0f;
            t2 = Mathf.Clamp(t2, 0f, segLen);
            Vector3 onRope = a + d2 * t2;
            Vector3 toPoint = onRope - ray.origin;

            if (toPoint.sqrMagnitude < 1f) continue;

            float angle = Vector3.Angle(d1, toPoint);

            if (angle < bestAngle)
            {
                bestAngle = angle;
                along = a0 + t2;
            }
        }

        return bestAngle;
    }

    private int Segment(float distance)
    {
        int lo = 0, hi = cumulative.Length - 2;

        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (cumulative[mid] <= distance) lo = mid;
            else hi = mid - 1;
        }

        return lo;
    }

    private void EnsureCache()
    {
        if (cumulative != null && cumulative.Length == points.Length) return;

        cumulative = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
        {
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }
    }

    private void OnValidate() => cumulative = null;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!IsValid) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < points.Length - 1; i++) Gizmos.DrawLine(points[i], points[i + 1]);
    }
#endif
}
