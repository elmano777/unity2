using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Brightens the Lane lighting and wires per-hero weapons. Safe to re-run.</summary>
public static class DeadlockLaneFixes
{
    [MenuItem("Deadlock/Setup/5 Lane Lighting And Hero Weapons")]
    public static void Run()
    {
        var seven = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>("Assets/Data/Heroes/Seven.asset");
        var vind = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>("Assets/Data/Heroes/Vindicta.asset");
        seven.weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/Seven_Weapon.prefab");
        vind.weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/Vindicta_Weapon.prefab");
        EditorUtility.SetDirty(seven);
        EditorUtility.SetDirty(vind);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Lane.unity", OpenSceneMode.Single);

        // The sun used to leave every vertical face (cover sides, minions, tower front) in near-black
        // shade: ambient was dim and the light shone from the side. Brighter flat-ish ambient + a sun
        // behind the player so what faces the player is lit.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.80f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.56f, 0.62f);
        RenderSettings.ambientGroundColor = new Color(0.34f, 0.32f, 0.32f);
        RenderSettings.ambientIntensity = 1f;

        foreach (Light l in Object.FindObjectsOfType<Light>())
        {
            if (l.type != LightType.Directional) continue;
            l.transform.rotation = Quaternion.Euler(42f, 12f, 0f);
            l.intensity = 1.2f;
        }

        if (Object.FindObjectOfType<HeroWeaponSwapper>() == null)
        {
            new GameObject("HeroWeaponSwapper").AddComponent<HeroWeaponSwapper>();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Deadlock Setup] Lane lighting and hero weapons done.");
    }
}
