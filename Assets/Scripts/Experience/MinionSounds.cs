using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The sounds of the wave: troopers shooting (a different shot for your side and the rival's), melee
/// swings, a minion falling, the wave marching and a bullet whistling past. Everything is 3D, from the
/// minion that makes it, and it loads whatever is in <c>Resources/Cues</c> and <c>Resources/Ambience</c>,
/// so a missing file just means that sound stays silent. Builds itself when the Lane loads.
/// </summary>
public class MinionSounds : MonoBehaviour
{
    private const string LaneScene = "Lane";

    private float nextShot;
    private float nextSwing;
    private float nextDeath;
    private float nextStep;
    private AudioClip[] steps = System.Array.Empty<AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<MinionSounds>() == null)
        {
            new GameObject(nameof(MinionSounds)).AddComponent<MinionSounds>();
        }
    }

    private void Start()
    {
        steps = Resources.LoadAll<AudioClip>("Ambience/MinionSteps");
    }

    private void OnEnable()
    {
        MinionAgent.AnyActed += OnActed;
        GameEvents.Died += OnDied;
    }

    private void OnDisable()
    {
        MinionAgent.AnyActed -= OnActed;
        GameEvents.Died -= OnDied;
    }

    private void OnActed(MinionAgent minion, MinionAgent.Action action, Vector3 target)
    {
        if (minion == null || action != MinionAgent.Action.Attack)
        {
            return;
        }

        Camera cam = Camera.main;
        float away = cam != null ? Vector3.Distance(cam.transform.position, minion.transform.position) : 0f;

        // Too far to hear: don't spend an audio source on it.
        if (away > 45f)
        {
            return;
        }

        Vector3 from = minion.transform.position + Vector3.up * 1.2f;

        if (minion.Role == MinionRole.Trooper)
        {
            if (Time.time < nextShot)
            {
                return;
            }

            nextShot = Time.time + 0.08f;
            bool mine = minion.Faction == MatchNet.LocalFaction;
            ExperienceCues.PlayAt(mine ? "minion_fire_ally" : "minion_fire_enemy", from, 0.45f);
            WhistleIfClose(from, target, cam);
        }
        else
        {
            if (Time.time < nextSwing)
            {
                return;
            }

            nextSwing = Time.time + 0.08f;
            ExperienceCues.PlayAt("minion_melee", from, 0.6f);
        }
    }

    /// <summary>A whistle where a minion's bullet passes closest to the player, if it passes close.</summary>
    private static void WhistleIfClose(Vector3 from, Vector3 to, Camera cam)
    {
        if (cam == null)
        {
            return;
        }

        Vector3 head = cam.transform.position;
        Vector3 line = to - from;
        float length = line.magnitude;

        if (length < 2f)
        {
            return;
        }

        float along = Mathf.Clamp(Vector3.Dot(head - from, line / length), 0f, length);
        Vector3 closest = from + line / length * along;

        if (Vector3.Distance(closest, head) < 3f && Vector3.Distance(to, head) > 1.5f)
        {
            ExperienceCues.PlayAt("minion_whizby", closest, 0.7f);
        }
    }

    private void OnDied(GameObject victim, DamageInfo info)
    {
        if (victim == null || Time.time < nextDeath || victim.GetComponentInParent<MinionAgent>() == null)
        {
            return;
        }

        Camera cam = Camera.main;

        if (cam != null && Vector3.Distance(cam.transform.position, victim.transform.position) > 45f)
        {
            return;
        }

        nextDeath = Time.time + 0.1f;
        ExperienceCues.PlayAt("minion_death", victim.transform.position + Vector3.up, 0.6f);
    }

    private void Update()
    {
        // The wave marching: a footstep from the nearest minion every so often, so a wave can be heard coming.
        if (steps.Length == 0 || Time.time < nextStep || Camera.main == null)
        {
            return;
        }

        nextStep = Time.time + 0.55f;
        Vector3 head = Camera.main.transform.position;
        MinionAgent nearest = null;
        float best = 20f;

        foreach (MinionAgent minion in FindObjectsOfType<MinionAgent>())
        {
            if (minion.CurrentState != MinionAgent.State.Advancing)
            {
                continue;
            }

            float distance = Vector3.Distance(minion.transform.position, head);

            if (distance < best)
            {
                best = distance;
                nearest = minion;
            }
        }

        if (nearest != null)
        {
            ExperienceCues.Spawn(steps[Random.Range(0, steps.Length)], nearest.transform.position, 0.35f, 1f);
        }
    }
}
