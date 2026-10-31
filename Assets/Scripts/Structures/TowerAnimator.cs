using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Brings the Guardian to life: an idle loop, a recoil when the staff fires, and a collapse when the
/// tower falls. While the tower has a target the staff swings so its tip points at it (the beam
/// leaves from the tip), and the torso turns a little with it. The clips come from Blender; the aim
/// is computed here, on top of whatever the clips did that frame, because the target is not known
/// in advance.
/// </summary>
[RequireComponent(typeof(Tower))]
public class TowerAnimator : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip attackClip;
    [SerializeField] private AnimationClip dieClip;

    [Tooltip("Name the clips give the node that holds the skeleton. The model's root is named after the file, so it is renamed to this.")]
    [SerializeField] private string rigRootName = "Guardian_Rig";

    [Header("Aim")]
    [Tooltip("How far the staff may lean away from its resting pose to point at a target, in degrees.")]
    [SerializeField] private float maxTilt = 24f;

    [Tooltip("How far the torso may turn towards the target, in degrees.")]
    [SerializeField] private float maxBodyYaw = 22f;

    [SerializeField] private float aimSharpness = 7f;

    [Header("Blend")]
    [SerializeField] private float attackBlendIn = 0.05f;
    [SerializeField] private float attackBlendOut = 0.2f;
    [SerializeField] private float dieBlendIn = 0.25f;

    private Tower tower;
    private Transform staff;
    private Transform staffTip;
    private Transform chest;
    private Transform head;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idle;
    private AnimationClipPlayable attack;
    private AnimationClipPlayable die;

    private float idleTime;
    private float attackTime = -1f;
    private float dieTime = -1f;
    private float aimWeight;
    private Vector3 smoothedAim;

    private void Awake()
    {
        tower = GetComponent<Tower>();

        foreach (Transform node in GetComponentsInChildren<Transform>(true))
        {
            switch (node.name)
            {
                case "Staff": staff = node; break;
                case "StaffTip": staffTip = node; break;
                case "Chest": chest = node; break;
                case "Head": head = node; break;
                case "Hips": SetUpClips(node); break;
            }
        }
    }

    private void OnEnable()
    {
        tower.Shot += OnShot;
        tower.Destroyed += OnDestroyed;
    }

    private void OnDisable()
    {
        tower.Shot -= OnShot;
        tower.Destroyed -= OnDestroyed;
    }

    private void SetUpClips(Transform hips)
    {
        if (idleClip == null || attackClip == null || dieClip == null || hips.parent == null || hips.parent.parent == null)
        {
            return;
        }

        // The clips bind bones by their path (Guardian_Rig/Hips/…): the animator goes on the node
        // above the one that holds Hips.
        hips.parent.name = rigRootName;
        Animator animator = hips.parent.parent.gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;

        graph = PlayableGraph.Create("TowerAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        idle = AnimationClipPlayable.Create(graph, idleClip);
        attack = AnimationClipPlayable.Create(graph, attackClip);
        die = AnimationClipPlayable.Create(graph, dieClip);
        idle.SetSpeed(0);
        attack.SetSpeed(0);
        die.SetSpeed(0);

        mixer = AnimationMixerPlayable.Create(graph, 3);
        graph.Connect(idle, 0, mixer, 0);
        graph.Connect(attack, 0, mixer, 1);
        graph.Connect(die, 0, mixer, 2);
        mixer.SetInputWeight(0, 1f);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Tower", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
    }

    private void OnShot(Vector3 point)
    {
        attackTime = 0f;
    }

    private void OnDestroyed()
    {
        dieTime = 0f;
    }

    private void Update()
    {
        if (!graph.IsValid())
        {
            return;
        }

        float dt = Time.deltaTime;
        idleTime = Mathf.Repeat(idleTime + dt, idleClip.length);
        idle.SetTime(idleTime);

        float attackWeight = 0f;

        if (attackTime >= 0f)
        {
            attackTime += dt;

            if (attackTime >= attackClip.length)
            {
                attackTime = -1f;
            }
            else
            {
                attack.SetTime(attackTime);
                float fadeIn = Mathf.Clamp01(attackTime / attackBlendIn);
                float fadeOut = Mathf.Clamp01((attackClip.length - attackTime) / attackBlendOut);
                attackWeight = Mathf.Min(fadeIn, fadeOut);
            }
        }

        float dieWeight = 0f;

        if (dieTime >= 0f)
        {
            dieTime = Mathf.Min(dieTime + dt, dieClip.length);
            die.SetTime(dieTime);
            dieWeight = Mathf.Clamp01(dieTime / dieBlendIn);
        }

        float standing = 1f - dieWeight;
        mixer.SetInputWeight(0, standing * (1f - attackWeight));
        mixer.SetInputWeight(1, standing * attackWeight);
        mixer.SetInputWeight(2, dieWeight);
        graph.Evaluate(0f);
    }

    private void LateUpdate()
    {
        if (staff == null || staffTip == null)
        {
            return;
        }

        bool aiming = tower.HasAimPoint && dieTime < 0f;
        aimWeight = Mathf.MoveTowards(aimWeight, aiming ? 1f : 0f, aimSharpness * 0.5f * Time.deltaTime);

        if (aiming)
        {
            smoothedAim = smoothedAim == Vector3.zero
                ? tower.AimPoint
                : Vector3.Lerp(smoothedAim, tower.AimPoint, 1f - Mathf.Exp(-aimSharpness * Time.deltaTime));
        }

        if (aimWeight <= 0.001f || smoothedAim == Vector3.zero)
        {
            return;
        }

        // Torso first: it carries the arm that holds the staff, so the staff aim is worked out after it.
        if (chest != null)
        {
            Vector3 flat = Vector3.ProjectOnPlane(smoothedAim - chest.position, Vector3.up);
            Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

            if (flat.sqrMagnitude > 0.01f && facing.sqrMagnitude > 0.01f)
            {
                float yaw = Mathf.Clamp(Vector3.SignedAngle(facing, flat, Vector3.up) * 0.4f, -maxBodyYaw, maxBodyYaw);
                chest.rotation = Quaternion.AngleAxis(yaw * aimWeight, Vector3.up) * chest.rotation;
            }
        }

        Vector3 along = staffTip.position - staff.position;
        Vector3 wanted = smoothedAim - staff.position;

        if (along.sqrMagnitude < 0.0001f || wanted.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Point the tip at the target, but never lean further than the arm can plausibly hold.
        Quaternion toTarget = Quaternion.FromToRotation(along, wanted);
        Quaternion limited = Quaternion.RotateTowards(Quaternion.identity, toTarget, maxTilt);
        staff.rotation = Quaternion.Slerp(Quaternion.identity, limited, aimWeight) * staff.rotation;

        if (head != null)
        {
            Vector3 look = Vector3.ProjectOnPlane(smoothedAim - head.position, Vector3.up);
            Vector3 headFacing = Vector3.ProjectOnPlane(head.forward, Vector3.up);

            if (look.sqrMagnitude > 0.01f && headFacing.sqrMagnitude > 0.01f)
            {
                float turn = Mathf.Clamp(Vector3.SignedAngle(headFacing, look, Vector3.up) * 0.3f, -12f, 12f);
                head.rotation = Quaternion.AngleAxis(turn * aimWeight, Vector3.up) * head.rotation;
            }
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
