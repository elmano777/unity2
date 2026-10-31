using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// Looks at the open rooms from the garage, before any Netcode session exists: signs in to the online
/// services (anonymously) and lists the rooms that still have a free seat. The Lane's
/// <see cref="DuelMatchmaker"/> shares the sign-in, so coming from the garage the player is already in.
/// </summary>
public static class RoomBrowser
{
    /// <summary>Lobby data key under which a room publishes the name of its host's hero.</summary>
    public const string HeroKey = "hero";

    public struct Room
    {
        public string id;
        public string name;
        public string hostHero;
        public int players;
        public int maxPlayers;
    }

    /// <summary>Initialises the services and signs in once; later calls return at once.</summary>
    public static async Task SignIn()
    {
        await UnityServices.InitializeAsync();

#if UNITY_EDITOR
        // The editor reuses one anonymous ID, which would make an editor and a second editor
        // (or a build signed in earlier) look like the same player to Lobby.
        // Only before the first sign-in: coming back from the garage the player is still signed in.
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.ClearSessionToken();
        }
#endif

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    /// <summary>The open rooms with a free seat, newest first (at most <paramref name="count"/>).</summary>
    public static async Task<List<Room>> List(int count = 8)
    {
        await SignIn();

        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
        {
            Count = count,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
            },
            Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.Created) },
        });

        var rooms = new List<Room>();

        foreach (Lobby lobby in response.Results)
        {
            string hero = lobby.Data != null && lobby.Data.TryGetValue(HeroKey, out DataObject data) ? data.Value : "";
            rooms.Add(new Room
            {
                id = lobby.Id,
                name = lobby.Name,
                hostHero = hero,
                players = lobby.Players != null ? lobby.Players.Count : 1,
                maxPlayers = lobby.MaxPlayers,
            });
        }

        return rooms;
    }
}
