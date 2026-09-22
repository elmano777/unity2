using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// Drives the Hero Select screen.
/// Each entry in <see cref="heroCards"/> is bound to the hero at the same index in
/// <see cref="availableHeroes"/>. Clicking a card calls SelectHero(index): it previews the hero's
/// 3D model on the pedestal, plays its select voice line and reveals the CONFIRMAR button.
/// ConfirmSelection() stores the hero in GameSession, fades to black, plays the hero's story intro
/// (if any) and loads the match scene.
/// Adding a hero = new HeroDefinitionSO + new card + new array entries.
/// </summary>
public class HeroSelectManager : MonoBehaviour
{
    [SerializeField] private HeroDefinitionSO[] availableHeroes;
    [Tooltip("2D source for the hero select voice line.")]
    [FormerlySerializedAs("entryAudioSource")]
    [SerializeField] private AudioSource voiceAudioSource;
    [SerializeField] private string nextSceneName = "Lane";

    [Header("UI")]
    [Tooltip("Card for each hero, same order as availableHeroes.")]
    [SerializeField] private HeroCardView[] heroCards;
    [SerializeField] private UnityEngine.UI.Button confirmButton;
    [SerializeField] private ScreenFader fader;
    [Tooltip("Shows the hero's intro lines on the black screen after confirming. Optional.")]
    [SerializeField] private IntroSequencePlayer introPlayer;
    [Tooltip("Seconds to fade the select voice line out right before the scene loads (if still playing).")]
    [SerializeField] private float voiceFadeOutDuration = 0.3f;

    [Header("3D Preview")]
    [Tooltip("Top-center of the pedestal; the model's feet are placed here. Its forward (+Z) should point at the player.")]
    [SerializeField] private Transform pedestalAnchor;
    [Tooltip("Shown while no hero is selected (e.g. a glowing '?').")]
    [SerializeField] private GameObject pedestalPlaceholder;
    [SerializeField] private float previewHeight = 1.8f;
    [SerializeField] private float popDuration = 0.35f;
    [SerializeField] private float turntableSpeed = 20f;

    private int selectedIndex = -1;
    private GameObject currentPreview;
    private bool isLoading;

    public HeroDefinitionSO SelectedHero =>
        selectedIndex >= 0 && availableHeroes != null && selectedIndex < availableHeroes.Length
            ? availableHeroes[selectedIndex]
            : null;

    private void Awake()
    {
        if (heroCards != null)
        {
            for (int i = 0; i < heroCards.Length; i++)
            {
                HeroCardView card = heroCards[i];
                if (card == null) continue;
                if (availableHeroes != null && i < availableHeroes.Length)
                {
                    card.Bind(availableHeroes[i]);
                }
                int index = i;
                card.Button.onClick.AddListener(() => SelectHero(index));
            }
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(ConfirmSelection);
            confirmButton.gameObject.SetActive(false);
        }

        if (pedestalPlaceholder != null)
        {
            pedestalPlaceholder.SetActive(true);
        }
    }

    /// <summary>
    /// Previews the hero at <paramref name="index"/>: shows its model on the pedestal,
    /// plays its select voice line, highlights its card and reveals the confirm button.
    /// </summary>
    public void SelectHero(int index)
    {
        if (isLoading) return;

        if (availableHeroes == null || index < 0 || index >= availableHeroes.Length)
        {
            Debug.LogError($"HeroSelectManager: invalid hero index {index}.");
            return;
        }

        HeroDefinitionSO hero = availableHeroes[index];
        if (hero == null)
        {
            Debug.LogError($"HeroSelectManager: availableHeroes[{index}] is not assigned.");
            return;
        }

        selectedIndex = index;

        if (heroCards != null)
        {
            for (int i = 0; i < heroCards.Length; i++)
            {
                if (heroCards[i] != null) heroCards[i].SetSelected(i == index);
            }
        }

        if (voiceAudioSource != null)
        {
            // Restart cleanly: re-selecting cuts the previous bark instead of layering it.
            voiceAudioSource.Stop();
            voiceAudioSource.clip = hero.selectClip;
            if (hero.selectClip != null) voiceAudioSource.Play();
        }

        ShowPreview(hero);

        if (confirmButton != null)
        {
            confirmButton.gameObject.SetActive(true);
        }
    }

