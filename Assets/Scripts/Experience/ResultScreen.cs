using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The end of a ritual (RF-14), in the hero shooter's three beats:
/// <list type="number">
/// <item>the result: the whole view washes in the winning side's colour (Amber gold for the Hidden
/// King, Sapphire blue for the Archmother) with VICTORIA / DERROTA across it;</item>
/// <item>the MVP: the winner's painted render, big, with "MVP" and the numbers that won it on the left,
/// the name over the figure and the other hero as the key player on the right;</item>
/// <item>the scoreboard: one band per side, each hero's row with souls, kills, deaths, minions, towers
/// and the rest, the like / dislike vote and the ways out.</item>
/// </list>
/// The pages live in the world in front of the player (not glued to the head) so they can be read
/// calmly and pressed with the laser. It builds itself in the Lane when the match ends.
/// </summary>
/// <remarks>
/// The rival's row only holds what this machine saw happen (their kills are this player's deaths, the
/// towers they took are this player's losses); the rest shows "—". Fallback controls so it is never a
/// dead end: A / X plays again, B / Y leaves; in the editor 1 / 2 vote, ← / → change page and
/// Enter / Escape play again / leave.
/// </remarks>
public class ResultScreen : MonoBehaviour
{
    private const string LaneScene = "Lane";
    private const string SelectScene = "HeroSelect";
    private const string TitleScene = "TitleScreen";

    private static readonly Color AmberColor = new Color(1f, 0.7f, 0.2f, 1f);
    private static readonly Color SapphireColor = new Color(0.36f, 0.64f, 1f, 1f);
    private static readonly Color AmberRow = new Color(0.34f, 0.21f, 0.05f, 0.96f);
    private static readonly Color SapphireRow = new Color(0.07f, 0.14f, 0.32f, 0.96f);
    private static readonly Color Ink = new Color(0.025f, 0.03f, 0.05f, 0.96f);
    private static readonly Color Paper = new Color(0.93f, 0.89f, 0.8f, 1f);
    private static readonly Color Muted = new Color(0.62f, 0.62f, 0.68f, 1f);
    private static readonly Color KillRed = new Color(1f, 0.4f, 0.35f, 1f);
    private static readonly Color SoulCyan = new Color(0.45f, 0.88f, 1f, 1f);
    private static readonly Color Ice = new Color(0.7f, 0.9f, 1f, 1f);

    private const float PanelDistance = 2.1f;
    private const float PanelWidth = 3.4f, PanelHeight = 1.95f;

    /// <summary>Render queue for the pages: past every transparent in the world (3000-3499).</summary>
    private const int OverlayQueue = 3500;

    private GameObject root;
    private GameObject mvpPage;
    private GameObject boardPage;
    private Transform target;
    private readonly List<Renderer> dots = new List<Renderer>();
    private float panelScale = 1f;
    private int page;

    private InputAction again;
    private InputAction leave;
    private InputAction like;
    private InputAction dislike;
    private InputAction next;
    private InputAction previous;
    private TMP_Text voteLabel;
    private bool shown;
    private bool leaving;

    /// <summary>One hero in the summary, with whatever this machine knows about their match.</summary>
    private class Entry
    {
        public Faction side;
        public HeroDefinitionSO hero;
        public string who;
        public bool local;
        public int? souls, kills, deaths, minions, towers, abilities, items, shots;
        public bool tookPatron;
    }

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

