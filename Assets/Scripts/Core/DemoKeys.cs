#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only shortcuts for recording the deliverable videos from Play over Quest Link: whoever
/// sits at the keyboard can hand out souls or move the objective chain along while the person in
/// the headset plays, so a few minutes of video can show the shop, the shields and the patron
/// without farming for half an hour. Not compiled into builds.
/// </summary>
/// <remarks>
/// F5 gives 300 souls, F6 leaves the next rival objective at 10 % health (the player lands the
/// last shots), F7 destroys it outright, F8 makes both abilities ready. The keys avoid the ones the
/// game uses in the editor (WASD/QE to move, 1/2 for abilities). Created on its own in the Lane.
/// </remarks>
public class DemoKeys : MonoBehaviour
{
    private const string LaneScene = "Lane";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded += (scene, mode) => Attach(scene);
        Attach(SceneManager.GetActiveScene());
    }

    private static void Attach(Scene scene)
    {
        if (scene.name != LaneScene || FindObjectOfType<DemoKeys>() != null)
        {
            return;
        }

        var go = new GameObject("DemoKeys (editor only)");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<DemoKeys>();
        Debug.Log("[DemoKeys] F5 +300 almas · F6 siguiente objetivo rival al 10 % · F7 destruirlo · F8 recargar habilidades");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        if (keyboard.f5Key.wasPressedThisFrame) GiveSouls(300);
        if (keyboard.f6Key.wasPressedThisFrame) HurtNextObjective(leaveFraction: 0.1f);
        if (keyboard.f7Key.wasPressedThisFrame) HurtNextObjective(leaveFraction: 0f);
        if (keyboard.f8Key.wasPressedThisFrame) RechargeAbilities();
    }

    /// <summary>Adds souls to the local player's wallet.</summary>
    public static void GiveSouls(int amount)
    {
        if (PlayerWallet.Instance == null)
        {
            Debug.LogWarning("[DemoKeys] No wallet in the scene yet.");
            return;
        }

        PlayerWallet.Instance.Add(amount);
        Debug.Log($"[DemoKeys] +{amount} almas → {PlayerWallet.Instance.Souls}");
    }

    /// <summary>
    /// Damages the rival's next unshielded objective, leaving <paramref name="leaveFraction"/> of
    /// its health (0 destroys it). Goes through the normal damage path, so online it reaches the host.
    /// </summary>
    public static void HurtNextObjective(float leaveFraction)
    {
        Faction rival = MatchNet.LocalFaction == Faction.Player ? Faction.Enemy : Faction.Player;
        Structure next = Structure.NextObjective(rival);

        if (next == null || !next.TryGetComponent(out Health health))
        {
            Debug.Log("[DemoKeys] The rival has no objective left.");
            return;
        }

        float amount = health.CurrentHealth - health.MaxHealth * Mathf.Clamp01(leaveFraction);

        if (amount <= 0f)
        {
            return;
        }

        health.TakeDamage(new DamageInfo(amount, MatchNet.LocalFaction, null, DamageType.Ability));
        Debug.Log($"[DemoKeys] {next.DisplayName} (nivel {next.Tier}) → {health.CurrentHealth:0}/{health.MaxHealth:0}");
    }

    /// <summary>Ends both abilities' cooldowns.</summary>
    public static void RechargeAbilities()
    {
        AbilityCaster caster = FindObjectOfType<AbilityCaster>();

        if (caster == null)
        {
            return;
        }

        caster.ResetCooldowns();
        Debug.Log("[DemoKeys] Habilidades listas.");
    }
}
#endif