    /// <summary>Stores the selected hero in GameSession, plays its intro and loads the match scene.</summary>
    public void ConfirmSelection()
    {
        HeroDefinitionSO hero = SelectedHero;
        if (isLoading || hero == null) return;
        isLoading = true;

        if (GameSession.Instance != null)
        {
            GameSession.Instance.SelectedHero = hero;
            GameSession.Instance.EntryLinePlayed = false;
        }
        else
        {
            Debug.LogWarning("HeroSelectManager: no GameSession in the scene; selected hero will not persist.");
        }

        StartCoroutine(ConfirmRoutine(hero));
    }

    private IEnumerator ConfirmRoutine(HeroDefinitionSO hero)
    {
        // Heroes without intro lines go straight to the match; their entry line then plays in the match
        // scene (HeroEntryAnnouncer), because GameSession.EntryLinePlayed stays false.
        bool hasIntro = introPlayer != null && IntroSequencePlayer.HasLines(hero.introLines);

        // The select bark fades out so that it is silent exactly when the screen is black
        // (never overlapping the entry line that starts with the intro).
        float fadeDuration = fader != null ? fader.DefaultDuration : 0f;
        float voiceFade = Mathf.Min(voiceFadeOutDuration, Mathf.Max(fadeDuration, 0.01f));
        Coroutine voiceRoutine = StartCoroutine(FadeOutVoice(voiceFade, Mathf.Max(0f, fadeDuration - voiceFade)));

        if (fader != null)
        {
            yield return fader.FadeOut();
        }
        yield return voiceRoutine;

        // Screen is black: freeze the preview.
        if (currentPreview != null)
        {
            TurntableRotator rotator = currentPreview.GetComponent<TurntableRotator>();
            if (rotator != null) rotator.enabled = false;
        }

        if (hasIntro)
        {
            if (GameSession.Instance != null && hero.entryClip != null)
            {
                GameSession.Instance.EntryLinePlayed = true;
            }
            yield return introPlayer.Play(hero.introLines, hero.entryClip);
        }

        SceneManager.LoadScene(nextSceneName);
    }

    private IEnumerator FadeOutVoice(float duration, float delay = 0f)
    {
        if (voiceAudioSource == null || !voiceAudioSource.isPlaying) yield break;

        float wait = 0f;
        while (wait < delay && voiceAudioSource.isPlaying)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!voiceAudioSource.isPlaying) yield break;

        float startVolume = voiceAudioSource.volume;
        float t = 0f;
        while (t < duration && voiceAudioSource.isPlaying)
        {
            t += Time.unscaledDeltaTime;
            voiceAudioSource.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(t / duration));
            yield return null;
        }
        voiceAudioSource.Stop();
        voiceAudioSource.volume = startVolume;
    }

    private void ShowPreview(HeroDefinitionSO hero)
    {
        if (currentPreview != null)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (pedestalPlaceholder != null)
        {
            pedestalPlaceholder.SetActive(hero.previewModel == null);
        }

        if (hero.previewModel == null || pedestalAnchor == null) return;

        currentPreview = BuildPreview(hero.previewModel, pedestalAnchor, previewHeight);
        currentPreview.AddComponent<TurntableRotator>().DegreesPerSecond = turntableSpeed;
        StartCoroutine(PopIn(currentPreview.transform));
    }

    /// <summary>
    /// Instantiates <paramref name="modelPrefab"/> under a new container at <paramref name="anchor"/>,
    /// uniformly scaled so the distance from its root pivot (expected at the feet) to the top of its
    /// renderer bounds is <paramref name="height"/>. The prefab should face +Z with its pivot at the
    /// soles (see Assets/Prefabs/Heroes/Seven_Preview.prefab). The container is what rotates / animates.
    /// </summary>
    public static GameObject BuildPreview(GameObject modelPrefab, Transform anchor, float height)
    {
        var container = new GameObject(modelPrefab.name + " (Preview)");
        container.transform.SetParent(anchor, false);

        GameObject model = Instantiate(modelPrefab);
        Transform mt = model.transform;
        Vector3 prefabScale = modelPrefab.transform.localScale;
        mt.SetPositionAndRotation(Vector3.zero, modelPrefab.transform.rotation);
        mt.localScale = prefabScale;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            float top = b.max.y;
            if (top > 0.0001f)
            {
                mt.localScale = prefabScale * (height / top);
            }
        }

        mt.SetParent(container.transform, false);
        return container;
    }

    private IEnumerator PopIn(Transform target)
    {
        float t = 0f;
        while (target != null && t < popDuration)
        {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / popDuration);
            // Ease-out-back: overshoots slightly past 1 then settles.
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float k = 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
            target.localScale = Vector3.one * k;
            yield return null;
        }
        if (target != null) target.localScale = Vector3.one;
    }
}
