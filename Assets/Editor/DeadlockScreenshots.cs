using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the report screenshots to PNG files outside the Assets folder.
/// </summary>
/// <remarks>
/// Section 6.d.2 of the report asks for images of each stage the player goes through. Generating
/// them from a script rather than grabbing the Game view means they are always the same framing
/// and resolution, so a screenshot can be regenerated after a change without looking different
/// from the others on the page.
/// </remarks>
public static class DeadlockScreenshots
{
    // Outside Assets so Unity never imports several megabytes of PNG as project assets.
    private const string OutputFolder = "Capturas";
    private const int Width = 1920;
    private const int Height = 1080;

    [MenuItem("Deadlock/Screenshots/Capture All")]
    public static void CaptureAll()
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFolder);
        Directory.CreateDirectory(folder);

        CaptureSceneCamera("Assets/Scenes/TitleScreen.unity", Path.Combine(folder, "01_pantalla_titulo.png"));
        CaptureSceneCamera("Assets/Scenes/HeroSelect.unity", Path.Combine(folder, "02_seleccion_heroe.png"));

        EditorSceneManager.OpenScene("Assets/Scenes/Lane.unity", OpenSceneMode.Single);

        CaptureFrom(new Vector3(0f, 14f, -36f), Quaternion.Euler(24f, 0f, 0f),
            Path.Combine(folder, "03_lane_vista_general.png"), 55f);

        CaptureFrom(new Vector3(-1f, 3.2f, 8f), LookAt(new Vector3(-1f, 3.2f, 8f), new Vector3(4.5f, 2.6f, 17f)),
            Path.Combine(folder, "04_torre_guardian.png"), 45f);

        CapturePlayerView(Path.Combine(folder, "05_vista_jugador.png"));
        CaptureMinionRow(Path.Combine(folder, "06_minions_los_seis.png"));

        AssetDatabase.Refresh();
        Debug.Log($"[Screenshots] Written to {folder}");
        EditorUtility.RevealInFinder(folder);
    }

    /// <summary>Opens a scene and renders it from whatever camera it already has.</summary>
    private static void CaptureSceneCamera(string scenePath, string outputPath)
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogWarning($"[Screenshots] Scene not found: {scenePath}");
            return;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Camera camera = FindSceneCamera();

        if (camera == null)
        {
            Debug.LogWarning($"[Screenshots] No camera in {scenePath}");
            return;
        }

        Render(camera, outputPath);
    }

    /// <summary>The lane seen from where the player actually stands, at eye height.</summary>
    private static void CapturePlayerView(string outputPath)
    {
        Camera rigCamera = FindSceneCamera();

        if (rigCamera == null)
        {
            Debug.LogWarning("[Screenshots] No XR camera found in the lane scene.");
            return;
        }

        // Outside Play Mode the controllers are not tracked, so the gun sits exactly on the
        // camera and fills the frame. Pose it where a held weapon would actually be for the
        // duration of the shot, then put it back.
        GameObject weapon = GameObject.Find("Seven_Weapon");
        Transform weaponTransform = weapon != null ? weapon.transform : null;
        Vector3 savedPosition = Vector3.zero;
        Quaternion savedRotation = Quaternion.identity;

        if (weaponTransform != null)
        {
            savedPosition = weaponTransform.localPosition;
            savedRotation = weaponTransform.localRotation;
            weaponTransform.localPosition = new Vector3(0.22f, -0.28f, 0.3f);
            weaponTransform.localRotation = Quaternion.Euler(6f, -4f, 0f);
        }

        Render(rigCamera, outputPath);

        if (weaponTransform != null)
        {
            weaponTransform.localPosition = savedPosition;
            weaponTransform.localRotation = savedRotation;
        }
    }

    /// <summary>
    /// Builds a temporary line-up of the six minions, shoots it, then removes it again so the
    /// saved lane scene is never left with inspection clutter in it.
    /// </summary>
    private static void CaptureMinionRow(string outputPath)
    {
        string[] order =
        {
            "Amber_Melee", "Amber_Trooper", "Amber_Medic",
            "Sapphire_Melee", "Sapphire_Trooper", "Sapphire_Medic",
        };

        GameObject row = new GameObject("__ScreenshotRow");
        // z = -20 is a clear stretch of lane, away from the cover blocks.
        row.transform.position = new Vector3(0f, 0f, -20f);

        float spacing = 1.5f;
        float startX = -(order.Length - 1) * spacing * 0.5f;

        for (int i = 0; i < order.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Minions/{order[i]}.prefab");

            if (prefab == null)
            {
                continue;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, row.transform);
            instance.transform.position = new Vector3(startX + i * spacing, 0f, -20f);
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        // Far enough back that the six of them fit with room at the edges.
        Vector3 cameraPosition = new Vector3(0f, 2.4f, -29.5f);
        CaptureFrom(cameraPosition, LookAt(cameraPosition, new Vector3(0f, 1.1f, -20f)), outputPath, 38f);

        Object.DestroyImmediate(row);
    }

    /// <summary>Drops a throwaway camera at a pose, renders one frame and cleans it up.</summary>
    private static void CaptureFrom(Vector3 position, Quaternion rotation, string outputPath, float fieldOfView)
    {
        GameObject holder = new GameObject("__ScreenshotCamera");
        holder.transform.SetPositionAndRotation(position, rotation);

        Camera camera = holder.AddComponent<Camera>();
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 300f;

        Render(camera, outputPath);
        Object.DestroyImmediate(holder);
    }

    private static void Render(Camera camera, string outputPath)
    {
        RenderTexture target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
        };

        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        camera.targetTexture = target;
        camera.Render();

        RenderTexture.active = target;
        Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();

        File.WriteAllBytes(outputPath, image.EncodeToPNG());

        // Put the camera back the way it was; several of these are scene cameras, not throwaways.
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        Object.DestroyImmediate(image);
        target.Release();
        Object.DestroyImmediate(target);

        Debug.Log($"[Screenshots] {Path.GetFileName(outputPath)}");
    }

    private static Camera FindSceneCamera()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

        // Camera.main only finds cameras tagged MainCamera; the XR rig's may not be.
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        return cameras.Length > 0 ? cameras[0] : null;
    }

    private static Quaternion LookAt(Vector3 from, Vector3 to)
    {
        return Quaternion.LookRotation((to - from).normalized, Vector3.up);
    }
}
