using UnityEngine;

/// <summary>
/// The route minions walk, as an ordered list of waypoints placed in the scene.
/// </summary>
/// <remarks>
/// Deliberately not a NavMesh. A single straight-ish lane needs nothing more than waypoints, and
/// avoiding <c>com.unity.ai.navigation</c> removes a real portability problem: that package is
/// version 2.x on Unity 6 and 1.1.x on Unity 2022, with a different component and a different
/// baking workflow. Waypoints behave identically on both.
/// </remarks>
public class LanePath : MonoBehaviour
{
    [Tooltip("Waypoints in order, from the player's base to the enemy base.")]
    [SerializeField] private Transform[] waypoints;

    [Tooltip("Colour of the gizmo drawn in the scene view.")]
    [SerializeField] private Color gizmoColor = new Color(1f, 0.6f, 0.2f, 0.9f);

    /// <summary>How many waypoints the lane has.</summary>
    public int Count => waypoints != null ? waypoints.Length : 0;

    /// <summary>
    /// World position of waypoint <paramref name="index"/>, clamped to the ends of the lane so a
    /// minion that overruns the last waypoint keeps a valid destination.
    /// </summary>
    public Vector3 GetPoint(int index)
    {
        if (Count == 0)
        {
            return transform.position;
        }

        int clamped = Mathf.Clamp(index, 0, Count - 1);
        Transform point = waypoints[clamped];

        return point != null ? point.position : transform.position;
    }

    /// <summary>True when <paramref name="index"/> is past the final waypoint.</summary>
    public bool IsEnd(int index) => index >= Count - 1;

    /// <summary>
    /// Index of the waypoint nearest to <paramref name="position"/>. Used when a minion breaks off
    /// a chase and has to rejoin the lane at a sensible place rather than walking back to the start.
    /// </summary>
    public int NearestIndex(Vector3 position)
    {
        int nearest = 0;
        float best = float.MaxValue;

        for (int i = 0; i < Count; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(waypoints[i].position - position);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The lane walked from the other end, so the enemy wave can reuse the same path object
    /// instead of needing a mirrored copy that can drift out of sync with it.
    /// </summary>
    public int ReverseIndex(int index) => Mathf.Clamp(Count - 1 - index, 0, Mathf.Max(0, Count - 1));

    private void OnDrawGizmos()
    {
        if (Count == 0)
        {
            return;
        }

        Gizmos.color = gizmoColor;

        for (int i = 0; i < Count; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            Gizmos.DrawWireSphere(waypoints[i].position, 0.4f);

            if (i + 1 < Count && waypoints[i + 1] != null)
            {
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
            }
        }
    }
}
