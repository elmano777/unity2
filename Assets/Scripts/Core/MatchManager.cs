using UnityEngine;

/// <summary>
/// Runs one ritual: holds the match state, keeps the running tally that becomes a
/// <see cref="MatchStats"/> record and decides when the match is over. There is no time limit:
/// the ritual ends only when a patron falls (or the player leaves).
/// </summary>
/// <remarks>
/// Every counter is accumulated here by listening to <see cref="GameEvents"/> rather than having
/// each system write to a shared object. That keeps the stats honest — there is one writer — and
/// it means an authoritative host can later own this component without touching anything else.
/// No XR or networking references, so it ports to Unity 2022 as-is.
/// </remarks>
public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }

    [Header("Rules")]
    [Tooltip("Seconds of patron dialogue before the player is free to act.")]
    [SerializeField] private float preMatchSeconds = 3f;

    [Header("Cover (RF-15)")]
    [Tooltip("The player counts as in cover when a wall or block this close (metres) stands between their head and a threat.")]
    [SerializeField] private float coverReach = 2.5f;

    [Tooltip("Only threats this close (metres) count: a tower or rival further away isn't something to hide from.")]
    [SerializeField] private float threatRange = 30f;

    [Header("Debug")]
    [Tooltip("Logs every state change and stat increment to the console.")]
    [SerializeField] private bool verboseLogging;

    private const float CoverSampleSeconds = 0.25f;

    private readonly MatchStats stats = new MatchStats();
    private readonly RaycastHit[] coverHits = new RaycastHit[8];
    private float elapsedSeconds;
    private float nextCoverSample;
    private bool statsWritten;

    /// <summary>Current phase of the ritual.</summary>
    public MatchState State { get; private set; } = MatchState.PreMatch;

    /// <summary>
    /// True once a patron has fallen or the player left: towers, minions, weapons and damage all
    /// stop, so nothing happens behind the summary screen.
    /// </summary>
    public static bool IsOver => Instance != null && (Instance.State == MatchState.Win || Instance.State == MatchState.Lose);

    /// <summary>Seconds played so far.</summary>
    public float Elapsed => elapsedSeconds;

    /// <summary>
    /// The running record for this match. Read it for the end screen; it is only final once the
    /// match has reached a terminal state.
    /// </summary>
    public MatchStats Stats => stats;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        HeroDefinitionSO hero = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;
        string source = GameSession.Instance != null ? GameSession.Instance.MatchSource : "unknown";
        stats.Stamp(hero != null ? hero.heroName : null, source);
        stats.matchIndexInSession = GameSession.Instance != null ? GameSession.Instance.MatchesThisSession + 1 : 1;
    }

    private void OnEnable()
    {
        GameEvents.MinionKilled += OnMinionKilled;
        GameEvents.SoulsCollected += OnSoulsCollected;
        GameEvents.TowerDestroyed += OnTowerDestroyed;
        GameEvents.PatronDestroyed += OnPatronDestroyed;
        GameEvents.AbilityUsed += OnAbilityUsed;
        GameEvents.ItemPurchased += OnItemPurchased;
        GameEvents.ShotFired += OnShotFired;
        GameEvents.Died += OnDied;
    }

    private void OnDisable()
    {
        GameEvents.MinionKilled -= OnMinionKilled;
        GameEvents.SoulsCollected -= OnSoulsCollected;
        GameEvents.TowerDestroyed -= OnTowerDestroyed;
        GameEvents.PatronDestroyed -= OnPatronDestroyed;
        GameEvents.AbilityUsed -= OnAbilityUsed;
        GameEvents.ItemPurchased -= OnItemPurchased;
        GameEvents.ShotFired -= OnShotFired;
        GameEvents.Died -= OnDied;
    }

    /// <summary>Hero kills and deaths, read from which body fell: the rival's or this player's.</summary>
    private void OnDied(GameObject victim, DamageInfo info)
    {
        if (State != MatchState.Playing || victim == null)
        {
            return;
        }

        string layer = LayerMask.LayerToName(victim.layer);

        if (layer == "Rival")
        {
            stats.heroKills += 1;
        }
        else if (layer == "PlayerBody")
        {
            stats.deaths += 1;
        }
    }

    private void Start()
    {
        MatchStatsRecorder.LogEvent("match_start", stats.heroName);
        SetState(MatchState.PreMatch);
        Invoke(nameof(BeginPlaying), preMatchSeconds);
    }

    private void OnApplicationQuit()
    {
        // The player pulled the headset off or quit mid-ritual. An abandoned match is still data.
        if (State == MatchState.Playing || State == MatchState.PreMatch)
        {
            Finish(MatchState.Playing, MatchResult.Abandoned);
        }
    }

    private void Update()
    {
        if (State != MatchState.Playing)
        {
            return;
        }

        elapsedSeconds += Time.deltaTime;

        if (Time.time >= nextCoverSample)
        {
            nextCoverSample = Time.time + CoverSampleSeconds;

            if (IsBehindCover())
            {
                stats.secondsInCover += CoverSampleSeconds;
            }
        }
    }

    /// <summary>
    /// The player is behind cover when, for the closest threat in range (a hostile tower still
    /// standing, or the rival hero), the line from it to the player's head is blocked by scenery
    /// close to the player. A wall far away doesn't count: that is just being out of sight.
    /// </summary>
    private bool IsBehindCover()
    {
        Camera cam = Camera.main;

        if (cam == null)
        {
            return false;
        }

        Vector3 head = cam.transform.position;
        Faction local = MatchNet.LocalFaction;
        bool found = false;
        Vector3 threat = Vector3.zero;
        float best = threatRange;

        foreach (Structure s in Structure.Standing)
        {
            if (s is Tower && s.IsStanding && FactionUtility.IsHostile(s.Faction, local))
            {
                Vector3 p = s.transform.position + Vector3.up * 3f;
                float d = Vector3.Distance(p, head);

                if (d < best)
                {
                    best = d;
                    threat = p;
                    found = true;
                }
            }
        }

        NetworkPlayer rival = FindRival();

        if (rival != null)
        {
            Vector3 p = rival.transform.position + Vector3.up * 1.5f;
            float d = Vector3.Distance(p, head);

            if (d < best)
            {
                threat = p;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        // Cast from the player toward the threat: the first scenery hit within reach is the cover.
        Vector3 toThreat = threat - head;
        float reach = Mathf.Min(coverReach, toThreat.magnitude - 0.5f);
        int ground = LayerMask.GetMask("Ground", "Default");
        int count = Physics.RaycastNonAlloc(head, toThreat.normalized, coverHits, reach, ground, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            // Ignore the floor under the player's own feet: the ray only counts walls and blocks.
            if (coverHits[i].normal.y < 0.7f)
            {
                return true;
            }
        }

        return false;
    }

    private NetworkPlayer cachedRival;
    private float nextRivalSearch;

    /// <summary>The other player's hero, looked up every couple of seconds (null in single player).</summary>
    private NetworkPlayer FindRival()
    {
        if (cachedRival != null || Time.time < nextRivalSearch)
        {
            return cachedRival;
        }

        nextRivalSearch = Time.time + 2f;

        foreach (NetworkPlayer p in FindObjectsOfType<NetworkPlayer>())
        {
            if (p != NetworkPlayer.Local)
            {
                cachedRival = p;
                break;
            }
        }

        return cachedRival;
    }

    /// <summary>Ends the pre-match dialogue and hands control to the player.</summary>
    public void BeginPlaying()
    {
        if (State != MatchState.PreMatch)
        {
            return;
        }

        SetState(MatchState.Playing);
    }

    /// <summary>
    /// Records the player's like/dislike from the summary screen (RF-14). Safe to call after the
    /// match record was already written — it rewrites nothing, it only updates the in-memory copy
    /// and appends the vote as its own event.
    /// </summary>
    public void SubmitVote(MatchVote vote)
    {
        stats.vote = vote;
        MatchStatsRecorder.LogEvent("vote", vote.ToString());
    }

    /// <summary>Marks that the player chose to play again, for the Retention metric.</summary>
    public void RegisterPlayAgain()
    {
        stats.pressedPlayAgain = true;
        MatchStatsRecorder.LogEvent("play_again", stats.heroName);
    }

    /// <summary>
    /// Ends the ritual early — used by a "leave match" button and by the tutorial when the player
    /// bails out of the practice.
    /// </summary>
    public void Abandon()
    {
        if (State == MatchState.Playing || State == MatchState.PreMatch)
        {
            Finish(MatchState.Lose, MatchResult.Abandoned);
        }
    }

    private void OnMinionKilled(Faction faction)
    {
        if (State != MatchState.Playing)
        {
            return;
        }

        // Only the rival's minions count as this player's farm: Sapphire's for the host, Amber's for
        // the player who joined.
        if (faction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            stats.minionsKilled += 1;
        }
    }

    private void OnSoulsCollected(int amount) => stats.soulsCollected += Mathf.Max(0, amount);

    private void OnAbilityUsed(AbilitySO ability) => stats.abilitiesUsed += 1;

    private void OnItemPurchased(ShopItemSO item) => stats.itemsPurchased += 1;

    private void OnShotFired() => stats.shotsFired += 1;

    private void OnTowerDestroyed(Structure tower, Faction faction)
    {
        if (faction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            stats.towersDestroyed += 1;
        }
        else if (faction == MatchNet.LocalFaction)
        {
            stats.towersLost += 1;
        }
    }

    private void OnPatronDestroyed(Faction losingFaction)
    {
        if (losingFaction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            stats.patronDestroyed = true;
            stats.secondsToPatron = elapsedSeconds;
            Finish(MatchState.Win, MatchResult.Win);
        }
        else if (losingFaction == MatchNet.LocalFaction)
        {
            Finish(MatchState.Lose, MatchResult.Lose);
        }
    }

    private void Finish(MatchState finalState, MatchResult result)
    {
        if (statsWritten)
        {
            return;
        }

        statsWritten = true;

        stats.result = result;
        stats.durationSeconds = elapsedSeconds;

        SetState(finalState);

        PlayerProgress.RegisterMatchPlayed();
        if (GameSession.Instance != null)
        {
            GameSession.Instance.MatchesThisSession += 1;
        }

        MatchStatsRecorder.WriteMatch(stats);
        MatchStatsRecorder.LogEvent("match_end", result.ToString());

        if (verboseLogging)
        {
            Debug.Log($"[MatchManager] {result} after {elapsedSeconds:F1}s — " +
                      $"{stats.soulsCollected} souls, {stats.minionsKilled} minions, {stats.towersDestroyed} towers.");
        }
    }

    private void SetState(MatchState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        GameEvents.RaiseMatchStateChanged(next);

        if (verboseLogging)
        {
            Debug.Log($"[MatchManager] state -> {next}");
        }
    }
}
