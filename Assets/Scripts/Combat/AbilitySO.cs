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

    /// <summary>
    /// A ball that travels in a straight line for its lifetime (Duration), zapping what is inside its
    /// radius; it crawls while it is touching enemies and stops for good where it meets the world.
    /// </summary>
    Projectile = 4,

    /// <summary>The caster becomes a storm: floats up, keeps moving, can't shoot, and lightning hits everything around.</summary>
    Channel = 5,
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

    [Header("Travelling ball (Projectile)")]
    [Tooltip("Metres per second the ball travels at.")]
    [Min(0.5f)]
    public float travelSpeed = 6f;

    [Tooltip("Damage per second to hostiles inside the ball's radius while it travels (the burst at the end deals Damage).")]
    [Min(0f)]
    public float damagePerSecond;

    [Tooltip("Seconds between zaps (the ball's, or the storm's damage ticks).")]
    [Min(0.1f)]
    public float tickInterval = 0.5f;

    [Tooltip("Share of its speed the ball keeps while it is touching enemies.")]
    [Range(0.05f, 1f)]
    public float slowedSpeed = 0.2f;

    [Header("Channel (storm cloud)")]
    [Tooltip("Seconds the caster stays a storm.")]
    [Min(0.5f)]
    public float channelSeconds = 6f;

    [Tooltip("Metres the caster floats up while channelling.")]
    [Min(0f)]
    public float riseHeight = 2.5f;

    [Tooltip("Radius the storm grows to (it starts at Radius).")]
    [Min(0f)]
    public float radiusEnd = 12f;

    [Tooltip("Metres per second the storm's radius grows.")]
    [Min(0f)]
    public float growSpeed = 1f;

    [Header("Movement effects")]
    [Tooltip("How much slower hostiles caught walk (0.3 = 30 % slower).")]
    [Range(0f, 0.9f)]
    public float slow;

    [Min(0f)]
    public float slowSeconds = 1.5f;

    [Tooltip("Seconds hostiles caught are held near where it landed (a stake's tether). 0 = no tether.")]
    [Min(0f)]
    public float tetherSeconds;

    [Tooltip("Metres a tethered hostile can stray from where it landed.")]
    [Min(0.5f)]
    public float tetherLength = 4f;

    [Header("Charge")]
    [Tooltip("Seconds of holding the button for full damage; released early it fires weaker. 0 = fires on press.")]
    [Min(0f)]
    public float chargeSeconds;

    [Tooltip("Fraction of Damage an uncharged release deals.")]
    [Range(0.1f, 1f)]
    public float unchargedDamage = 0.5f;

    [Header("Charges")]
    [Tooltip("Casts it holds. Each one used refills on its own after Cooldown.")]
    [Min(1)]
    public int charges = 1;

    [Tooltip("Seconds between two casts while it still has charges.")]
    [Min(0f)]
    public float chargeGap = 2.5f;

    [Header("Line shots")]
    [Tooltip("The shot goes through enemies and hurts every one on its line, stopped only by the world.")]
    public bool pierce;

    [Tooltip("Extra damage against targets under half health, growing to this as their health drops to zero.")]
    [Min(0f)]
    public float lowHealthBonus;

    /// <summary>True when the ability holds or slows what it catches.</summary>
    public bool HasMovementEffect => slow > 0f || tetherSeconds > 0f;
}
