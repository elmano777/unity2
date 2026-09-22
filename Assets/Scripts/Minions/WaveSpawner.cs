using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends a wave of minions down the lane every so often, for one side. Put two of these in the
/// scene — one per faction, walking the same <see cref="LanePath"/> in opposite directions — and
/// the lane fights itself, which is how the whole system can be verified without a player.
/// </summary>
public class WaveSpawner : MonoBehaviour
{
    /// <summary>
    /// One slot of a wave: how many of a given minion to send. The order of these entries is the
    /// marching order, so melee goes first, troopers follow and the medic walks at the back.
    /// </summary>
    [System.Serializable]
    public class WaveEntry
    {
        public MinionDefinitionSO definition;

        [Min(0)]
        public int count = 1;
    }

    [Header("What to spawn")]
    [Tooltip("The wave, in marching order. The design calls for 2 melee, 3 troopers and 1 medic.")]
    [SerializeField]
    private WaveEntry[] composition = new WaveEntry[0];

    [Tooltip("Side the spawned minions fight for.")]
    [SerializeField] private Faction faction = Faction.Enemy;

    [Header("Lane")]
    [SerializeField] private LanePath lane;

    [Tooltip("True for the side that starts at the far end and walks the lane backwards.")]
    [SerializeField] private bool walkReversed;

    [Tooltip("Where minions appear. Defaults to this transform.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Timing")]
    [Tooltip("Seconds before the first wave, counted from when the match starts playing.")]
    [SerializeField] private float firstWaveDelay = 5f;

    [Tooltip("Seconds between waves.")]
    [SerializeField] private float waveInterval = 30f;

    [Tooltip("Seconds between each minion within a wave, so they walk out in a column.")]
    [SerializeField] private float spawnSpacing = 0.6f;

    [Tooltip("Sideways scatter at the spawn point, in metres, so they don't stack up.")]
    [SerializeField] private float spawnScatter = 0.8f;

    [Header("Limits")]
    [Tooltip("Stops spawning past this many live minions from this spawner. Protects the frame rate on Quest.")]
    [SerializeField] private int maxAlive = 12;

    [Header("Behaviour")]
    [Tooltip("Start spawning immediately instead of waiting for the match to reach Playing. Useful for the practice scene.")]
    [SerializeField] private bool autoStart;

    private readonly List<MinionAgent> alive = new List<MinionAgent>();
    private Coroutine loop;

    /// <summary>How many minions from this spawner are currently in the lane.</summary>
    public int AliveCount
    {
        get
        {
            alive.RemoveAll(m => m == null || m.CurrentState == MinionAgent.State.Dead);
            return alive.Count;
        }
    }

    /// <summary>Total minions one full wave would send, useful for balancing readouts.</summary>
    public int WaveSize
    {
        get
        {
            int total = 0;

            foreach (WaveEntry entry in composition)
            {
                if (entry != null && entry.definition != null)
                {
                    total += Mathf.Max(0, entry.count);
                }
            }

            return total;
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
        if (spawnPoint == null)
        {
            spawnPoint = transform;
        }

        if (autoStart)
        {
            StartSpawning();
        }
    }

    /// <summary>Begins the wave loop. Safe to call more than once.</summary>
    public void StartSpawning()
    {
        if (loop == null)
        {
            loop = StartCoroutine(SpawnLoop());
        }
    }

    /// <summary>Stops sending new waves. Minions already in the lane keep going.</summary>
    public void StopSpawning()
    {
        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }
    }

    private void OnMatchStateChanged(MatchState state)
    {
        if (state == MatchState.Playing)
        {
            StartSpawning();
        }
        else if (state != MatchState.PreMatch)
        {
            // Match is over, one way or another.
            StopSpawning();
        }
    }

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(firstWaveDelay);

        while (true)
        {
            yield return SpawnWave();
            yield return new WaitForSeconds(waveInterval);
        }
    }

    private IEnumerator SpawnWave()
    {
        foreach (WaveEntry entry in composition)
        {
            if (entry == null || entry.definition == null)
            {
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                if (AliveCount >= maxAlive)
                {
                    // Lane is congested; skip the rest of this wave rather than piling up.
                    yield break;
                }

                SpawnOne(entry.definition);
                yield return new WaitForSeconds(spawnSpacing);
            }
        }
    }

    private void SpawnOne(MinionDefinitionSO definition)
    {
        if (definition.prefab == null)
        {
            Debug.LogWarning($"[WaveSpawner] '{definition.name}' has no prefab assigned.", this);
            return;
        }

        Vector3 scatter = new Vector3(Random.Range(-spawnScatter, spawnScatter), 0f, Random.Range(-spawnScatter, spawnScatter));
        Vector3 position = spawnPoint.position + scatter;

        GameObject spawned = Instantiate(definition.prefab, position, spawnPoint.rotation);

        if (spawned.TryGetComponent(out MinionAgent agent))
        {
            agent.Initialize(definition, lane, faction, walkReversed);
            alive.Add(agent);
        }
        else
        {
            Debug.LogWarning($"[WaveSpawner] Prefab '{definition.prefab.name}' has no MinionAgent.", this);
        }
    }
}
