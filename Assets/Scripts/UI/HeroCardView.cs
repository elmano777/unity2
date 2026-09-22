using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Visual + interaction for one hero card on the Hero Select canvas.
/// Displays the hero's portrait and name, scales up and brightens its border on hover,
/// and stays highlighted while selected. HeroSelectManager binds it at runtime.
/// </summary>
[RequireComponent(typeof(Button))]
public class HeroCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image border;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;

    [Header("Feedback")]
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float selectedScale = 1.03f;
    [SerializeField] private float scaleSpeed = 12f;
    [SerializeField] private Color normalBorderColor = new Color(0.72f, 0.42f, 0.12f, 1f);
    [SerializeField] private Color hoverBorderColor = new Color(1f, 0.72f, 0.3f, 1f);
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.85f, 0.45f, 1f);

    private Button button;
    private bool hovered;
    private bool selected;
    private Vector3 baseScale;

    public Button Button => button != null ? button : (button = GetComponent<Button>());

    private void Awake()
    {
        button = GetComponent<Button>();
        baseScale = transform.localScale;
        RefreshBorder();
    }

    /// <summary>Fills the card with a hero's data.</summary>
    public void Bind(HeroDefinitionSO hero)
    {
        if (hero == null) return;
        if (portraitImage != null && hero.portrait != null)
        {
            portraitImage.sprite = hero.portrait;
            portraitImage.preserveAspect = true;
        }
        if (nameText != null)
        {
            nameText.text = hero.heroName.ToUpperInvariant();
        }
    }

    public void SetSelected(bool value)
    {
        selected = value;
        RefreshBorder();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        RefreshBorder();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        RefreshBorder();
    }

    private void OnDisable()
    {
        hovered = false;
        transform.localScale = baseScale;
    }

    private void Update()
    {
        float target = hovered ? hoverScale : (selected ? selectedScale : 1f);
        transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target,
            1f - Mathf.Exp(-scaleSpeed * Time.unscaledDeltaTime));
    }

    private void RefreshBorder()
    {
        if (border == null) return;
        border.color = selected ? selectedBorderColor : (hovered ? hoverBorderColor : normalBorderColor);
    }
}
