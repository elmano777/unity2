using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Post-processing (bloom, grading, vignette) is on for PC / Quest Link but costs too much on the
/// Quest's own GPU (RNF-01, 72 FPS). On Android this turns it off on every camera when the scene
/// starts; the emissive halos in the scenes still read without it.
/// </summary>
public class PostFxPlatformGate : MonoBehaviour
{
    [Tooltip("Keep post-processing on Android too (for profiling on the headset).")]
    [SerializeField] private bool allowOnAndroid;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (allowOnAndroid) return;

        foreach (Camera cam in FindObjectsOfType<Camera>(true))
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = false;
        }
#endif
    }
}
