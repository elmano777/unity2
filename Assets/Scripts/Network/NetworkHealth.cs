using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Puts a <see cref="Health"/> on the network (RF-08, RF-09). The host owns the hit points: it applies
/// every hit and publishes the result. The joining player's copy only mirrors those numbers and sends
/// its own hits to the host instead of applying them, so both screens always agree on who is alive.
/// </summary>
/// <remarks>
/// Works the same for a minion, a tower and the other player's body. Nothing here knows which one
/// it is: they all expose a <see cref="Health"/> and the rest of the gameplay keeps reading that.
/// </remarks>
[RequireComponent(typeof(Health))]
public class NetworkHealth : NetworkBehaviour
{
    /// <summary>Largest hit the host accepts from a client, so a bad packet can't one-shot a tower.</summary>
    private const float MaxAcceptedDamage = 250f;

    private readonly NetworkVariable<float> current = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<float> max = new NetworkVariable<float>(1f);
    private readonly NetworkVariable<int> faction = new NetworkVariable<int>(0);

    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        if (IsServer)
        {
            health.HealthChanged += PublishFromHost;
            PublishFromHost(health.CurrentHealth, health.MaxHealth);
            return;
        }

        health.MarkReplica();
        health.DamageForwarder = ForwardHit;

        current.OnValueChanged += OnMirrorChanged;
        max.OnValueChanged += OnMirrorChanged;
        faction.OnValueChanged += OnMirrorChanged;

        Mirror();
    }

    public override void OnNetworkDespawn()
    {
        if (health == null)
        {
            return;
        }

        health.HealthChanged -= PublishFromHost;
        health.DamageForwarder = null;

        current.OnValueChanged -= OnMirrorChanged;
        max.OnValueChanged -= OnMirrorChanged;
        faction.OnValueChanged -= OnMirrorChanged;
    }

    private void PublishFromHost(float currentHealth, float maxHealth)
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        faction.Value = (int)health.Faction;
        max.Value = maxHealth;
        current.Value = currentHealth;
    }

    private void OnMirrorChanged(float previous, float next) => Mirror();

    private void OnMirrorChanged(int previous, int next) => Mirror();

    private void Mirror()
    {
        health.ApplyReplica((Faction)faction.Value, current.Value, max.Value);
    }

    /// <summary>Called by <see cref="Health"/> on the mirror when a shot lands: hand it to the host.</summary>
    private bool ForwardHit(DamageInfo info)
    {
        if (!IsSpawned)
        {
            return false;
        }

        HitServerRpc(info.amount, (int)info.type, info.point, info.direction);
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void HitServerRpc(float amount, int type, Vector3 point, Vector3 direction, ServerRpcParams rpcParams = default)
    {
        if (!health.IsAlive || float.IsNaN(amount))
        {
            return;
        }

        // Whoever sent the hit fights for the side the room gave them, whatever the packet says.
        Faction shooter = rpcParams.Receive.SenderClientId == NetworkManager.ServerClientId ? Faction.Player : Faction.Enemy;

        DamageInfo info = new DamageInfo(Mathf.Clamp(amount, 0f, MaxAcceptedDamage), shooter, null, (DamageType)type)
            .AtImpact(point, direction);

        health.TakeDamage(info);
    }
}
