using UnityEditor;
using UnityEngine;

/// <summary>
/// Dresses the rival as their hero: the NetworkPlayer prefab swaps its hand cubes for the same
/// gloved hands the local player sees (fixed pose, they don't follow this machine's controllers),
/// and lists the primitive head and body so <see cref="NetworkPlayer"/> can hide them once the
/// hero's model stands in. Safe to re-run.
/// </summary>
public static class DeadlockRivalAvatar
{
    private const string PrefabPath = "Assets/Prefabs/Network/NetworkPlayer.prefab";

    [MenuItem("Deadlock/Setup/17 Rival Avatar")]
    public static void Run()
    {
        Material glove = DeadlockSceneKit.Lit("Hand_Glove", new Color(0.22f, 0.15f, 0.11f), 0.35f);
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        NetworkPlayer player = root.GetComponent<NetworkPlayer>();

        if (player == null)
        {
            Debug.LogError($"[Deadlock Setup] {PrefabPath}: no NetworkPlayer.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        var so = new SerializedObject(player);
        var right = (Transform)so.FindProperty("rightHand").objectReferenceValue;
        var left = (Transform)so.FindProperty("leftHand").objectReferenceValue;
        var head = (Transform)so.FindProperty("head").objectReferenceValue;
        var body = (Transform)so.FindProperty("body").objectReferenceValue;

        // The hand transforms follow the rival's controller anchors, so the same local pose as
        // the local player's hands puts the glove round the synced gun.
        DressHand(right, "RightHandMesh", true, glove);
        DressHand(left, "LeftHandMesh", false, glove);

        SerializedProperty placeholders = so.FindProperty("placeholders");
        placeholders.arraySize = 2;
        placeholders.GetArrayElementAtIndex(0).objectReferenceValue = head.GetComponent<Renderer>();
        placeholders.GetArrayElementAtIndex(1).objectReferenceValue = body.GetComponent<Renderer>();
        so.FindProperty("groundMask").intValue = LayerMask.GetMask("Ground");
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Rival avatar done.");
    }

    private static void DressHand(Transform hand, string cubeName, bool isRight, Material glove)
    {
        Transform cube = hand.Find(cubeName);

        if (cube != null)
        {
            Object.DestroyImmediate(cube.gameObject);
        }

        GameObject model = DeadlockControllerHands.BuildHand(hand, isRight, glove);
        var so = new SerializedObject(model.GetComponent<ControllerHand>());
        so.FindProperty("followControllers").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
