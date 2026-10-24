using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's own hand, drawn with Meta's rigged hand mesh and posed from the Touch controller:
/// grip closes the middle, ring and pinky fingers and the trigger curls the index. On the weapon
/// hand the fingers stay wrapped round the grip and only the index moves, so pulling the real
/// trigger is mirrored on the gun's.
/// </summary>
/// <remarks>
/// The project runs on controllers without hand tracking, so the pose is made up here instead of
/// tracked. Bones are found by the OculusHand naming (b_r_index1 …) and curled around their local
/// Z, which is the flexion axis on both the left and right mesh. Input System directly, like
/// <see cref="Weapon"/>, so nothing here depends on XRI.
/// </remarks>
public class ControllerHand : MonoBehaviour
{
    public enum Side
    {
        Left,
        Right,
    }

    [SerializeField] private Side side = Side.Right;

    [Tooltip("Holds the weapon: middle, ring, pinky and thumb stay wrapped round the grip whatever the grip button does.")]
    [SerializeField] private bool holdsWeapon;

    [Tooltip("Off on the rival's hands: they hold a fixed pose instead of following this machine's controllers.")]
    [SerializeField] private bool followControllers = true;

    [Tooltip("Grip, 0..1, held while not following the controllers.")]
    [SerializeField] private float idleGrip = 0.5f;

    [Header("Finger curl, degrees at each joint (knuckle, middle, tip)")]
    [SerializeField] private Vector3 relaxedCurl = new Vector3(12f, 18f, 10f);
    [SerializeField] private Vector3 fistCurl = new Vector3(70f, 95f, 55f);
    [SerializeField] private Vector3 triggerRestCurl = new Vector3(5f, 20f, 15f);
    [SerializeField] private Vector3 triggerPulledCurl = new Vector3(25f, 50f, 30f);

    [Header("Thumb curl, degrees at each joint (base, middle, tip)")]
    [SerializeField] private Vector3 thumbRelaxed = new Vector3(0f, 5f, 5f);
    [SerializeField] private Vector3 thumbWrapped = new Vector3(15f, 30f, 25f);

    [Tooltip("How fast the fingers follow the buttons. Higher = snappier.")]
    [SerializeField] private float smoothing = 20f;

    private static readonly string[] Fingers = { "index", "middle", "ring", "pinky" };

    private Transform[][] fingerJoints;
    private Quaternion[][] fingerRest;
    private Transform[] thumbJoints;
    private Quaternion[] thumbRest;

    private InputAction triggerAction;
    private InputAction gripAction;
    private float trigger;
    private float grip;

    private void OnEnable()
    {
        if (!followControllers)
        {
            return;
        }

        string hand = side == Side.Right ? "{RightHand}" : "{LeftHand}";

        triggerAction = new InputAction("Trigger", InputActionType.Value, $"<XRController>{hand}/{{Trigger}}");
        gripAction = new InputAction("Grip", InputActionType.Value, $"<XRController>{hand}/{{Grip}}");
        triggerAction.Enable();
        gripAction.Enable();
    }

    private void OnDisable()
    {
        triggerAction?.Dispose();
        gripAction?.Dispose();
        triggerAction = null;
        gripAction = null;
    }

    private void LateUpdate()
    {
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        trigger = Mathf.Lerp(trigger, triggerAction != null ? triggerAction.ReadValue<float>() : 0f, t);
        grip = Mathf.Lerp(grip, gripAction != null ? gripAction.ReadValue<float>() : idleGrip, t);

        ApplyPose(trigger, grip);
    }

    /// <summary>
    /// Poses the fingers for the given button values, 0..1. Public so a throwaway copy can be
    /// posed in the editor, where LateUpdate doesn't run. Never call it on a saved hand outside
    /// Play mode: the curled bones would be stored as the rest pose.
    /// </summary>
    public void ApplyPose(float triggerValue, float gripValue)
    {
        if (!FindBones())
        {
            return;
        }

        float wrap = holdsWeapon ? 1f : gripValue;
        Vector3 index = holdsWeapon
            ? Vector3.Lerp(triggerRestCurl, triggerPulledCurl, triggerValue)
            : Vector3.Lerp(relaxedCurl, fistCurl, triggerValue);
        Vector3 others = Vector3.Lerp(relaxedCurl, fistCurl, wrap);

        Curl(fingerJoints[0], fingerRest[0], index);

        for (int f = 1; f < Fingers.Length; f++)
        {
            Curl(fingerJoints[f], fingerRest[f], others);
        }

        Curl(thumbJoints, thumbRest, Vector3.Lerp(thumbRelaxed, thumbWrapped, wrap));
    }

    private static void Curl(Transform[] joints, Quaternion[] rest, Vector3 degrees)
    {
        for (int j = 0; j < joints.Length; j++)
        {
            if (joints[j] != null)
            {
                joints[j].localRotation = rest[j] * Quaternion.AngleAxis(-degrees[j], Vector3.forward);
            }
        }
    }

    /// <summary>Caches every finger joint and its rest rotation the first time it is needed.</summary>
    private bool FindBones()
    {
        if (fingerJoints != null)
        {
            return true;
        }

        string prefix = side == Side.Right ? "b_r_" : "b_l_";
        var all = GetComponentsInChildren<Transform>(true);

        fingerJoints = new Transform[Fingers.Length][];
        fingerRest = new Quaternion[Fingers.Length][];

        for (int f = 0; f < Fingers.Length; f++)
        {
            fingerJoints[f] = Joints(all, prefix + Fingers[f], 1, out fingerRest[f]);
        }

        thumbJoints = Joints(all, prefix + "thumb", 1, out thumbRest);

        if (fingerJoints[0][0] == null)
        {
            Debug.LogWarning($"[ControllerHand] {name}: no {prefix}index1 bone, is this an OculusHand mesh?");
            fingerJoints = null;
            return false;
        }

        return true;
    }

    private static Transform[] Joints(Transform[] all, string bone, int first, out Quaternion[] rest)
    {
        var joints = new Transform[3];
        rest = new Quaternion[3];

        for (int j = 0; j < 3; j++)
        {
            string wanted = bone + (first + j);

            foreach (Transform t in all)
            {
                if (t.name == wanted)
                {
                    joints[j] = t;
                    rest[j] = t.localRotation;
                    break;
                }
            }
        }

        return joints;
    }
}
