using UnityEngine;

/// <summary>
/// Runs one ritual: holds the match state, keeps the running tally that becomes a
/// <see cref="MatchStats"/> record, enforces the time cap and decides when the match is over.
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
    [Tooltip("Hard cap on a ritual, in minutes. The design brief sets this at about 25.")]
    [SerializeField] private float matchLengthMinutes = 25f;

    [Tooltip("Seconds of patron dialogue before the player is free to act.")]
    [SerializeField] private float preMatchSeconds = 3f;

    [Header("Debug")]
    [Tooltip("Logs every state change and stat increment to the console.")]
    [SerializeField] private bool verboseLogging;

    private readonly MatchStats stats = new MatchStats();
    private float elapsedSeconds;
    private bool statsWritten;

    /// <summary>Current phase of the ritual.</summary>
    public MatchState State { get; private set; } = MatchState.PreMatch;

    /// <summary>Seconds played so far.</summary>
    public float Elapsed => elapsedSeconds;

    /// <summary>Seconds left before the cap, floored at zero.</summary>
    public float Remaining => Mathf.Max(0f, matchLengthMinutes * 60f - elapsedSeconds);

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

        if (Remaining <= 0f)
        {
            Finish(MatchState.TimeUp, MatchResult.TimeUp);
        }
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

        // Only the rival's minions count as the player's farm.
        if (faction == Faction.Enemy)
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
        if (faction == Faction.Enemy)
        {
            stats.towersDestroyed += 1;
        }
    }

    private void OnPatronDestroyed(Faction losingFaction)
    {
        if (losingFaction == Faction.Enemy)
        {
            stats.patronDestroyed = true;
            stats.secondsToPatron = elapsedSeconds;
            Finish(MatchState.Win, MatchResult.Win);
        }
        else if (losingFaction == Faction.Player)
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
