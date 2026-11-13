using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shows the host's neutral creature spitting on the joining player's copy. The creature only
/// thinks on the host (see <see cref="NeutralCreep"/>); its health travels through
/// <see cref="NetworkHealth"/> and it never moves, so this is all the rest it needs.
/// </summary>
[RequireComponent(typeof(NeutralCreep))]
public class NetworkNeutralActions : NetworkBehaviour
{
    private NeutralCreep creep;

    private void Awake()
    {
        creep = GetComponent<NeutralCreep>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            creep.Attacked += OnAttacked;
        }
    }

    public override void OnNetworkDespawn()
    {
        creep.Attacked -= OnAttacked;
    }

    private void OnAttacked(Vector3 point)
    {
        if (IsSpawned)
        {
            AttackedClientRpc(point);
        }
    }

    // Purely cosmetic and frequent; a lost one is not worth waiting for.
    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void AttackedClientRpc(Vector3 point)
    {
        if (!IsServer)
        {
            creep.ShowAttack(point);
        }
    }
}
