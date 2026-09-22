using UnityEngine;

/// <summary>How an ability picks what it affects.</summary>
public enum AbilityShape
{
    /// <summary>Affects the caster only — a shield, a speed burst.</summary>
    Self = 0,

    /// <summary>A sphere centred on the caster.</summary>
    AreaAroundCaster = 1,

    /// <summary>A sphere at the point the player is aiming at.</summary>
    AreaAtAim = 2,

    /// <summary>A single hitscan line, like a heavier shot.</summary>
    Line = 3,
}

/// <summary>
/// One hero ability, cast with a controller button (RF-10). Data only for now — the component
/// that casts these arrives with the abilities deliverable; defining the data early keeps the
/// hero assets complete and lets the numbers be tuned during playtests.
/// Create via Assets > Create > Deadlock > Ability.
/// </summary>
[CreateAssetMenu(fileName = "NewAbility", menuName = "Deadlock/Ability", order = 21)]
public class AbilitySO : ScriptableObject
{
    [Header("Presentation")]
    public string abilityName = "New Ability";

    [TextArea(2, 4)]
    public string description;

    public Sprite icon;

    [Tooltip("Voice line or cast sound.")]
    public AudioClip castClip;

    [Tooltip("Effect spawned at the point of impact.")]
    public GameObject effectPrefab;

    [Header("Cost and timing")]
    [Tooltip("Seconds before it can be cast again.")]
    [Min(0f)]
    public float cooldown = 8f;

    [Tooltip("Seconds the effect lasts, for abilities that persist.")]
    [Min(0f)]
    public float duration;

    [Header("Effect")]
    public AbilityShape shape = AbilityShape.AreaAtAim;

    [Tooltip("Damage dealt to each hostile caught in the shape.")]
    public float damage = 60f;

    [Tooltip("Radius in metres for the area shapes, or range for Line.")]
    public float radius = 4f;

    [Tooltip("Maximum distance the player can place an aimed ability.")]
    public float castRange = 20f;
}
