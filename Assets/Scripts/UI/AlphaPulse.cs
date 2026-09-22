using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slowly pulses the alpha of a UI Graphic (Image, TextMeshProUGUI, ...) or a TextMeshPro 3D text.
/// </summary>
public class AlphaPulse : MonoBehaviour
{
    [SerializeField] private float minAlpha = 0.25f;
    [SerializeField] private float maxAlpha = 1f;
    [Tooltip("Seconds for a full bright-dim-bright cycle.")]
    [SerializeField] private float period = 2f;

    private Graphic graphic;
    private TMPro.TMP_Text tmpText;

    private void Awake()
    {
        graphic = GetComponent<Graphic>();
        tmpText = GetComponent<TMPro.TMP_Text>();
    }

    private void Update()
    {
        float k = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.01f, period));
        float a = Mathf.Lerp(minAlpha, maxAlpha, k);

        if (tmpText != null)
        {
            tmpText.alpha = a;
        }
        else if (graphic != null)
        {
            Color c = graphic.color;
            c.a = a;
            graphic.color = c;
        }
    }
}
