using System.Collections;
using UnityEngine;

/// <summary>
/// Raises a tower where this object stands once the duel can begin. Towers are created at runtime
/// instead of sitting in the scene so the host can spawn them on the network for both players; the
/// joining player's copy of this component waits and never creates anything (see <see cref="MatchNet"/>).
/// </summary>
/// <remarks>
/// Offline ("Mi misión") it simply creates the tower at once. Later towers in the sequence (E4) are
/// more of these, each with a higher tier.
/// </remarks>
public class StructureSpawner : MonoBehaviour
{
    [SerializeField] private GameObject prefab;

    [Tooltip("Side the tower defends.")]
    [SerializeField] private Faction faction = Faction.Enemy;

    [Tooltip("Position in the sequence, 1 for the first tower. Higher tiers get more health and damage.")]
    [SerializeField] private int tier = 1;

    /// <summary>Side the tower defends.</summary>
    public Faction Faction => faction;

    /// <summary>The tower once it exists on this machine; null before that.</summary>
    public Structure Spawned { get; private set; }

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => MatchNet.CanSpawnWorld);

        if (prefab == null)
        {
            Debug.LogWarning("[StructureSpawner] No tower prefab assigned.", this);
            yield break;
        }

        GameObject created = MatchNet.Spawn(prefab, transform.position, transform.rotation, instance =>
        {
            if (instance.TryGetComponent(out Structure structure))
            {
                structure.Setup(faction, tier);
            }
        });

        Spawned = created.GetComponent<Structure>();
    }
}
