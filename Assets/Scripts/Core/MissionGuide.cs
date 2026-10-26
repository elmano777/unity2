using System.Collections;
using UnityEngine;

/// <summary>
/// "Mi misión": the guided practice. It walks a new player through the loop of the ritual one step at
/// a time (move, turn, shoot, kill a minion, collect a soul, destroy the tower), asking for each with a
/// hint and moving on when the game reports they did it. Finishing it marks the mission as completed,
/// which stops the title screen recommending it (RF-04).
/// </summary>
/// <remarks>
/// Only active in a practice run. The practice is offline and calm: the player can't be hurt, the
/// Amber wave and tower are switched off, and only the Sapphire wave and tower are left to practise on.
/// </remarks>
public class MissionGuide : MonoBehaviour
{
    [Tooltip("Metres the player has to walk for the movement step.")]
    [SerializeField] private float walkDistance = 2f;

    [Tooltip("Degrees the player has to turn for the turning step.")]
    [SerializeField] private float turnDegrees = 40f;

    [Tooltip("Shots the player has to fire for the shooting step.")]
    [SerializeField] private int shotsNeeded = 3;

    [Tooltip("Seconds the closing message stays up.")]
    [SerializeField] private float closingSeconds = 12f;

    private enum Step { Walk, Turn, Shoot, KillMinion, CollectSoul, DestroyTower, Done }

    private Step step = Step.Walk;
    private Transform rig;
    private Vector3 startPosition;
    private float startYaw;
    private int shots;
    private HintBoard board;
    private bool running;

    private IEnumerator Start()
    {
        // The matchmaker decides on the first frame whether this is a practice run.
        yield return null;

        if (GameSession.Instance == null || !GameSession.Instance.IsPracticeRun)
        {
            enabled = false;
            yield break;
        }

        board = FindObjectOfType<HintBoard>();
        OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
        rig = cameraRig != null ? cameraRig.transform : null;

        if (rig != null)
        {
            startPosition = rig.position;
            startYaw = rig.eulerAngles.y;

            // A practice is for learning, not for losing: nothing in it can hurt the player.
            foreach (Health body in rig.GetComponentsInChildren<Health>(true))
            {
                body.Invulnerable = true;
            }
        }

        // Only the rival's side is left to practise on.
        foreach (WaveSpawner spawner in FindObjectsOfType<WaveSpawner>())
        {
            if (spawner.Faction == MatchNet.LocalFaction)
            {
                spawner.gameObject.SetActive(false);
            }
        }

        foreach (StructureSpawner spawner in FindObjectsOfType<StructureSpawner>())
        {
            if (spawner.Faction == MatchNet.LocalFaction)
            {
                // It may already have raised its tower in the first frame, before this ran.
                if (spawner.Spawned != null)
                {
                    Destroy(spawner.Spawned.gameObject);
                }

                spawner.gameObject.SetActive(false);
            }
        }

        GameEvents.ShotFired += OnShot;
        GameEvents.MinionKilled += OnMinionKilled;
        GameEvents.SoulsCollected += OnSouls;
        GameEvents.TowerDestroyed += OnTowerDestroyed;

        running = true;
        ShowStep();
    }

    private void OnDestroy()
    {
        GameEvents.ShotFired -= OnShot;
        GameEvents.MinionKilled -= OnMinionKilled;
        GameEvents.SoulsCollected -= OnSouls;
        GameEvents.TowerDestroyed -= OnTowerDestroyed;
    }

    private void Update()
    {
        if (!running || rig == null)
        {
            return;
        }

        if (step == Step.Walk)
        {
            Vector3 moved = rig.position - startPosition;
            moved.y = 0f;

            if (moved.magnitude >= walkDistance)
            {
                Advance(Step.Turn);
            }
        }
        else if (step == Step.Turn)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(startYaw, rig.eulerAngles.y)) >= turnDegrees)
            {
                Advance(Step.Shoot);
            }
        }
    }

    private void OnShot()
    {
        if (step == Step.Shoot && ++shots >= shotsNeeded)
        {
            Advance(Step.KillMinion);
        }
    }

    private void OnMinionKilled(Faction faction)
    {
        if (step == Step.KillMinion && faction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            Advance(Step.CollectSoul);
        }
    }

    private void OnSouls(int amount)
    {
        if (step == Step.CollectSoul)
        {
            Advance(Step.DestroyTower);
        }
    }

    private void OnTowerDestroyed(Structure tower, Faction faction)
    {
        if (step == Step.DestroyTower && faction == FactionUtility.Opposite(MatchNet.LocalFaction))
        {
            Advance(Step.Done);
        }
    }

    private void Advance(Step next)
    {
        step = next;

        if (step == Step.Done)
        {
            Complete();
        }

        ShowStep();
    }

    private void Complete()
    {
        running = false;
        PlayerProgress.MissionCompleted = true;
        PlayerPrefs.SetInt(TitleScreenManager.MissionCompletedKey, 1);
        PlayerPrefs.Save();
    }

    private void ShowStep()
    {
        string text;

        switch (step)
        {
            case Step.Walk:
                text = "MISIÓN 1/6\nCamina con el stick izquierdo";
                break;

            case Step.Turn:
                text = "MISIÓN 2/6\nGira con el stick derecho";
                break;

            case Step.Shoot:
                text = "MISIÓN 3/6\nDispara con el gatillo derecho";
                break;

            case Step.KillMinion:
                text = "MISIÓN 4/6\nElimina un minion enemigo.\nUsa las coberturas del carril";
                break;

            case Step.CollectSoul:
                text = "MISIÓN 5/6\nDispara a un alma brillante\npara recogerla";
                break;

            case Step.DestroyTower:
                text = "MISIÓN 6/6\nAvanza y destruye la torre Guardián";
                break;

            default:
                text = "¡MISIÓN CUMPLIDA!\nYa sabes cómo es el ritual.\nMantén B/Y para volver al garage";
                break;
        }

        Debug.Log($"[MissionGuide] {step}");

        if (board == null)
        {
            GameEvents.RaiseHint(text);
            return;
        }

        board.Show(text, step == Step.Done ? closingSeconds : 0f);
    }
}
