using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The seer's crystal ball in the lobby. The player strikes it, either by touching it with a
/// controller or by pointing the laser at it and pulling the trigger, and it answers with a flash,
/// a chime and the menu runes ("Partida online", "Mi misión") rising out of it.
/// </summary>
/// <remarks>
/// Replaces the flat title buttons with something the player walks up to and acts on, so the very
/// first screen already teaches moving (stick) and selecting (trigger) in the space.
/// </remarks>
public class OracleOrb : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private Renderer orbRenderer;
    [SerializeField] private Light orbLight;
    [SerializeField] private ParticleSystem strikeBurst;
    [SerializeField] private AudioSource strikeSource;
    [SerializeField] private AudioSource droneSource;

    [Tooltip("Shown when the orb is struck: the menu runes.")]
    [SerializeField] private GameObject revealOnStrike;

    [Tooltip("Floating instruction above the orb.")]
    [SerializeField] private TMP_Text hint;
    [SerializeField] [TextArea] private string hintBefore = "Camina hacia la vidente\ny toca la esfera";
    [SerializeField] [TextArea] private string hintAfter = "Elige tu camino";

    [Header("Touch")]
    [Tooltip("A controller this close to the orb's centre counts as striking it.")]
    [SerializeField] private float touchRadius = 0.32f;

    [Header("Glow")]
    [SerializeField] private float idleEnergy = 1f;
    [SerializeField] private float hoverEnergy = 1.7f;
    [SerializeField] private float strikeEnergy = 3f;
    [SerializeField] private float baseLightIntensity = 2.2f;

    private static readonly int EnergyId = Shader.PropertyToID("_Energy");

    private MaterialPropertyBlock block;
    private Transform leftHand;
    private Transform rightHand;
    private bool hovered;
    private bool struck;
    private bool handInside;
    private float energy;
    private float flash;

    public bool IsStruck => struck;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        energy = idleEnergy;

        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();

        if (rig != null)
        {
            leftHand = rig.leftControllerAnchor != null ? rig.leftControllerAnchor : rig.leftHandAnchor;
            rightHand = rig.rightControllerAnchor != null ? rig.rightControllerAnchor : rig.rightHandAnchor;
        }

        if (revealOnStrike != null)
        {
            revealOnStrike.SetActive(false);
        }

        if (hint != null)
        {
            hint.text = hintBefore;
        }

        if (droneSource != null)
        {
            droneSource.clip = ProceduralAudio.Drone();
            droneSource.loop = true;
            droneSource.Play();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.15f, 0.03f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Strike(HapticFeedback.Hand.Right);
    }

    /// <summary>Flash, chime and reveal the runes. Striking again replays the effect only.</summary>
    public void Strike(HapticFeedback.Hand hand)
    {
        flash = 1f;
        HapticFeedback.Pulse(hand, 0.8f, 0.15f);

        if (strikeBurst != null) strikeBurst.Play();

        if (strikeSource != null)
        {
            strikeSource.PlayOneShot(ProceduralAudio.Chime());
        }

        if (struck)
        {
            return;
        }

        struck = true;

        if (revealOnStrike != null) revealOnStrike.SetActive(true);
        if (hint != null) hint.text = hintAfter;
    }

    private void Update()
    {
        CheckTouch();

        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.5f);

        float baseEnergy = hovered ? hoverEnergy : (struck ? (idleEnergy + hoverEnergy) * 0.5f : idleEnergy);
        float pulse = 0.12f * Mathf.Sin(Time.time * 2.1f);
        float target = Mathf.Lerp(baseEnergy + pulse, strikeEnergy, flash);
        energy = Mathf.Lerp(energy, target, 1f - Mathf.Exp(-10f * Time.deltaTime));

        if (orbRenderer != null)
        {
            orbRenderer.GetPropertyBlock(block);
            block.SetFloat(EnergyId, energy);
            orbRenderer.SetPropertyBlock(block);
        }

        if (orbLight != null)
        {
            orbLight.intensity = baseLightIntensity * (0.7f + 0.3f * energy);
        }
    }

    /// <summary>Strike on entering the orb with a hand, once per entry, so holding a hand inside doesn't repeat it.</summary>
    private void CheckTouch()
    {
        bool left = IsInside(leftHand);
        bool right = IsInside(rightHand);
        bool inside = left || right;

        if (inside && !handInside)
        {
            Strike(right ? HapticFeedback.Hand.Right : HapticFeedback.Hand.Left);
        }

        handInside = inside;
    }

    private bool IsInside(Transform hand)
    {
        return hand != null && hand.gameObject.activeInHierarchy &&
               (hand.position - transform.position).sqrMagnitude < touchRadius * touchRadius;
    }
}
