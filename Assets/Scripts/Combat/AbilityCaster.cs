using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Casts the selected hero's abilities (RF-10): A on the right controller for the first, X on the
/// left for the second (1 and 2 on the keyboard in the editor). Aimed with the right hand, like the
/// gun. Each ability has its own cooldown, shown on the <see cref="WristHud"/>.
/// </summary>
/// <remarks>
/// Damage goes through <see cref="IDamageable.TakeDamage"/> exactly like a bullet, so online the
/// rival's copies forward it to the host and nothing here needs to know about the network. The
/// <see cref="Cast"/> event lets <see cref="NetworkPlayer"/> show the cast on the rival's screen.
/// B and Y stay free for <c>BackToGarage</c>.
/// </remarks>
public class AbilityCaster : MonoBehaviour
{
    [Tooltip("Hero used when the Lane is played without going through HeroSelect (Play in the editor).")]
    [SerializeField] private HeroDefinitionSO fallbackHero;

    [Tooltip("Where aimed abilities leave from and point. Usually the right controller anchor.")]
    [SerializeField] private Transform aim;

    [Tooltip("What an aimed cast stops on and what a beam can hit. Copy the weapon's hit mask.")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Tooltip("Layers the floor is on, to drop an aimed area that hit nothing down to the ground.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [SerializeField] private float hapticAmplitude = 0.6f;
    [SerializeField] private float hapticDuration = 0.12f;

    private readonly Collider[] overlapBuffer = new Collider[48];
    private readonly HashSet<IDamageable> struck = new HashSet<IDamageable>();
    private readonly InputAction[] actions = new InputAction[2];
    private AbilitySO[] abilities = System.Array.Empty<AbilitySO>();
    private float[] readyAt = System.Array.Empty<float>();

    /// <summary>Raised on every cast with the ability's index, where it left from and where it landed.</summary>
    public event System.Action<int, Vector3, Vector3> Cast;

    /// <summary>Abilities of the hero being played, in button order.</summary>
    public IReadOnlyList<AbilitySO> Abilities => abilities;

    /// <summary>Button names in the same order as <see cref="Abilities"/>.</summary>
    public static readonly string[] ButtonNames = { "A", "X" };

    private void Awake()
    {
        HeroDefinitionSO hero = GameSession.Instance != null && GameSession.Instance.SelectedHero != null
            ? GameSession.Instance.SelectedHero
            : fallbackHero;

        if (hero != null && hero.abilities != null)
        {
            abilities = hero.abilities;
        }

        readyAt = new float[abilities.Length];

        if (aim == null)
        {
            OVRCameraRig rig = GetComponentInChildren<OVRCameraRig>() ?? FindObjectOfType<OVRCameraRig>();
            aim = rig != null ? rig.rightControllerAnchor : transform;
        }
    }

    private void OnEnable()
    {
        actions[0] = new InputAction("Ability1", InputActionType.Button);
        actions[0].AddBinding("<XRController>{RightHand}/{PrimaryButton}");
        actions[0].AddBinding("<Keyboard>/1");

        actions[1] = new InputAction("Ability2", InputActionType.Button);
        actions[1].AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
        actions[1].AddBinding("<Keyboard>/2");

        for (int i = 0; i < actions.Length; i++)
        {
            int index = i;
            actions[i].performed += _ => TryCast(index);
            actions[i].Enable();
        }
    }

    private void OnDisable()
    {
        for (int i = 0; i < actions.Length; i++)
        {
            actions[i]?.Dispose();
            actions[i] = null;
        }
    }

    /// <summary>Seconds until ability <paramref name="index"/> can be cast again; 0 when ready.</summary>
    public float CooldownRemaining(int index)
    {
        return index >= 0 && index < readyAt.Length ? Mathf.Max(0f, readyAt[index] - Time.time) : 0f;
    }

    /// <summary>Makes every ability ready now (editor demo keys).</summary>
    public void ResetCooldowns()
    {
        for (int i = 0; i < readyAt.Length; i++)
        {
            readyAt[i] = 0f;
        }
    }

    /// <summary>
    /// Casts ability <paramref name="index"/> if it is off cooldown. Public so the tutorial and tests
    /// can cast without the controller.
    /// </summary>
    /// <returns>True when the ability went off.</returns>
    public bool TryCast(int index)
    {
        if (!isActiveAndEnabled || index < 0 || index >= abilities.Length || abilities[index] == null)
        {
            return false;
        }

        if (Time.time < readyAt[index])
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.1f, 0.03f);
            return false;
        }

        // Nobody casts before the ritual starts or after it ends.
        if (MatchManager.Instance != null && MatchManager.Instance.State != MatchState.Playing)
        {
            return false;
        }

        AbilitySO ability = abilities[index];
        readyAt[index] = Time.time + ability.cooldown;

        Vector3 from = aim.position;
        Vector3 to = Resolve(ability, from, aim.forward);

        AbilityFx.Play(ability, from, to);
        HapticFeedback.Pulse(index == 0 ? HapticFeedback.Hand.Right : HapticFeedback.Hand.Left, hapticAmplitude, hapticDuration);
        GameEvents.RaiseAbilityUsed(ability);
        Cast?.Invoke(index, from, to);

        return true;
    }

