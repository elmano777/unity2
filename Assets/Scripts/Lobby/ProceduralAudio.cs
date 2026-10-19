using UnityEngine;

/// <summary>
/// Sounds for the lobby built in code, so the project needs no extra audio files: a crystal chime
/// for striking the orb and a low drone that hums around it.
/// </summary>
public static class ProceduralAudio
{
    private const int SampleRate = 44100;

    private static AudioClip chime;
    private static AudioClip drone;

    /// <summary>Bell-like strike: a few inharmonic partials that ring out over ~2.5 s.</summary>
    public static AudioClip Chime()
    {
        if (chime != null) return chime;

        float[] partials = { 523.25f, 784f, 1046.5f, 1567.98f, 2093f };
        float[] gains = { 0.5f, 0.3f, 0.22f, 0.12f, 0.08f };
        float[] decays = { 1.4f, 2f, 2.6f, 3.4f, 4.2f };

        int length = (int)(SampleRate * 2.5f);
        var data = new float[length];

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float attack = Mathf.Clamp01(t / 0.004f);
            float sample = 0f;

            for (int p = 0; p < partials.Length; p++)
            {
                sample += Mathf.Sin(2f * Mathf.PI * partials[p] * t) * gains[p] * Mathf.Exp(-decays[p] * t);
            }

            data[i] = sample * attack * 0.6f;
        }

        chime = AudioClip.Create("OracleChime", length, 1, SampleRate, false);
        chime.SetData(data, 0);
        return chime;
    }

    /// <summary>
    /// Seamless 4 s loop: every frequency fits a whole number of cycles in 4 s, so the loop point
    /// is inaudible.
    /// </summary>
    public static AudioClip Drone()
    {
        if (drone != null) return drone;

        const float seconds = 4f;
        int length = (int)(SampleRate * seconds);
        var data = new float[length];

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
            float sample =
                Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.5f +
                Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.3f +
                Mathf.Sin(2f * Mathf.PI * 110f * t + Mathf.Sin(2f * Mathf.PI * 0.5f * t)) * 0.2f;
            data[i] = sample * swell * 0.35f;
        }

        drone = AudioClip.Create("OracleDrone", length, 1, SampleRate, false);
        drone.SetData(data, 0);
        return drone;
    }
}
