using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot setup for the second hero (Vindicta, patron Archmother): sprite import settings,
/// HeroDefinition asset with its three voice lines, Seven's win line, preview prefab, weapon
/// prefab and the second card on the Hero Select scene. Safe to re-run.
/// </summary>
public static class DeadlockAddVindicta
{
    private const string Audio = "Assets/Audio/Heroes/";

    [MenuItem("Deadlock/Setup/3 Add Vindicta")]
    public static void Run()
    {
        ImportCard();
        AssetDatabase.Refresh();

        HeroDefinitionSO seven = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>("Assets/Data/Heroes/Seven.asset");
        seven.winClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "gigawatt_ping_post_game_01.mp3");
        EditorUtility.SetDirty(seven);

        GameObject preview = CreatePreviewPrefab();
        CreateWeaponPrefab();

        HeroDefinitionSO v = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>("Assets/Data/Heroes/Vindicta.asset");
        if (v == null)
        {
            v = ScriptableObject.CreateInstance<HeroDefinitionSO>();
            AssetDatabase.CreateAsset(v, "Assets/Data/Heroes/Vindicta.asset");
        }
        v.heroName = "Vindicta";
        v.portrait = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Heroes/Vindicta_card.png");
        v.selectClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "hornet_select_06.mp3");
        v.entryClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "patron_female_ally_hornet_start_02.mp3");
        v.winClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "hornet_ping_post_game_02.mp3");
        v.previewModel = preview;
        v.introLines = new[]
        {
            "You were a hunter once.",
            "You stalked the shadows of the old city.",
            "But the Archmother has other plans.",
            "Complete the ritual, Vindicta.",
            "And take your place in the court of The Archmother."
        };
        EditorUtility.SetDirty(v);
        AssetDatabase.SaveAssets();

        AddCardToHeroSelect(v);
        Debug.Log("[Deadlock Setup] Vindicta added.");
    }

    private static void ImportCard()
    {
        string path = "Assets/Art/Heroes/Vindicta_card.png";
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        var seven = (TextureImporter)AssetImporter.GetAtPath("Assets/Art/Heroes/Seven_card.png");
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = seven.maxTextureSize;
        imp.SaveAndReimport();
    }

    private static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // Root pivot at the soles, model on a child (same structure as Seven_Preview).
    private static GameObject CreatePreviewPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Heroes/Vindicta.glb");
        GameObject root = new GameObject("Vindicta_Preview");
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Vindicta";
        visual.transform.SetParent(root.transform, false);
        // Extracted glTF characters face -Z after import; Seven needed a -90 degree turn, Vindicta is checked by eye.
        visual.transform.localRotation = Quaternion.Euler(0f, PreviewYaw, 0f);
        Bounds b = WorldBounds(visual);
        visual.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        string path = "Assets/Prefabs/Heroes/Vindicta_Preview.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    public static float PreviewYaw = 0f;

    private static void CreateWeaponPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/Vindicta_Weapon.glb");
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/Seven_Weapon.prefab");
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "Vindicta_Weapon";

        Transform old = root.transform.Find("Visual");
        Object.DestroyImmediate(old.gameObject);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);

        Bounds raw = WorldBounds(visual);
        float longest = Mathf.Max(raw.size.x, Mathf.Max(raw.size.y, raw.size.z));
        visual.transform.localScale = Vector3.one * (0.55f / longest);
        Bounds scaled = WorldBounds(visual);
        visual.transform.localPosition -= new Vector3(scaled.center.x, scaled.center.y, scaled.min.z);
        Bounds placed = WorldBounds(visual);
        root.transform.Find("Muzzle").localPosition = new Vector3(0f, 0f, placed.max.z);

        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Weapons/Vindicta_Weapon.prefab");
        Object.DestroyImmediate(root);
    }

    private static void AddCardToHeroSelect(HeroDefinitionSO vindicta)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/HeroSelect.unity", OpenSceneMode.Single);
        var mgr = Object.FindObjectOfType<HeroSelectManager>();
        var so = new SerializedObject(mgr);
        var heroes = so.FindProperty("availableHeroes");
        var cards = so.FindProperty("heroCards");

        for (int i = 0; i < heroes.arraySize; i++)
        {
            if (heroes.GetArrayElementAtIndex(i).objectReferenceValue == vindicta)
            {
                Debug.Log("[Deadlock Setup] HeroSelect already has Vindicta.");
                return;
            }
        }

        GameObject sevenCard = GameObject.Find("HeroCard_Seven");
        GameObject copy = Object.Instantiate(sevenCard, sevenCard.transform.parent, false);
        copy.name = "HeroCard_Vindicta";
        RectTransform rt = copy.GetComponent<RectTransform>();
        Vector2 p = rt.anchoredPosition;
        rt.anchoredPosition = new Vector2(-p.x, p.y);

        int n = heroes.arraySize;
        heroes.InsertArrayElementAtIndex(n);
        heroes.GetArrayElementAtIndex(n).objectReferenceValue = vindicta;
        cards.InsertArrayElementAtIndex(n);
        cards.GetArrayElementAtIndex(n).objectReferenceValue = copy.GetComponent<HeroCardView>();
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
