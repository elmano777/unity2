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

    /// <summary>True when nothing but the transform shares this object.</summary>
    private bool IsAlone => GetComponents<Component>().Length <= 2;

    private void Awake()
    {
        // In the Lane this sits on "Managers" next to MatchManager, PlayerWallet, AbilityFx... Coming
        // from HeroSelect the session already exists, and destroying the whole object took the wallet
        // and the ability effects with it. Only the extra component goes.
        if (Instance != null && Instance != this)
        {
            if (IsAlone)
            {
                Destroy(gameObject);
            }
            else
            {
                Destroy(this);
            }

            return;
        }

        // Playing the Lane straight: persisting this object would carry the Lane's managers into the
        // next scenes. Move to an object of its own first (it has no serialized fields to copy).
        if (!IsAlone)
        {
            var holder = new GameObject(nameof(GameSession));
            holder.AddComponent<GameSession>();
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Unity freezes Play Mode when the Editor window loses focus, which stalls the
        // MCP bridge (see CLAUDE.md). Keep the game simulating in the background.
        Application.runInBackground = true;
    }
}
