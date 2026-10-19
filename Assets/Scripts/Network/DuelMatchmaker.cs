using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Puts the two players of "el ritual" in the same session (RF-08): joins an open lobby if there
/// is one, otherwise creates a Relay allocation plus a 2-player lobby and waits as host.
/// </summary>
/// <remarks>
/// Same flow as Meta's Auto Matchmaking building block from the course lab (Lobby + Relay + NGO),
/// rewritten because the block in Meta XR SDK 201 now requires the newer
/// <c>com.unity.services.multiplayer</c> package instead of the standalone Lobby and Relay
/// packages the lab installs. Owning it also lets us cap rooms at 2 and report progress to the UI.
/// </remarks>
[RequireComponent(typeof(NetworkManager))]
public class DuelMatchmaker : MonoBehaviour
{
    public enum Status { Idle, SigningIn, Searching, WaitingForRival, Joining, Connected, RivalLeft, Offline, Failed }

    private const int MaxPlayers = 2;
    private const string JoinCodeKey = "joinCode";

    [Tooltip("Name given to the lobby this player creates when no open one is found.")]
    [SerializeField] private string lobbyName = "ritual";

    [Tooltip("Connection attempts before giving up on joining or creating a room.")]
    [SerializeField] private int maxRetries = 3;

    [Tooltip("Random wait, in seconds, between failed attempts so two players don't collide again.")]
    [SerializeField] private Vector2 retryInterval = new Vector2(1f, 3f);

    [Tooltip("Skip matchmaking when the player came from \"Mi misión\" (guided practice is offline).")]
    [SerializeField] private bool offlineForMission = true;

    private Lobby lobby;
    private NetworkManager network;

    public Status Current { get; private set; } = Status.Idle;

    /// <summary>Human-readable detail for the current status (e.g. the failure reason).</summary>
    public string Detail { get; private set; } = "";

    public event Action<Status, string> StatusChanged;

    private bool IsLobbyHost => lobby != null && lobby.HostId == AuthenticationService.Instance.PlayerId;

    private void Awake()
    {
        network = GetComponent<NetworkManager>();
    }

    private async void Start()
    {
        bool mission = PlayerPrefs.GetInt(TitleScreenManager.MissionRequestedKey, 0) == 1;
        bool cameFromMenu = GameSession.Instance != null && GameSession.Instance.SelectedHero != null;

        if (GameSession.Instance != null)
        {
            GameSession.Instance.MatchSource = mission ? "mission" : "online";
            GameSession.Instance.IsPracticeRun = mission;
        }

        // Pressing Play straight in the Lane scene has no menu choice behind it: go online so the
        // duel can be tested without walking through the menus every time.
        if (offlineForMission && mission && cameFromMenu)
        {
            SetStatus(Status.Offline, "Mi misión: práctica sin conexión");
            return;
        }

        network.OnClientConnectedCallback += OnClientConnected;
        network.OnClientDisconnectCallback += OnClientDisconnected;

        try
        {
            SetStatus(Status.SigningIn);
            await UnityServices.InitializeAsync();
#if UNITY_EDITOR
            // The editor reuses one anonymous ID, which would make an editor and a second editor
            // (or a build signed in earlier) look like the same player to Lobby.
            AuthenticationService.Instance.ClearSessionToken();
#endif
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            SetStatus(Status.Searching);
            lobby = await JoinOrCreate();

            if (IsLobbyHost)
            {
                StartCoroutine(Heartbeat(lobby.Id, 15f));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DuelMatchmaker] {e}");
            SetStatus(Status.Failed, e.Message);
        }
    }

    private async Task<Lobby> JoinOrCreate()
    {
        try
        {
            return await Retry(Join);
        }
        catch (Exception e)
        {
            if (e is LobbyServiceException lobbyError && lobbyError.Reason != LobbyExceptionReason.NoOpenLobbies)
            {
                Debug.LogWarning($"[DuelMatchmaker] Could not join a lobby: {lobbyError.Reason}");
            }

            return await Retry(Create);
        }
    }

    private async Task<Lobby> Retry(Func<Task<Lobby>> attempt)
    {
        Exception last = null;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await attempt();
            }
            catch (Exception e)
            {
                last = e;

                // No open lobby is an answer, not a glitch: retrying the join only wastes seconds.
                if (e is LobbyServiceException lobbyError && lobbyError.Reason == LobbyExceptionReason.NoOpenLobbies)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(UnityEngine.Random.Range(retryInterval.x, retryInterval.y)));
            }
        }

        throw last ?? new Exception("Matchmaking failed");
    }

    private async Task<Lobby> Join()
    {
        Lobby found = await LobbyService.Instance.QuickJoinLobbyAsync();
        SetStatus(Status.Joining);

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(found.Data[JoinCodeKey].Value);
        GetComponent<UnityTransport>().SetClientRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, allocation.HostConnectionData);

        network.StartClient();
        return found;
    }

    private async Task<Lobby> Create()
    {
        // Relay counts connections besides the host's own.
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        Lobby created = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MaxPlayers, new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
            }
        });

        GetComponent<UnityTransport>().SetHostRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);

        network.StartHost();
        SetStatus(Status.WaitingForRival);
        return created;
    }

    private IEnumerator Heartbeat(string lobbyId, float seconds)
    {
        // Lobby drops rooms that go quiet for 30 s; the host keeps its room listed.
        var wait = new WaitForSecondsRealtime(seconds);

        while (lobby != null)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return wait;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // The host sees itself connect first; the duel starts when the second player arrives.
        if (network.IsHost && network.ConnectedClientsIds.Count < MaxPlayers)
        {
            return;
        }

        SetStatus(Status.Connected);

        // Full room: take it off the list so nobody else tries to join.
        if (IsLobbyHost)
        {
            LobbyService.Instance.UpdateLobbyAsync(lobby.Id, new UpdateLobbyOptions { IsLocked = true });
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (network.IsHost && clientId != network.LocalClientId)
        {
            SetStatus(Status.RivalLeft, "El rival se desconectó");
        }
        else if (!network.IsHost)
        {
            SetStatus(Status.RivalLeft, "Se perdió la conexión con el anfitrión");
        }
    }

    private void SetStatus(Status status, string detail = "")
    {
        Current = status;
        Detail = detail;
        Debug.Log($"[DuelMatchmaker] {status} {detail}");
        StatusChanged?.Invoke(status, detail);
    }

    private void OnDestroy()
    {
        if (network != null)
        {
            network.OnClientConnectedCallback -= OnClientConnected;
            network.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        LeaveLobby();
    }

    private void OnApplicationQuit()
    {
        LeaveLobby();
    }

    private void LeaveLobby()
    {
        if (lobby == null)
        {
            return;
        }

        try
        {
            if (IsLobbyHost)
            {
                LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
            }
            else
            {
                LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[DuelMatchmaker] Error leaving lobby: {e.Message}");
        }

        lobby = null;
    }
}
