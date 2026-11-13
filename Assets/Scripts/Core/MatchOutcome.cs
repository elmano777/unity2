using UnityEngine;

/// <summary>
/// Tells the player how the ritual ended the moment it does: the result on the hint board and, on a
/// win, the hero's victory line (the <c>winClip</c> that had nothing playing it). The full summary
/// screen with the vote arrives with Avance 5; this is what makes the patron's fall land now.
/// Also announces each objective that falls, so the player knows the sequence moved on (RF-12).
/// </summary>
public class MatchOutcome : MonoBehaviour
{
    [Tooltip("Hero whose win line plays when the Lane is played without going through HeroSelect.")]
    [SerializeField] private HeroDefinitionSO fallbackHero;

    [SerializeField] private AudioSource voice;

    private void OnEnable()
    {
        GameEvents.MatchStateChanged += OnMatchStateChanged;
        GameEvents.TowerDestroyed += OnTowerDestroyed;
    }

    private void OnDisable()
    {
        GameEvents.MatchStateChanged -= OnMatchStateChanged;
        GameEvents.TowerDestroyed -= OnTowerDestroyed;
    }

    private void OnTowerDestroyed(Structure structure, Faction faction)
    {
        // The guided practice runs its own hints; don't talk over them.
        if (GameSession.Instance != null && GameSession.Instance.IsPracticeRun)
        {
            return;
        }

        Faction rival = FactionUtility.Opposite(MatchNet.LocalFaction);
        Structure next = Structure.NextObjective(faction);
        string nextText = next != null ? $" Siguiente: {next.DisplayName}." : string.Empty;

        GameEvents.RaiseHint(faction == rival
            ? $"Cayó el {structure.DisplayName} rival.{nextText}"
            : $"¡Perdiste tu {structure.DisplayName}!{nextText}");

        CancelInvoke(nameof(ClearHint));
        Invoke(nameof(ClearHint), 4f);
    }

    private void ClearHint() => GameEvents.RaiseHint(string.Empty);

    private void OnMatchStateChanged(MatchState state)
    {
        switch (state)
        {
            case MatchState.Win:
                CancelInvoke(nameof(ClearHint));
                GameEvents.RaiseHint("¡VICTORIA!\nDestruiste al patrón rival.");
                PlayWinLine();
                break;

            case MatchState.Lose:
                CancelInvoke(nameof(ClearHint));
                GameEvents.RaiseHint("DERROTA\nTu patrón ha caído.");
                break;
        }
    }

    private void PlayWinLine()
    {
        HeroDefinitionSO hero = GameSession.Instance != null && GameSession.Instance.SelectedHero != null
            ? GameSession.Instance.SelectedHero
            : fallbackHero;

        if (hero == null || hero.winClip == null)
        {
            return;
        }

        if (voice != null)
        {
            voice.PlayOneShot(hero.winClip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(hero.winClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }
    }
}
