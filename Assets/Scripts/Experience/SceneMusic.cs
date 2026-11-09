using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Background music for the menus: a loop that fades in when the title screen or the hero select loads
/// and goes away with the scene. The files are <c>Resources/Cues/music_title</c> and
/// <c>music_menu</c>; with no file nothing plays. The Lane has no music: its sound is the place itself.
/// </summary>
public class SceneMusic : MonoBehaviour
{
    [SerializeField] private float volume = 0.35f;
    [SerializeField] private float fadeSeconds = 2.5f;

    private AudioSource source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string cue = scene.name == "TitleScreen" ? "music_title" : scene.name == "HeroSelect" ? "music_menu" : null;

        if (cue == null || FindObjectOfType<SceneMusic>() != null)
        {
            return;
        }

        SceneMusic music = new GameObject(nameof(SceneMusic)).AddComponent<SceneMusic>();
        music.Begin(cue);
    }

    private void Begin(string cue)
    {
        AudioClip clip = ExperienceCues.Clip(cue);

        if (clip == null)
        {
            return;
        }

        source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.Play();
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            source.volume = Mathf.Lerp(0f, volume, t / fadeSeconds);
            yield return null;
        }

        source.volume = volume;
    }
}
