using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gives the player their own hands in the Lane: Meta's rigged hand mesh under each controller
/// anchor, posed by <see cref="ControllerHand"/>, the right one wrapped round the gun. Also pulls
/// both hero weapons back so the trigger sits under the index finger; they used to hang off the
/// controller by the butt of the stock. Safe to re-run.
/// </summary>
public static class DeadlockControllerHands
{
    private const string LanePath = "Assets/Scenes/Lane.unity";
    private const string RightModel = "Packages/com.meta.xr.sdk.core/Meshes/HandTracking/OculusHand_R.fbx";
    private const string LeftModel = "Packages/com.meta.xr.sdk.core/Meshes/HandTracking/OculusHand_L.fbx";

    /// <summary>
    /// Right wrist in controller-anchor space; the left hand is its mirror. Tuned on side renders so
    /// the palm sits against the stock and the index finger reaches the trigger.
    /// </summary>
    public static readonly Vector3 WristPosition = new Vector3(0.035f, 0.012f, -0.085f);

    /// <summary>
    /// Tilt applied on top of "fingers forward, back of the hand to the right, thumb up": 25° down
    /// at the front, like the raked handle of a Touch controller, so the fist closes round the stock.
    /// </summary>
    public static readonly Vector3 WristTilt = new Vector3(25f, 0f, 0f);

    /// <summary>How far in front of the controller each weapon's trigger ends up.</summary>
    private const float TriggerZ = 0.045f;

    // Where the trigger is on each model as setup step 3/5 builds it (Visual at z 0.275, Muzzle at
    // 0.55), measured off side renders. Absolute so re-running doesn't keep sliding the gun back.
    private const float BuiltVisualZ = 0.275f;
    private const float BuiltMuzzleZ = 0.55f;

    private static readonly (string path, float triggerZ)[] Weapons =
    {
        ("Assets/Prefabs/Weapons/Seven_Weapon.prefab", 0.144f),
        ("Assets/Prefabs/Weapons/Vindicta_Weapon.prefab", 0.157f),
    };

    /// <summary>Right-hand rotation: OculusHand's fingers (+X) forward, back of hand (+Y) right, thumb (+Z) up.</summary>
    public static Quaternion WristRotation => Quaternion.Euler(WristTilt) * Quaternion.LookRotation(Vector3.up, Vector3.right);

    [MenuItem("Deadlock/Setup/16 Controller Hands")]
    public static void Run()
    {
        foreach (var (path, triggerZ) in Weapons)
        {
            MoveTriggerToHand(path, triggerZ);
        }

        Material glove = DeadlockSceneKit.Lit("Hand_Glove", new Color(0.22f, 0.15f, 0.11f), 0.35f);

        var scene = EditorSceneManager.OpenScene(LanePath, OpenSceneMode.Single);
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();

        if (rig == null)
        {
            Debug.LogError("[Deadlock Setup] Lane has no OVRCameraRig.");
            return;
        }

        BuildHand(DeadlockSceneKit.FindDeep(rig.transform, "RightControllerAnchor"), true, glove);
        BuildHand(DeadlockSceneKit.FindDeep(rig.transform, "LeftControllerAnchor"), false, glove);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Setup] Controller hands done.");
    }

    /// <summary>Puts a posable hand under <paramref name="anchor"/>, replacing the one made last time.</summary>
    public static GameObject BuildHand(Transform anchor, bool right, Material material)
    {
        string name = right ? "RightHandModel" : "LeftHandModel";
        Transform old = anchor.Find(name);

        if (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
        }

        GameObject hand = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(right ? RightModel : LeftModel), anchor);
        hand.name = name;

        // OculusHand_L is OculusHand_R inverted through the origin, so the mirrored pose is the
        // wrist mirrored across the controller's YZ plane and the rotation turned 180° about X.
        Vector3 position = WristPosition;
        Quaternion rotation = WristRotation;

        if (!right)
        {
            position.x = -position.x;
            rotation = Quaternion.Euler(180f, 0f, 0f) * rotation;
        }

        hand.transform.SetLocalPositionAndRotation(position, rotation);

        foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterial = material;
        }

        ControllerHand controllerHand = hand.AddComponent<ControllerHand>();
        var so = new SerializedObject(controllerHand);
        so.FindProperty("side").enumValueIndex = (int)(right ? ControllerHand.Side.Right : ControllerHand.Side.Left);
        so.FindProperty("holdsWeapon").boolValue = right;
        so.ApplyModifiedPropertiesWithoutUndo();

        return hand;
    }

    private static void MoveTriggerToHand(string path, float triggerZ)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        Transform visual = root.transform.Find("Visual");
        Transform muzzle = root.transform.Find("Muzzle");

        if (visual == null || muzzle == null)
        {
            Debug.LogWarning($"[Deadlock Setup] {path}: no Visual or Muzzle, left where it was.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        float shift = TriggerZ - triggerZ;
        visual.localPosition = new Vector3(visual.localPosition.x, visual.localPosition.y, BuiltVisualZ + shift);
        muzzle.localPosition = new Vector3(muzzle.localPosition.x, muzzle.localPosition.y, BuiltMuzzleZ + shift);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }
}
