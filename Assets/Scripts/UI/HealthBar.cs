using UnityEngine;

/// <summary>
/// Floating bar over a minion or tower that shows its <see cref="Health"/>. Hidden at full health
/// so a fresh wave doesn't clutter the lane, shown from the first hit on, and turned towards the
/// player's head every frame.
/// </summary>
/// <remarks>
/// Built from two quads rather than a world-space Canvas: cheaper on Quest and needs no
/// EventSystem. The fill quad hangs off <see cref="fillPivot"/>, placed on the bar's left edge, so
/// scaling the pivot on X empties the bar towards the left.
/// </remarks>
public class HealthBar : MonoBehaviour
{
    [Tooltip("Health to display. Defaults to the one on a parent.")]
    [SerializeField] private Health health;

    [Tooltip("Child on the bar's left edge; its X scale is set to the fraction of health left.")]
    [SerializeField] private Transform fillPivot;

    [Tooltip("Object turned on once the target has taken damage. Usually the bar's root child.")]
    [SerializeField] private GameObject visual;

    [Tooltip("Keep the bar visible even at full health.")]
    [SerializeField] private bool alwaysVisible;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }

    private void OnEnable()
    {
        if (health == null)
        {
            return;
        }

        health.HealthChanged += OnHealthChanged;
        health.Killed += OnKilled;
        OnHealthChanged(health.CurrentHealth, health.MaxHealth);
    }

    private void OnDisable()
    {
        if (health == null)
        {
            return;
        }

        health.HealthChanged -= OnHealthChanged;
        health.Killed -= OnKilled;
    }

    private void LateUpdate()
    {
        Camera viewer = Camera.main;

        if (viewer == null || visual == null || !visual.activeSelf)
        {
            return;
        }

        Vector3 away = transform.position - viewer.transform.position;
        away.y = 0f;

        if (away.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(away);
        }
    }

    private void OnHealthChanged(float current, float max)
    {
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        if (fillPivot != null)
        {
            Vector3 scale = fillPivot.localScale;
            scale.x = Mathf.Max(0.0001f, fraction);
            fillPivot.localScale = scale;
        }

        if (visual != null)
        {
            visual.SetActive(alwaysVisible || (fraction < 0.999f && health.IsAlive));
        }
    }

    private void OnKilled(DamageInfo info)
    {
        if (visual != null)
        {
            visual.SetActive(false);
        }
    }
}
