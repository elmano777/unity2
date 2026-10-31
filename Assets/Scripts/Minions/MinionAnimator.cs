using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays a minion's idle, walk and action clips (a strike, a shot, or a medic's orb throw). The walk
/// blends in with the speed the minion really moves at, read off its position, so the host's minions
/// and the joining player's copies walk the same way. Actions come from <see cref="MinionAgent.Acted"/>.
/// A medic's throw also lets go of a glowing orb that flies to the ally; the heal lands when it does.
/// </summary>
[RequireComponent(typeof(MinionAgent))]
public class MinionAnimator : MonoBehaviour
{
    private const float StandStill = 0.15f;
    private const float FullWalk = 0.6f;

    [Header("Clips")]
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip attackClip;

    [Tooltip("Medic only: the throw that releases the healing orb. Leave empty for everyone else.")]
    [SerializeField] private AnimationClip healClip;

    [Tooltip("Name the clips give the node that holds the skeleton. The model's root is named after the file, so it is renamed to this.")]
    [SerializeField] private string rigRootName = "Trooper_Rig";

    [Tooltip("Metres per second the walk clip covers at the model's own scale; its playback is sped up or slowed down to match the real speed.")]
    [SerializeField] private float walkClipSpeed = 1.2f;

    [Header("Action blend")]
    [SerializeField] private float actionBlendIn = 0.05f;
    [SerializeField] private float actionBlendOut = 0.15f;

    [Header("Healing orb (medic)")]
    [Tooltip("Bone the orb leaves from.")]
    [SerializeField] private string orbHandBone = "RightHand";

    [SerializeField] private Material orbMaterial;
    [SerializeField] private float orbSize = 0.45f;

    [Tooltip("Seconds the orb a dying medic leaves takes to reach whoever killed it.")]
    [SerializeField] private float deathOrbFlightTime = 0.8f;

    private MinionAgent agent;
    private Transform orbHand;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idle;
    private AnimationClipPlayable walk;
    private AnimationClipPlayable attack;
    private AnimationClipPlayable heal;

    private float idleTime;
    private float walkTime;
    private float attackTime = -1f;
    private float healTime = -1f;
    private float walkWeight;
    private float speed;
    private Vector3 lastPosition;

    private void Awake()
    {
        agent = GetComponent<MinionAgent>();

        foreach (Transform node in GetComponentsInChildren<Transform>(true))
        {
            if (node.name == orbHandBone)
            {
                orbHand = node;
            }
        }

        foreach (Transform node in GetComponentsInChildren<Transform>(true))
        {
            if (node.name == "Hips")
            {
                SetUpClips(node);
                break;
            }
        }

        lastPosition = transform.position;
    }

    private void OnEnable()
    {
        agent.Acted += OnActed;
        agent.DeathOrb += OnDeathOrb;
    }

    private void OnDisable()
    {
        agent.Acted -= OnActed;
        agent.DeathOrb -= OnDeathOrb;
    }

