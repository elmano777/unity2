using UnityEditor;
using UnityEngine;

/// <summary>
/// Lays the six minion prefabs out in a row so they can be eyeballed side by side: scale against
/// each other, material quality after the texture conversion, and how they read at a distance.
/// </summary>
/// <remarks>
/// Minions only exist at runtime once a wave spawns, so without this there is no way to look at
/// them in the editor or take a clean screenshot of the roster for the report. The row is built
/// into the open scene and removed again by the companion command.
/// </remarks>
public static class DeadlockMinionPreview
{
    private const string RowName = "__MinionPreviewRow";

    private static readonly string[] Order =
    {
        "Amber_Melee", "Amber_Trooper", "Amber_Medic",
        "Sapphire_Melee", "Sapphire_Trooper", "Sapphire_Medic",
    };

    [MenuItem("Deadlock/Preview/Show Minion Row")]
    public static void ShowRow()
    {
        ClearRow();

        GameObject row = new GameObject(RowName);
        // Kept out of any build: this is an inspection aid, not part of the lane.
        row.tag = "EditorOnly";

        float spacing = 1.6f;
        float startX = -(Order.Length - 1) * spacing * 0.5f;
        int placed = 0;

        for (int i = 0; i < Order.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/Prefabs/Minions/{Order[i]}.prefab");

            if (prefab == null)
            {
                Debug.LogWarning($"[Minion Preview] Missing prefab for '{Order[i]}'.");
                continue;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, row.transform);
            instance.transform.position = new Vector3(startX + i * spacing, 0f, 0f);
            // Facing the viewer, so the row reads as a character line-up.
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            placed++;
        }

        Selection.activeGameObject = row;
        Debug.Log($"[Minion Preview] Placed {placed}/{Order.Length} minions. " +
                  "Run 'Hide Minion Row' when done.");
    }

    [MenuItem("Deadlock/Preview/Hide Minion Row")]
    public static void ClearRow()
    {
        GameObject existing = GameObject.Find(RowName);

        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
    }
}
