/// <summary>
/// Phases of a ritual. The HUD, the hint system and the end screen all key off this, so the
/// match flow lives in exactly one place instead of being inferred from scattered booleans.
/// </summary>
public enum MatchState
{
    /// <summary>Scene loaded, patron speaking, player not yet free to act.</summary>
    PreMatch = 0,

    /// <summary>The ritual is running.</summary>
    Playing = 1,

    /// <summary>The rival's patron fell.</summary>
    Win = 2,

    /// <summary>The player's own patron fell.</summary>
    Lose = 3,

    /// <summary>The time cap ran out with both patrons standing.</summary>
    TimeUp = 4,
}
