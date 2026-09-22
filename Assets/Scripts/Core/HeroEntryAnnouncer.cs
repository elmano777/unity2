using System.Collections;
using UnityEngine;

/// <summary>
/// Match scene: plays the selected hero's entry voice line once, shortly after the scene starts
/// (the delay keeps it clear of scene-load hitches and lets the screen fade in first).
/// Skipped when the line already played during the Hero Select story intro
/// (<see cref="GameSession.EntryLinePlayed"/>); the flag is consumed here, so reloading the match
/// scene later plays the line again.
/// Does nothing if there is no GameSession / selected hero (e.g. pressing Play directly in this scene).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class HeroEntryAnnouncer : MonoBehaviour
{
    [Tooltip("Seconds (unscaled) to wait after the scene starts before playing the entry line.")]
    [SerializeField] private float delay = 0.5f;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
    }

    private IEnumerator Start()
    {
        GameSession session = GameSession.Instance;
        HeroDefinitionSO hero = session != null ? session.SelectedHero : null;
        if (hero == null || hero.entryClip == null)
        {
            yield break;
        }

        if (session.EntryLinePlayed)
        {
            session.EntryLinePlayed = false;
            Debug.Log($"HeroEntryAnnouncer: entry line for {hero.heroName} already played in the intro; skipping.");
            yield break;
        }

        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        audioSource.clip = hero.entryClip;
        audioSource.Play();
        Debug.Log($"HeroEntryAnnouncer: playing entry line '{hero.entryClip.name}' for {hero.heroName}.");
    }
}
