using UnityEngine;

/// <summary>
/// Defines a selectable hero: display name, portrait card art, 3D preview model, voice lines
/// (hero-select bark and match-entry line) and the story intro shown after confirming the hero.
/// Create new hero instances via Assets > Create > Deadlock > Hero Definition.
/// </summary>
[CreateAssetMenu(fileName = "NewHero", menuName = "Deadlock/Hero Definition", order = 0)]
public class HeroDefinitionSO : ScriptableObject
{
    [Tooltip("Display name shown on the Hero Select card.")]
    public string heroName;

    [Tooltip("Portrait art shown on the Hero Select card.")]
    public Sprite portrait;

    [Tooltip("Voice line played when the player picks this hero on the select screen.")]
    public AudioClip selectClip;

    [Tooltip("Subtitle shown while selectClip plays (RNF-08).")]
    [TextArea]
    public string selectLine;

    [Tooltip("Voice line played together with the story intro (or, for heroes without intro lines, when the match starts).")]
    public AudioClip entryClip;

    [Tooltip("Voice line played on the end screen when the player wins the match.")]
    public AudioClip winClip;

    [Tooltip("Weapon prefab held in the right hand during the match.")]
    public GameObject weaponPrefab;

    [Tooltip("3D model shown on the hero-select pedestal.")]
    public GameObject previewModel;

    [Tooltip("Looping idle clip played on the rival's model while this hero stands still (optional: models without a skeleton stay static).")]
    public AnimationClip idleClip;

    [Tooltip("Looping walk clip played on the rival's model while this hero moves (optional).")]
    public AnimationClip walkClip;

    [Tooltip("Name the clips give the node that holds the skeleton (Hips and its children). Unity names that node after the model file, so the rival's copy is renamed to this before the clips play.")]
    public string rigRootName = "Seven_Rig";

    [Tooltip("How fast the walk clip travels in metres per second at the model's original scale; the rival's walk is slowed or sped up to match their real speed.")]
    public float walkClipSpeed = 1.7f;

    [Tooltip("Abilities in button order: the first on A, the second on X (RF-10).")]
    public AbilitySO[] abilities;

    [Header("Stats (scaled from the original game to this 1v1 VR lane)")]
    [Tooltip("Hit points of the hero's body at the start of the ritual.")]
    [Min(1f)]
    public float maxHealth = 300f;

    [Tooltip("Hit points healed per second while hurt, before shop items.")]
    [Min(0f)]
    public float healthRegen;

    [Tooltip("Walking speed in m/s at full stick, before the shop's boots.")]
    [Min(0.5f)]
    public float moveSpeed = 3.75f;

    [Header("Levels (each level the souls collected reach, see HeroProgression)")]
    [Tooltip("Hit points added to the body at each level up.")]
    [Min(0f)]
    public float healthPerLevel;

    [Tooltip("Damage added to each bullet at each level up.")]
    [Min(0f)]
    public float bulletDamagePerLevel;

    [Tooltip("Ability damage added at each level up, as a fraction (0.03 = +3 %): the original game's spirit power.")]
    [Min(0f)]
    public float abilityPowerPerLevel;

    [Tooltip("Story lines shown together (one per line) on a black screen after confirming this hero, while entryClip plays.")]
    [TextArea]
    public string[] introLines;

    [Header("Art (from the wiki)")]
    [Tooltip("Full-body render with a transparent background: the big figure on the MVP end screen.")]
    public Texture2D render;

    [Tooltip("The hero's painted backdrop, shown behind the render on the end screen and the select card.")]
    public Texture2D selectBackground;

    [Tooltip("The hero's name as a white logo (optional: the name is written in text when missing).")]
    public Texture2D nameLogo;

    [Tooltip("Tall cover art floated over the hologram on the hero-select screen.")]
    public Texture2D selectCover;

    [Tooltip("HUD portrait while all is well.")]
    public Texture2D hudNormal;

    [Tooltip("HUD portrait when health is critical.")]
    public Texture2D hudCritical;

    [Tooltip("HUD portrait while on a streak (a kill, a structure down, several minions in a row).")]
    public Texture2D hudStreak;
}
