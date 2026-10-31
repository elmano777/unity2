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

    [Tooltip("Story lines shown together (one per line) on a black screen after confirming this hero, while entryClip plays.")]
    [TextArea]
    public string[] introLines;
}
