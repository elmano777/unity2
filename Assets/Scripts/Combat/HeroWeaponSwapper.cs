using UnityEngine;

/// <summary>
/// Lane scene: replaces the placeholder weapon held in the right hand with the selected hero's weapon
/// (<see cref="HeroDefinitionSO.weaponPrefab"/>), keeping the same parent and local pose.
/// Does nothing when no hero was selected (e.g. pressing Play directly in the Lane scene).
/// </summary>
public class HeroWeaponSwapper : MonoBehaviour
{
    private void Start()
    {
        GameSession session = GameSession.Instance;
        HeroDefinitionSO hero = session != null ? session.SelectedHero : null;
        if (hero == null || hero.weaponPrefab == null) return;

        Weapon current = FindObjectOfType<Weapon>();
        if (current == null)
        {
            Debug.LogWarning("HeroWeaponSwapper: no Weapon in the scene to replace.");
            return;
        }

        if (current.name.StartsWith(hero.weaponPrefab.name)) return;

        Transform t = current.transform;
        GameObject swapped = Instantiate(hero.weaponPrefab, t.parent);
        swapped.transform.SetLocalPositionAndRotation(t.localPosition, t.localRotation);
        Destroy(current.gameObject);
        Debug.Log($"HeroWeaponSwapper: {hero.heroName} now holds '{hero.weaponPrefab.name}'.");
    }
}
