using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Title screen with the two entry points of RF-01: "Partida online" and "Mi misión".
/// Both lead to the hero select for now (the guided practice and the online room are later deliverables);
/// "Mi misión" remembers the choice in PlayerPrefs and is flagged as recommended until the practice
/// has been completed (RF-04). Space/Enter on keyboard starts "Partida online" for desktop testing.
/// </summary>
public class TitleScreenManager : MonoBehaviour
{
    /// <summary>PlayerPrefs key: 1 once the player has finished the guided practice.</summary>
    public const string MissionCompletedKey = "MissionCompleted";

    /// <summary>PlayerPrefs key: 1 when the player entered through "Mi misión" (practice requested).</summary>
    public const string MissionRequestedKey = "MissionRequested";

    [SerializeField] private string nextSceneName = "HeroSelect";

    [Header("Buttons")]
    [SerializeField] private Button onlineButton;
    [SerializeField] private Button missionButton;
    [Tooltip("Shown on the mission button until the practice has been completed.")]
    [SerializeField] private TMP_Text missionRecommendedLabel;

    [Header("Transition")]
    [SerializeField] private ScreenFader fader;
    [Tooltip("Ignore input for this long after the scene starts (avoids instant skip).")]
    [SerializeField] private float inputDelay = 0.5f;

    private bool isLoading;
    private float startTime;

    private void Awake()
    {
        if (onlineButton != null) onlineButton.onClick.AddListener(PlayOnline);
        if (missionButton != null) missionButton.onClick.AddListener(StartMission);
        if (missionRecommendedLabel != null)
        {
            missionRecommendedLabel.gameObject.SetActive(PlayerPrefs.GetInt(MissionCompletedKey, 0) == 0);
        }
    }

    private void OnEnable()
    {
        startTime = Time.unscaledTime;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            PlayOnline();
        }
    }

    /// <summary>"Partida online": straight to hero select (and later the room).</summary>
    public void PlayOnline()
    {
        Begin(false);
    }

    /// <summary>"Mi misión": guided practice (not built yet, so it goes to hero select and is flagged).</summary>
    public void StartMission()
    {
        Begin(true);
    }

    private void Begin(bool mission)
    {
        if (isLoading || Time.unscaledTime - startTime < inputDelay) return;
        isLoading = true;
        PlayerPrefs.SetInt(MissionRequestedKey, mission ? 1 : 0);
        PlayerPrefs.Save();
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
