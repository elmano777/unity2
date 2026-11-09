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
/// One hero ability, cast with a controller button (RF-10). <see cref="AbilityCaster"/> reads these
/// from the selected hero's <see cref="HeroDefinitionSO.abilities"/>; the numbers are tuned here so
/// playtests only touch assets.
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

    [Tooltip("Sound where the ability lands.")]
    public AudioClip impactClip;

    [Tooltip("Optional hum that loops where the ability lands for Loop Seconds (e.g. the storm cloud).")]
    public AudioClip loopClip;

    [Tooltip("Optional sound when the ability fades out, after the loop.")]
    public AudioClip endClip;

    [Tooltip("How long the loop plays before the end sound.")]
    [Min(0f)]
    public float loopSeconds = 2.5f;

    [Tooltip("Effect spawned at the point of impact.")]
    public GameObject effectPrefab;

    [Tooltip("Colour of the orb, the burst or the beam.")]
    public Color tint = new Color(0.45f, 0.8f, 1f, 1f);

    [Header("Cost and timing")]
    [Tooltip("Seconds before it can be cast again.")]
    [Min(0f)]
    public float cooldown = 8f;

    [Tooltip("Seconds the effect lasts, for abilities that persist.")]
    [Min(0f)]
    public float duration;

    [Tooltip("Seconds the orb takes to fly to an aimed area before it bursts. 0 bursts at once.")]
    [Min(0f)]
    public float travelTime = 0.3f;

    [Header("Effect")]
    public AbilityShape shape = AbilityShape.AreaAtAim;

    [Tooltip("Damage dealt to each hostile caught in the shape.")]
    public float damage = 60f;

    [Tooltip("Radius in metres for the area shapes, or range for Line.")]
    public float radius = 4f;

    [Tooltip("Maximum distance the player can place an aimed ability.")]
    public float castRange = 20f;
}
