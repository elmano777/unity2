using System.Collections;
using UnityEngine;

/// <summary>
/// Raises a tower where this object stands once the duel can begin. Towers are created at runtime
/// instead of sitting in the scene so the host can spawn them on the network for both players; the
/// joining player's copy of this component waits and never creates anything (see <see cref="MatchNet"/>).
/// </summary>
/// <remarks>
/// Offline ("Mi misión") it simply creates the tower at once. Every objective of the sequence (RF-12)
/// is one of these: Guardian, Walker, Base Guardians, shrines and patron, each with its tier.
/// </remarks>
public class StructureSpawner : MonoBehaviour
{
    [SerializeField] private GameObject prefab;

    [Tooltip("Side the tower defends.")]
    [SerializeField] private Faction faction = Faction.Enemy;

    [Tooltip("Position in the sequence, 1 for the first tower. Higher tiers get more health and damage.")]
    [SerializeField] private int tier = 1;

    [Tooltip("Name on the HUD, overriding the prefab's (e.g. \"Guardián de base\"). Empty keeps the prefab's.")]
    [SerializeField] private string displayName;

    /// <summary>Side the tower defends.</summary>
    public Faction Faction => faction;

    /// <summary>The tower once it exists on this machine; null before that.</summary>
    public Structure Spawned { get; private set; }

    /// <summary>
    /// True when the prefab carries its own looks (the Guardian, the base guardians). The Walker,
    /// effigies and patron are logic only: their looks are part of the map.
    /// </summary>
    public bool PrefabHasLooks
    {
        get
        {
            if (prefab == null) return false;

            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (r.name == "Shield" || r.GetComponentInParent<HealthBar>(true) != null) continue;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// A local, inert copy of the tower: only its looks. Used by the practice, which takes the
    /// player's own side out of play but should still show it. It does not shoot, cannot be hit,
    /// is not an objective and minions ignore it.
    /// </summary>
    public GameObject SpawnScenery()
    {
        if (prefab == null) return null;

        GameObject copy = Instantiate(prefab, transform.position, transform.rotation);
        copy.name = $"{prefab.name} (scenery)";

        foreach (Behaviour b in copy.GetComponentsInChildren<Behaviour>(true))
        {
            if (b is Animator) continue; // keep its idle pose moving
            b.enabled = false;
        }

        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true))
        {
            c.enabled = false;
        }

        foreach (HealthBar bar in copy.GetComponentsInChildren<HealthBar>(true))
        {
            bar.gameObject.SetActive(false);
        }

        Transform shieldObject = copy.transform.Find("Shield");
        if (shieldObject != null) shieldObject.gameObject.SetActive(false);

        return copy;
    }

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
                structure.Setup(faction, tier, displayName);
            }
        });

        Spawned = created.GetComponent<Structure>();
    }
}
