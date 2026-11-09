using UnityEngine;

/// <summary>
/// Swings the object back and forth around its local Y (and a little around X) from the rotation it
/// starts with. Used for the searchlight beams over the city in the intro.
/// </summary>
public class SlowSweep : MonoBehaviour
{
    public float yawAmplitude = 25f;
    public float pitchAmplitude = 4f;
    [Tooltip("Seconds for one full swing.")]
    public float period = 14f;
    public float phase;

    private Quaternion baseRotation;

    private void Awake()
    {
        baseRotation = transform.localRotation;
    }

    private void Update()
    {
        float t = (Time.time / Mathf.Max(period, 0.1f) + phase) * Mathf.PI * 2f;
        transform.localRotation = baseRotation * Quaternion.Euler(Mathf.Sin(t * 0.5f) * pitchAmplitude, Mathf.Sin(t) * yawAmplitude, 0f);
    }
}
