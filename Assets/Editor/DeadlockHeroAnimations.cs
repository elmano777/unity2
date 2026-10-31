using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives each rigged hero his idle and walk clips: picks them out of the armature-only glb exported
/// from Tools/seven_rig or Tools/vindicta_rig (so the animations don't drag a second copy of the
/// model into the project) and lists them on the hero's HeroDefinitionSO for the rival's avatar.
/// Safe to re-run.
/// </summary>
public static class DeadlockHeroAnimations
{
    private struct Entry
    {
        public string heroPath;
        public string clipsPath;
        public string rigRoot;
        public float walkClipSpeed;
    }

    private static readonly Entry[] Heroes =
    {
        new Entry { heroPath = "Assets/Data/Heroes/Seven.asset", clipsPath = "Assets/Models/Heroes/Seven_Anims.glb", rigRoot = "Seven_Rig", walkClipSpeed = 1.7f },
        new Entry { heroPath = "Assets/Data/Heroes/Vindicta.asset", clipsPath = "Assets/Models/Heroes/Vindicta_Anims.glb", rigRoot = "Vindicta_Rig", walkClipSpeed = 1.5f },
    };

    [MenuItem("Deadlock/Setup/20 Hero Animations")]
    public static void Run()
    {
        foreach (Entry entry in Heroes)
        {
            Assign(entry);
        }

        AssetDatabase.SaveAssets();
    }

    private static void Assign(Entry entry)
    {
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(entry.heroPath);

        if (hero == null)
        {
            Debug.LogError($"[Deadlock Setup] {entry.heroPath}: not found.");
            return;
        }

        AnimationClip idle = null;
        AnimationClip walk = null;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(entry.clipsPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
            {
                if (clip.name.EndsWith("Idle")) idle = clip;
                if (clip.name.EndsWith("Walk")) walk = clip;
            }
        }

        if (idle == null || walk == null)
        {
            Debug.LogError($"[Deadlock Setup] {entry.clipsPath}: idle / walk clips not found (imported yet?).");
            return;
        }

        hero.idleClip = idle;
        hero.walkClip = walk;
        hero.rigRootName = entry.rigRoot;
        hero.walkClipSpeed = entry.walkClipSpeed;
        EditorUtility.SetDirty(hero);
        Debug.Log($"[Deadlock Setup] {hero.heroName} animations: idle {idle.length:0.0}s, walk {walk.length:0.0}s, legacy={idle.legacy}.");
    }
}
