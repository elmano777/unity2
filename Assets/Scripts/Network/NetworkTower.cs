using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shows the host's tower shots on the joining player's screen. The tower only aims and fires on the
/// host (see <see cref="Tower"/>); this tells the other machine where the beam went. Health and
/// destruction travel through <see cref="NetworkHealth"/>.
/// </summary>
[RequireComponent(typeof(Tower))]
public class NetworkTower : NetworkBehaviour
{
    private Tower tower;

    private void Awake()
    {
        tower = GetComponent<Tower>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            tower.Fired += OnFired;
        }
    }

    public override void OnNetworkDespawn()
    {
        tower.Fired -= OnFired;
    }

    private void OnFired(Vector3 targetPoint)
    {
        if (IsSpawned)
        {
            ShotClientRpc(targetPoint);
        }
    }

    // Beams are cosmetic and fire every second or so; a lost one is not worth waiting for.
    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ShotClientRpc(Vector3 targetPoint)
    {
        if (!IsServer)
        {
            tower.ShowShot(targetPoint);
        }
    }
}
