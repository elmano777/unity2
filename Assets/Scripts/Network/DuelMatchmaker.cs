using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Puts the two players of "el ritual" in the same session (RF-08). What the player chose in the garage
/// (<see cref="OnlineIntent"/>) decides how: create a room (a Relay allocation plus a 2-player lobby, and
/// wait as host), or join the room they picked from the list. A Lane started without a menu choice
/// (Play in the editor, a test client) joins an open room if there is one and otherwise creates one.
/// </summary>
/// <remarks>
/// Rooms are tagged with a hash of the network setup so a player can't enter a room of a different build:
/// Netcode would silently refuse them. The room's name ("Sala 4821") is shown while waiting so both
/// players can check they are in the same room. A room whose host never answers (it quit without closing
/// it) is given up after <see cref="joinTimeout"/>.
/// </remarks>
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
    private const string BuildKey = "build";

    [Tooltip("Connection attempts before giving up on joining or creating a room.")]
    [SerializeField] private int maxRetries = 3;

    [Tooltip("Random wait, in seconds, between failed attempts.")]
    [SerializeField] private Vector2 retryInterval = new Vector2(1f, 3f);

    [Tooltip("Skip matchmaking when the player came from \"Mi misión\" (guided practice is offline).")]
    [SerializeField] private bool offlineForMission = true;

    [Tooltip("Seconds to wait for a room's host to answer before treating the room as abandoned.")]
    [SerializeField] private float joinTimeout = 12f;

    private Lobby lobby;
    private NetworkManager network;
    private string buildTag;

    /// <summary>The matchmaker of the open Lane, so the world can wait for the rival (see <see cref="MatchNet"/>).</summary>
    public static DuelMatchmaker Instance { get; private set; }

    public Status Current { get; private set; } = Status.Idle;

    /// <summary>Human-readable detail for the current status (e.g. the failure reason).</summary>
    public string Detail { get; private set; } = "";

    /// <summary>Name of the room this player is in ("Sala 4821"), empty until there is one.</summary>
    public string RoomName => lobby != null ? lobby.Name : "";

    public event Action<Status, string> StatusChanged;

    private bool IsLobbyHost => lobby != null && lobby.HostId == AuthenticationService.Instance.PlayerId;

    private void Awake()
    {
        Instance = this;
        network = GetComponent<NetworkManager>();
    }

    private async void Start()
    {
        bool mission = PlayerPrefs.GetInt(TitleScreenManager.MissionRequestedKey, 0) == 1;
        bool cameFromMenu = GameSession.Instance != null && GameSession.Instance.SelectedHero != null;

#if UNITY_EDITOR
        // Deadlock/Debug/Force Practice Run: play the Lane straight as "Mi misión", without the menus.
        if (UnityEditor.EditorPrefs.GetBool("Deadlock.ForcePractice", false))
        {
            mission = true;
            cameFromMenu = true;
        }
#endif

        if (GameSession.Instance != null)
        {
            GameSession.Instance.MatchSource = mission ? "mission" : "online";
            GameSession.Instance.IsPracticeRun = mission;
        }

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
            await RoomBrowser.SignIn();

            // Builds whose networked prefabs or settings differ can't play together.
            // Not cached (false): a hash taken before Netcode registers the prefabs, if kept, is what
            // the joining player would later send, and the host (which has the prefabs) refuses it.
            buildTag = network.NetworkConfig.GetConfig(false).ToString("X");

            switch (OnlineIntent.Mode)
            {
                case OnlineIntent.OnlineMode.Create:
                    SetStatus(Status.Searching, "Creando la sala...");
                    lobby = await Retry(Create);
                    break;

                case OnlineIntent.OnlineMode.Join:
                    SetStatus(Status.Joining, OnlineIntent.JoinRoomName ?? "");
                    lobby = await JoinChosen(OnlineIntent.JoinLobbyId);
                    break;

                default:
                    SetStatus(Status.Searching);
                    lobby = await JoinOrCreate();
                    break;
            }

            if (IsLobbyHost)
            {
                StartCoroutine(Heartbeat(lobby.Id, 15f));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DuelMatchmaker] {e}");
            SetStatus(Status.Failed, FriendlyReason(e));
        }
    }

    private static string FriendlyReason(Exception e)
    {
        if (e is LobbyServiceException lobbyError)
        {
            switch (lobbyError.Reason)
            {
                case LobbyExceptionReason.LobbyNotFound:
                case LobbyExceptionReason.NoOpenLobbies:
                    return "Esa sala ya no existe";

                case LobbyExceptionReason.LobbyFull:
                    return "La sala ya está llena";
            }
        }

        return e.Message;
    }

    /// <summary>Joins the room the player picked from the list.</summary>
    private async Task<Lobby> JoinChosen(string lobbyId)
    {
        Lobby joined = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

        // A room of another build would be refused by Netcode without a word: say so here instead.
        if (joined.Data != null && joined.Data.TryGetValue(BuildKey, out DataObject data) && data.Value != buildTag)
        {
            try { await LobbyService.Instance.RemovePlayerAsync(joined.Id, AuthenticationService.Instance.PlayerId); }
            catch (Exception) { /* the room is closed to us anyway */ }

            throw new Exception("Esa sala es de otra versión del juego: ¿tienen el mismo APK?");
        }

        return await ConnectAsClient(joined);
    }

    /// <summary>Direct Play in the Lane: the newest open room of this build, or a room of our own.</summary>
    private async Task<Lobby> JoinOrCreate()
    {
        try
        {
            Lobby joined = await Retry(JoinNewest);
            if (joined != null) return joined;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DuelMatchmaker] Could not join a lobby: {e.Message}");
        }

        return await Retry(Create);
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

        throw last ?? new Exception("No se pudo conectar");
    }

    private async Task<Lobby> JoinNewest()
    {
        QueryResponse open = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
        {
            Count = 5,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                new QueryFilter(QueryFilter.FieldOptions.S1, buildTag, QueryFilter.OpOptions.EQ)
            },
            Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.Created) }
        });

        if (open.Results.Count == 0) return null;

        Lobby joined = await LobbyService.Instance.JoinLobbyByIdAsync(open.Results[0].Id);
        return await ConnectAsClient(joined);
    }

    private async Task<Lobby> ConnectAsClient(Lobby found)
    {
        SetStatus(Status.Joining, found.Name);

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(found.Data[JoinCodeKey].Value);
        GetComponent<UnityTransport>().SetClientRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, allocation.HostConnectionData);

        network.StartClient();
        LogConfig("client");
        GiveUpIfNoHost(found.Id);
        return found;
    }

    /// <summary>
    /// A room whose host quit without closing it (app killed, Play stopped) stays listed for about
    /// 30 s, and joining it never connects. If the host hasn't answered in time, give up with a message.
    /// </summary>
    private async void GiveUpIfNoHost(string roomId)
    {
        await Task.Delay(TimeSpan.FromSeconds(joinTimeout));
        if (this == null || Current != Status.Joining || lobby == null || lobby.Id != roomId) return;

        Debug.LogWarning($"[DuelMatchmaker] No answer from the host of {lobby.Name}.");
        network.Shutdown();
        SetStatus(Status.Failed, "La sala no respondió: puede que el anfitrión ya se haya ido");
    }

    private async Task<Lobby> Create()
    {
        // Relay counts connections besides the host's own.
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        string heroName = GameSession.Instance != null && GameSession.Instance.SelectedHero != null
            ? GameSession.Instance.SelectedHero.heroName
            : "";

        Lobby created = await LobbyService.Instance.CreateLobbyAsync($"Sala {UnityEngine.Random.Range(1000, 10000)}", MaxPlayers, new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) },
                { BuildKey, new DataObject(DataObject.VisibilityOptions.Public, buildTag, DataObject.IndexOptions.S1) },
                { RoomBrowser.HeroKey, new DataObject(DataObject.VisibilityOptions.Public, heroName) },
            }
        });

        GetComponent<UnityTransport>().SetHostRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);

        network.StartHost();
        LogConfig("host");
        SetStatus(Status.WaitingForRival, created.Name);
        return created;
    }

    /// <summary>The network setup both players must share, logged so a refused join can be traced.</summary>
    private void LogConfig(string role)
    {
        Debug.Log($"[DuelMatchmaker] {role} config {network.NetworkConfig.GetConfig(false):X} (sent {network.NetworkConfig.GetConfig():X}), " +
                  $"{network.NetworkConfig.Prefabs.NetworkPrefabOverrideLinks.Count} prefabs, room tag {buildTag}");
    }

    private IEnumerator Heartbeat(string lobbyId, float seconds)
    {
        // Lobby drops rooms that go quiet for 30 s; the host keeps its room listed.
        var wait = new WaitForSecondsRealtime(seconds);

        while (lobby != null && lobby.Id == lobbyId)
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
            if (Current == Status.WaitingForRival)
            {
                // Someone joined the room but Netcode never let them in (different build, or their
                // connection dropped while entering): keep waiting and free their lobby seat.
                Debug.LogWarning("[DuelMatchmaker] A player reached the room but could not enter.");
                SetStatus(Status.WaitingForRival, $"{lobby?.Name}\nUn jugador no pudo entrar: ¿tienen el mismo APK?");
                FreeLobbySeats();
            }
            else
            {
                SetStatus(Status.RivalLeft, "El rival se desconectó");
            }
        }
        else if (!network.IsHost)
        {
            if (Current == Status.Joining)
            {
                // Never got in: dead room or refused by the host.
                Debug.LogWarning($"[DuelMatchmaker] Could not enter room {lobby?.Name}: {network.DisconnectReason}");
                SetStatus(Status.Failed, "No se pudo entrar a la sala: ¿tienen el mismo APK?");
            }
            else if (Current != Status.Failed)
            {
                SetStatus(Status.RivalLeft, "Se perdió la conexión con el anfitrión");
            }
        }
    }

    /// <summary>Removes everyone but the host from our lobby, so its free seat is listed again.</summary>
    private async void FreeLobbySeats()
    {
        if (lobby == null || !IsLobbyHost) return;
        try
        {
            Lobby fresh = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
            foreach (Player player in fresh.Players)
            {
                if (player.Id != fresh.HostId) await LobbyService.Instance.RemovePlayerAsync(fresh.Id, player.Id);
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[DuelMatchmaker] Could not free the lobby seat: {e.Message}");
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
        if (Instance == this)
        {
            Instance = null;
        }

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
