using System.Collections;
using UnityEngine;

/// <summary>
/// Fades a black CanvasGroup placed just in front of the (XR) camera.
/// Works in VR because it is a world-space canvas parented to the camera rather than a screen overlay.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    [SerializeField] private float defaultDuration = 0.5f;
    [Tooltip("Start fully black and fade in when the scene starts.")]
    [SerializeField] private bool fadeInOnStart = true;

    private CanvasGroup group;

    public float DefaultDuration => defaultDuration;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = fadeInOnStart ? 1f : 0f;
    }

    private void Start()
    {
        if (fadeInOnStart)
        {
            StartCoroutine(FadeTo(0f, defaultDuration));
        }
    }

    public Coroutine FadeOut(float duration = -1f)
    {
        StopAllCoroutines();
        return StartCoroutine(FadeTo(1f, duration < 0f ? defaultDuration : duration));
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        float start = group.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        group.alpha = target;
    }
}
