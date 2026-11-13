using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the open scene from a free camera to a PNG in <c>Temp/Shots</c>, in edit mode or in
/// Play. Used to check the map from angles the player's rig never stands at (overhead, rooftops)
/// without moving anything in the scene.
/// </summary>
public static class DeadlockMapShots
{
    /// <summary>Renders from <paramref name="position"/> looking at <paramref name="lookAt"/> and returns the file written.</summary>
    public static string Shot(string name, Vector3 position, Vector3 lookAt, float fov = 70f, int width = 1280, int height = 720)
    {
        var go = new GameObject("__MapShotCamera") { hideFlags = HideFlags.HideAndDontSave };

        try
        {
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 600f;
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(lookAt - position);

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());

            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
            return path;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>The same shot from straight above with an orthographic camera: a map of the layout.</summary>
    public static string Plan(string name, Vector3 center, float halfHeight, int width = 1280, int height = 720)
    {
        var go = new GameObject("__MapShotCamera") { hideFlags = HideFlags.HideAndDontSave };

        try
        {
            Camera camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = halfHeight;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 400f;
            camera.transform.position = center + Vector3.up * 200f;
            // Up on the image is +X, so the lane (Z) runs left to right.
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.right);

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());

            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
            return path;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
