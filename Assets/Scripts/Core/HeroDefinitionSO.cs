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

    [Tooltip("Voice line played together with the story intro (or, for heroes without intro lines, when the match starts).")]
    public AudioClip entryClip;

    [Tooltip("3D model shown on the hero-select pedestal.")]
    public GameObject previewModel;

    [Tooltip("Story lines shown together (one per line) on a black screen after confirming this hero, while entryClip plays.")]
    [TextArea]
    public string[] introLines;
}