    /// <summary>Finds where the ability lands and deals its damage, now or when the orb arrives.</summary>
    private Vector3 Resolve(AbilitySO ability, Vector3 from, Vector3 direction)
    {
        switch (ability.shape)
        {
            case AbilityShape.Line:
            {
                float range = Mathf.Max(ability.radius, ability.castRange);
                Vector3 end = from + direction * range;

                foreach (RaycastHit hit in SortedHits(from, direction, range))
                {
                    IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                    if (target != null && target.IsAlive && target.Faction != Faction.Neutral)
                    {
                        if (FactionUtility.IsHostile(MatchNet.LocalFaction, target.Faction))
                        {
                            Hit(target, ability, hit.point, direction);
                        }

                        end = hit.point;
                        break;
                    }

                    // Walls and cover stop the beam; souls and allies' triggers don't.
                    if (!hit.collider.isTrigger && target == null)
                    {
                        end = hit.point;
                        break;
                    }
                }

                return end;
            }

            case AbilityShape.AreaAtAim:
            {
                Vector3 point = AimPoint(from, direction, ability.castRange);
                StartCoroutine(DamageAfter(ability, point, ability.travelTime));
                return point;
            }

            case AbilityShape.AreaAroundCaster:
            {
                Vector3 centre = aim.position;

                if (Physics.Raycast(centre, Vector3.down, out RaycastHit floor, 3f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    centre = floor.point;
                }

                DamageArea(ability, centre);
                return centre;
            }

            default:
                return from;
        }
    }

    private RaycastHit[] SortedHits(Vector3 from, Vector3 direction, float range)
    {
        RaycastHit[] hits = Physics.SphereCastAll(from, 0.2f, direction, range, hitMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    private Vector3 AimPoint(Vector3 from, Vector3 direction, float range)
    {
        if (Physics.Raycast(from, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        // Aimed at the sky: land it at full range, dropped onto the floor below.
        Vector3 far = from + direction * range;

        if (Physics.Raycast(far + Vector3.up * 20f, Vector3.down, out RaycastHit floor, 40f, groundMask, QueryTriggerInteraction.Ignore))
        {
            return floor.point;
        }

        return far;
    }

    private IEnumerator DamageAfter(AbilitySO ability, Vector3 point, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        DamageArea(ability, point);
    }

    private void DamageArea(AbilitySO ability, Vector3 centre)
    {
        struck.Clear();

        int count = Physics.OverlapSphereNonAlloc(centre, ability.radius, overlapBuffer, hitMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = overlapBuffer[i].GetComponentInParent<IDamageable>();

            // Souls are neutral: an ability is not a way to vacuum them up.
            if (target == null || !target.IsAlive || target.Faction == Faction.Neutral || !struck.Add(target))
            {
                continue;
            }

            if (FactionUtility.IsHostile(MatchNet.LocalFaction, target.Faction))
            {
                Vector3 point = overlapBuffer[i].bounds.center;
                Hit(target, ability, point, (point - centre).normalized);
            }
        }
    }

    private void Hit(IDamageable target, AbilitySO ability, Vector3 point, Vector3 direction)
    {
        DamageInfo info = new DamageInfo(ability.damage, MatchNet.LocalFaction, gameObject, DamageType.Ability)
            .AtImpact(point, direction);

        target.TakeDamage(info);
    }
}
