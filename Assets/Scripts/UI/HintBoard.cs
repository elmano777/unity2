using TMPro;
using UnityEngine;

/// <summary>
/// A floating panel with the current hint (RF-13). It listens to <see cref="GameEvents.HintRequested"/>,
/// so anything can ask for a hint without knowing the panel exists, and it drifts after the player's
/// gaze instead of being glued to the head, which is uncomfortable to read in VR.
/// </summary>
public class HintBoard : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Visible background behind the text. Hidden together with it when there is no hint.")]
    [SerializeField] private GameObject panel;

    [Header("Placement")]
    [SerializeField] private float distance = 1.9f;

    [Tooltip("Metres below eye level, so the panel sits just under the line of sight.")]
    [SerializeField] private float drop = 0.25f;

    [Tooltip("How quickly the panel turns to follow where the player faces. Lower = lazier.")]
    [SerializeField] private float followSpeed = 1.8f;

    private Transform head;
    private Vector3 smoothForward = Vector3.forward;
    private float hideTime = -1f;

    private void OnEnable()
    {
        GameEvents.HintRequested += Show;
        Apply(string.Empty);
    }

    private void OnDisable()
    {
        GameEvents.HintRequested -= Show;
    }

    /// <summary>Shows a hint. With <paramref name="seconds"/> above zero it clears itself afterwards.</summary>
    public void Show(string message) => Show(message, 0f);

    public void Show(string message, float seconds)
    {
        hideTime = seconds > 0f ? Time.time + seconds : -1f;
        Apply(message);
    }

    private void Apply(string message)
    {
        if (label != null)
        {
            label.text = message;
        }

        if (panel != null)
        {
            panel.SetActive(!string.IsNullOrEmpty(message));
        }
    }

    private void LateUpdate()
    {
        if (hideTime > 0f && Time.time >= hideTime)
        {
            hideTime = -1f;
            Apply(string.Empty);
        }

        if (head == null)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            head = camera.transform;
            smoothForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        }

        Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);

        if (facing.sqrMagnitude > 0.01f)
        {
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            smoothForward = Vector3.Slerp(smoothForward, facing.normalized, t);
        }

        Vector3 position = head.position + smoothForward * distance + Vector3.down * drop;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
    }
}
