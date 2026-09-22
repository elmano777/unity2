using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Story intro shown on the black screen after confirming a hero.
/// All lines appear together as one centered block while the hero's voice line plays. Input is refused
/// until the voice line has finished; then a pulsing hint appears and a fresh trigger press (or
/// Space/Enter) fades the text out and ends the sequence.
/// Lives on a world-space canvas parented to the (XR) camera ~2 m in front of the eyes, with a sorting
/// order above the ScreenFader so the text draws on top of the black fade.
/// Usage from another coroutine: <c>yield return introPlayer.Play(lines, voiceClip);</c>
/// </summary>
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasGroup))]
public class IntroSequencePlayer : MonoBehaviour
{
    public enum Phase { Idle, Speaking, WaitingForContinue, Finishing }

    [Header("References")]
    [Tooltip("Text block that shows all story lines (one sentence per line).")]
    [FormerlySerializedAs("lineText")]
    [SerializeField] private TMP_Text storyText;
    [Tooltip("'Press the trigger to continue' hint, shown once the voice line has finished. Should have an AlphaPulse.")]
    [SerializeField] private TMP_Text hintText;
    [Tooltip("2D source for the hero voice line. Created automatically if empty.")]
    [SerializeField] private AudioSource voiceSource;

    [Header("Timing (seconds, unscaled)")]
    [SerializeField] private float textFadeInDuration = 0.8f;
    [SerializeField] private float hintFadeInDuration = 0.4f;
    [SerializeField] private float textFadeOutDuration = 0.6f;
    [Tooltip("If there is no voice clip (or it can't play), wait this long before offering to continue.")]
    [SerializeField] private float minReadTimeWithoutVoice = 3f;
    [Tooltip("After the hint starts appearing, ignore input for this long.")]
    [SerializeField] private float continueInputDelay = 0.25f;

    [Header("Input")]
    [Tooltip("e.g. XRI Left Interaction/Activate (trigger).")]
    [SerializeField] private InputActionReference leftTriggerAction;
    [Tooltip("e.g. XRI Right Interaction/Activate (trigger).")]
    [SerializeField] private InputActionReference rightTriggerAction;

    [Header("Look")]
    [Tooltip("Phrases highlighted in the accent color wherever they appear (TMP rich text).")]
    [SerializeField] private string[] accentPhrases = { "The Hidden King" };
    [SerializeField] private Color accentColor = new Color(1f, 0.68f, 0.25f, 1f);
    [Tooltip("While the intro runs, the parent camera only renders these layers (the screen is black anyway). " +
             "Keeps nearby opaque objects such as controller models from cutting into the text.")]
    [SerializeField] private bool isolateCamera = true;
    [SerializeField] private LayerMask introCullingMask = 1 << 5; // UI layer

    private Canvas canvas;
    private CanvasGroup group;
    private CanvasGroup storyGroup;
    private CanvasGroup hintGroup;
    private float continueAllowedAt;
    private bool continueRequested;

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public bool IsPlaying => CurrentPhase != Phase.Idle;
    public AudioSource VoiceSource => voiceSource;

    /// <summary>True once the voice line has finished and the player may continue.</summary>
    public bool CanContinue => CurrentPhase == Phase.WaitingForContinue && Time.unscaledTime >= continueAllowedAt;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        storyGroup = GetOrAddGroup(storyText);
        hintGroup = GetOrAddGroup(hintText);

        if (voiceSource == null)
        {
            voiceSource = GetComponent<AudioSource>();
            if (voiceSource == null) voiceSource = gameObject.AddComponent<AudioSource>();
        }
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.spatialBlend = 0f;

