using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shows the host's minion strikes, shots and orb throws on the joining player's copy. The agent only
/// thinks on the host (see <see cref="MinionAgent"/>); this tells the other machine when the minion acted
/// so its model can act it out, and where a dying medic's last orb is headed. Health and movement travel
/// through <see cref="NetworkHealth"/> and the network transform.
/// </summary>
[RequireComponent(typeof(MinionAgent))]
public class NetworkMinionActions : NetworkBehaviour
{
    private MinionAgent agent;

    private void Awake()
    {
        agent = GetComponent<MinionAgent>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            agent.Acted += OnActed;
            agent.DeathOrb += OnDeathOrb;
        }
    }

    public override void OnNetworkDespawn()
    {
        agent.Acted -= OnActed;
        agent.DeathOrb -= OnDeathOrb;
    }

    private void OnActed(MinionAgent.Action action, Vector3 point)
    {
        if (IsSpawned)
        {
            ActedClientRpc((int)action, point);
        }
    }

    private void OnDeathOrb(Vector3 from, Transform target, Health healed)
    {
        if (!IsSpawned || target == null)
        {
            return;
        }

        NetworkObject body = target.GetComponentInParent<NetworkObject>();

        if (body != null && body.IsSpawned)
        {
            DeathOrbClientRpc(from, body);
        }
    }

    // Purely cosmetic and frequent; a lost one is not worth waiting for.
    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ActedClientRpc(int action, Vector3 point)
    {
        if (!IsServer)
        {
            agent.ShowAction((MinionAgent.Action)action, point);
        }
    }

    // Reliable: it happens once, as the medic dies, and the body it flies to is a player.
    [ClientRpc]
    private void DeathOrbClientRpc(Vector3 from, NetworkObjectReference target)
    {
        if (IsServer || !target.TryGet(out NetworkObject body))
        {
            return;
        }

        Health hitbox = body.GetComponentInChildren<Health>();
        agent.ShowDeathOrb(from, hitbox != null ? hitbox.transform : body.transform);
    }
}
