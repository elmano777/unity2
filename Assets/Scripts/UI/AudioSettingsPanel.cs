using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Volume and subtitle settings on the title screen (RNF-08). Plain buttons rather than a slider:
/// with the laser pointer a click is far easier to hit than a drag, and 10 % steps are enough.
/// Values live in <see cref="PlayerProgress"/>, so they survive between launches.
/// </summary>
public class AudioSettingsPanel : MonoBehaviour
{
    [SerializeField] private Button volumeDownButton;
    [SerializeField] private Button volumeUpButton;
    [SerializeField] private TMP_Text volumeLabel;
    [SerializeField] private Button subtitlesButton;
    [SerializeField] private TMP_Text subtitlesLabel;
    [SerializeField, Range(0.05f, 0.5f)] private float step = 0.1f;

    private void Awake()
    {
        if (volumeDownButton != null) volumeDownButton.onClick.AddListener(() => ChangeVolume(-step));
        if (volumeUpButton != null) volumeUpButton.onClick.AddListener(() => ChangeVolume(step));
        if (subtitlesButton != null) subtitlesButton.onClick.AddListener(ToggleSubtitles);
        Refresh();
    }

    private void ChangeVolume(float delta)
    {
        // Round to the step so repeated clicks land on 0 %, 10 %, ... instead of drifting.
        float steps = Mathf.Round((PlayerProgress.Volume + delta) / step);
        PlayerProgress.Volume = steps * step;
        Refresh();
    }

    private void ToggleSubtitles()
    {
        PlayerProgress.SubtitlesEnabled = !PlayerProgress.SubtitlesEnabled;
        Refresh();
    }

    private void Refresh()
    {
        if (volumeLabel != null) volumeLabel.text = $"VOLUMEN {Mathf.RoundToInt(PlayerProgress.Volume * 100f)}%";
        if (subtitlesLabel != null) subtitlesLabel.text = PlayerProgress.SubtitlesEnabled ? "SUBTÍTULOS: SÍ" : "SUBTÍTULOS: NO";
    }
}
