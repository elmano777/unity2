using UnityEngine;

/// <summary>Billboard: keeps a flame quad or a floating label turned towards the player's head.</summary>
public class FaceCamera : MonoBehaviour
{
    [Tooltip("Only turn around the vertical axis, so labels stay upright.")]
    [SerializeField] private bool yawOnly = true;

    private void LateUpdate()
    {
        Camera viewer = Camera.main;

        if (viewer == null)
        {
            return;
        }

        Vector3 away = transform.position - viewer.transform.position;

        if (yawOnly)
        {
            away.y = 0f;
        }

        if (away.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