        HideImmediate();
    }

    private void OnEnable()
    {
        EnableAction(leftTriggerAction);
        EnableAction(rightTriggerAction);
    }

    private static CanvasGroup GetOrAddGroup(Component target)
    {
        if (target == null) return null;
        CanvasGroup g = target.GetComponent<CanvasGroup>();
        if (g == null) g = target.gameObject.AddComponent<CanvasGroup>();
        g.interactable = false;
        g.blocksRaycasts = false;
        return g;
    }

    private static void EnableAction(InputActionReference reference)
    {
        if (reference != null && reference.action != null) reference.action.Enable();
    }

    private void HideImmediate()
    {
        group.alpha = 0f;
        canvas.enabled = false;
        if (storyGroup != null) storyGroup.alpha = 0f;
        if (hintGroup != null) hintGroup.alpha = 0f;
    }

    /// <summary>True if the array has at least one non-empty line.</summary>
    public static bool HasLines(string[] lines)
    {
        if (lines == null) return false;
        foreach (string line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line)) return true;
        }
        return false;
    }

    /// <summary>
    /// Same as pressing a trigger. Only accepted once the voice line has finished and the hint is up.
    /// Returns true if the request was accepted.
    /// </summary>
    public bool RequestContinue()
    {
        if (!CanContinue || continueRequested) return false;
        continueRequested = true;
        return true;
    }

    /// <summary>
    /// Shows all lines at once, plays <paramref name="voice"/> at the same moment and waits for it to finish,
    /// then for the player to continue. Yield on it from a coroutine.
    /// </summary>
    public IEnumerator Play(string[] lines, AudioClip voice)
    {
        if (!HasLines(lines) || storyText == null) yield break;

        Camera cam = isolateCamera ? GetComponentInParent<Camera>() : null;
        int previousMask = cam != null ? cam.cullingMask : 0;

        continueRequested = false;
        storyText.text = BuildText(lines);
        storyGroup.alpha = 0f;
        if (hintGroup != null) hintGroup.alpha = 0f;
        group.alpha = 1f;
        canvas.enabled = true;
        if (cam != null) cam.cullingMask = introCullingMask;

        try
        {
            // Text and voice start together.
            CurrentPhase = Phase.Speaking;
            bool voiceStarted = false;
            if (voice != null && voiceSource != null)
            {
                voiceSource.Stop();
                voiceSource.clip = voice;
                voiceSource.time = 0f;
                voiceSource.Play();
                voiceStarted = true;
            }

            StartCoroutine(FadeGroup(storyGroup, 1f, textFadeInDuration));
            float speakStart = Time.unscaledTime;

            yield return WaitForVoice(voiceStarted ? voice : null);
            Debug.Log($"IntroSequencePlayer: voice line finished after {Time.unscaledTime - speakStart:F2} s " +
                      $"(clip length {(voice != null ? voice.length : 0f):F2} s); waiting for the player to continue.");

            // Make sure the text is fully visible before offering to continue.
            while (storyGroup.alpha < 1f) yield return null;

            CurrentPhase = Phase.WaitingForContinue;
            continueAllowedAt = Time.unscaledTime + continueInputDelay;
            if (hintGroup != null) StartCoroutine(FadeGroup(hintGroup, 1f, hintFadeInDuration));

            while (!continueRequested)
            {
                yield return null;
                PollInput();
            }

            CurrentPhase = Phase.Finishing;
            yield return FadeGroup(group, 0f, textFadeOutDuration);
        }
        finally
        {
            if (cam != null) cam.cullingMask = previousMask;
            CurrentPhase = Phase.Idle;
            continueRequested = false;
            HideImmediate();
        }
    }

    /// <summary>
    /// Waits until the voice line has played to the end. A source paused mid-clip (isPlaying false but
    /// playback position inside the clip) keeps waiting. If the clip never starts playing (no audio
    /// device, audio disabled), falls back to waiting the clip length in real time.
    /// </summary>
    private IEnumerator WaitForVoice(AudioClip voice)
    {
        if (voice == null)
        {
            yield return WaitRealtime(minReadTimeWithoutVoice);
            yield break;
        }

        // Give the audio system a couple of frames to start the source.
        float startTime = Time.unscaledTime;
        bool everPlayed = false;
        while (Time.unscaledTime - startTime < 0.25f)
        {
            if (voiceSource.isPlaying) { everPlayed = true; break; }
            yield return null;
        }

        if (!everPlayed)
        {
            Debug.LogWarning("IntroSequencePlayer: voice line did not start playing; waiting its length instead.");
            yield return WaitRealtime(Mathf.Max(minReadTimeWithoutVoice, voice.length - (Time.unscaledTime - startTime)));
            yield break;
        }

        float endMargin = 0.05f;
        while (true)
        {
            if (voiceSource.clip != voice) break; // someone swapped the clip: stop waiting
            if (!voiceSource.isPlaying)
            {
                float pos = voiceSource.time;
                bool pausedMidClip = pos > 0f && pos < voice.length - endMargin;
                if (!pausedMidClip) break; // finished (Unity rewinds to 0 at the end) or stopped
            }
            yield return null;
        }
    }

    private void PollInput()
    {
        if (!CanContinue || continueRequested) return;

        bool pressed = WasPressed(leftTriggerAction) || WasPressed(rightTriggerAction);
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            pressed = true;
        }

        if (pressed) RequestContinue();
    }

    private static bool WasPressed(InputActionReference reference)
    {
        // WasPressedThisFrame only fires on a fresh press, so a trigger held since before the hint is ignored.
        return reference != null && reference.action != null && reference.action.WasPressedThisFrame();
    }

    private static IEnumerator FadeGroup(CanvasGroup target, float to, float duration)
    {
        float from = target.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            target.alpha = Mathf.SmoothStep(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        target.alpha = to;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private string BuildText(string[] lines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(ApplyAccents(raw.Trim()));
        }
        return sb.ToString();
    }

    private string ApplyAccents(string line)
    {
        if (accentPhrases == null) return line;
        string hex = ColorUtility.ToHtmlStringRGB(accentColor);
        foreach (string phrase in accentPhrases)
        {
            if (string.IsNullOrEmpty(phrase)) continue;
            line = line.Replace(phrase, $"<color=#{hex}>{phrase}</color>");
        }
        return line;
    }
}