    private void SetUpClips(Transform hips)
    {
        if (idleClip == null || walkClip == null || attackClip == null || hips.parent == null || hips.parent.parent == null)
        {
            return;
        }

        // The clips bind bones by their path (Trooper_Rig/Hips/…): the animator goes on the node
        // above the one that holds Hips.
        hips.parent.name = rigRootName;
        Animator animator = hips.parent.parent.gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;

        graph = PlayableGraph.Create("MinionAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        idle = AnimationClipPlayable.Create(graph, idleClip);
        walk = AnimationClipPlayable.Create(graph, walkClip);
        attack = AnimationClipPlayable.Create(graph, attackClip);
        heal = AnimationClipPlayable.Create(graph, healClip != null ? healClip : attackClip);
        idle.SetSpeed(0);
        walk.SetSpeed(0);
        attack.SetSpeed(0);
        heal.SetSpeed(0);

        mixer = AnimationMixerPlayable.Create(graph, 4);
        graph.Connect(idle, 0, mixer, 0);
        graph.Connect(walk, 0, mixer, 1);
        graph.Connect(attack, 0, mixer, 2);
        graph.Connect(heal, 0, mixer, 3);
        mixer.SetInputWeight(0, 1f);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Minion", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
    }

    private void OnActed(MinionAgent.Action action, Vector3 point)
    {
        if (action == MinionAgent.Action.Heal && healClip != null)
        {
            healTime = 0f;
            attackTime = -1f;
            StartCoroutine(ThrowOrb(point));
        }
        else if (action == MinionAgent.Action.Attack)
        {
            attackTime = 0f;
        }
    }

    private void Update()
    {
        if (!graph.IsValid())
        {
            return;
        }

        float dt = Time.deltaTime;

        if (dt > 0f)
        {
            Vector3 step = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up);
            float measured = step.magnitude < 2f ? step.magnitude / dt : 0f;
            speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-10f * dt));
        }

        lastPosition = transform.position;

        bool dead = agent.CurrentState == MinionAgent.State.Dead;
        float targetWalk = dead ? 0f : Mathf.InverseLerp(StandStill, FullWalk, speed);
        walkWeight = Mathf.Lerp(walkWeight, targetWalk, 1f - Mathf.Exp(-10f * dt));

        float modelScale = Mathf.Max(0.01f, transform.lossyScale.y);
        float cadence = Mathf.Clamp(speed / (walkClipSpeed * modelScale), 0.6f, 1.8f);

        idleTime = Mathf.Repeat(idleTime + dt, idleClip.length);
        walkTime = Mathf.Repeat(walkTime + dt * cadence, walkClip.length);
        idle.SetTime(idleTime);
        walk.SetTime(walkTime);

        float attackWeight = StepAction(ref attackTime, attackClip, attack, dt);
        float healWeight = healClip != null ? StepAction(ref healTime, healClip, heal, dt) : 0f;
        float action = Mathf.Clamp01(attackWeight + healWeight);

        float rest = 1f - action;
        mixer.SetInputWeight(0, rest * (1f - walkWeight));
        mixer.SetInputWeight(1, rest * walkWeight);
        mixer.SetInputWeight(2, attackWeight);
        mixer.SetInputWeight(3, healWeight);
        graph.Evaluate(0f);
    }

    /// <summary>Advances a one-shot clip and returns how much of the pose it owns right now.</summary>
    private float StepAction(ref float time, AnimationClip clip, AnimationClipPlayable playable, float dt)
    {
        if (time < 0f)
        {
            return 0f;
        }

        time += dt;

        if (time >= clip.length)
        {
            time = -1f;
            return 0f;
        }

        playable.SetTime(time);
        return Mathf.Min(Mathf.Clamp01(time / actionBlendIn), Mathf.Clamp01((clip.length - time) / actionBlendOut));
    }

    private IEnumerator ThrowOrb(Vector3 target)
    {
        yield return new WaitForSeconds(agent.HealOrbDelay);

        if (orbMaterial == null || orbHand == null)
        {
            yield break;
        }

        HealOrb.Launch(orbHand.position, null, target, Vector3.zero, agent.HealOrbFlightTime, orbMaterial, orbSize, 0.5f, null);
    }

    /// <summary>
    /// The medic's last orb: it flies from the body to whoever killed it. On the host it heals them when
    /// it lands; the joining player's copy only draws it.
    /// </summary>
    private void OnDeathOrb(Vector3 from, Transform target, Health healed)
    {
        if (orbMaterial == null || target == null)
        {
            return;
        }

        float amount = agent.HealAmount;

        HealOrb.Launch(from, target, from, Vector3.up * 0.3f, deathOrbFlightTime, orbMaterial, orbSize * 1.4f, 1.0f, () =>
        {
            if (healed != null && healed.IsAlive)
            {
                healed.Heal(amount);
            }
        });
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
