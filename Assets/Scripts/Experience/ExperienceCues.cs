using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short sounds that make the ritual feel alive (a soul, a hit, a tower falling, victory). Each cue
/// looks for a real clip at <c>Resources/Cues/&lt;name&gt;</c> first, so dropping a file with that name
/// into <c>Assets/Resources/Cues/</c> replaces the placeholder without touching a scene or a script.
/// While no file exists, a simple tone built in code stands in, so every cue is already audible.
/// </summary>
public static class ExperienceCues
{
    public const string Soul = "soul_collect";
    public const string SoulDenied = "soul_denied";
    public const string Hit = "player_hit";
    public const string LowHealth = "heartbeat";
    public const string TowerWarning = "tower_warning";
    public const string TowerFall = "tower_fall";
    public const string Victory = "victory_sting";
    public const string Defeat = "defeat_sting";
    public const string Result = "result_panel";
    public const string Ready = "ability_ready";
    public const string HitMajor = "player_hit_major";
    public const string PlayerDown = "player_down";
    public const string HitConfirm = "hit_confirm";
    public const string KillConfirm = "kill_confirm";
    public const string HeroKill = "hero_kill";
    public const string Revive = "respawn_revive";
    public const string NotReady = "ability_not_ready";
    public const string ShopUpgrade = "shop_upgrade";
    public const string LowHealthAlert = "low_health_alert";
    public const string HintPing = "hint_ping";
    public const string ZiplineStart = "zipline_start";
    public const string ZiplineLoop = "zipline_loop";

    private const int SampleRate = 44100;
    private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    /// <summary>Plays a cue in the head (not positional). Volume 0..1.</summary>
    public static void Play(string cue, float volume = 1f)
    {
        AudioClip clip = Get(cue);

        if (clip == null)
        {
            return;
        }

        Vector3 at = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Spawn(clip, at, volume, 0f);
    }

    /// <summary>Plays a cue at a place in the world, so it comes from that direction.</summary>
    public static void PlayAt(string cue, Vector3 position, float volume = 1f)
    {
        AudioClip clip = Get(cue);

        if (clip != null)
        {
            Spawn(clip, position, volume, 1f);
        }
    }

    /// <summary>Plays any clip once at a place, 3D (spatialBlend 1) or in the head (0), then removes itself.</summary>
    public static void Spawn(AudioClip clip, Vector3 position, float volume, float spatialBlend)
    {
        GameObject go = new GameObject("Cue_" + clip.name);
        go.transform.position = position;
        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 30f;
        source.Play();
        Object.Destroy(go, clip.length + 0.2f);
    }

    /// <summary>The clip behind a cue (a file in Resources/Cues, or the placeholder tone), or null if there is none.</summary>
    public static AudioClip Clip(string cue) => Get(cue);

    private static AudioClip Get(string cue)
    {
        if (cache.TryGetValue(cue, out AudioClip cached) && cached != null)
        {
            return cached;
        }

        AudioClip clip = Resources.Load<AudioClip>("Cues/" + cue) ?? Build(cue);
        cache[cue] = clip;
        return clip;
    }

    // ---- Placeholder tones ----

    private static AudioClip Build(string cue)
    {
        switch (cue)
        {
            case Soul: return Tone(cue, 0.22f, t => Mathf.Sin(2f * Mathf.PI * (660f + 900f * t) * t), 14f, 0.35f);
            case SoulDenied: return Tone(cue, 0.3f, t => Mathf.Sin(2f * Mathf.PI * (520f - 380f * t) * t) * 0.9f, 9f, 0.4f);
            case Hit: return Tone(cue, 0.28f, t => Mathf.Sin(2f * Mathf.PI * (140f - 70f * t) * t) + Noise() * 0.25f, 11f, 0.7f);
            case LowHealth: return Heartbeat(cue);
            case TowerWarning: return Tone(cue, 0.55f, t => Mathf.Sin(2f * Mathf.PI * (t < 0.25f ? 392f : 523f) * t) * 0.8f, 3.5f, 0.45f);
            case TowerFall: return Tone(cue, 2.2f, t => Mathf.Sin(2f * Mathf.PI * (70f - 20f * t) * t) + Noise() * (1f - t / 2.2f) * 0.6f, 1.4f, 0.8f);
            case Victory: return Arpeggio(cue, new[] { 392f, 494f, 587f, 784f }, 0.18f, 1.6f, 0.55f);
            case Defeat: return Arpeggio(cue, new[] { 392f, 311f, 262f, 196f }, 0.28f, 1.8f, 0.5f);
            case Result: return Tone(cue, 0.9f, t => Mathf.Sin(2f * Mathf.PI * 523f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 784f * t) * 0.3f, 3f, 0.35f);
            case Ready: return Tone(cue, 0.3f, t => Mathf.Sin(2f * Mathf.PI * 880f * t), 9f, 0.3f);
            default: return null;
        }
    }

    private static float Noise() => Random.value * 2f - 1f;

    private static AudioClip Tone(string name, float seconds, System.Func<float, float> wave, float decay, float gain)
    {
        int length = (int)(SampleRate * seconds);
        float[] data = new float[length];

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float attack = Mathf.Clamp01(t / 0.004f);
            data[i] = wave(t) * attack * Mathf.Exp(-decay * t) * gain;
        }

        return Make(name, data);
    }

    private static AudioClip Arpeggio(string name, float[] notes, float step, float seconds, float gain)
    {
        int length = (int)(SampleRate * seconds);
        float[] data = new float[length];

        for (int n = 0; n < notes.Length; n++)
        {
            int start = (int)(n * step * SampleRate);

            for (int i = start; i < length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                data[i] += (Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * notes[n] * t))
                           * Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-2.4f * t) * gain * 0.5f;
            }
        }

        return Make(name, data);
    }

    private static AudioClip Heartbeat(string name)
    {
        int length = (int)(SampleRate * 0.9f);
        float[] data = new float[length];
        float[] starts = { 0f, 0.22f };

        foreach (float s in starts)
        {
            int start = (int)(s * SampleRate);

            for (int i = start; i < length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                data[i] += Mathf.Sin(2f * Mathf.PI * 58f * t) * Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-16f * t) * 0.8f;
            }
        }

        return Make(name, data);
    }

    private static AudioClip Make(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
