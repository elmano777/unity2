using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets minions fade out the moment they die instead of standing frozen until they vanish: makes
/// the Deadlock/MinionFade material and hands it to every minion prefab. The spacing that stops a
/// wave stacking into one model needs no setup, it reads each prefab's capsule. Safe to re-run.
/// </summary>
public static class DeadlockMinionDeathFade
{
    private const string MaterialPath = "Assets/Art/Materials/Combat/Minion_DeathFade.mat";

    private static readonly string[] MinionPrefabs =
    {
        "Assets/Prefabs/Minions/Amber_Melee.prefab",
        "Assets/Prefabs/Minions/Amber_Trooper.prefab",
        "Assets/Prefabs/Minions/Amber_Medic.prefab",
        "Assets/Prefabs/Minions/Sapphire_Melee.prefab",
        "Assets/Prefabs/Minions/Sapphire_Trooper.prefab",
        "Assets/Prefabs/Minions/Sapphire_Medic.prefab",
    };

    [MenuItem("Deadlock/Setup/15 Minion Death Fade")]
    public static void Run()
    {
        DeadlockSceneKit.EnsureFolder("Assets/Art/Materials/Combat");

        Material fade = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (fade == null)
        {
            fade = new Material(Shader.Find("Deadlock/MinionFade"));
            AssetDatabase.CreateAsset(fade, MaterialPath);
        }
        else
        {
            fade.shader = Shader.Find("Deadlock/MinionFade");
        }

        EditorUtility.SetDirty(fade);

        foreach (string path in MinionPrefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            MinionAgent agent = root.GetComponent<MinionAgent>();

            if (agent == null)
            {
                Debug.LogWarning($"[Deadlock Setup] {path}: no MinionAgent, skipped.");
                PrefabUtility.UnloadPrefabContents(root);
                continue;
            }

            var so = new SerializedObject(agent);
            so.FindProperty("deathFadeMaterial").objectReferenceValue = fade;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Minion death fade done.");
    }
}
