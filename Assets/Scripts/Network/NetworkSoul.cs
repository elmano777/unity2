using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networks a <see cref="SoulPickup"/> (RF-07). The host creates the soul and knows whose it is (the
/// side that killed the minion). Whoever shoots it asks the host: if the shooter owns it, it is
/// secured and the owner is paid with a bonus; if the shooter is the rival, it is denied and nobody is
/// paid; if nobody shoots it, the host pays the owner when it finishes floating up. Both screens play
/// what happened. Without this, two players shooting the same soul would each be paid for it.
/// </summary>
[RequireComponent(typeof(SoulPickup))]
public class NetworkSoul : NetworkBehaviour
{
    [Tooltip("Seconds the taken soul stays so the pickup sound reaches the other screen before it is removed.")]
    [SerializeField] private float despawnDelay = 0.3f;

    // Declared before `taken` on purpose: both change in the same tick, and the outcome has to be
    // known when the "taken" callback runs.
    private readonly NetworkVariable<int> outcome = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> ownerSide = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(false);

    private SoulPickup soul;

    private void Awake()
    {
        soul = GetComponent<SoulPickup>();
    }

    public override void OnNetworkSpawn()
    {
        soul.ResetAnchor();
        taken.OnValueChanged += OnTakenChanged;
        PublishOwner();
    }

    public override void OnNetworkDespawn()
    {
        taken.OnValueChanged -= OnTakenChanged;
    }

    /// <summary>The owner as the host decided it; before it arrives, the local guess.</summary>
    public Faction OwnerFaction(Faction fallback)
    {
        return ownerSide.Value >= 0 ? (Faction)ownerSide.Value : fallback;
    }

    /// <summary>Host only: copies who owns the soul so the other screen agrees.</summary>
    public void PublishOwner()
    {
        if (IsSpawned && IsServer)
        {
            ownerSide.Value = (int)soul.LocalOwner;
        }
    }

    /// <summary>Asks the host to resolve this soul for the local shooter.</summary>
    public void Claim()
    {
        if (IsServer)
        {
            Award(NetworkManager.ServerClientId);
        }
        else
        {
            ClaimServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ClaimServerRpc(ServerRpcParams rpcParams = default)
    {
        Award(rpcParams.Receive.SenderClientId);
    }

    private void Award(ulong clientId)
    {
        if (taken.Value)
        {
            return;
        }

        // Host = Amber end (Player), anyone who joins = Sapphire end (Enemy).
        Faction shooter = clientId == NetworkManager.ServerClientId ? Faction.Player : Faction.Enemy;
        bool secured = shooter == soul.Owner;

        outcome.Value = secured ? (int)SoulOutcome.Secured : (int)SoulOutcome.Denied;
        taken.Value = true;

        if (secured)
        {
            PayFaction(shooter, soul.SecuredValue);
        }

        StartCoroutine(RemoveSoon());
    }

    /// <summary>Host only: nobody shot it, so its owner is paid the plain value.</summary>
    public void AutoAward()
    {
        if (!IsServer || taken.Value)
        {
            return;
        }

        outcome.Value = (int)SoulOutcome.Expired;
        taken.Value = true;
        PayFaction(soul.Owner, soul.Value);
        StartCoroutine(RemoveSoon());
    }

    /// <summary>Pays the player of that side: the host locally, the other player through an RPC.</summary>
    private void PayFaction(Faction side, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (side == Faction.Player)
        {
            Pay(amount);
            return;
        }

        foreach (ulong id in NetworkManager.ConnectedClientsIds)
        {
            if (id == NetworkManager.ServerClientId)
            {
                continue;
            }

            PayClientRpc(amount, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { id } },
            });
        }
    }

    [ClientRpc]
    private void PayClientRpc(int amount, ClientRpcParams clientRpcParams = default)
    {
        Pay(amount);
    }

    private static void Pay(int amount)
    {
        if (PlayerWallet.Instance != null)
        {
            PlayerWallet.Instance.Add(amount);
        }
        else
        {
            GameEvents.RaiseSoulsCollected(amount);
        }
    }

    private void OnTakenChanged(bool previous, bool now)
    {
        if (now)
        {
            soul.PlayCollected((SoulOutcome)Mathf.Clamp(outcome.Value, 0, 2));
        }
    }

    private IEnumerator RemoveSoon()
    {
        yield return new WaitForSeconds(despawnDelay);

        if (IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }
}
