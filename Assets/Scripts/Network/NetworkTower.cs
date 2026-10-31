using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shows the host's tower on the joining player's screen. The tower only aims and fires on the host
/// (see <see cref="Tower"/>); this tells the other machine where the staff points and where each beam
/// went. Health and destruction travel through <see cref="NetworkHealth"/>.
/// </summary>
[RequireComponent(typeof(Tower))]
public class NetworkTower : NetworkBehaviour
{
    private const float AimInterval = 0.12f;
    private const float AimMoveThreshold = 0.4f;
    private const float KeepAlive = 0.35f;

    private Tower tower;
    private float nextAimTime;
    private float lastSendTime;
    private bool sentAim;
    private Vector3 sentPoint;

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

    private void Update()
    {
        if (!IsServer || !IsSpawned || Time.time < nextAimTime)
        {
            return;
        }

        bool has = tower.HasAimPoint;
        bool moved = has && (!sentAim || (tower.AimPoint - sentPoint).sqrMagnitude > AimMoveThreshold * AimMoveThreshold);

        // Aim goes out when a target comes, goes or moves, and now and then while it holds still,
        // because the copy lets the aim lapse on its own after a moment.
        if (has != sentAim || moved || (has && Time.time - lastSendTime > KeepAlive))
        {
            nextAimTime = Time.time + AimInterval;
            lastSendTime = Time.time;
            sentAim = has;
            sentPoint = tower.AimPoint;
            AimClientRpc(has, tower.AimPoint);
        }
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
            tower.SetAim(true, targetPoint);
            tower.ShowShot(targetPoint);
        }
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void AimClientRpc(bool has, Vector3 point)
    {
        if (!IsServer)
        {
            tower.SetAim(has, point);
        }
    }
}
