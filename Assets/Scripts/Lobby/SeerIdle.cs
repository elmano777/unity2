using UnityEngine;

/// <summary>
/// Keeps the seer alive while she waits behind the orb: slow breathing, a slight sway, eyes that
/// flicker, and her head turning to follow the player around the room.
/// </summary>
public class SeerIdle : MonoBehaviour
{
    [SerializeField] private Transform body;
    [SerializeField] private Transform head;
    [SerializeField] private Renderer[] eyes;
    [SerializeField] private float breathAmount = 0.015f;
    [SerializeField] private float breathSpeed = 0.8f;
    [SerializeField] private float swayDegrees = 1.5f;
    [Tooltip("How far the head may turn from the body to look at the player.")]
    [SerializeField] private float maxHeadTurn = 35f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Vector3 bodyScale;
    private Quaternion headRest;
    private Color eyeColor;
    private MaterialPropertyBlock block;

    private void Awake()
    {
        bodyScale = body != null ? body.localScale : Vector3.one;
        headRest = head != null ? head.localRotation : Quaternion.identity;
        block = new MaterialPropertyBlock();

        if (eyes != null && eyes.Length > 0 && eyes[0] != null)
        {
            eyeColor = eyes[0].sharedMaterial.GetColor(ColorId);
        }
    }

    private void Update()
    {
        float t = Time.time;

        if (body != null)
        {
            float breath = 1f + Mathf.Sin(t * breathSpeed * Mathf.PI * 2f * 0.25f) * breathAmount;
            body.localScale = new Vector3(bodyScale.x * breath, bodyScale.y, bodyScale.z * breath);
            body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.35f) * swayDegrees);
        }

        LookAtPlayer();
        FlickerEyes(t);
    }

    private void LookAtPlayer()
    {
        Camera viewer = Camera.main;

        if (head == null || viewer == null)
        {
            return;
        }

        Vector3 local = transform.InverseTransformPoint(viewer.transform.position);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxHeadTurn, maxHeadTurn);
        Quaternion target = headRest * Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Slerp(head.localRotation, target, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
    }

    private void FlickerEyes(float t)
    {
        if (eyes == null)
        {
            return;
        }

        float flicker = 0.85f + 0.15f * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 3.1f);

        foreach (Renderer eye in eyes)
        {
            if (eye == null) continue;
            eye.GetPropertyBlock(block);
            block.SetColor(ColorId, eyeColor * flicker);
            eye.SetPropertyBlock(block);
        }
    }
}
