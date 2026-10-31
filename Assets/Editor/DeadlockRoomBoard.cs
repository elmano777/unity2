using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the room board to the garage: next to the two main runes, the runes of the online choice
/// (CREAR SALA, ELEGIR SALA, ATRÁS, ACTUALIZAR and one rune per room in the list) and the
/// <see cref="RoomBoard"/> that shows them. Re-run it after setup step 9 rebuilds the garage. Safe to re-run.
/// </summary>
public static class DeadlockRoomBoard
{
    private const string ScenePath = "Assets/Scenes/TitleScreen.unity";
    private const int RoomSlots = 4;

    [MenuItem("Deadlock/Setup/24 Room Board (title screen)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        DeadlockOracleLobby.CreateMaterials();

        var orb = Object.FindObjectOfType<OracleOrb>();
        var manager = Object.FindObjectOfType<TitleScreenManager>();
        Transform runes = FindRunes();

        if (orb == null || manager == null || runes == null)
        {
            Debug.LogError("[Deadlock Room Board] The garage isn't built (no orb, title manager or MenuRunes): run setup step 9 first.");
            return;
        }

        Transform old = runes.Find("RoomBoard");

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        var board = new GameObject("RoomBoard");
        board.transform.SetParent(runes, false);

        const float z = -0.55f;
        WorldButton create = Make(board.transform, orb.transform, "Rune_Create", new Vector3(-0.62f, 1.55f, z), "CREAR SALA", "Abre una sala y espera a tu rival");
        WorldButton choose = Make(board.transform, orb.transform, "Rune_Choose", new Vector3(0.62f, 1.55f, z), "ELEGIR SALA", "Entra a la sala de otro jugador");
        WorldButton back = Make(board.transform, orb.transform, "Rune_Back", new Vector3(-1.1f, 0.95f, z), "ATRÁS", "");
        WorldButton refresh = Make(board.transform, orb.transform, "Rune_Refresh", new Vector3(1.1f, 0.95f, z), "ACTUALIZAR", "Buscar de nuevo");

        var roomRunes = new WorldButton[RoomSlots];

        for (int i = 0; i < RoomSlots; i++)
        {
            roomRunes[i] = Make(board.transform, orb.transform, $"Rune_Room{i + 1}", new Vector3(0f, 2.05f - i * 0.45f, z), "SALA", "");
        }

        TextMeshPro status = DeadlockSceneKit.Text("Status", board.transform, new Vector3(0f, 2.7f, z), "", 0.8f,
            new Color(0.75f, 1f, 0.85f, 0.95f), new Vector2(2.6f, 0.5f));
        status.gameObject.AddComponent<FaceCamera>();

        var component = board.AddComponent<RoomBoard>();
        var so = new SerializedObject(component);
        so.FindProperty("manager").objectReferenceValue = manager;

        SerializedProperty main = so.FindProperty("mainRunes");
        main.arraySize = 2;
        main.GetArrayElementAtIndex(0).objectReferenceValue = runes.Find("Rune_Online").gameObject;
        main.GetArrayElementAtIndex(1).objectReferenceValue = runes.Find("Rune_Mission").gameObject;

        so.FindProperty("createRune").objectReferenceValue = create;
        so.FindProperty("chooseRune").objectReferenceValue = choose;
        so.FindProperty("backRune").objectReferenceValue = back;
        so.FindProperty("refreshRune").objectReferenceValue = refresh;

        SerializedProperty rooms = so.FindProperty("roomRunes");
        rooms.arraySize = RoomSlots;

        for (int i = 0; i < RoomSlots; i++)
        {
            rooms.GetArrayElementAtIndex(i).objectReferenceValue = roomRunes[i];
        }

        so.FindProperty("statusLabel").objectReferenceValue = status;
        so.ApplyModifiedPropertiesWithoutUndo();

        var managerSo = new SerializedObject(manager);
        managerSo.FindProperty("roomBoard").objectReferenceValue = component;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        // The runes start hidden (RoomBoard.Awake) and the board adds no static geometry.
        foreach (Transform t in board.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.isStatic = false;
        }

        DeadlockMenuPolish.ApplyToOpenScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Room Board] Room board built in TitleScreen.");
    }

    private static WorldButton Make(Transform parent, Transform orb, string name, Vector3 position, string title, string subtitle)
    {
        return DeadlockOracleLobby.Rune(parent, orb, name, position, title, subtitle, out _);
    }

    private static Transform FindRunes()
    {
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name == "MenuRunes")
            {
                return t;
            }
        }

        return null;
    }
}
