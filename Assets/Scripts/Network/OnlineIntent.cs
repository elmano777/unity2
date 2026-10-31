/// <summary>
/// What the player chose in the garage's room board, carried to the Lane (the garage has no GameSession):
/// create a room, or join the one they picked. <see cref="OnlineMode.Auto"/> is what a Lane started
/// straight from the editor or a test client does, with no menu behind it: join an open room if there is
/// one, otherwise open one.
/// </summary>
public static class OnlineIntent
{
    public enum OnlineMode { Auto, Create, Join }

    public static OnlineMode Mode { get; private set; } = OnlineMode.Auto;

    /// <summary>Lobby id of the room to join, when <see cref="Mode"/> is Join.</summary>
    public static string JoinLobbyId { get; private set; }

    /// <summary>Name of that room, for messages ("Sala 4821").</summary>
    public static string JoinRoomName { get; private set; }

    public static void Create()
    {
        Mode = OnlineMode.Create;
        JoinLobbyId = null;
        JoinRoomName = null;
    }

    public static void Join(string lobbyId, string roomName)
    {
        Mode = OnlineMode.Join;
        JoinLobbyId = lobbyId;
        JoinRoomName = roomName;
    }

    public static void Clear()
    {
        Mode = OnlineMode.Auto;
        JoinLobbyId = null;
        JoinRoomName = null;
    }
}
