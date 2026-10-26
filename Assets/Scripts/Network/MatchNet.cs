using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Answers "who decides?" for the lane. The host runs the world (minions, towers, souls, damage)
/// and the joining player only sees it; offline modes ("Mi misión", or no connection) behave as a
/// host with nobody else. Gameplay code asks here instead of touching Netcode directly.
/// </summary>
public static class MatchNet
{
    /// <summary>True while a Netcode session is running on this machine.</summary>
    public static bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    /// <summary>True when this machine decides what happens: the host, or anyone playing offline.</summary>
    public static bool IsAuthority => !Online || NetworkManager.Singleton.IsServer;

    /// <summary>Side this machine's player fights for: the host is Amber, the joining player Sapphire.</summary>
    public static Faction LocalFaction => Online && !NetworkManager.Singleton.IsServer ? Faction.Enemy : Faction.Player;

    /// <summary>
    /// True when this machine should be creating the world right now. Offline play does at once; a
    /// duel waits until the rival is in the room and only the host creates anything.
    /// </summary>
    public static bool CanSpawnWorld
    {
        get
        {
            DuelMatchmaker matchmaker = DuelMatchmaker.Instance;

            if (matchmaker == null)
            {
                return true;
            }

            switch (matchmaker.Current)
            {
                case DuelMatchmaker.Status.Offline:
                case DuelMatchmaker.Status.Failed:
                    return true;

                case DuelMatchmaker.Status.Connected:
                    return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Creates an object in the world. Online, the host also spawns it for the rival, so
    /// <paramref name="configure"/> runs first and the rival receives the finished object.
    /// </summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
        System.Action<GameObject> configure = null)
    {
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        configure?.Invoke(instance);

        if (Online && NetworkManager.Singleton.IsServer && instance.TryGetComponent(out NetworkObject networked))
        {
            networked.Spawn(true);
        }

        return instance;
    }
}
