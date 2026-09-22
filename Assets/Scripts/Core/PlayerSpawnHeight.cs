using UnityEngine;

/// <summary>
/// Keeps the player's head above the lane floor when the scene starts.
/// </summary>
/// <remarks>
/// OVRCameraRig ships with the Eye Level tracking origin, which puts the headset at the rig's
/// height — with the rig on the ground (y = 0) the player spawns with their eyes in the floor.
/// This switches the lane to Floor Level so the real standing height is used, and if tracking
/// still reports a head too low (no floor calibrated, Link in the editor, no headset at all) it
/// lifts the rig so the eyes end up at <see cref="standingEyeHeight"/>.
/// </remarks>
public class PlayerSpawnHeight : MonoBehaviour
{
    [Tooltip("Rig to correct. Found automatically (OVRCameraRig) when left empty.")]
    [SerializeField] private Transform rig;
    [Tooltip("Head. Found automatically (CenterEyeAnchor) when left empty.")]
    [SerializeField] private Transform head;
    [Tooltip("World height of the lane floor surface.")]
    [SerializeField] private float floorHeight = 0f;
    [Tooltip("A head closer to the floor than this counts as spawned inside it.")]
    [SerializeField] private float minEyeHeight = 1.2f;
    [Tooltip("Eye height the rig is lifted to when the head is too low.")]
    [SerializeField] private float standingEyeHeight = 1.65f;
    [Tooltip("How long after load to keep checking, while tracking settles.")]
    [SerializeField] private float settleSeconds = 3f;

    private float elapsed;
    private float spawnRigHeight;

    private void Awake()
    {
        if (rig == null)
        {
            OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
            rig = cameraRig != null ? cameraRig.transform : null;
        }

        if (head == null && rig != null)
        {
            OVRCameraRig cameraRig = rig.GetComponent<OVRCameraRig>();
            head = cameraRig != null ? cameraRig.centerEyeAnchor : rig.GetComponentInChildren<Camera>()?.transform;
        }

        OVRManager manager = rig != null ? rig.GetComponent<OVRManager>() : FindObjectOfType<OVRManager>();

        if (manager != null)
        {
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        }

        if (rig != null)
        {
            spawnRigHeight = rig.position.y;
        }
    }

    private void LateUpdate()
    {
        if (rig == null || head == null)
        {
            enabled = false;
            return;
        }

        // Worked out from the rig's spawn height each frame rather than nudged, so once floor
        // tracking kicks in the lift goes back to zero instead of stacking on top of it.
        float headAboveRig = head.position.y - rig.position.y;
        float spawnEyeHeight = spawnRigHeight + headAboveRig - floorHeight;
        float lift = spawnEyeHeight < minEyeHeight ? standingEyeHeight - spawnEyeHeight : 0f;

        Vector3 position = rig.position;
        position.y = spawnRigHeight + lift;
        rig.position = position;

        elapsed += Time.deltaTime;

        if (elapsed >= settleSeconds)
        {
            enabled = false;
        }
    }
}
