using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Controller vibration for shooting and hit confirmation (RF-06).
/// </summary>
/// <remarks>
/// This is one of the few XR-dependent files in the project, and it deliberately uses
/// <see cref="UnityEngine.XR.InputDevices"/> from Unity's built-in XR module rather than the
/// XR Interaction Toolkit. That API is identical in Unity 2022 and Unity 6, so this ports without
/// changes, while XRI's own haptic calls moved between XRI 2.x and 3.x.
/// </remarks>
public static class HapticFeedback
{
    /// <summary>Which hand to buzz.</summary>
    public enum Hand
    {
        Left,
        Right,
        Both,
    }

    /// <summary>
    /// Sends a vibration pulse. Silently does nothing when no headset is connected, so this is
    /// safe to call from the editor without a device attached.
    /// </summary>
    /// <param name="hand">Which controller should buzz.</param>
    /// <param name="amplitude">Strength, 0..1.</param>
    /// <param name="duration">Length in seconds. Keep short — long pulses read as a fault.</param>
    public static void Pulse(Hand hand, float amplitude = 0.35f, float duration = 0.08f)
    {
        amplitude = Mathf.Clamp01(amplitude);
        duration = Mathf.Max(0f, duration);

        if (amplitude <= 0f || duration <= 0f)
        {
            return;
        }

        if (hand == Hand.Left || hand == Hand.Both)
        {
            SendTo(XRNode.LeftHand, amplitude, duration);
        }

        if (hand == Hand.Right || hand == Hand.Both)
        {
            SendTo(XRNode.RightHand, amplitude, duration);
        }
    }

    private static void SendTo(XRNode node, float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);

        if (!device.isValid)
        {
            return;
        }

        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
        {
            device.SendHapticImpulse(0u, amplitude, duration);
        }
    }
}
