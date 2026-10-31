using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives Seven his idle and walk clips: picks them out of Seven_Anims.glb (an armature-only export
/// from Tools/seven_rig, so the animations don't drag a second copy of the model into the
/// project) and lists them on his HeroDefinitionSO for the rival's avatar. Safe to re-run.
/// </summary>
public static class DeadlockHeroAnimations
{
    private const string ClipsPath = "Assets/Models/Heroes/Seven_Anims.glb";
    private const string HeroPath = "Assets/Data/Heroes/Seven.asset";

    [MenuItem("Deadlock/Setup/20 Hero Animations")]
    public static void Run()
    {
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(HeroPath);

        if (hero == null)
        {
            Debug.LogError($"[Deadlock Setup] {HeroPath}: not found.");
            return;
        }

        AnimationClip idle = null;
        AnimationClip walk = null;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ClipsPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
            {
                if (clip.name.EndsWith("Idle")) idle = clip;
                if (clip.name.EndsWith("Walk")) walk = clip;
            }
        }

        if (idle == null || walk == null)
        {
            Debug.LogError($"[Deadlock Setup] {ClipsPath}: Seven_Idle / Seven_Walk clips not found (imported yet?).");
            return;
        }

        hero.idleClip = idle;
        hero.walkClip = walk;
        hero.walkClipSpeed = 1.7f;
        EditorUtility.SetDirty(hero);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Deadlock Setup] Seven animations: idle {idle.length:0.0}s, walk {walk.length:0.0}s, legacy={idle.legacy}.");
    }
}
