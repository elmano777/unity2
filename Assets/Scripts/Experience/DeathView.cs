using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// What being down looks like. The moment the local hero reaches 0 health the hands stop (see
/// <see cref="InputGate.LocalDown"/>: no shooting, casting, walking or riding) and the world drains to
/// grey until the respawn; before, a dead player could keep acting for a while and nothing showed it.
/// The rival falling turns its body into a grey ghost instead of vanishing (<see cref="SetGhost"/>).
/// </summary>
/// <remarks>
/// The grey is a global URP volume (saturation -100, a little darker) faded in and out. Where the
/// camera renders no post-processing (the Quest's own GPU, see <see cref="PostFxPlatformGate"/>) a
/// translucent grey veil in front of the eyes does the job. Builds itself when the Lane loads.
/// </remarks>
public class DeathView : MonoBehaviour
{
    private const string LaneScene = "Lane";

    [SerializeField] private float fadeSpeed = 2.5f;

    private Health body;
    private float nextBodySearch;
    private Volume volume;
    private Renderer veil;
    private Material veilMaterial;
    private float weight;
    private bool down;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<DeathView>() == null)
        {
            new GameObject(nameof(DeathView)).AddComponent<DeathView>();
        }
    }

    private void Start()
    {
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        ColorAdjustments grade = profile.Add<ColorAdjustments>(true);
        grade.saturation.Override(-100f);
        grade.postExposure.Override(-0.5f);
        grade.contrast.Override(-10f);

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 0f;
        volume.sharedProfile = profile;
    }

    private void OnDestroy()
    {
        InputGate.LocalDown = false;
    }

    private void Update()
    {
        FindBody();

        bool nowDown = body != null && !body.IsAlive && !MatchManager.IsOver;

        if (nowDown != down)
        {
            down = nowDown;
            InputGate.LocalDown = down;

            if (!down)
            {
                // Back in: the press that may be held from before shouldn't fire on the first frame.
                InputGate.Hold();
                InputGate.Release();
            }
        }

        weight = Mathf.MoveTowards(weight, down ? 1f : 0f, fadeSpeed * Time.deltaTime);

        if (volume != null)
        {
            volume.weight = weight;
        }

        UpdateVeil();
    }

    /// <summary>The local hero's body: the network one online (it spawns after the Lane loads), else the rig's.</summary>
    private void FindBody()
    {
        NetworkPlayer local = NetworkPlayer.Local;

        if (local != null && local.Body != null)
        {
            body = local.Body;
            return;
        }

        if (body != null || Time.time < nextBodySearch)
        {
            return;
        }

        nextBodySearch = Time.time + 1f;
        int layer = LayerMask.NameToLayer("PlayerBody");

        foreach (Health health in FindObjectsOfType<Health>())
        {
            if (health.gameObject.layer == layer)
            {
                body = health;
                return;
            }
        }
    }

    /// <summary>Without post-processing on the camera, a grey veil just in front of the eyes.</summary>
    private void UpdateVeil()
    {
        Camera camera = Camera.main;

        if (camera == null)
        {
            return;
        }

        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        bool needVeil = data == null || !data.renderPostProcessing;

        if (!needVeil || weight <= 0f)
        {
            if (veil != null && veil.enabled) veil.enabled = false;
            return;
        }

        if (veil == null)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "DeathVeil";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(camera.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, camera.nearClipPlane + 0.05f);
            quad.transform.localScale = new Vector3(2f, 2f, 1f);
            veil = quad.GetComponent<Renderer>();
            veilMaterial = new Material(Shader.Find("Sprites/Default"));
            veil.sharedMaterial = veilMaterial;
            veil.shadowCastingMode = ShadowCastingMode.Off;
            veil.sortingOrder = 32000;
        }

        veil.enabled = true;
        veilMaterial.color = new Color(0.32f, 0.32f, 0.34f, 0.55f * weight);
    }

    // ---- The rival as a grey ghost ----

    private static Material ghostMaterial;

    /// <summary>
    /// Turns every renderer under <paramref name="root"/> grey and see-through (or back), leaving health
    /// bars alone. Used for the rival's body while it is down.
    /// </summary>
    public static void SetGhost(GameObject root, bool ghost)
    {
        if (root == null)
        {
            return;
        }

        GhostState state = root.GetComponent<GhostState>();

        if (state == null)
        {
            state = root.AddComponent<GhostState>();
        }

        if (ghost == state.IsGhost)
        {
            return;
        }

        if (ghostMaterial == null)
        {
            ghostMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.55f, 0.55f, 0.58f, 0.45f) };
        }

        if (ghost)
        {
            state.Saved.Clear();

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<HealthBar>() != null || renderer is LineRenderer || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                state.Saved[renderer] = renderer.sharedMaterials;
                Material[] grey = new Material[renderer.sharedMaterials.Length];

                for (int i = 0; i < grey.Length; i++)
                {
                    grey[i] = ghostMaterial;
                }

                renderer.sharedMaterials = grey;
            }
        }
        else
        {
            foreach (KeyValuePair<Renderer, Material[]> pair in state.Saved)
            {
                if (pair.Key != null)
                {
                    pair.Key.sharedMaterials = pair.Value;
                }
            }

            state.Saved.Clear();
        }

        state.IsGhost = ghost;
    }

    private sealed class GhostState : MonoBehaviour
    {
        public bool IsGhost;
        public readonly Dictionary<Renderer, Material[]> Saved = new Dictionary<Renderer, Material[]>();
    }
}
