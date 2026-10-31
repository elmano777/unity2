using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The runes of an online match in the garage. "Partida online" swaps the two main runes for two
/// choices: CREAR SALA (open a room and wait for a rival) and ELEGIR SALA (a short list of the rooms that
/// are open, one rune each). Picking either goes on to the hero select; the Lane then creates the room or
/// joins the picked one (<see cref="OnlineIntent"/>). The rune objects are built in the scene by setup
/// step 24; this only decides which are shown and what they say.
/// </summary>
public class RoomBoard : MonoBehaviour
{
    private enum Page { Main, Options, Rooms }

    [SerializeField] private TitleScreenManager manager;

    [Tooltip("The two main runes (Partida online, Mi misión), hidden while the board is open.")]
    [SerializeField] private GameObject[] mainRunes;

    [SerializeField] private WorldButton createRune;
    [SerializeField] private WorldButton chooseRune;
    [SerializeField] private WorldButton backRune;
    [SerializeField] private WorldButton refreshRune;

    [Tooltip("One rune per room shown, top to bottom.")]
    [SerializeField] private WorldButton[] roomRunes;

    [Tooltip("A line above the runes: what is going on, or what to do.")]
    [SerializeField] private TMP_Text statusLabel;

    private Page page = Page.Main;
    private List<RoomBrowser.Room> rooms = new List<RoomBrowser.Room>();
    private int searchId;

    private void Awake()
    {
        if (manager == null) manager = FindObjectOfType<TitleScreenManager>();

        createRune.OnClick.AddListener(() => manager.CreateRoom());
        chooseRune.OnClick.AddListener(ShowRooms);
        backRune.OnClick.AddListener(Back);
        refreshRune.OnClick.AddListener(ShowRooms);

        for (int i = 0; i < roomRunes.Length; i++)
        {
            int index = i;
            roomRunes[i].OnClick.AddListener(() => PickRoom(index));
        }

        SetPage(Page.Main);
    }

    /// <summary>"Partida online" was pressed: offer to create a room or to pick one.</summary>
    public void Open()
    {
        SetPage(Page.Options);
    }

    private void Back()
    {
        searchId++;
        SetPage(page == Page.Rooms ? Page.Options : Page.Main);
    }

    private void PickRoom(int index)
    {
        if (index < 0 || index >= rooms.Count) return;

        RoomBrowser.Room room = rooms[index];
        manager.JoinRoom(room.id, room.name);
    }

    private async void ShowRooms()
    {
        SetPage(Page.Rooms);
        SetStatus("Buscando salas...");
        int mine = ++searchId;

        try
        {
            List<RoomBrowser.Room> found = await RoomBrowser.List(roomRunes.Length);
            if (this == null || mine != searchId) return;

            rooms = found;
        }
        catch (System.Exception e)
        {
            if (this == null || mine != searchId) return;

            Debug.LogWarning($"[RoomBoard] Could not list the rooms: {e.Message}");
            rooms.Clear();
            SetStatus("<color=#FF6B6B>No se pudo buscar salas</color>\n<size=70%>Revisa tu conexión y pulsa ACTUALIZAR</size>");
            FillRooms();
            return;
        }

        FillRooms();
        SetStatus(rooms.Count > 0 ? "Elige una sala" : "No hay salas abiertas\n<size=70%>Crea una y espera a tu rival</size>");
    }

    private void FillRooms()
    {
        for (int i = 0; i < roomRunes.Length; i++)
        {
            bool has = page == Page.Rooms && i < rooms.Count;
            roomRunes[i].gameObject.SetActive(has);

            if (has)
            {
                RoomBrowser.Room room = rooms[i];
                SetLabel(roomRunes[i], room.name.ToUpper(),
                    string.IsNullOrEmpty(room.hostHero) ? $"{room.players}/{room.maxPlayers}" : $"Anfitrión: {room.hostHero}  ·  {room.players}/{room.maxPlayers}");
            }
        }
    }

    private void SetPage(Page next)
    {
        page = next;

        foreach (GameObject rune in mainRunes)
        {
            if (rune != null) rune.SetActive(page == Page.Main);
        }

        createRune.gameObject.SetActive(page == Page.Options);
        chooseRune.gameObject.SetActive(page == Page.Options);
        backRune.gameObject.SetActive(page != Page.Main);
        refreshRune.gameObject.SetActive(page == Page.Rooms);

        FillRooms();

        if (page == Page.Options) SetStatus("¿Cómo quieres jugar?");
        if (page == Page.Main) SetStatus("");
    }

    private void SetStatus(string text)
    {
        if (statusLabel == null) return;

        statusLabel.text = text;
        statusLabel.gameObject.SetActive(text.Length > 0);
    }

    private static void SetLabel(WorldButton rune, string title, string subtitle)
    {
        foreach (TMP_Text text in rune.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "Title") text.text = title;
            else if (text.name == "Subtitle") text.text = subtitle;
        }
    }
}
