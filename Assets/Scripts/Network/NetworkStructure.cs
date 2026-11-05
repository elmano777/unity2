using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tells the rival's copy of a <see cref="Structure"/> what the host knows about it: its side, its
/// place in the sequence, its name and whether it is still shielded (RF-12). The spawner sets those
/// on the host only, so without this the copy would keep its prefab's defaults.
/// </summary>
[RequireComponent(typeof(Structure))]
public class NetworkStructure : NetworkBehaviour
{
    private readonly NetworkVariable<int> faction = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> tier = new NetworkVariable<int>(1);
    private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();
    private readonly NetworkVariable<bool> locked = new NetworkVariable<bool>(false);

    private Structure structure;

    private void Awake()
    {
        structure = GetComponent<Structure>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Publish();
            return;
        }

        faction.OnValueChanged += OnChanged;
        tier.OnValueChanged += OnChanged;
        displayName.OnValueChanged += OnChanged;
        locked.OnValueChanged += OnChanged;
        Mirror();
    }

    public override void OnNetworkDespawn()
    {
        faction.OnValueChanged -= OnChanged;
        tier.OnValueChanged -= OnChanged;
        displayName.OnValueChanged -= OnChanged;
        locked.OnValueChanged -= OnChanged;
    }

    private void Update()
    {
        if (IsServer && IsSpawned)
        {
            Publish();
        }
    }

    private void Publish()
    {
        // NetworkVariables only send when the value actually changes, so this is cheap every frame.
        faction.Value = (int)structure.Faction;
        tier.Value = structure.Tier;
        locked.Value = structure.IsStanding && structure.IsLocked;

        if (displayName.Value.ToString() != structure.DisplayName)
        {
            displayName.Value = new FixedString64Bytes(structure.DisplayName);
        }
    }

    private void OnChanged(int previous, int next) => Mirror();

    private void OnChanged(bool previous, bool next) => Mirror();

    private void OnChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror();

    private void Mirror()
    {
        structure.ApplyReplicaState((Faction)faction.Value, tier.Value, displayName.Value.ToString(), locked.Value);
    }
}
