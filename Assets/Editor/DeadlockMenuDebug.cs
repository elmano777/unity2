using UnityEditor;
using UnityEngine;

/// <summary>
/// Play Mode shortcuts to walk the 3D menus without a headset (strike the orb, press a rune, pick a
/// hero, confirm), so the flow can be checked from the editor or over MCP.
/// </summary>
public static class DeadlockMenuDebug
{
    [MenuItem("Deadlock/Debug/Strike Orb")]
    private static void StrikeOrb() => Object.FindObjectOfType<OracleOrb>()?.Strike(HapticFeedback.Hand.Right);

    [MenuItem("Deadlock/Debug/Press Rune Online")]
    private static void PressOnline() => PressRune("Rune_Online");

    [MenuItem("Deadlock/Debug/Press Rune Mission")]
    private static void PressMission() => PressRune("Rune_Mission");

    [MenuItem("Deadlock/Debug/Choose Hero 1")]
    private static void ChooseFirst() => ChooseHero(0);

    [MenuItem("Deadlock/Debug/Choose Hero 2")]
    private static void ChooseSecond() => ChooseHero(1);

    [MenuItem("Deadlock/Debug/Confirm Hero")]
    private static void Confirm() => GameObject.Find("ConfirmSigil")?.GetComponent<WorldButton>()?.Press();

    [MenuItem("Deadlock/Debug/Move Player To Orb")]
    private static void MoveToOrb()
    {
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null) rig.transform.position = new Vector3(0f, rig.transform.position.y, -1.2f);
    }

    [MenuItem("Deadlock/Debug/Strike Orb", true)]
    [MenuItem("Deadlock/Debug/Press Rune Online", true)]
    [MenuItem("Deadlock/Debug/Press Rune Mission", true)]
    [MenuItem("Deadlock/Debug/Choose Hero 1", true)]
    [MenuItem("Deadlock/Debug/Choose Hero 2", true)]
    [MenuItem("Deadlock/Debug/Confirm Hero", true)]
    [MenuItem("Deadlock/Debug/Move Player To Orb", true)]
    private static bool InPlayMode() => Application.isPlaying;

    private static void PressRune(string name)
    {
        GameObject rune = GameObject.Find(name);

        if (rune == null)
        {
            Debug.LogWarning($"[Deadlock Debug] {name} is not visible yet: strike the orb first.");
            return;
        }

        rune.GetComponent<WorldButton>().Press();
    }

    private static void ChooseHero(int index)
    {
        foreach (SpiritHero spirit in Object.FindObjectsOfType<SpiritHero>())
        {
            if (new SerializedObject(spirit).FindProperty("heroIndex").intValue == index)
            {
                spirit.Choose();
                return;
            }
        }

        Debug.LogWarning($"[Deadlock Debug] No spirit hero with index {index}.");
    }
}
