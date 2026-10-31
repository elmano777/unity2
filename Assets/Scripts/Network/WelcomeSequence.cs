using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// The welcome both players hear together before an online match. While one is alone in the room the
/// Lane is still and the status line says so; the moment the second player is in, both screens go black
/// and each plays their hero's welcome (the story lines and the entry voice line, until they press the
/// trigger). When a player's ends they wait for the other, and when both have finished the screens fade
/// back in and the match starts from zero on both machines (see <see cref="MatchStartSync"/>).
/// </summary>
public class WelcomeSequence : MonoBehaviour
{
    [SerializeField] private IntroSequencePlayer intro;
    [SerializeField] private ScreenFader fader;
    [SerializeField] private DuelMatchmaker matchmaker;
    [SerializeField] private MatchStartSync sync;

    [Tooltip("Shown on the black screen while the other player is still on their welcome.")]
    [SerializeField] private string waitingMessage = "Esperando al otro jugador...";

    private bool running;

    private void Awake()
    {
        if (matchmaker == null) matchmaker = FindObjectOfType<DuelMatchmaker>();
        if (sync == null) sync = FindObjectOfType<MatchStartSync>();
    }

    private void OnEnable()
    {
        if (matchmaker != null)
        {
            matchmaker.StatusChanged += OnStatus;
            OnStatus(matchmaker.Current, matchmaker.Detail);
        }
    }

    private void OnDisable()
    {
        if (matchmaker != null)
        {
            matchmaker.StatusChanged -= OnStatus;
        }
    }

    private void OnStatus(DuelMatchmaker.Status status, string detail)
    {
        if (status == DuelMatchmaker.Status.Connected && !running && sync != null && !sync.Started)
        {
            StartCoroutine(Run());
        }
    }

    private IEnumerator Run()
    {
        running = true;

        HeroDefinitionSO hero = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;

        // No hero behind this Lane (Play straight in the editor, a test client) means no menu and no
        // welcome: report ready at once so two such players still start together.
        if (hero == null || IsSelfTest())
        {
            sync.SetLocalReady();
            running = false;
            yield break;
        }

        if (fader != null)
        {
            yield return fader.FadeOut();
        }

        if (intro != null && IntroSequencePlayer.HasLines(hero.introLines))
        {
            yield return intro.Play(hero.introLines, hero.entryClip);
        }
        else if (hero.entryClip != null && intro != null && intro.VoiceSource != null)
        {
            intro.VoiceSource.clip = hero.entryClip;
            intro.VoiceSource.Play();
            yield return new WaitForSecondsRealtime(hero.entryClip.length);
        }

        sync.SetLocalReady();

        if (intro != null)
        {
            Func<bool> over = () => sync.Started || matchmaker == null || matchmaker.Current != DuelMatchmaker.Status.Connected;
            yield return intro.Hold(waitingMessage, over);
        }
        else
        {
            while (!sync.Started) yield return null;
        }

        if (fader != null)
        {
            yield return fader.FadeIn();
        }

        running = false;
    }

    private static bool IsSelfTest()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg == "-selftest")
            {
                return true;
            }
        }

        return false;
    }
}
