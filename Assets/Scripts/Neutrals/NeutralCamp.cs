using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A camp of neutral creatures in a side street: extra souls for whoever leaves the lane to take
/// them. It fills up a while after the match starts and again some time after it has been cleared.
/// </summary>
/// <remarks>
/// Only the host creates the creatures (<see cref="MatchNet"/>), exactly like <see cref="WaveSpawner"/>
/// does with a wave; the joining player's copy of this component waits and never spawns anything.
/// </remarks>
public class NeutralCamp : MonoBehaviour
{
    /// <summary>One creature of the camp: which prefab and where, relative to the camp.</summary>
    [System.Serializable]
    public class Slot
    {
        public GameObject prefab;
        public Vector3 localPosition;

        [Tooltip("Degrees it is turned from the camp's forward.")]
        public float yaw;
    }

    [SerializeField] private Slot[] slots = new Slot[0];

    [Header("Timing")]
    [Tooltip("Seconds after the match starts before the camp first appears.")]
    [SerializeField] private float firstSpawnDelay = 15f;

    [Tooltip("Seconds after the last creature dies before the camp is back.")]
    [SerializeField] private float respawnDelay = 60f;

    private readonly List<NeutralCreep> alive = new List<NeutralCreep>();
    private Coroutine loop;

    /// <summary>Creatures of this camp still standing.</summary>
    public int AliveCount
    {
        get
        {
            alive.RemoveAll(c => c == null || !c.IsAlive);
            return alive.Count;
        }
    }

    private void OnEnable()
    {
        GameEvents.MatchStateChanged += OnMatchStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.MatchStateChanged -= OnMatchStateChanged;
    }

    private void Start()
    {
        // A lane started straight into play (no menus) has already announced its state.
        if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Playing)
        {
            Begin();
        }
    }

    private void OnMatchStateChanged(MatchState state)
    {
        if (state == MatchState.Playing)
        {
            Begin();
        }
        else if (state != MatchState.PreMatch && loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }
    }

    private void Begin()
    {
        if (loop == null)
        {
            loop = StartCoroutine(Loop());
        }
    }

    private IEnumerator Loop()
    {
        yield return new WaitUntil(() => MatchNet.CanSpawnWorld);
        yield return new WaitForSeconds(firstSpawnDelay);

        while (true)
        {
            Fill();
            yield return new WaitUntil(() => AliveCount == 0);
            yield return new WaitForSeconds(respawnDelay);
        }
    }

    private void Fill()
    {
        ExperienceCues.PlayAt("neutral_spawn", transform.position + Vector3.up, 0.5f);

        foreach (Slot slot in slots)
        {
            if (slot == null || slot.prefab == null)
            {
                continue;
            }

            Vector3 position = transform.TransformPoint(slot.localPosition);
            Quaternion rotation = transform.rotation * Quaternion.Euler(0f, slot.yaw, 0f);
            NeutralCreep created = null;

            MatchNet.Spawn(slot.prefab, position, rotation, spawned =>
            {
                if (spawned.TryGetComponent(out NeutralCreep creep))
                {
                    creep.Camp = this;
                    created = creep;
                }
            });

            if (created != null)
            {
                alive.Add(created);
            }
        }
    }

    /// <summary>One of the camp was hit by <paramref name="side"/>'s hero: all of it turns on them.</summary>
    public void Alert(Faction side)
    {
        foreach (NeutralCreep creep in alive)
        {
            if (creep != null && creep.IsAlive)
            {
                creep.Provoke(side);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 1f, 0.5f, 0.7f);

        foreach (Slot slot in slots)
        {
            if (slot != null)
            {
                Gizmos.DrawWireSphere(transform.TransformPoint(slot.localPosition) + Vector3.up, 0.8f);
            }
        }
    }
}
