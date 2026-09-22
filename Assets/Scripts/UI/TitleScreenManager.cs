using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Title screen: waits for either controller trigger (or Space/Enter on keyboard for desktop testing),
/// fades to black and loads the Hero Select scene.
/// </summary>
public class TitleScreenManager : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "HeroSelect";

    [Header("Input (XRI Default Input Actions)")]
    [Tooltip("e.g. XRI Left Interaction/Activate (trigger).")]
    [SerializeField] private InputActionReference leftTriggerAction;
    [Tooltip("e.g. XRI Right Interaction/Activate (trigger).")]
    [SerializeField] private InputActionReference rightTriggerAction;

    [Header("Transition")]
    [SerializeField] private ScreenFader fader;
    [Tooltip("Ignore input for this long after the scene starts (avoids instant skip).")]
    [SerializeField] private float inputDelay = 0.5f;

    private bool isLoading;
    private float startTime;

    private void OnEnable()
    {
        startTime = Time.unscaledTime;
        Subscribe(leftTriggerAction);
        Subscribe(rightTriggerAction);
    }

    private void OnDisable()
    {
        Unsubscribe(leftTriggerAction);
        Unsubscribe(rightTriggerAction);
    }

    private void Subscribe(InputActionReference reference)
    {
        if (reference == null || reference.action == null) return;
        reference.action.performed += OnTriggerPerformed;
        reference.action.Enable();
    }

    private void Unsubscribe(InputActionReference reference)
    {
        if (reference == null || reference.action == null) return;
        reference.action.performed -= OnTriggerPerformed;
    }

    private void OnTriggerPerformed(InputAction.CallbackContext context)
    {
        BeginGame();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            BeginGame();
        }
    }

    /// <summary>Starts the fade + scene load. Safe to call multiple times.</summary>
    public void BeginGame()
    {
        if (isLoading || Time.unscaledTime - startTime < inputDelay) return;
        isLoading = true;
        StartCoroutine(FadeAndLoad());
    }

    private IEnumerator FadeAndLoad()
    {
        if (fader != null)
        {
            yield return fader.FadeOut();
        }
        SceneManager.LoadScene(nextSceneName);
    }
}
