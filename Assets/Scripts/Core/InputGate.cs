using UnityEngine;

/// <summary>
/// Whether the player's hands may act (shoot, cast, ride the zipline, open the upgrades). Closed while
/// the welcome is on screen: the trigger there only moves the dialog on, and a gun going off or an
/// ability sounding behind the black screen broke the moment. It stays closed a little after the
/// welcome ends, so the press that closed it does not also fire.
/// </summary>
public static class InputGate
{
    private const float Grace = 0.6f;

    private static int holds;
    private static float openAt;

    /// <summary>True while gameplay input must be ignored.</summary>
    public static bool Blocked => holds > 0 || LocalDown || Time.unscaledTime < openAt;

    /// <summary>The local hero is at 0 health, waiting to respawn: no shooting, casting, walking or riding.</summary>
    public static bool LocalDown { get; set; }

    /// <summary>Closes the gate until a matching <see cref="Release"/>.</summary>
    public static void Hold() => holds++;

    /// <summary>Opens the gate again after a short grace.</summary>
    public static void Release()
    {
        holds = Mathf.Max(0, holds - 1);
        openAt = Time.unscaledTime + Grace;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        holds = 0;
        openAt = 0f;
        LocalDown = false;
    }
}
