using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// The sound of a place: looping beds that fade in when the scene starts, and one-shots that go off at
/// random now and then (a pigeon, a ring of crystal) from somewhere around the player. The channels of a
/// multichannel bed (L, R and the two surrounds of the garage) are separate loops, started together on
/// the same audio clock so they stay in phase, and panned across the ears.
/// </summary>
public class AmbientSoundscape : MonoBehaviour
{
    [Serializable]
    public class Bed
    {
        public AudioClip clip;

        [Range(0f, 1f)]
        public float volume = 0.6f;

        [Tooltip("-1 is the left ear and 1 the right. Surround channels sit part way, quieter.")]
        [Range(-1f, 1f)]
        public float pan;
    }

    [Serializable]
    public class Scatter
    {
        [Tooltip("Variants of the same sound; one is picked at random each time, never the same twice in a row.")]
        public AudioClip[] clips;

        [Tooltip("Seconds to wait between one and the next, picked at random between these two.")]
        public Vector2 interval = new Vector2(8f, 20f);

        [Range(0f, 1f)]
        public float volume = 0.5f;

        [Tooltip("How far from the player it plays, in metres (min, max).")]
        public Vector2 distance = new Vector2(3f, 8f);

        [Tooltip("Height of the sound relative to the player's head, in metres (min, max).")]
        public Vector2 height = new Vector2(-0.5f, 1.5f);

        [Tooltip("Pitch is shifted a little each time so the variants don't sound repeated.")]
        public Vector2 pitch = new Vector2(0.96f, 1.04f);
    }

    [SerializeField] private Bed[] beds;
    [SerializeField] private Scatter[] scatters;

    [Tooltip("Seconds the beds take to fade in, so the place doesn't pop into existence.")]
    [SerializeField] private float fadeIn = 2f;

    private AudioSource[] bedSources;
    private float[] lastPicked;
    private float fade;

    private void OnEnable()
    {
        bedSources = new AudioSource[beds != null ? beds.Length : 0];
        double start = AudioSettings.dspTime + 0.2;

        for (int i = 0; i < bedSources.Length; i++)
        {
            if (beds[i].clip == null)
            {
                continue;
            }

            var source = new GameObject("Bed_" + beds[i].clip.name).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.clip = beds[i].clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.panStereo = beds[i].pan;
            source.volume = 0f;
            source.playOnAwake = false;
            source.PlayScheduled(start);
            bedSources[i] = source;
        }

        fade = 0f;
        lastPicked = new float[scatters != null ? scatters.Length : 0];

        for (int i = 0; i < lastPicked.Length; i++)
        {
            lastPicked[i] = -1f;
            StartCoroutine(RunScatter(i));
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();

        foreach (AudioSource source in bedSources)
        {
            if (source != null)
            {
                Destroy(source.gameObject);
            }
        }
    }

    private void Update()
    {
        fade = fadeIn > 0f ? Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / fadeIn) : 1f;

        for (int i = 0; i < bedSources.Length; i++)
        {
            if (bedSources[i] != null)
            {
                bedSources[i].volume = beds[i].volume * fade;
            }
        }
    }

    private IEnumerator RunScatter(int index)
    {
        Scatter scatter = scatters[index];

        if (scatter.clips == null || scatter.clips.Length == 0)
        {
            yield break;
        }

        // The first one comes sooner than a full interval, so a short visit still hears something.
        yield return new WaitForSeconds(UnityEngine.Random.Range(scatter.interval.x * 0.3f, scatter.interval.y * 0.6f));

        while (true)
        {
            Play(scatter, index);
            yield return new WaitForSeconds(UnityEngine.Random.Range(scatter.interval.x, scatter.interval.y));
        }
    }

    private void Play(Scatter scatter, int index)
    {
        int pick = UnityEngine.Random.Range(0, scatter.clips.Length);

        if (scatter.clips.Length > 1 && pick == (int)lastPicked[index])
        {
            pick = (pick + 1) % scatter.clips.Length;
        }

        lastPicked[index] = pick;
        AudioClip clip = scatter.clips[pick];

        if (clip == null)
        {
            return;
        }

        Transform listener = Camera.main != null ? Camera.main.transform : transform;
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float distance = UnityEngine.Random.Range(scatter.distance.x, scatter.distance.y);
        Vector3 position = listener.position + new Vector3(Mathf.Cos(angle) * distance, UnityEngine.Random.Range(scatter.height.x, scatter.height.y), Mathf.Sin(angle) * distance);

        var shot = new GameObject("Scatter_" + clip.name);
        shot.transform.SetParent(transform, true);
        shot.transform.position = position;

        AudioSource source = shot.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = scatter.volume * fade;
        source.pitch = UnityEngine.Random.Range(scatter.pitch.x, scatter.pitch.y);
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = Mathf.Max(scatter.distance.y + 6f, 12f);
        source.Play();
        Destroy(shot, clip.length / Mathf.Max(0.1f, source.pitch) + 0.5f);
    }
}
