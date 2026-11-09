using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Footsteps: yours while you walk with the stick (so the body has a sound, which helps presence), and
/// the rival's in 3D from where they are, so you can hear them coming before you see them. A step is
/// played every fixed stretch walked, not on a timer, so standing still is silent and running is faster.
/// Loads <c>Resources/Ambience/SelfSteps</c> and <c>RivalSteps</c>; builds itself when the Lane loads.
/// </summary>
public class FootstepSounds : MonoBehaviour
{
    private const string LaneScene = "Lane";

    [Tooltip("Metres walked between two of your own steps.")]
    [SerializeField] private float selfStride = 0.75f;

    [Tooltip("Metres the rival walks between two steps you hear.")]
    [SerializeField] private float rivalStride = 0.9f;

    [Tooltip("The rival's steps are only played within this many metres of you.")]
    [SerializeField] private float hearingRange = 30f;

    private AudioClip[] selfSteps = System.Array.Empty<AudioClip>();
    private AudioClip[] rivalSteps = System.Array.Empty<AudioClip>();
    private ComfortLocomotion locomotion;

    private Vector3 lastSelf;
    private bool haveSelf;
    private float selfWalked;

    private Transform rival;
    private Vector3 lastRival;
    private float rivalWalked;
    private float nextRivalSearch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<FootstepSounds>() == null)
        {
            new GameObject(nameof(FootstepSounds)).AddComponent<FootstepSounds>();
        }
    }

    private void Start()
    {
        selfSteps = Resources.LoadAll<AudioClip>("Ambience/SelfSteps");
        rivalSteps = Resources.LoadAll<AudioClip>("Ambience/RivalSteps");
        locomotion = FindObjectOfType<ComfortLocomotion>();
    }

    private void Update()
    {
        Camera cam = Camera.main;

        if (cam == null)
        {
            return;
        }

        StepSelf(cam.transform.position);
        StepRival(cam.transform.position);
    }

    private void StepSelf(Vector3 head)
    {
        Vector3 flat = new Vector3(head.x, 0f, head.z);

        if (!haveSelf)
        {
            lastSelf = flat;
            haveSelf = true;
            return;
        }

        float moved = Vector3.Distance(flat, lastSelf);
        lastSelf = flat;

        // A jump of more than a couple of metres in a frame is the zipline or a respawn, not walking.
        bool walking = moved < 1.5f && locomotion != null && locomotion.Speed01 > 0.1f && locomotion.ExternalMotion01 <= 0f;

        if (!walking || selfSteps.Length == 0)
        {
            return;
        }

        selfWalked += moved;

        if (selfWalked >= selfStride)
        {
            selfWalked = 0f;
            ExperienceCues.Spawn(selfSteps[Random.Range(0, selfSteps.Length)], new Vector3(head.x, head.y - 1.5f, head.z), 0.3f, 0f);
        }
    }

    private void StepRival(Vector3 head)
    {
        if (rivalSteps.Length == 0)
        {
            return;
        }

        if (rival == null)
        {
            if (Time.time < nextRivalSearch)
            {
                return;
            }

            nextRivalSearch = Time.time + 1f;
            int layer = LayerMask.NameToLayer("Rival");

            if (layer < 0)
            {
                return;
            }

            foreach (Health health in FindObjectsOfType<Health>())
            {
                if (health.gameObject.layer == layer)
                {
                    rival = health.transform;
                    lastRival = rival.position;
                    return;
                }
            }

            return;
        }

        Vector3 p = rival.position;
        float moved = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(lastRival.x, lastRival.z));
        lastRival = p;

        // Too big a jump is the rival respawning: not steps.
        if (moved > 1.5f || Vector3.Distance(p, head) > hearingRange)
        {
            return;
        }

        rivalWalked += moved;

        if (rivalWalked >= rivalStride)
        {
            rivalWalked = 0f;
            ExperienceCues.Spawn(rivalSteps[Random.Range(0, rivalSteps.Length)], new Vector3(p.x, p.y - 1.2f, p.z), 0.6f, 1f);
        }
    }
}
