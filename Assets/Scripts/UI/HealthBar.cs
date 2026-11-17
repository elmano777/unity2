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

    [Tooltip("Optional second row (the patron's two bars): with it, the top row is the first half of the health and empties first, this one the second half.")]
    [SerializeField] private Transform secondFillPivot;

    /// <summary>
    /// Builds a bar at runtime over <paramref name="target"/> (for the rival's body, which is spawned
    /// from a prefab that has none). Always visible, red, turned to the viewer every frame.
    /// </summary>
    public static HealthBar CreateOver(Health target, Transform parent, Vector3 localOffset, float width = 0.7f, float height = 0.07f)
    {
        var root = new GameObject("RivalHealthBar");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localOffset;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);

        Shader shader = Shader.Find("Sprites/Default");
        MakeQuad("Back", visual.transform, Vector3.zero, new Vector3(width + 0.02f, height + 0.02f, 1f), new Color(0.03f, 0.03f, 0.05f, 0.9f), shader, 0);

        var pivot = new GameObject("FillPivot").transform;
        pivot.SetParent(visual.transform, false);
        pivot.localPosition = new Vector3(-width / 2f, 0f, -0.001f);
        MakeQuad("Fill", pivot, new Vector3(width / 2f, 0f, 0f), new Vector3(width, height, 1f), new Color(0.95f, 0.22f, 0.2f, 1f), shader, 1);

        HealthBar bar = root.AddComponent<HealthBar>();
        bar.OnDisable(); // Awake/OnEnable ran before the fields were set
        bar.health = target;
        bar.fillPivot = pivot;
        bar.visual = visual;
        bar.alwaysVisible = true;
        bar.Hook();
        return bar;
    }

    private static void MakeQuad(string name, Transform parent, Vector3 position, Vector3 scale, Color color, Shader shader, int order)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = scale;
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(shader) { color = color };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.sortingOrder = order;
    }

    private bool hooked;

    private void Hook()
    {
        if (health == null || hooked)
        {
            return;
        }

        hooked = true;
        health.HealthChanged += OnHealthChanged;
        health.Killed += OnKilled;
        OnHealthChanged(health.CurrentHealth, health.MaxHealth);
    }

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }

    private void OnEnable() => Hook();

    private void OnDisable()
    {
        if (health == null || !hooked)
        {
            return;
        }

        hooked = false;
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

        if (secondFillPivot != null)
        {
            SetFill(fillPivot, Mathf.Clamp01(fraction * 2f - 1f));
            SetFill(secondFillPivot, Mathf.Clamp01(fraction * 2f));
        }
        else
        {
            SetFill(fillPivot, fraction);
        }

        if (visual != null)
        {
            visual.SetActive(alwaysVisible || (fraction < 0.999f && health.IsAlive));
        }
    }

    private static void SetFill(Transform pivot, float fraction)
    {
        if (pivot == null)
        {
            return;
        }

        Vector3 scale = pivot.localScale;
        scale.x = Mathf.Max(0.0001f, fraction);
        pivot.localScale = scale;
    }

    private void OnKilled(DamageInfo info)
    {
        if (visual != null)
        {
            visual.SetActive(false);
        }
    }
}
