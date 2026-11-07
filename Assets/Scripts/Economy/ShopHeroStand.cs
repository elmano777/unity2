using UnityEngine;

/// <summary>
/// The player's hero standing next to their own shop board, as in the shop screen of the game the
/// project is based on. Built from the hero's <see cref="HeroDefinitionSO.previewModel"/> once the
/// board knows it is the local player's (online that is only known after joining the room).
/// </summary>
public class ShopHeroStand : MonoBehaviour
{
    [SerializeField] private ShopBoard board;

    [Tooltip("Hero when the Lane is played without going through hero select.")]
    [SerializeField] private HeroDefinitionSO fallbackHero;

    [SerializeField] private float height = 1.85f;

    private bool built;

    private void Update()
    {
        if (built || board == null || !board.IsMine)
        {
            return;
        }

        built = true;
        HeroDefinitionSO hero = GameSession.Instance != null && GameSession.Instance.SelectedHero != null
            ? GameSession.Instance.SelectedHero
            : fallbackHero;

        if (hero == null || hero.previewModel == null)
        {
            return;
        }

        GameObject model = HeroSelectManager.BuildPreview(hero.previewModel, transform, height);

        // The model is only looks: nothing to shoot or walk into, and no gun of its own to confuse
        // with the player's.
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }

        enabled = false;
    }
}
