using System;

/// <summary>How a ritual ended.</summary>
public enum MatchResult
{
    /// <summary>The player left before either patron fell.</summary>
    Abandoned = 0,
    Win = 1,
    Lose = 2,

    /// <summary>The 25-minute cap ran out with both patrons standing.</summary>
    TimeUp = 3,
}

/// <summary>The optional like/dislike the player gives on the summary screen (RF-14).</summary>
public enum MatchVote
{
    None = 0,
    Like = 1,
    Dislike = 2,
}

/// <summary>
/// Everything one ritual produced, written to disk as a single JSON record (RF-16).
/// Every field here exists because a HEART metric needs it — see the mapping below.
/// </summary>
/// <remarks>
/// Plain [Serializable] class with only primitives and enums, because JsonUtility is the one
/// serializer guaranteed to behave identically in Unity 2022 and Unity 6.
/// </remarks>
[Serializable]
public class MatchStats
{
    // ---- Identity ----

    /// <summary>Anonymous device id, so several matches can be grouped into one session.</summary>
    public string playerId;

    /// <summary>ISO-8601 UTC timestamp of when the match ended.</summary>
    public string endedAtUtc;

    /// <summary>Which hero was played, for comparing experience between Seven and Vindicta.</summary>
    public string heroName;

    /// <summary>Index of this match within the current app launch, starting at 1 (Retention).</summary>
    public int matchIndexInSession;

    /// <summary>Which menu button started this match: "online" or "mission" (Adoption).</summary>
    public string startedFrom;

    /// <summary>Whether the player had already completed the practice before this match.</summary>
    public bool missionCompletedBefore;

    // ---- Task success (HEART) ----

    public MatchResult result;

    /// <summary>Towers destroyed by the player. Task success threshold is "at least one".</summary>
    public int towersDestroyed;

    /// <summary>True when the player brought down the rival's patron.</summary>
    public bool patronDestroyed;

    /// <summary>Seconds from match start to the patron falling, or 0 if it never did.</summary>
    public float secondsToPatron;

    // ---- Engagement (HEART) ----

    /// <summary>Total seconds played, however the match ended.</summary>
    public float durationSeconds;

    public int soulsCollected;
    public int minionsKilled;
    public int abilitiesUsed;
    public int itemsPurchased;
    public int shotsFired;

    /// <summary>Seconds spent behind cover, as a rough read on whether RF-15 gets used.</summary>
    public float secondsInCover;

    // ---- Happiness (HEART) ----

    /// <summary>The optional vote. <see cref="MatchVote.None"/> means the player skipped it.</summary>
    public MatchVote vote;

    // ---- Retention (HEART) ----

    /// <summary>True when the player pressed "Jugar otra vez" instead of leaving.</summary>
    public bool pressedPlayAgain;

    /// <summary>Fills in the fields that are the same for every record.</summary>
    public void Stamp(string hero, string source)
    {
        playerId = PlayerProgress.PlayerId;
        endedAtUtc = DateTime.UtcNow.ToString("o");
        heroName = string.IsNullOrEmpty(hero) ? "unknown" : hero;
        startedFrom = string.IsNullOrEmpty(source) ? "unknown" : source;
        missionCompletedBefore = PlayerProgress.MissionCompleted;
    }
}
