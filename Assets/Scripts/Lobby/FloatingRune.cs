using UnityEngine;

/// <summary>
/// A menu option shown as a glowing rune that rises out of the orb when it is enabled, then hovers
/// in place, bobbing gently and turning to face the player.
/// </summary>
public class FloatingRune : MonoBehaviour
{
    [Tooltip("Where the rune flies out from (the orb). Its own start position is where it ends up.")]
    [SerializeField] private Transform origin;
    [SerializeField] private float riseDuration = 0.8f;
    [SerializeField] private float bobHeight = 0.04f;
    [SerializeField] private float bobSpeed = 1.3f;
    [Tooltip("Turn around Y to face the player's head.")]
    [SerializeField] private bool facePlayer = true;

    private Vector3 restPosition;
    private Vector3 restScale;
    private float shownAt;
    private float phase;

    private void Awake()
    {
        restPosition = transform.position;
        restScale = transform.localScale;
        phase = Random.value * 10f;
    }

    private void OnEnable()
    {
        shownAt = Time.time;
        transform.localScale = Vector3.zero;

        if (origin != null)
        {
            transform.position = origin.position;
        }
    }

    private void Update()
    {
        float x = Mathf.Clamp01((Time.time - shownAt) / riseDuration);
        float ease = 1f - Mathf.Pow(1f - x, 3f);

        Vector3 from = origin != null ? origin.position : restPosition;
        Vector3 bob = Vector3.up * (Mathf.Sin((Time.time + phase) * bobSpeed) * bobHeight * ease);
        transform.position = Vector3.Lerp(from, restPosition, ease) + bob;
        transform.localScale = restScale * ease;

        Camera viewer = Camera.main;

        if (facePlayer && viewer != null)
        {
            Vector3 away = transform.position - viewer.transform.position;
            away.y = 0f;

            if (away.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(away);
            }
        }
    }
}
