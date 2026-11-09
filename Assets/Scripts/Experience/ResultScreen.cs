using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The summary at the end of a ritual (RF-14): result, how long it took, souls, towers and the rest of
/// the scoreboard, a closing line that tells the story of the match, an optional like / dislike vote
/// and "Jugar otra vez" / "Salir". It builds itself in the Lane when the match ends, in the world in
/// front of the player (not glued to the head) so it can be read calmly and pressed with the laser.
/// </summary>
/// <remarks>
/// Fallback controls so it is never a dead end: A / X plays again, B / Y leaves, and in the editor the
/// keys 1 / 2 vote and Enter / Escape play again / leave.
/// </remarks>
public class ResultScreen : MonoBehaviour
{
    private const string LaneScene = "Lane";
    private const string SelectScene = "HeroSelect";
    private const string TitleScene = "TitleScreen";

    private static readonly Color Gold = new Color(1f, 0.82f, 0.35f, 1f);
    private static readonly Color Red = new Color(1f, 0.38f, 0.32f, 1f);
    private static readonly Color Ice = new Color(0.7f, 0.9f, 1f, 1f);

    private GameObject root;
    private InputAction again;
    private InputAction leave;
    private InputAction like;
    private InputAction dislike;
    private TMP_Text voteLabel;
    private bool shown;
    private bool leaving;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<ResultScreen>() == null)
        {
            new GameObject(nameof(ResultScreen)).AddComponent<ResultScreen>();
        }
    }

    private void OnEnable()
    {
        GameEvents.MatchStateChanged += OnMatchState;

        again = new InputAction("Again", InputActionType.Button);
        again.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
        again.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
        again.AddBinding("<Keyboard>/enter");

        leave = new InputAction("Leave", InputActionType.Button);
        leave.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
        leave.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
        leave.AddBinding("<Keyboard>/escape");

        like = new InputAction("Like", InputActionType.Button);
        like.AddBinding("<Keyboard>/1");
        dislike = new InputAction("Dislike", InputActionType.Button);
        dislike.AddBinding("<Keyboard>/2");

        again.Enable();
        leave.Enable();
        like.Enable();
        dislike.Enable();
    }

    private void OnDisable()
    {
        GameEvents.MatchStateChanged -= OnMatchState;

        foreach (InputAction action in new[] { again, leave, like, dislike })
        {
            action?.Dispose();
        }
    }

    private void Update()
    {
        if (!shown || leaving)
        {
            return;
        }

        if (like.WasPressedThisFrame()) Vote(MatchVote.Like);
        if (dislike.WasPressedThisFrame()) Vote(MatchVote.Dislike);
        if (again.WasPressedThisFrame()) PlayAgain();
        if (leave.WasPressedThisFrame()) Leave();
    }

    private void OnMatchState(MatchState state)
    {
        bool practice = GameSession.Instance != null && GameSession.Instance.IsPracticeRun;

        if (shown || practice || state == MatchState.PreMatch || state == MatchState.Playing)
        {
            return;
        }

        StartCoroutine(ShowAfterDelay(state));
    }

    private IEnumerator ShowAfterDelay(MatchState state)
    {
        // Let the victory line and the "tower fell" moment land before the numbers appear.
        yield return new WaitForSeconds(4f);
        Build(state);
    }

    // ---- Content ----

    private void Build(MatchState state)
    {
        Camera cam = Camera.main;
        MatchManager match = MatchManager.Instance;

        if (cam == null || match == null)
        {
            return;
        }

        shown = true;
        MatchStats stats = match.Stats;

        Vector3 flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 position = cam.transform.position + flat * 2.1f + Vector3.up * -0.05f;

        root = new GameObject("ResultScreen");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(flat, Vector3.up));

        EnsureRaycaster(cam);

        bool won = state == MatchState.Win;
        Color accent = won ? Gold : state == MatchState.Lose ? Red : Ice;

        Quad("Panel", new Vector3(0f, 0f, 0f), new Vector3(2.3f, 1.55f, 1f), new Color(0.04f, 0.05f, 0.09f, 0.92f), null);
        Quad("Accent", new Vector3(0f, 0.74f, -0.001f), new Vector3(2.3f, 0.025f, 1f), accent, null);

        Text(Title(state), new Vector3(0f, 0.6f, -0.01f), 0.26f, accent, new Vector2(2.2f, 0.3f), FontStyles.Bold);
        Text(ClosingLine(state, stats), new Vector3(0f, 0.38f, -0.01f), 0.085f, Color.white, new Vector2(2.1f, 0.2f), FontStyles.Italic);

        Text(LeftColumn(stats), new Vector3(-0.55f, 0.02f, -0.01f), 0.085f, Color.white, new Vector2(1.0f, 0.5f), FontStyles.Normal);
        Text(RightColumn(stats), new Vector3(0.55f, 0.02f, -0.01f), 0.085f, Color.white, new Vector2(1.0f, 0.5f), FontStyles.Normal);

        voteLabel = Text("¿Te gustó la partida?", new Vector3(0f, -0.3f, -0.01f), 0.075f, Ice, new Vector2(2f, 0.15f), FontStyles.Normal);

        Button("Me gusta", new Vector3(-0.28f, -0.43f, -0.01f), new Vector2(0.5f, 0.15f), new Color(0.2f, 0.55f, 0.35f, 1f), () => Vote(MatchVote.Like));
        Button("No me gusta", new Vector3(0.28f, -0.43f, -0.01f), new Vector2(0.5f, 0.15f), new Color(0.55f, 0.25f, 0.25f, 1f), () => Vote(MatchVote.Dislike));
        Button("Jugar otra vez  (A)", new Vector3(-0.5f, -0.64f, -0.01f), new Vector2(0.9f, 0.16f), new Color(0.15f, 0.35f, 0.6f, 1f), PlayAgain);
        Button("Salir  (B)", new Vector3(0.5f, -0.64f, -0.01f), new Vector2(0.9f, 0.16f), new Color(0.3f, 0.3f, 0.36f, 1f), Leave);

        ExperienceCues.Play(ExperienceCues.Result, 0.5f);
        HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.3f, 0.08f);

        // The post-game music, low, for as long as the summary is up.
        AudioClip music = ExperienceCues.Clip("music_postgame");

        if (music != null)
        {
            AudioSource loop = root.AddComponent<AudioSource>();
            loop.clip = music;
            loop.loop = true;
            loop.spatialBlend = 0f;
            loop.volume = 0.3f;
            loop.Play();
        }

        StartCoroutine(PopIn());
    }

    private static string Title(MatchState state)
    {
        switch (state)
        {
            case MatchState.Win: return "VICTORIA";
            case MatchState.Lose: return "DERROTA";
            default: return "TIEMPO AGOTADO";
        }
    }

    /// <summary>One line that closes the story of the match (the narrative arc's "progress" beat).</summary>
    private static string ClosingLine(MatchState state, MatchStats stats)
    {
        string hero = string.IsNullOrEmpty(stats.heroName) ? "tu héroe" : stats.heroName;

        switch (state)
        {
            case MatchState.Win:
                return stats.towersDestroyed > 0
                    ? $"{hero} derribó {stats.towersDestroyed} torres y el patrón rival cayó. El ritual es tuyo."
                    : $"{hero} llegó hasta el patrón rival. El ritual es tuyo.";
            case MatchState.Lose:
                return stats.towersDestroyed > 0
                    ? $"Tu patrón cayó, pero {hero} derribó {stats.towersDestroyed} torres. La próxima será distinta."
                    : $"Tu patrón cayó. {hero} aprendió el camino: ahora toca atacar.";
            default:
                return "Los dos patrones siguen en pie. El ritual quedó empatado.";
        }
    }

    private static string LeftColumn(MatchStats s)
    {
        int minutes = Mathf.FloorToInt(s.durationSeconds / 60f);
        int seconds = Mathf.FloorToInt(s.durationSeconds % 60f);

        return $"<color=#7FE0FF>Tiempo</color>  {minutes:00}:{seconds:00}\n" +
               $"<color=#7FE0FF>Almas</color>  {s.soulsCollected}\n" +
               $"<color=#7FE0FF>Torres</color>  {s.towersDestroyed}\n" +
               $"<color=#7FE0FF>Minions</color>  {s.minionsKilled}";
    }

    private static string RightColumn(MatchStats s)
    {
        return $"<color=#7FE0FF>Habilidades</color>  {s.abilitiesUsed}\n" +
               $"<color=#7FE0FF>Compras</color>  {s.itemsPurchased}\n" +
               $"<color=#7FE0FF>Disparos</color>  {s.shotsFired}\n" +
               $"<color=#7FE0FF>En cobertura</color>  {Mathf.RoundToInt(s.secondsInCover)} s";
    }

    // ---- Actions ----

    private void Vote(MatchVote vote)
    {
        if (leaving || MatchManager.Instance == null)
        {
            return;
        }

        MatchManager.Instance.SubmitVote(vote);

        if (voteLabel != null)
        {
            voteLabel.text = vote == MatchVote.Like ? "¡Gracias! Anotamos tu me gusta." : "Gracias por contarnos. Lo tendremos en cuenta.";
        }

        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.4f, 0.06f);
    }

    private void PlayAgain()
    {
        if (leaving)
        {
            return;
        }

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.RegisterPlayAgain();
        }

        StartCoroutine(Go(SelectScene));
    }

    private void Leave()
    {
        if (!leaving)
        {
            StartCoroutine(Go(TitleScene));
        }
    }

    private IEnumerator Go(string scene)
    {
        leaving = true;
        ScreenFader fader = FindObjectOfType<ScreenFader>();

        if (fader != null)
        {
            yield return fader.FadeOut();
        }

        // Same clean-up as BackToGarage: the next match needs a fresh NetworkManager.
        NetworkManager network = NetworkManager.Singleton;

        if (network != null)
        {
            if (network.IsListening)
            {
                network.Shutdown();
            }

            Destroy(network.gameObject);
        }

        SceneManager.LoadScene(scene);
    }

    // ---- Building blocks ----

    private static void EnsureRaycaster(Camera cam)
    {
        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
        GameObject host = rig != null ? rig.gameObject : cam.gameObject;

        if (host.GetComponent<UnityEngine.EventSystems.OVRPhysicsRaycaster>() == null)
        {
            host.AddComponent<UnityEngine.EventSystems.OVRPhysicsRaycaster>();
        }
    }

    private GameObject Quad(string name, Vector3 local, Vector3 scale, Color color, System.Action unused)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = local;
        quad.transform.localScale = scale;

        // A quad's visible side faces -Z, and the root looks away from the player, so no rotation is needed.
        Collider collider = quad.GetComponent<Collider>();

        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return quad;
    }

    private TMP_Text Text(string content, Vector3 local, float size, Color color, Vector2 box, FontStyles style)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = local;

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = content;
        text.fontSize = size * 10f;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.rectTransform.sizeDelta = box;
        return text;
    }

    private void Button(string label, Vector3 local, Vector2 size, Color color, System.Action onClick)
    {
        GameObject quad = Quad("Button_" + label, local, new Vector3(size.x, size.y, 1f), color, null);
        BoxCollider box = quad.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.2f);

        WorldButton button = quad.AddComponent<WorldButton>();
        button.OnClick.AddListener(() => onClick());

        TMP_Text text = Text(label, local + new Vector3(0f, 0f, -0.012f), size.y * 0.5f, Color.white, size, FontStyles.Bold);
        text.name = "Label";
    }

    private IEnumerator PopIn()
    {
        Vector3 full = Vector3.one;
        float t = 0f;

        while (t < 0.35f && root != null)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 3f);
            root.transform.localScale = full * Mathf.Lerp(0.7f, 1f, k);
            yield return null;
        }
    }
}
