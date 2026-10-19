using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One hero in the spirit-vision selector: the hero's model drawn as a green hologram standing on
/// a rune circle. Pointing at it makes it glow; pulling the trigger chooses it through
/// <see cref="HeroSelectManager.SelectHero"/>, which plays the voice line and materialises the solid
/// hero in the middle of the vision.
/// </summary>
public class SpiritHero : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private HeroSelectManager manager;
    [SerializeField] private HeroDefinitionSO hero;
    [SerializeField] private int heroIndex;
    [SerializeField] private Material hologramMaterial;
    [SerializeField] private float height = 1.85f;

    [Tooltip("Rune circle under the hero; brighter while hovered or chosen.")]
    [SerializeField] private Renderer runeCircle;

    [SerializeField] private float idleIntensity = 0.8f;
    [SerializeField] private float hoverIntensity = 2f;
    [SerializeField] private float chosenIntensity = 1.6f;
    [SerializeField] private float dimmedIntensity = 0.45f;

    private static readonly List<SpiritHero> All = new List<SpiritHero>();
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock block;
    private Color runeColor;
    private float intensity;
    private bool hovered;
    private bool chosen;
    private bool someoneChosen;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        intensity = idleIntensity;

        if (runeCircle != null)
        {
            runeColor = runeCircle.sharedMaterial.GetColor(ColorId);
        }

        BuildHologram();
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void BuildHologram()
    {
        if (hero == null || hero.previewModel == null)
        {
            bodyRenderers = new Renderer[0];
            return;
        }

        GameObject model = HeroSelectManager.BuildPreview(hero.previewModel, transform, height);
        bodyRenderers = model.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in bodyRenderers)
        {
            var materials = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = hologramMaterial;
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // The laser needs something to hit: a capsule around the figure.
        var capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
        capsule.height = height;
        capsule.radius = 0.4f;
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
        Choose();
    }

    /// <summary>Selects this hero (same as a laser click).</summary>
    public void Choose()
    {
        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.5f, 0.08f);

        if (manager != null)
        {
            manager.SelectHero(heroIndex);
        }

        foreach (SpiritHero spirit in All)
        {
            spirit.chosen = spirit == this;
            spirit.someoneChosen = true;
        }
    }

    private void Update()
    {
        float target = hovered ? hoverIntensity
            : chosen ? chosenIntensity
            : someoneChosen ? dimmedIntensity
            : idleIntensity;

        intensity = Mathf.Lerp(intensity, target, 1f - Mathf.Exp(-10f * Time.deltaTime));

        foreach (Renderer r in bodyRenderers)
        {
            r.GetPropertyBlock(block);
            block.SetFloat(IntensityId, intensity);
            r.SetPropertyBlock(block);
        }

        if (runeCircle != null)
        {
            runeCircle.GetPropertyBlock(block);
            block.SetColor(ColorId, runeColor * (0.5f + 0.5f * intensity));
            runeCircle.SetPropertyBlock(block);
        }
    }
}
