using System.Collections;
using UnityEngine;

/// <summary>
/// Looks of an objective that is built into the map (the Walker, a shrine, the patron). The part that
/// fights and can be hit is a separate <see cref="Structure"/> the host spawns on this spot; this
/// finds it once it exists on this machine and, when it falls, sinks and darkens the scenery.
/// </summary>
/// <remarks>
/// Splitting looks from logic keeps the map's geometry in the scene (both machines already have it)
/// while hit points and the fall travel through the network like any tower's.
/// </remarks>
public class ObjectiveVisual : MonoBehaviour
{
    [Tooltip("Side the objective belongs to; only a structure of this side is linked.")]
    [SerializeField] private Faction faction = Faction.Player;

    [Tooltip("How far from this object the spawned structure may stand, ignoring height.")]
    [SerializeField] private float linkRadius = 3f;

    [Header("Fall")]
    [SerializeField] private float sinkDepth = 3f;
    [SerializeField] private float tiltDegrees = 12f;
    [SerializeField] private float fallSeconds = 2.5f;
    [SerializeField] private Color fallenTint = new Color(0.22f, 0.2f, 0.19f, 1f);

    private Structure linked;

    /// <summary>The structure standing in for this scenery, once found.</summary>
    public Structure Linked => linked;

    private IEnumerator Start()
    {
        while (linked == null)
        {
            linked = FindStructure();

            if (linked == null)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!linked.IsStanding)
        {
            OnFallen(default);
            yield break;
        }

        linked.Health.Killed += OnFallen;
    }

    private void OnDestroy()
    {
        if (linked != null && linked.Health != null)
        {
            linked.Health.Killed -= OnFallen;
        }
    }

    private Structure FindStructure()
    {
        foreach (Structure s in Structure.Standing)
        {
            Vector3 offset = s.transform.position - transform.position;
            offset.y = 0f;

            if (s.Faction == faction && offset.sqrMagnitude <= linkRadius * linkRadius)
            {
                return s;
            }
        }

        return null;
    }

    private void OnFallen(DamageInfo info)
    {
        StartCoroutine(Fall());
    }

    private IEnumerator Fall()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            // Glowing parts (eyes, cores) go out; the rest turns to rubble colour.
            r.material.color = fallenTint;

            if (r.material.HasProperty("_EmissionColor"))
            {
                r.material.SetColor("_EmissionColor", Color.black);
            }
        }

        Vector3 start = transform.position;
        Quaternion startRotation = transform.rotation;
        Quaternion tilted = startRotation * Quaternion.Euler(tiltDegrees, 0f, tiltDegrees * 0.5f);

        for (float t = 0f; t < fallSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / fallSeconds);
            transform.SetPositionAndRotation(start + Vector3.down * (sinkDepth * k), Quaternion.Slerp(startRotation, tilted, k));
            yield return null;
        }

        transform.SetPositionAndRotation(start + Vector3.down * sinkDepth, tilted);
    }
}
