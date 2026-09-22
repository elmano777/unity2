using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes the evaluation data for RF-16 to <see cref="Application.persistentDataPath"/>.
/// This is the only source the HEART analysis reads from, so it has to survive a crash, a quit
/// and a headset battery dying — every record is flushed the moment it is produced.
/// </summary>
/// <remarks>
/// Two files, both newline-delimited JSON so they can be appended to safely and opened with any
/// tool without parsing a whole array:
/// <list type="bullet">
/// <item><c>matches.jsonl</c> — one <see cref="MatchStats"/> per finished ritual.</item>
/// <item><c>events.jsonl</c> — menu presses and flow milestones, for Adoption and Retention.</item>
/// </list>
/// On Quest these land in <c>/sdcard/Android/data/&lt;package&gt;/files/</c>, which is reachable
/// over adb or by mounting the headset, so the data can be pulled after a test session.
/// </remarks>
public static class MatchStatsRecorder
{
    private const string MatchesFileName = "matches.jsonl";
    private const string EventsFileName = "events.jsonl";

    /// <summary>One line of <c>events.jsonl</c>.</summary>
    [Serializable]
    private class LoggedEvent
    {
        public string playerId;
        public string atUtc;
        public string name;
        public string value;
    }

    /// <summary>Full path of the per-match stats file, handy for a debug readout in the editor.</summary>
    public static string MatchesFilePath => Path.Combine(Application.persistentDataPath, MatchesFileName);

    /// <summary>Full path of the event stream file.</summary>
    public static string EventsFilePath => Path.Combine(Application.persistentDataPath, EventsFileName);

    /// <summary>
    /// Appends one finished ritual. Called once per match, including matches the player abandoned,
    /// because a high abandon rate is itself a result worth reporting.
    /// </summary>
    public static void WriteMatch(MatchStats stats)
    {
        if (stats == null)
        {
            return;
        }

        Append(MatchesFilePath, JsonUtility.ToJson(stats));
    }

    /// <summary>
    /// Appends a flow event. Use short stable names — <c>menu_shown</c>, <c>online_pressed</c>,
    /// <c>mission_pressed</c>, <c>mission_completed</c>, <c>match_start</c>, <c>play_again</c> —
    /// because the analysis groups on them.
    /// </summary>
    public static void LogEvent(string name, string value = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        LoggedEvent entry = new LoggedEvent
        {
            playerId = PlayerProgress.PlayerId,
            atUtc = DateTime.UtcNow.ToString("o"),
            name = name,
            value = value ?? string.Empty,
        };

        Append(EventsFilePath, JsonUtility.ToJson(entry));
    }

    /// <summary>
    /// Reads every recorded match back. Used by the in-headset debug panel and, after a test
    /// session, to sanity-check the data before exporting it.
    /// </summary>
    public static List<MatchStats> ReadAllMatches()
    {
        List<MatchStats> results = new List<MatchStats>();

        try
        {
            if (!File.Exists(MatchesFilePath))
            {
                return results;
            }

            foreach (string line in File.ReadAllLines(MatchesFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                MatchStats parsed = JsonUtility.FromJson<MatchStats>(line);
                if (parsed != null)
                {
                    results.Add(parsed);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MatchStatsRecorder] Could not read '{MatchesFilePath}': {e.Message}");
        }

        return results;
    }

    /// <summary>
    /// Deletes both files. Only for resetting between participants during a testing session —
    /// there is no undo, so it should sit behind an explicit confirmation in any UI that calls it.
    /// </summary>
    public static void DeleteAll()
    {
        TryDelete(MatchesFilePath);
        TryDelete(EventsFilePath);
    }

    private static void Append(string path, string line)
    {
        try
        {
            // Append + flush per line: a match that ends with the player yanking the headset off
            // still leaves a complete record behind.
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception e)
        {
            // Never let telemetry take the game down with it.
            Debug.LogWarning($"[MatchStatsRecorder] Could not write to '{path}': {e.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MatchStatsRecorder] Could not delete '{path}': {e.Message}");
        }
    }
}
