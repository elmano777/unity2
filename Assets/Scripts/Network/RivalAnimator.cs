using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays a hero's idle and walk clips on the rival's model, blending between them by how fast
/// the rival is moving. Goes on the node that holds the model's skeleton root, since the clips
/// bind bones by their path from it. Time is advanced by hand so the clips loop whatever their
/// import settings say.
/// </summary>
public class RivalAnimator : MonoBehaviour
{
    private const float StandStill = 0.15f;
    private const float FullWalk = 0.6f;
    private const float BlendSharpness = 10f;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable walkPlayable;
    private AnimationClip idle;
    private AnimationClip walk;
    private float walkClipSpeed;
    private float idleTime;
    private float walkTime;
    private float walkWeight;
    private float speed;

    public void Setup(AnimationClip idleClip, AnimationClip walkClip, float clipSpeed)
    {
        idle = idleClip;
        walk = walkClip;
        walkClipSpeed = Mathf.Max(0.1f, clipSpeed);

        Animator animator = gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;

        graph = PlayableGraph.Create("RivalAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        idlePlayable = AnimationClipPlayable.Create(graph, idle);
        walkPlayable = AnimationClipPlayable.Create(graph, walk);
        idlePlayable.SetSpeed(0);
        walkPlayable.SetSpeed(0);

        mixer = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(idlePlayable, 0, mixer, 0);
        graph.Connect(walkPlayable, 0, mixer, 1);
        mixer.SetInputWeight(0, 1f);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Rival", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
    }

    /// <summary>The rival's horizontal speed in world metres per second.</summary>
    public void SetSpeed(float metresPerSecond)
    {
        speed = metresPerSecond;
    }

    private void Update()
    {
        if (!graph.IsValid())
        {
            return;
        }

        float dt = Time.deltaTime;
        float target = Mathf.InverseLerp(StandStill, FullWalk, speed);
        walkWeight = Mathf.Lerp(walkWeight, target, 1f - Mathf.Exp(-BlendSharpness * dt));

        // The model is scaled to the hero's height, so its walk covers less ground than it did
        // in the modelling tool: stretch the clip's time to match the speed the rival really has.
        float modelScale = Mathf.Max(0.01f, transform.lossyScale.y);
        float cadence = Mathf.Clamp(speed / (walkClipSpeed * modelScale), 0.6f, 1.6f);

        idleTime = Mathf.Repeat(idleTime + dt, idle.length);
        walkTime = Mathf.Repeat(walkTime + dt * cadence, walk.length);
        idlePlayable.SetTime(idleTime);
        walkPlayable.SetTime(walkTime);
        mixer.SetInputWeight(0, 1f - walkWeight);
        mixer.SetInputWeight(1, walkWeight);
        graph.Evaluate(0f);
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
