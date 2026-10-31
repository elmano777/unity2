using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Makes the two players start the match together. When both are in the room each plays the welcome
/// (see <see cref="WelcomeSequence"/>) and, as soon as theirs ends, says "ready"; the host starts the match for
/// both once it has both answers. Nothing in the world is created before that, so the match begins from
/// zero at the same moment on both screens.
/// </summary>
/// <remarks>
/// Uses Netcode's named messages, so it needs no spawned object: it can sit next to the NetworkManager
/// from the moment the Lane loads. A direct Play without a hero (the editor, a test client) has no
/// welcome to wait for and reports ready by itself.
/// </remarks>
[RequireComponent(typeof(NetworkManager))]
public class MatchStartSync : MonoBehaviour
{
    private const string ReadyMessage = "dl_ready";
    private const string StartMessage = "dl_start";

    private NetworkManager network;
    private bool registered;
    private bool localReady;
    private bool remoteReady;

    /// <summary>The Lane's sync, so the world can ask whether the match has started.</summary>
    public static MatchStartSync Instance { get; private set; }

    /// <summary>True once both players have finished the welcome and the match is on.</summary>
    public bool Started { get; private set; }

    /// <summary>True once this player has finished the welcome and is waiting for the other.</summary>
    public bool LocalReady => localReady;

    public event Action MatchStarted;

    private void Awake()
    {
        Instance = this;
        network = GetComponent<NetworkManager>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Unregister();
    }

    private void Update()
    {
        if (!registered && network.IsListening && network.CustomMessagingManager != null)
        {
            Register();
        }
        else if (registered && !network.IsListening)
        {
            registered = false;
        }
    }

    private void Register()
    {
        registered = true;

        if (network.IsServer)
        {
            network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReadyReceived);
        }
        else
        {
            network.CustomMessagingManager.RegisterNamedMessageHandler(StartMessage, OnStartReceived);
        }
    }

    private void Unregister()
    {
        if (network == null || network.CustomMessagingManager == null)
        {
            return;
        }

        network.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
        network.CustomMessagingManager.UnregisterNamedMessageHandler(StartMessage);
    }

    /// <summary>This player has finished the welcome. The match starts when both have.</summary>
    public void SetLocalReady()
    {
        if (localReady)
        {
            return;
        }

        localReady = true;

        if (network.IsServer)
        {
            TryStart();
        }
        else if (network.IsListening)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
            {
                network.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer);
            }
        }
    }

    private void OnReadyReceived(ulong sender, FastBufferReader reader)
    {
        remoteReady = true;
        TryStart();
    }

    private void OnStartReceived(ulong sender, FastBufferReader reader)
    {
        Begin();
    }

    private void TryStart()
    {
        if (Started || !localReady || !remoteReady)
        {
            return;
        }

        foreach (ulong client in network.ConnectedClientsIds)
        {
            if (client == network.LocalClientId)
            {
                continue;
            }

            using (var writer = new FastBufferWriter(1, Allocator.Temp))
            {
                network.CustomMessagingManager.SendNamedMessage(StartMessage, client, writer);
            }
        }

        Begin();
    }

    private void Begin()
    {
        if (Started)
        {
            return;
        }

        Started = true;
        Debug.Log("[MatchStartSync] Both players ready: the match starts.");
        MatchStarted?.Invoke();
    }
}
