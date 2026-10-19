using UnityEngine;

/// <summary>
/// Comfort vignette (RNF-02): darkens the edge of the view while the stick moves the player, and
/// blinks briefly on a snap turn. Narrowing peripheral vision during artificial motion is the
/// standard way to cut VR motion sickness. Can be switched off in the settings (PlayerPrefs).
/// </summary>
/// <remarks>
/// A quad parented to the head, drawn last with a radial gradient shader. Kept a metre away
/// rather than at the near plane so both eyes see it centred the same way.
/// </remarks>
public class ComfortVignette : MonoBehaviour
{
    public const string EnabledKey = "ComfortVignette";

    [SerializeField] private ComfortLocomotion locomotion;
    [SerializeField] private Renderer quad;

    [Tooltip("Edge darkness at full stick speed (0-1).")]
    [SerializeField] private float maxStrength = 0.8f;

    [Tooltip("Darkness held for a moment after a snap turn.")]
    [SerializeField] private float turnStrength = 0.9f;

    [Tooltip("How fast the vignette closes in and opens up again.")]
    [SerializeField] private float fadeInSpeed = 6f;
    [SerializeField] private float fadeOutSpeed = 3f;

    private static readonly int StrengthId = Shader.PropertyToID("_Strength");

    private Material material;
    private float strength;
    private float turnBoost;

    /// <summary>Whether the player has the vignette turned on. On by default.</summary>
    public static bool IsEnabled
    {
        get => PlayerPrefs.GetInt(EnabledKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void Awake()
    {
        if (locomotion == null)
        {
            locomotion = FindObjectOfType<ComfortLocomotion>();
        }

        if (quad != null)
        {
            material = quad.material;
        }
    }

    private void OnEnable()
    {
        if (locomotion != null)
        {
            locomotion.Turned += OnTurned;
        }
    }

    private void OnDisable()
    {
        if (locomotion != null)
        {
            locomotion.Turned -= OnTurned;
        }
    }

    private void OnTurned()
    {
        turnBoost = turnStrength;
    }

    private void LateUpdate()
    {
        if (material == null)
        {
            return;
        }

        float target = 0f;

        if (IsEnabled)
        {
            float moving = locomotion != null ? locomotion.Speed01 * maxStrength : 0f;
            target = Mathf.Max(moving, turnBoost);
        }

        float speed = target > strength ? fadeInSpeed : fadeOutSpeed;
        strength = Mathf.MoveTowards(strength, target, speed * Time.deltaTime);
        turnBoost = Mathf.MoveTowards(turnBoost, 0f, fadeOutSpeed * Time.deltaTime);

        material.SetFloat(StrengthId, strength);
        quad.enabled = strength > 0.001f;
    }
}
