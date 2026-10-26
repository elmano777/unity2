using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Writes what each machine sees of the duel to its log every few seconds (host and joining player
/// side by side), so a run between two machines can be checked without putting on a headset. With
/// the <c>-selftest</c> command-line flag the joining player also fires a few hits at the host's
/// world, which exercises the path real shots take: client hit, host applies it, both screens agree.
/// </summary>
public class DuelDiagnostics : MonoBehaviour
{
    [SerializeField] private float interval = 4f;

    private float nextReport;
    private int testStep;
    private bool selfTest;

    private void Awake()
    {
        selfTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-selftest") >= 0;
    }

    private void Update()
    {
        if (!MatchNet.Online || Time.time < nextReport)
        {
            return;
        }

        nextReport = Time.time + interval;
        Report();

        if (selfTest && !NetworkManager.Singleton.IsServer)
        {
            RunSelfTest();
        }
    }

    private void Report()
    {
        NetworkManager manager = NetworkManager.Singleton;
        int minions = 0, mirrors = 0, souls = 0, towers = 0;
        float minionHealth = 0f;
        float towerHealth = 0f;

        foreach (MinionAgent minion in FindObjectsOfType<MinionAgent>())
        {
            Health health = minion.GetComponent<Health>();
            minions++;
            minionHealth += health.CurrentHealth;

            if (health.IsReplica)
            {
                mirrors++;
            }
        }

        foreach (Tower tower in FindObjectsOfType<Tower>())
        {
            towers++;
            towerHealth += tower.Health.CurrentHealth;
        }

        souls = FindObjectsOfType<SoulPickup>().Length;

        string players = "";

        foreach (NetworkPlayer player in FindObjectsOfType<NetworkPlayer>())
        {
            Health body = player.GetComponentInChildren<Health>();
            players += $" {(player.IsOwner ? "me" : "rival")}={(body != null ? body.CurrentHealth : -1f):F0}";
        }

        Debug.Log($"[DuelDiagnostics] {(manager.IsServer ? "HOST" : "CLIENT")} players={manager.ConnectedClientsIds.Count} " +
                  $"minions={minions} (mirrors {mirrors}, hp {minionHealth:F0}) towers={towers} (hp {towerHealth:F0}) souls={souls} bodies:{players}");
    }

    /// <summary>One thing per tick: hit an Amber minion, hit the Amber tower, claim a soul, hit the host.</summary>
    private void RunSelfTest()
    {
        DamageInfo hit = new DamageInfo(15f, Faction.Enemy, null, DamageType.Bullet);

        switch (testStep++ % 4)
        {
            case 0:
                foreach (MinionAgent minion in FindObjectsOfType<MinionAgent>())
                {
                    Health health = minion.GetComponent<Health>();

                    if (health.IsAlive && health.Faction == Faction.Player)
                    {
                        Debug.Log($"[DuelDiagnostics] selftest: client hits {minion.name} (hp {health.CurrentHealth:F0})");
                        health.TakeDamage(hit);
                        return;
                    }
                }

                Debug.Log("[DuelDiagnostics] selftest: no Amber minion to hit yet");
                break;

            case 1:
                foreach (Tower tower in FindObjectsOfType<Tower>())
                {
                    if (tower.Health.IsAlive && tower.Health.Faction == Faction.Player)
                    {
                        Debug.Log($"[DuelDiagnostics] selftest: client hits the Amber tower (hp {tower.Health.CurrentHealth:F0})");
                        tower.Health.TakeDamage(hit);
                        return;
                    }
                }

                Debug.Log("[DuelDiagnostics] selftest: no Amber tower to hit yet");
                break;

            case 2:
                foreach (SoulPickup soul in FindObjectsOfType<SoulPickup>())
                {
                    if (soul.IsAlive)
                    {
                        Debug.Log("[DuelDiagnostics] selftest: client shoots a soul");
                        soul.TakeDamage(hit);
                        return;
                    }
                }

                Debug.Log("[DuelDiagnostics] selftest: no soul to shoot yet");
                break;

            default:
                foreach (NetworkPlayer player in FindObjectsOfType<NetworkPlayer>())
                {
                    Health body = player.GetComponentInChildren<Health>();

                    if (!player.IsOwner && body != null && body.IsAlive)
                    {
                        Debug.Log($"[DuelDiagnostics] selftest: client hits the host (hp {body.CurrentHealth:F0})");
                        body.TakeDamage(hit);
                        return;
                    }
                }

                Debug.Log("[DuelDiagnostics] selftest: host body not found");
                break;
        }
    }
}
