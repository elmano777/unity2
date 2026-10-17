using UnityEngine;

/// <summary>
/// The handful of things the game remembers between launches, stored in PlayerPrefs (RF-04).
/// Kept tiny on purpose: anything worth analysing later goes to <see cref="MatchStatsRecorder"/>
/// instead, because PlayerPrefs is for preferences, not for a data set.
/// </summary>
public static class PlayerProgress
{
    private const string MissionCompletedKey = "deadlock.mission.completed";
    private const string MatchesPlayedKey = "deadlock.matches.played";
    private const string LastHeroKey = "deadlock.hero.last";
    private const string SubtitlesKey = "deadlock.settings.subtitles";
    private const string VolumeKey = "deadlock.settings.volume";
    private const string PlayerIdKey = "deadlock.player.id";

    /// <summary>
    /// True once the player finished "Mi misión". The menu recommends the practice while this is
    /// false and leaves it as a plain option afterwards, so nobody is forced through it twice.
    /// </summary>
    public static bool MissionCompleted
    {
        get => PlayerPrefs.GetInt(MissionCompletedKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(MissionCompletedKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>How many rituals this player has finished, on this device, ever.</summary>
    public static int MatchesPlayed
    {
        get => PlayerPrefs.GetInt(MatchesPlayedKey, 0);
        private set
        {
            PlayerPrefs.SetInt(MatchesPlayedKey, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Name of the hero picked last time, so the select screen can pre-highlight it.</summary>
    public static string LastHero
    {
        get => PlayerPrefs.GetString(LastHeroKey, string.Empty);
        set
        {
            PlayerPrefs.SetString(LastHeroKey, value ?? string.Empty);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Whether voice lines show subtitles (RNF-08). On by default for accessibility.</summary>
    public static bool SubtitlesEnabled
    {
        get => PlayerPrefs.GetInt(SubtitlesKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(SubtitlesKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Overall volume, 0..1 (RNF-08). Applied through <see cref="AudioListener.volume"/> so voices,
    /// shots and effects all follow it without each AudioSource having to know about the setting.
    /// </summary>
    public static float Volume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        set
        {
            float v = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, v);
            PlayerPrefs.Save();
            AudioListener.volume = v;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplySavedVolume()
    {
        AudioListener.volume = Volume;
    }

    /// <summary>
    /// Anonymous per-device id written into every stats record, so a session's matches can be
    /// grouped when analysing retention without identifying the person behind them.
    /// </summary>
    public static string PlayerId
    {
        get
        {
            string stored = PlayerPrefs.GetString(PlayerIdKey, string.Empty);
            if (string.IsNullOrEmpty(stored))
            {
                stored = System.Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString(PlayerIdKey, stored);
                PlayerPrefs.Save();
            }

            return stored;
        }
    }

    /// <summary>Called by the match manager when a ritual ends, however it ended.</summary>
    public static void RegisterMatchPlayed()
    {
        MatchesPlayed += 1;
    }

    /// <summary>
    /// Wipes everything. Only for testing sessions where each participant should start clean —
    /// call it from an editor menu or a debug button, never from normal gameplay.
    /// </summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(MissionCompletedKey);
        PlayerPrefs.DeleteKey(MatchesPlayedKey);
        PlayerPrefs.DeleteKey(LastHeroKey);
        PlayerPrefs.DeleteKey(SubtitlesKey);
        PlayerPrefs.DeleteKey(VolumeKey);
        PlayerPrefs.DeleteKey(PlayerIdKey);
        PlayerPrefs.Save();
    }
}
