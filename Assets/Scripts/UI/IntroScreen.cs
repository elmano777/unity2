using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Opening of the game, like Deadlock's: the player stands on a rooftop over the city at dusk, the
/// wheel logo burns into the sky, the DEADLOCK wordmark follows under it and "pulsa cualquier botón"
/// pulses until any controller button (or key / click in the editor) is pressed; then it flashes,
/// chimes and fades into the title screen. A press during the reveal skips straight to the end of it.
/// </summary>
public class IntroScreen : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "TitleScreen";

    [Header("Title art (Deadlock/IntroLogo material)")]
    [SerializeField] private Renderer logo;
    [SerializeField] private Renderer wordmark;
    [SerializeField] private TMP_Text prompt;
    [SerializeField] private Light logoLight;
    [SerializeField] private ParticleSystem startBurst;

    [Header("Sound and transition")]
    [Tooltip("Title music; it loops while the screen waits and fades out with the picture.")]
    [SerializeField] private AudioClip music;
    [SerializeField] private AudioSource ambience;
    [SerializeField] private AudioSource sfx;
    [SerializeField] private ScreenFader fader;

    [Header("Timing (seconds)")]
    [SerializeField] private float logoDelay = 1.6f;
    [SerializeField] private float logoDuration = 2.4f;
    [SerializeField] private float wordmarkDelay = 1.5f;
    [SerializeField] private float wordmarkDuration = 1.6f;
    [SerializeField] private float promptDelay = 0.6f;

    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private MaterialPropertyBlock block;
    private InputAction startAction;
    private float logoReveal, wordReveal, promptAlpha, flash;
    private float lightIntensity;
    private Vector3 logoRest, wordRest;
    private bool skipped, ready, starting;

    private void Awake()
    {
#if UNITY_EDITOR
        // Keep Play Mode running while the Editor is unfocused, or the MCP bridge stalls (see CLAUDE.md).
        Application.runInBackground = true;
#endif
        block = new MaterialPropertyBlock();
        if (logo != null) logoRest = logo.transform.localPosition;
        if (wordmark != null) wordRest = wordmark.transform.localPosition;
        if (logoLight != null)
        {
            lightIntensity = logoLight.intensity;
            logoLight.intensity = 0f;
        }
        Apply();
    }

    private void OnEnable()
    {
        // Every face button, trigger and grip on both Touch controllers, plus keyboard, mouse and gamepad.
        startAction = new InputAction("Start", InputActionType.Button);
        foreach (string hand in new[] { "{LeftHand}", "{RightHand}" })
        {
            startAction.AddBinding($"<XRController>{hand}/triggerPressed");
            startAction.AddBinding($"<XRController>{hand}/gripPressed");
            startAction.AddBinding($"<XRController>{hand}/{{PrimaryButton}}");
            startAction.AddBinding($"<XRController>{hand}/{{SecondaryButton}}");
            startAction.AddBinding($"<XRController>{hand}/{{Primary2DAxisClick}}");
        }
        startAction.AddBinding("<Keyboard>/anyKey");
        startAction.AddBinding("<Mouse>/leftButton");
        startAction.AddBinding("<Gamepad>/start");
        startAction.AddBinding("<Gamepad>/buttonSouth");
        startAction.Enable();
    }

    private void OnDisable()
    {
        startAction?.Dispose();
        startAction = null;
    }

    private IEnumerator Start()
    {
        if (ambience != null)
        {
            ambience.clip = music != null ? music : ProceduralAudio.Drone();
            ambience.loop = true;
            ambience.Play();
        }

        yield return Wait(logoDelay);
        if (sfx != null && !skipped) sfx.PlayOneShot(ProceduralAudio.Chime(), 0.35f);
        yield return Animate(logoDuration, k => logoReveal = Mathf.SmoothStep(0f, 1.1f, k));
        yield return Wait(wordmarkDelay - logoDuration * 0.5f);
        yield return Animate(wordmarkDuration, k => wordReveal = Mathf.SmoothStep(0f, 1.1f, k));
        yield return Wait(promptDelay);
        ready = true;
    }

    private IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds && !skipped; t += Time.deltaTime) yield return null;
    }

    private IEnumerator Animate(float seconds, System.Action<float> step)
    {
        for (float t = 0f; t < seconds && !skipped; t += Time.deltaTime)
        {
            step(t / seconds);
            yield return null;
        }
        step(1f);
    }

    private void Update()
    {
        if (startAction != null && startAction.WasPressedThisFrame())
        {
            if (ready) StartCoroutine(BeginGame());
            else Skip();
        }

        if (ready && !starting) promptAlpha = Mathf.MoveTowards(promptAlpha, 1f, Time.deltaTime * 2f);
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.5f);

        // The logo floats a little and breathes once it is fully lit.
        float t = Time.time;
        if (logo != null) logo.transform.localPosition = logoRest + Vector3.up * (Mathf.Sin(t * 0.8f) * 0.04f);
        if (wordmark != null) wordmark.transform.localPosition = wordRest + Vector3.up * (Mathf.Sin(t * 0.8f - 0.6f) * 0.025f);
        if (logoLight != null) logoLight.intensity = lightIntensity * Mathf.Clamp01(logoReveal) * (0.85f + 0.15f * Mathf.Sin(t * 1.7f)) * (1f + flash);

        Apply();
    }

    private void Skip()
    {
        skipped = true;
        logoReveal = wordReveal = 1.1f;
        ready = true;
    }

    private IEnumerator BeginGame()
    {
        if (starting) yield break;
        starting = true;
        flash = 1.5f;
        promptAlpha = 0f;

        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.7f, 0.15f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.7f, 0.15f);
        if (sfx != null) sfx.PlayOneShot(ProceduralAudio.Chime());
        if (startBurst != null) startBurst.Play();

        yield return new WaitForSeconds(0.45f);
        if (ambience != null) StartCoroutine(FadeMusic(1f));
        if (fader != null) yield return fader.FadeOut(1f);
        SceneManager.LoadScene(nextSceneName);
    }

    private IEnumerator FadeMusic(float seconds)
    {
        float start = ambience.volume;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            ambience.volume = Mathf.Lerp(start, 0f, t / seconds);
            yield return null;
        }
        ambience.volume = 0f;
    }

    private void Apply()
    {
        SetArt(logo, logoReveal);
        SetArt(wordmark, wordReveal);

        if (prompt != null)
        {
            float pulse = 0.55f + 0.45f * Mathf.Cos(Time.time * Mathf.PI);
            prompt.alpha = promptAlpha * pulse;
        }
    }

    private void SetArt(Renderer target, float reveal)
    {
        if (target == null) return;
        target.GetPropertyBlock(block);
        block.SetFloat(RevealId, reveal);
        block.SetFloat(FlashId, flash);
        block.SetFloat(FadeId, 1f);
        target.SetPropertyBlock(block);
    }
}
