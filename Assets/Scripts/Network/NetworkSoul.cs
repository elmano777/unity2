using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networks a <see cref="SoulPickup"/> (RF-07). The host creates the soul; whoever shoots it asks the
/// host for it, the host gives it to the first asker and pays that player's wallet, and both screens
/// play the pickup. Without this, two players shooting the same soul would each be paid for it.
/// </summary>
[RequireComponent(typeof(SoulPickup))]
public class NetworkSoul : NetworkBehaviour
{
    [Tooltip("Seconds the taken soul stays so the pickup sound reaches the other screen before it is removed.")]
    [SerializeField] private float despawnDelay = 0.3f;

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
    }

    public override void OnNetworkDespawn()
    {
        taken.OnValueChanged -= OnTakenChanged;
    }

    /// <summary>Asks the host for this soul. The host answers by paying the wallet if nobody beat us to it.</summary>
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

        taken.Value = true;

        if (clientId == NetworkManager.ServerClientId)
        {
            Pay(soul.Value);
        }
        else
        {
            PayClientRpc(soul.Value, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
            });
        }

        StartCoroutine(RemoveSoon());
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
            soul.PlayCollected();
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
