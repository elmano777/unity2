using UnityEngine;

/// <summary>
/// Small persistent singleton that carries player choices (e.g. selected hero)
/// across scene loads, from Hero Select into the match scene.
/// </summary>
public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    public HeroDefinitionSO SelectedHero { get; set; }

    /// <summary>
    /// True when the selected hero's entry voice line already played before the match scene loaded
    /// (during the story intro), so the match scene shouldn't play it again. Cleared when a hero is
    /// confirmed and consumed by <see cref="HeroEntryAnnouncer"/>.
    /// </summary>
    public bool EntryLinePlayed { get; set; }

    /// <summary>
    /// Which menu button led into this match: "online" for the ritual, "mission" for the guided
    /// practice. Recorded on every <see cref="MatchStats"/> so the Adoption metric can tell the
    /// two entry points apart.
    /// </summary>
    public string MatchSource { get; set; } = "unknown";

    /// <summary>
    /// How many rituals have finished since the app launched. Drives the Retention metric, which
    /// asks whether a player starts a second match rather than whether they ever played one.
    /// </summary>
    public int MatchesThisSession { get; set; }

    /// <summary>
    /// True while the player is inside "Mi misión", so the match scene can skip the rival, the
    /// time cap and the win condition, and the stats record can be tagged as practice.
    /// </summary>
    public bool IsPracticeRun { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
