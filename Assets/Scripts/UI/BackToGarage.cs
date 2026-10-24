using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// "Atrás": returns to the garage (the title screen) from the hero select or the lane, for when the
/// player picked the wrong option. Pressed with the laser on a back rune (<see cref="GoBack"/>) or
/// with B / Y on the controllers. In the lane the button must be held for <see cref="holdSeconds"/>
/// so a stray press mid-duel doesn't drop the match; leaving also closes the online room.
/// </summary>
public class BackToGarage : MonoBehaviour
{
    [SerializeField] private string garageScene = "TitleScreen";

    [Tooltip("0 = a press goes back at once; otherwise B / Y must be held this long.")]
    [SerializeField] private float holdSeconds = 0f;

    [Tooltip("Optional text that shows the hint and the hold progress.")]
    [SerializeField] private TMP_Text holdLabel;
    [SerializeField] private string hintText = "Mantén B o Y para volver al garage";
    [Tooltip("Seconds the hint stays up when the scene starts (0 = never shown on its own).")]
    [SerializeField] private float showHintFor = 8f;

    [Tooltip("Fade used for the transition. Found on the rig when left empty.")]
    [SerializeField] private ScreenFader fader;

    private InputAction backAction;
    private float held;
    private bool leaving;

    private void Awake()
    {
        if (fader == null) fader = FindObjectOfType<ScreenFader>();
    }

    private void OnEnable()
    {
        backAction = new InputAction("Back", InputActionType.Button);
        backAction.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
        backAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
        backAction.AddBinding("<Keyboard>/escape");
        backAction.AddBinding("<Keyboard>/backspace");
        backAction.Enable();
    }

    private void OnDisable()
    {
        backAction?.Dispose();
        backAction = null;
    }

    private void Update()
    {
        if (leaving || backAction == null) return;

        if (holdSeconds <= 0f)
        {
            if (backAction.WasPressedThisFrame()) GoBack();
            return;
        }

        held = backAction.IsPressed() ? held + Time.unscaledDeltaTime : 0f;
        if (held >= holdSeconds) GoBack();

        if (holdLabel != null)
        {
            bool hint = Time.timeSinceLevelLoad < showHintFor;
            holdLabel.text = held > 0f ? $"Volviendo al garage... {Mathf.RoundToInt(held / holdSeconds * 100f)}%" : hint ? hintText : "";
        }
    }

    /// <summary>Fades out, leaves the online room if there is one, and loads the garage.</summary>
    public void GoBack()
    {
        if (leaving) return;

        // Not while the hero select is already loading the match.
        HeroSelectManager select = FindObjectOfType<HeroSelectManager>();
        if (select != null && select.IsLoading) return;

        leaving = true;
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.4f, 0.08f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.4f, 0.08f);
        StartCoroutine(Leave());
    }

    private IEnumerator Leave()
    {
        if (holdLabel != null) holdLabel.text = "Volviendo al garage...";
        if (fader != null) yield return fader.FadeOut();

        // The NetworkManager survives scene loads: shut it down and remove it so the next match
        // starts from a clean one (the matchmaker leaves or deletes its lobby when destroyed).
        NetworkManager network = NetworkManager.Singleton;
        if (network != null)
        {
            if (network.IsListening) network.Shutdown();
            Destroy(network.gameObject);
        }

        SceneManager.LoadScene(garageScene);
    }
}
