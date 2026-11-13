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

    [Header("Cover card")]
    [Tooltip("Height of the hero's cover card floating over the hologram, in metres.")]
    [SerializeField] private float cardHeight = 0.7f;

    [Tooltip("Gap between the hologram's head and the bottom of the card, clear of the name above the hero.")]
    [SerializeField] private float cardGap = 0.7f;

    private Renderer[] bodyRenderers;
    private Transform card;
    private Material[] cardMaterials = System.Array.Empty<Material>();
    private Color[] cardColors = System.Array.Empty<Color>();
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
        BuildCard();
    }

    /// <summary>
    /// The hero's cover art as a small framed card over the hologram: the painted backdrop, the cover
    /// on it and a thin gold frame. It turns to face the player and lights up with the hologram.
    /// </summary>
    private void BuildCard()
    {
        Texture cover = hero == null ? null
            : hero.selectCover != null ? hero.selectCover
            : hero.portrait != null ? hero.portrait.texture
            : null;

        if (cover == null)
        {
            return;
        }

        float aspect = (float)cover.width / cover.height;
        Vector2 size = new Vector2(cardHeight * aspect, cardHeight);

        card = new GameObject("CoverCard").transform;
        card.SetParent(transform, false);
        card.localPosition = new Vector3(0f, height + cardGap + size.y / 2f, 0f);

        var materials = new List<Material>();
        materials.Add(CardQuad("Frame", Vector3.zero, size + new Vector2(0.03f, 0.03f), new Color(1f, 0.78f, 0.35f, 1f), null, 3000));

        if (hero.selectBackground != null)
        {
            Material back = CardQuad("Backdrop", new Vector3(0f, 0f, -0.001f), size, Color.white, hero.selectBackground, 3001);
            float textureAspect = (float)hero.selectBackground.width / hero.selectBackground.height;
            float scale = aspect / textureAspect;
            back.mainTextureScale = new Vector2(scale, 1f);
            back.mainTextureOffset = new Vector2((1f - scale) / 2f, 0f);
            materials.Add(back);
        }

        materials.Add(CardQuad("Cover", new Vector3(0f, 0f, -0.002f), size, Color.white, cover, 3002));

        cardMaterials = materials.ToArray();
        cardColors = new Color[cardMaterials.Length];

        for (int i = 0; i < cardMaterials.Length; i++)
        {
            cardColors[i] = cardMaterials[i].color;
        }
    }

    private Material CardQuad(string name, Vector3 local, Vector2 size, Color color, Texture texture, int queue)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(card, false);
        quad.transform.localPosition = local;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        Destroy(quad.GetComponent<Collider>());

        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color, mainTexture = texture, renderQueue = queue };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer.sharedMaterial;
    }

    /// <summary>Turns the card toward the player and brightens it with the hologram's glow.</summary>
    private void UpdateCard()
    {
        if (card == null)
        {
            return;
        }

        Camera cam = Camera.main;

        if (cam != null)
        {
            Vector3 away = card.position - cam.transform.position;
            away.y = 0f;

            if (away.sqrMagnitude > 0.001f)
            {
                // A quad shows its face along -Z, so it looks away from the viewer to be seen.
                card.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }

        float bright = Mathf.Clamp01(0.35f + 0.4f * intensity);
        float grow = hovered || chosen ? 1.08f : 1f;
        card.localScale = Vector3.Lerp(card.localScale, Vector3.one * grow, 1f - Mathf.Exp(-10f * Time.deltaTime));

        for (int i = 0; i < cardMaterials.Length; i++)
        {
            Color c = cardColors[i];
            cardMaterials[i].color = new Color(c.r * bright, c.g * bright, c.b * bright, c.a);
        }
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
        UpdateCard();

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