        again = Action("Again", "<XRController>{RightHand}/{PrimaryButton}", "<XRController>{LeftHand}/{PrimaryButton}", "<Keyboard>/enter");
        leave = Action("Leave", "<XRController>{RightHand}/{SecondaryButton}", "<XRController>{LeftHand}/{SecondaryButton}", "<Keyboard>/escape");
        like = Action("Like", "<Keyboard>/1");
        dislike = Action("Dislike", "<Keyboard>/2");
        next = Action("Next", "<Keyboard>/rightArrow");
        previous = Action("Previous", "<Keyboard>/leftArrow");
    }

    private static InputAction Action(string name, params string[] bindings)
    {
        var action = new InputAction(name, InputActionType.Button);

        foreach (string binding in bindings)
        {
            action.AddBinding(binding);
        }

        action.Enable();
        return action;
    }

    private void OnDisable()
    {
        GameEvents.MatchStateChanged -= OnMatchState;

        foreach (InputAction action in new[] { again, leave, like, dislike, next, previous })
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
        if (next.WasPressedThisFrame()) ShowPage(1);
        if (previous.WasPressedThisFrame()) ShowPage(0);
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

        StartCoroutine(Sequence(state));
    }

    private IEnumerator Sequence(MatchState state)
    {
        // A beat for the patron's fall and the victory line, then the colour, then the pages.
        yield return new WaitForSeconds(1.2f);
        yield return Banner(state);
        Build(state);
    }

    // ------------------------------------------------------------------ beat 1: the result

    /// <summary>
    /// The view washes in the winner's colour with the result written across it, then fades. Glued to
    /// the head on purpose (it is a moment, not something to read) and brief.
    /// </summary>
    private IEnumerator Banner(MatchState state)
    {
        Camera cam = Camera.main;

        if (cam == null)
        {
            yield break;
        }

        bool won = state == MatchState.Win;
        Faction winner = won ? MatchNet.LocalFaction : FactionUtility.Opposite(MatchNet.LocalFaction);
        Color team = TeamColor(winner);

        // The hint board's "¡VICTORIA!" said its part; the banner says it bigger.
        GameEvents.RaiseHint(string.Empty);

        var banner = new GameObject("ResultBanner").transform;
        banner.SetParent(cam.transform, false);
        banner.localPosition = new Vector3(0f, 0f, 1.2f);
        banner.localRotation = Quaternion.identity;
        target = banner;

        Material wash = QuadMaterial(Quad("Wash", new Vector3(0f, 0f, 0.4f), new Vector3(6f, 4f, 1f), new Color(team.r, team.g, team.b, 0f), null, 4100));
        Material band = QuadMaterial(Quad("Band", new Vector3(0f, 0.02f, 0.01f), new Vector3(3f, 0.42f, 1f), new Color(0f, 0f, 0f, 0f), null, 4101));
        Material lineTop = QuadMaterial(Quad("LineTop", new Vector3(0f, 0.23f, 0f), new Vector3(1.6f, 0.008f, 1f), new Color(team.r, team.g, team.b, 0f), null, 4102));
        Material lineBottom = QuadMaterial(Quad("LineBottom", new Vector3(0f, -0.19f, 0f), new Vector3(1.6f, 0.008f, 1f), new Color(team.r, team.g, team.b, 0f), null, 4102));

        TMP_Text title = Text(won ? "VICTORIA" : "DERROTA", new Vector3(0f, 0.06f, -0.01f), 0.2f, Color.white, new Vector2(2.4f, 0.3f), FontStyles.Bold, 4103);
        title.characterSpacing = 18f;
        TMP_Text subtitle = Text($"{TeamName(winner)}  ·  VICTORIOSO", new Vector3(0f, -0.12f, -0.01f), 0.05f, team, new Vector2(2.4f, 0.1f), FontStyles.Bold, 4103);
        subtitle.characterSpacing = 10f;

        if (won)
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, 0.6f, 0.2f);
        }

        const float fadeIn = 0.45f, hold = 2.6f, fadeOut = 0.6f;

        for (float t = 0f; t < fadeIn + hold + fadeOut; t += Time.deltaTime)
        {
            float a = t < fadeIn ? t / fadeIn : t < fadeIn + hold ? 1f : 1f - (t - fadeIn - hold) / fadeOut;
            a = Mathf.SmoothStep(0f, 1f, a);

            wash.color = new Color(team.r, team.g, team.b, 0.42f * a);
            band.color = new Color(0f, 0f, 0f, 0.55f * a);
            lineTop.color = new Color(team.r, team.g, team.b, a);
            lineBottom.color = new Color(team.r, team.g, team.b, a);
            title.alpha = a;
            subtitle.alpha = a;

            // The title settles in from a little larger.
            banner.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, Mathf.Clamp01(t / (fadeIn + 0.4f)));
            yield return null;
        }

        Destroy(banner.gameObject);
    }

    // ------------------------------------------------------------------ pages

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
        bool won = state == MatchState.Win;

        Vector3 flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;

        // A wall or a spawn-room window right in front would hide the panel: bring it closer (and
        // smaller, so it covers the same part of the view) instead of putting it behind the glass.
        float distance = ClearDistance(cam, flat);
        Vector3 position = cam.transform.position + flat * distance + Vector3.up * -0.05f;

        root = new GameObject("ResultScreen");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(flat, Vector3.up));
        panelScale = distance / PanelDistance;
        root.transform.localScale = Vector3.one * panelScale;

        EnsureRaycaster(cam);

        Entry mine = LocalEntry(stats);
        Entry theirs = RivalEntry(stats);
        Faction winner = won ? mine.side : theirs.side;
        Entry mvp = winner == mine.side || theirs.hero == null ? mine : theirs;
        Entry other = mvp == mine ? theirs : mine;

        mvpPage = new GameObject("Page_MVP");
        mvpPage.transform.SetParent(root.transform, false);
        target = mvpPage.transform;
        BuildMvp(mvp, other, mvp.side == winner, stats);

        boardPage = new GameObject("Page_Scoreboard");
        boardPage.transform.SetParent(root.transform, false);
        target = boardPage.transform;
        BuildBoard(mine, theirs, winner, stats);

        // The two page dots under both pages.
        target = root.transform;
        for (int i = 0; i < 2; i++)
        {
            GameObject dot = Quad("PageDot" + i, new Vector3(-0.05f + i * 0.1f, -PanelHeight / 2f - 0.06f, -0.004f), new Vector3(0.035f, 0.035f, 1f), Muted, null);
            dot.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            dots.Add(dot.GetComponent<Renderer>());
        }

        ShowPage(0);

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

    private void ShowPage(int index)
    {
        if (mvpPage == null || boardPage == null || (index == page && mvpPage.activeSelf == (index == 0) && boardPage.activeSelf == (index == 1)))
        {
            return;
        }

        page = index;
        mvpPage.SetActive(index == 0);
        boardPage.SetActive(index == 1);

        for (int i = 0; i < dots.Count; i++)
        {
            dots[i].sharedMaterial.color = i == index ? Paper : new Color(0.3f, 0.3f, 0.35f, 1f);
        }

        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.2f, 0.04f);
    }

    private static Entry LocalEntry(MatchStats s)
    {
        HeroDefinitionSO hero = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;

        return new Entry
        {
            side = MatchNet.LocalFaction,
            hero = hero,
            who = "Tú",
            local = true,
            souls = s.soulsCollected,
            kills = s.heroKills,
            deaths = s.deaths,
            minions = s.minionsKilled,
            towers = s.towersDestroyed,
            abilities = s.abilitiesUsed,
            items = s.itemsPurchased,
            shots = s.shotsFired,
            tookPatron = s.patronDestroyed,
        };
    }

    /// <summary>The rival as this machine saw them: their kills are our deaths, their towers our losses.</summary>
    private static Entry RivalEntry(MatchStats s)
    {
        HeroDefinitionSO hero = null;

        foreach (NetworkPlayer p in FindObjectsOfType<NetworkPlayer>())
        {
            if (p != NetworkPlayer.Local && p.Hero != null)
            {
                hero = p.Hero;
                break;
            }
        }

        return new Entry
        {
            side = FactionUtility.Opposite(MatchNet.LocalFaction),
            hero = hero,
            who = hero != null ? "Rival" : "Sin rival conectado",
            kills = hero != null ? s.deaths : (int?)null,
            deaths = hero != null ? s.heroKills : (int?)null,
            towers = s.towersLost,
            tookPatron = s.result == MatchResult.Lose,
        };
    }

    // ------------------------------------------------------------------ beat 2: the MVP

    /// <summary>
    /// The MVP page: the painted render big in the middle (it carries its own backdrop), "MVP" and the
    /// best numbers down its left side, the name over the figure, and the other hero's card on the right.
    /// </summary>
    private void BuildMvp(Entry mvp, Entry other, bool mvpWon, MatchStats stats)
    {
        float top = PanelHeight / 2f, bottom = -PanelHeight / 2f, left = -PanelWidth / 2f, right = PanelWidth / 2f;
        Color team = TeamColor(mvp.side);

        Quad("Backdrop", Vector3.zero, new Vector3(PanelWidth, PanelHeight, 1f), Ink, null, OverlayQueue);
        Quad("TeamRule", new Vector3(0f, top, -0.001f), new Vector3(PanelWidth, 0.02f, 1f), team, null, OverlayQueue + 4);

        // The render: as tall as the panel and a little over its top edge, standing on the bottom.
        HeroDefinitionSO hero = mvp.hero;
        if (hero != null && hero.render != null)
        {
            float aspect = (float)hero.render.width / hero.render.height;
            float height = PanelHeight * 1.04f;
            float width = height * aspect;
            Quad("Render", new Vector3(-0.15f, bottom + height / 2f, -0.002f), new Vector3(width, height, 1f), Color.white, hero.render, OverlayQueue + 2);
        }

        // A dark wash down the left so the numbers read over the painting, fading out to the right.
        Quad("Shade", new Vector3(left + 0.7f, 0f, -0.003f), new Vector3(1.4f, PanelHeight, 1f), new Color(0f, 0f, 0f, 0.75f), FadeRight(), OverlayQueue + 3);

        // Left: "MVP" and its numbers, stepping right as they go down like the reference's slant.
        float x = left + 0.22f;
        GameObject diamond = Quad("MvpDiamond", new Vector3(x + 0.06f, 0.4f, -0.005f), new Vector3(0.08f, 0.08f, 1f), team, null, OverlayQueue + 4);
        diamond.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Text(mvpWon ? "MVP" : "TU HÉROE", new Vector3(x + 0.18f + 0.45f, 0.4f, -0.01f), 0.17f, Paper, new Vector2(0.9f, 0.22f), FontStyles.Bold)
            .alignment = TextAlignmentOptions.Left;

        var lines = new List<(string value, string label, Color color)>();

        if (mvp.local)
        {
            lines.Add((stats.towersDestroyed.ToString(), "TORRES\nDERRIBADAS", team));
            if (stats.heroKills > 0) lines.Add((stats.heroKills.ToString(), "BAJAS\nAL RIVAL", KillRed));
            lines.Add((stats.soulsCollected.ToString("N0"), "ALMAS\nRECOGIDAS", SoulCyan));
            lines.Add((stats.minionsKilled.ToString(), "MINIONS\nDERRIBADOS", Paper));
            if (stats.patronDestroyed) lines.Add((Clock(stats.secondsToPatron), "PATRÓN\nDERRIBADO", team));
        }
        else
        {
            lines.Add(((mvp.towers ?? 0).ToString(), "TORRES\nDERRIBADAS", team));
            lines.Add(((mvp.kills ?? 0).ToString(), "BAJAS", KillRed));
            lines.Add((Clock(stats.durationSeconds), "PATRÓN\nDERRIBADO", team));
        }

        for (int i = 0; i < lines.Count; i++)
        {
            float y = 0.18f - i * 0.19f;
            float shift = i * 0.05f;
            TMP_Text value = Text(lines[i].value, new Vector3(x + shift + 0.18f, y, -0.01f), 0.12f, lines[i].color, new Vector2(0.36f, 0.16f), FontStyles.Bold);
            value.alignment = TextAlignmentOptions.Right;
            TMP_Text label = Text(lines[i].label, new Vector3(x + shift + 0.6f, y, -0.01f), 0.038f, Paper, new Vector2(0.44f, 0.12f), FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Left;
            label.lineSpacing = -18f;
        }

        // The name over the lower part of the figure, and who played it.
        float nameX = 0.05f, nameY = bottom + 0.62f;
        if (hero != null && hero.nameLogo != null)
        {
            float w = 0.95f;
            float h = w * hero.nameLogo.height / hero.nameLogo.width;
            Quad("Name", new Vector3(nameX, nameY, -0.006f), new Vector3(w, h, 1f), Paper, hero.nameLogo, OverlayQueue + 4);
        }
        else
        {
            Text(hero != null ? hero.heroName.ToUpperInvariant() : "HÉROE", new Vector3(nameX, nameY, -0.01f), 0.2f, Paper, new Vector2(1.2f, 0.26f), FontStyles.Bold);
        }

        Text("jugado por", new Vector3(nameX, nameY - 0.2f, -0.01f), 0.035f, Muted, new Vector2(0.6f, 0.06f), FontStyles.Normal);
        Text(mvp.who, new Vector3(nameX, nameY - 0.27f, -0.01f), 0.05f, Color.white, new Vector2(0.8f, 0.08f), FontStyles.Bold);

        // Right: the other hero's card, as the key player.
        if (other.hero != null)
        {
            float cx = right - 0.55f;
            Color otherTeam = TeamColor(other.side);
            Text("JUGADOR CLAVE", new Vector3(cx, 0.68f, -0.01f), 0.045f, Paper, new Vector2(0.8f, 0.08f), FontStyles.Bold).characterSpacing = 6f;

            Texture art = other.hero.hudNormal != null ? other.hero.hudNormal : other.hero.portrait != null ? other.hero.portrait.texture : null;
            const float cardH = 0.82f, cardW = cardH * 280f / 380f;
            Quad("CardFrame", new Vector3(cx, 0.2f, -0.003f), new Vector3(cardW + 0.03f, cardH + 0.03f, 1f), otherTeam, null, OverlayQueue + 3);
            if (art != null)
            {
                Quad("Card", new Vector3(cx, 0.2f, -0.004f), new Vector3(cardW, cardH, 1f), Color.white, art, OverlayQueue + 4);
            }

            string line = other.local
                ? $"<color=#{Hex(otherTeam)}>{other.towers ?? 0}</color> <size=55%>TORRES</size>    <color=#{Hex(SoulCyan)}>{other.souls ?? 0:N0}</color> <size=55%>ALMAS</size>"
                : $"<color=#{Hex(KillRed)}>{other.kills ?? 0}</color> <size=55%>BAJAS</size>    <color=#{Hex(otherTeam)}>{other.towers ?? 0}</color> <size=55%>TORRES</size>";
            Text(line, new Vector3(cx, -0.33f, -0.01f), 0.075f, Paper, new Vector2(1f, 0.12f), FontStyles.Bold);
            Text(other.hero.heroName, new Vector3(cx, -0.46f, -0.01f), 0.04f, Muted, new Vector2(0.8f, 0.06f), FontStyles.Normal);
            Text(other.who, new Vector3(cx, -0.53f, -0.01f), 0.05f, Color.white, new Vector2(0.8f, 0.08f), FontStyles.Bold);
        }

        Button("JUGAR OTRA VEZ  (A)", new Vector3(0.05f, bottom + 0.1f, -0.012f), new Vector2(0.95f, 0.15f), new Color(0.1f, 0.11f, 0.15f, 1f), PlayAgain, team);
        Button("TABLA  >", new Vector3(right - 0.4f, bottom + 0.1f, -0.012f), new Vector2(0.5f, 0.15f), new Color(0.1f, 0.11f, 0.15f, 1f), () => ShowPage(1), Muted);
    }

    // ------------------------------------------------------------------ beat 3: the scoreboard

    private static readonly string[] Columns = { "ALMAS", "BAJAS", "MUERTES", "MINIONS", "TORRES", "HABILID.", "COMPRAS", "DISPAROS" };

    private void BuildBoard(Entry mine, Entry theirs, Faction winner, MatchStats stats)
    {
        float top = PanelHeight / 2f, bottom = -PanelHeight / 2f, left = -PanelWidth / 2f, right = PanelWidth / 2f;

        Quad("Backdrop", Vector3.zero, new Vector3(PanelWidth, PanelHeight, 1f), Ink, null, OverlayQueue);
        Quad("TeamRule", new Vector3(0f, top, -0.001f), new Vector3(PanelWidth, 0.02f, 1f), TeamColor(winner), null, OverlayQueue + 4);

        TMP_Text kind = Text("RITUAL 1 VS 1", new Vector3(right - 0.45f, top - 0.09f, -0.01f), 0.05f, Muted, new Vector2(0.8f, 0.08f), FontStyles.Bold);
        kind.alignment = TextAlignmentOptions.Right;
        TMP_Text when = Text($"{System.DateTime.Now:dd/MM/yyyy HH:mm}    {Clock(stats.durationSeconds)}", new Vector3(right - 0.45f, top - 0.16f, -0.01f), 0.035f, Muted, new Vector2(0.8f, 0.06f), FontStyles.Normal);
        when.alignment = TextAlignmentOptions.Right;

        // Amber (the Hidden King) first, then Sapphire (the Archmother), as the game lists them.
        Entry amber = mine.side == Faction.Player ? mine : theirs;
        Entry sapphire = amber == mine ? theirs : mine;
        TeamSection(amber, winner, 0.5f);
        TeamSection(sapphire, winner, 0.0f);

        // Closing line, vote and the ways out.
        Text(ClosingLine(winner == mine.side, stats), new Vector3(0f, -0.42f, -0.01f), 0.05f, Paper, new Vector2(PanelWidth - 0.4f, 0.1f), FontStyles.Italic);

        voteLabel = Text("¿Te gustó la partida?", new Vector3(-0.55f, -0.6f, -0.01f), 0.05f, Ice, new Vector2(1f, 0.08f), FontStyles.Normal);
        Button("Me gusta", new Vector3(0.22f, -0.6f, -0.012f), new Vector2(0.44f, 0.11f), new Color(0.16f, 0.45f, 0.28f, 1f), () => Vote(MatchVote.Like), null);
        Button("No me gusta", new Vector3(0.72f, -0.6f, -0.012f), new Vector2(0.48f, 0.11f), new Color(0.48f, 0.2f, 0.2f, 1f), () => Vote(MatchVote.Dislike), null);

        Button("<  MVP", new Vector3(left + 0.4f, bottom + 0.1f, -0.012f), new Vector2(0.5f, 0.15f), new Color(0.1f, 0.11f, 0.15f, 1f), () => ShowPage(0), Muted);
        Button("JUGAR OTRA VEZ  (A)", new Vector3(0f, bottom + 0.1f, -0.012f), new Vector2(0.95f, 0.15f), new Color(0.1f, 0.11f, 0.15f, 1f), PlayAgain, TeamColor(winner));
        Button("SALIR  (B)", new Vector3(right - 0.4f, bottom + 0.1f, -0.012f), new Vector2(0.5f, 0.15f), new Color(0.1f, 0.11f, 0.15f, 1f), Leave, Muted);
    }

    /// <summary>One side's band: its patron's name (and "Victorioso"), the column titles and the hero's row.</summary>
    private void TeamSection(Entry entry, Faction winner, float y)
    {
        float left = -PanelWidth / 2f;
        Color team = TeamColor(entry.side);

        TMP_Text name = Text(TeamName(entry.side), new Vector3(left + 0.62f, y + 0.2f, -0.01f), 0.1f, team, new Vector2(1.1f, 0.14f), FontStyles.Bold);
        name.alignment = TextAlignmentOptions.Left;
        name.characterSpacing = 4f;

        if (entry.side == winner)
        {
            // A small tag over the side's name.
            TMP_Text victor = Text("VICTORIOSO", new Vector3(left + 0.62f, y + 0.3f, -0.01f), 0.04f, Paper, new Vector2(1.1f, 0.06f), FontStyles.Bold);
            victor.alignment = TextAlignmentOptions.Left;
            victor.characterSpacing = 8f;
        }

        for (int c = 0; c < Columns.Length; c++)
        {
            Text(Columns[c], new Vector3(ColumnX(c), y + 0.19f, -0.01f), 0.032f, Muted, new Vector2(0.3f, 0.05f), FontStyles.Bold);
        }

        // The row: brighter for this player, with a marker on the left.
        float rowY = y + 0.05f;
        Quad("Row_" + entry.side, new Vector3(0.05f, rowY, -0.002f), new Vector3(PanelWidth - 0.3f, 0.18f, 1f),
            entry.side == Faction.Player ? AmberRow : SapphireRow, null, OverlayQueue + 1);

        if (entry.local)
        {
            Quad("Mine", new Vector3(left + 0.12f, rowY, -0.004f), new Vector3(0.05f, 0.05f, 1f), team, null)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Quad("RowEdge", new Vector3(0.05f, rowY - 0.09f, -0.003f), new Vector3(PanelWidth - 0.3f, 0.008f, 1f), team, null);
        }

        Texture face = entry.hero == null ? null : entry.hero.hudNormal != null ? entry.hero.hudNormal : entry.hero.portrait != null ? entry.hero.portrait.texture : null;
        float faceX = left + 0.3f;

        if (face != null)
        {
            Quad("Face", new Vector3(faceX, rowY, -0.004f), new Vector3(0.12f, 0.163f, 1f), Color.white, face);
        }

        TMP_Text who = Text(entry.who, new Vector3(faceX + 0.4f, rowY + 0.03f, -0.01f), 0.05f, Color.white, new Vector2(0.62f, 0.07f), FontStyles.Bold);
        who.alignment = TextAlignmentOptions.Left;
        TMP_Text heroName = Text(entry.hero != null ? entry.hero.heroName : "—", new Vector3(faceX + 0.4f, rowY - 0.035f, -0.01f), 0.038f, Muted, new Vector2(0.62f, 0.06f), FontStyles.Normal);
        heroName.alignment = TextAlignmentOptions.Left;

        int?[] values = { entry.souls, entry.kills, entry.deaths, entry.minions, entry.towers, entry.abilities, entry.items, entry.shots };

        for (int c = 0; c < values.Length; c++)
        {
            string text = values[c].HasValue ? (c == 0 ? values[c].Value.ToString("N0") : values[c].Value.ToString()) : "—";
            Color color = !values[c].HasValue ? Muted : c == 1 && values[c] > 0 ? KillRed : c == 4 && values[c] > 0 ? team : Paper;
            Text(text, new Vector3(ColumnX(c), rowY, -0.01f), 0.06f, color, new Vector2(0.3f, 0.09f), FontStyles.Bold);
        }
    }

    private static float ColumnX(int column) => -0.5f + column * 0.28f;

    // ------------------------------------------------------------------ text helpers

    private static Color TeamColor(Faction side) => side == Faction.Player ? AmberColor : SapphireColor;

    private static string TeamName(Faction side) => side == Faction.Player ? "HIDDEN KING" : "ARCHMOTHER";

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private static string Clock(float seconds) => $"{Mathf.FloorToInt(seconds / 60f):00}:{Mathf.FloorToInt(seconds % 60f):00}";

    /// <summary>One line that closes the story of the match (the narrative arc's "progress" beat).</summary>
    private static string ClosingLine(bool won, MatchStats stats)
    {
        string hero = string.IsNullOrEmpty(stats.heroName) || stats.heroName == "unknown" ? "Tu héroe" : stats.heroName;

        if (won)
        {
            return stats.towersDestroyed > 0
                ? $"{hero} derribó {stats.towersDestroyed} torres y el patrón rival cayó. El ritual es tuyo."
                : $"{hero} llegó hasta el patrón rival. El ritual es tuyo.";
        }

        return stats.towersDestroyed > 0
            ? $"Tu patrón cayó, pero {hero} derribó {stats.towersDestroyed} torres. La próxima será distinta."
            : $"Tu patrón cayó. {hero} aprendió el camino: ahora toca atacar.";
    }

    // ------------------------------------------------------------------ actions

    private void Vote(MatchVote vote)
    {
        if (leaving || MatchManager.Instance == null)
        {
            return;
        }

        MatchManager.Instance.SubmitVote(vote);

        if (voteLabel != null)
        {
            voteLabel.text = vote == MatchVote.Like ? "¡Gracias! Anotamos tu me gusta." : "Gracias por contarnos.";
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

    // ------------------------------------------------------------------ building blocks

    private static void EnsureRaycaster(Camera cam)
    {
        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
        GameObject host = rig != null ? rig.gameObject : cam.gameObject;

        if (host.GetComponent<UnityEngine.EventSystems.OVRPhysicsRaycaster>() == null)
        {
            host.AddComponent<UnityEngine.EventSystems.OVRPhysicsRaycaster>();
        }
    }

    private static Material QuadMaterial(GameObject quad) => quad.GetComponent<Renderer>().sharedMaterial;

    private static Texture2D fadeRight;

    /// <summary>White, opaque on the left and fading to nothing on the right (made once).</summary>
    private static Texture2D FadeRight()
    {
        if (fadeRight == null)
        {
            const int size = 64;
            fadeRight = new Texture2D(size, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int x = 0; x < size; x++)
            {
                float k = x / (size - 1f);
                fadeRight.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0.35f, 1f, k)));
            }

            fadeRight.Apply();
        }

        return fadeRight;
    }

    /// <summary>A flat coloured or textured rectangle on the page being built.</summary>
    private GameObject Quad(string name, Vector3 local, Vector3 scale, Color color, Texture texture, int queue = OverlayQueue + 3)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(target, false);
        quad.transform.localPosition = local;
        quad.transform.localScale = scale;

        // A quad's visible side faces -Z, and the page looks away from the player, so no rotation is needed.
        Collider collider = quad.GetComponent<Collider>();

        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = quad.GetComponent<Renderer>();
        // Drawn after the world's see-through things (spawn-room glass, barriers, glows): otherwise a
        // big window behind the panel can sort in front of it and wash the summary out.
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
        {
            color = color,
            mainTexture = texture,
            renderQueue = queue,
        };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return quad;
    }

    private TMP_Text Text(string content, Vector3 local, float size, Color color, Vector2 box, FontStyles style, int queue = OverlayQueue + 6)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(target, false);
        go.transform.localPosition = local;

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = content;
        text.fontSize = size * 10f;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.rectTransform.sizeDelta = box;

        // Its own copy of the font material, so only this panel's text draws on top of the quads.
        text.fontMaterial.renderQueue = queue;
        return text;
    }

    /// <summary>A pressable plate with a one-line label and an optional coloured outline.</summary>
    private void Button(string label, Vector3 local, Vector2 size, Color color, System.Action onClick, Color? outline)
    {
        if (outline.HasValue)
        {
            Quad("Outline_" + label, local + new Vector3(0f, 0f, 0.001f), new Vector3(size.x + 0.016f, size.y + 0.016f, 1f), outline.Value, null, OverlayQueue + 3);
        }

        GameObject quad = Quad("Button_" + label, local, new Vector3(size.x, size.y, 1f), color, null, OverlayQueue + 4);
        BoxCollider box = quad.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.2f);

        WorldButton button = quad.AddComponent<WorldButton>();
        button.OnClick.AddListener(() => onClick());

        TMP_Text text = Text(label, local + new Vector3(0f, 0f, -0.012f), size.y * 0.5f, Color.white, size, FontStyles.Bold);
        text.name = "Label";

        // One line always: a long label shrinks to fit the button instead of wrapping.
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMax = size.y * 4.5f;
        text.fontSizeMin = size.y * 2f;
        text.characterSpacing = 4f;
        text.rectTransform.sizeDelta = size - new Vector2(0.06f, 0f);
    }

    /// <summary>
    /// How far in front of the player the panel can float without anything solid in between: the
    /// usual 2.1 m, or just short of the first wall, window or block, never closer than 0.8 m.
    /// The player's own hands, weapon and body are ignored.
    /// </summary>
    private static float ClearDistance(Camera cam, Vector3 flat)
    {
        Vector3 origin = cam.transform.position;
        float distance = PanelDistance;

        foreach (RaycastHit hit in Physics.RaycastAll(origin, flat, PanelDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance < 0.35f || hit.transform.root == cam.transform.root)
            {
                continue;
            }

            string layer = LayerMask.LayerToName(hit.collider.gameObject.layer);

            if (layer == "PlayerBody" || layer == "Minion" || layer == "Soul" || layer == "Rival")
            {
                continue;
            }

            distance = Mathf.Min(distance, hit.distance - 0.3f);
        }

        return Mathf.Clamp(distance, 0.8f, PanelDistance);
    }

    private IEnumerator PopIn()
    {
        Vector3 full = Vector3.one * panelScale;
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
