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

    /// <summary>The hero being played (the selected one, or the fallback when the Lane is played directly).</summary>
    public HeroDefinitionSO Hero { get; private set; }

    /// <summary>Button names in the same order as <see cref="Abilities"/>.</summary>
    public static readonly string[] ButtonNames = { "A", "X" };

    private void Awake()
    {
        HeroDefinitionSO hero = GameSession.Instance != null && GameSession.Instance.SelectedHero != null
            ? GameSession.Instance.SelectedHero
            : fallbackHero;

        Hero = hero;

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
            actions[i].started += _ => OnPress(index);
            actions[i].canceled += _ => OnRelease(index);
            actions[i].Enable();
        }
    }

    // ---- Charged abilities (Vindicta's Assassinate): hold to charge, release to fire ----

    private int charging = -1;
    private float chargeStart;
    private float nextChargePulse;
    private bool chargeFull;

    /// <summary>0..1 while an ability is being charged, else 0. For the HUD.</summary>
    public float ChargeAmount => charging >= 0 && abilities[charging] != null && abilities[charging].chargeSeconds > 0f
        ? Mathf.Clamp01((Time.time - chargeStart) / abilities[charging].chargeSeconds)
        : 0f;

    private void OnPress(int index)
    {
        if (index >= abilities.Length || abilities[index] == null)
        {
            return;
        }

        if (abilities[index].chargeSeconds <= 0f)
        {
            TryCast(index);
            return;
        }

        if (charging < 0 && CanStart(index))
        {
            charging = index;
            chargeStart = Time.time;
            chargeFull = false;
            nextChargePulse = 0f;
        }
    }

    private void OnRelease(int index)
    {
        if (charging != index)
        {
            return;
        }

        float k = ChargeAmount;
        charging = -1;
        AbilitySO ability = abilities[index];
        TryCast(index, Mathf.Lerp(ability.unchargedDamage, 1f, k));
    }

    private void Update()
    {
        if (charging < 0)
        {
            return;
        }

        // A rising buzz in the hand while it charges, and a firm knock when it is full.
        float k = ChargeAmount;

        if (k >= 1f && !chargeFull)
        {
            chargeFull = true;
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.8f, 0.1f);
        }
        else if (k < 1f && Time.time >= nextChargePulse)
        {
            nextChargePulse = Time.time + 0.12f;
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, Mathf.Lerp(0.08f, 0.4f, k), 0.04f);
        }
    }

    private bool CanStart(int index)
    {
        if (Time.time < readyAt[index])
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.1f, 0.03f);
            ExperienceCues.Play(ExperienceCues.NotReady, 0.5f);
            return false;
        }

        return MatchManager.Instance == null || MatchManager.Instance.State == MatchState.Playing;
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
    public bool TryCast(int index) => TryCast(index, 1f);

    /// <summary>Casts with its damage scaled by <paramref name="damageScale"/> (a partly charged shot).</summary>
    public bool TryCast(int index, float damageScale)
    {
        if (!isActiveAndEnabled || index < 0 || index >= abilities.Length || abilities[index] == null)
        {
            return false;
        }

        if (Time.time < readyAt[index] || IsChanneling)
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.1f, 0.03f);
            ExperienceCues.Play(ExperienceCues.NotReady, 0.5f);
            return false;
        }

        // Nobody casts before the ritual starts or after it ends.
        if (MatchManager.Instance != null && MatchManager.Instance.State != MatchState.Playing)
        {
            return false;
        }

        AbilitySO ability = abilities[index];
        readyAt[index] = Time.time + ability.cooldown * (PlayerUpgrades.Instance != null ? PlayerUpgrades.Instance.CooldownMultiplier : 1f);

        Vector3 from = aim.position;
        castScale = Mathf.Clamp(damageScale, 0.1f, 1f);
        Vector3 to = Resolve(ability, from, aim.forward);

        // The effect grows with the radius item, as in Deadlock: what you see is the area that hurts.
        float radiusScale = ability.radius > 0f && PlayerUpgrades.Instance != null
            ? PlayerUpgrades.Instance.AbilityRadius(ability.radius) / ability.radius
            : 1f;
        AbilityFx.Play(ability, from, to, radiusScale);
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
                        // The shooter's own body and allies don't block the shot; the first enemy takes it.
                        if (!FactionUtility.IsHostile(MatchNet.LocalFaction, target.Faction))
                        {
                            continue;
                        }

                        Hit(target, ability, hit.point, direction);
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

                DamageArea(ability, centre, ability.damage);
                AbilityEffects.ApplyMovement(ability, MatchNet.LocalFaction, centre, ScaledRadius(ability));
                return centre;
            }

            case AbilityShape.Projectile:
            {
                // Straight along the aim, as far as its lifetime carries it, or to the wall or floor it
                // meets first: there it stops and keeps crackling until it fades.
                Vector3 dir = direction.normalized;
                float range = AbilityEffects.MaxTravel(ability);

                if (Physics.SphereCast(from, 0.25f, dir, out RaycastHit wall, range, groundMask, QueryTriggerInteraction.Ignore))
                {
                    range = Mathf.Max(0.3f, wall.distance);
                }

                AbilityEffects.RunProjectile(ability, MatchNet.LocalFaction, from, dir, range, ScaledRadius(ability),
                    (at, amount) => DamageArea(ability, at, amount));
                return from + dir * range;
            }

            case AbilityShape.Channel:
            {
                StartCoroutine(Channel(ability, System.Array.IndexOf(abilities, ability)));
                return from;
            }

            default:
                return from;
        }
    }

    // ---- Channelled abilities (Seven's storm cloud) ----

    /// <summary>True while this machine's player is channelling (a storm): no shooting, no other ability.</summary>
    public static bool LocalChanneling { get; private set; }

    /// <summary>True while this caster is channelling.</summary>
    public bool IsChanneling { get; private set; }

    /// <summary>Index of the ability being channelled, or -1.</summary>
    public int ChannelingIndex { get; private set; } = -1;

    /// <summary>Seconds of channel left.</summary>
    public float ChannelRemaining => IsChanneling ? Mathf.Max(0f, channelEnd - Time.time) : 0f;

    private float channelEnd;

    /// <summary>
    /// The caster turns into a storm: floats up a few metres (gently: no sudden lifts in VR), can still
    /// walk with the stick but not shoot or cast, and lightning hurts every enemy in sight inside the
    /// growing radius. The look is <see cref="AbilityEffects.RunStorm"/>, following the head.
    /// </summary>
    private IEnumerator Channel(AbilitySO ability, int index)
    {
        IsChanneling = true;
        LocalChanneling = true;
        ChannelingIndex = index;
        float seconds = ability.channelSeconds;
        channelEnd = Time.time + seconds;

        ComfortLocomotion locomotion = FindObjectOfType<ComfortLocomotion>();
        Transform body = locomotion != null ? locomotion.Rig : transform;
        float baseY = body.position.y;
        float lifted = 0f;
        Transform head = Camera.main != null ? Camera.main.transform : aim;
        float radiusScale = ability.radius > 0f ? ScaledRadius(ability) / ability.radius : 1f;
        AbilityEffects.RunStorm(ability, head, seconds, radiusScale);

        float interval = Mathf.Max(0.1f, ability.tickInterval);
        float nextTick = 0f;

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (MatchManager.Instance != null && MatchManager.Instance.State != MatchState.Playing)
            {
                break;
            }

            // Up over the first second, down over the last one.
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((seconds - t) / 1f));
            float lift = ability.riseHeight * k;
            Vector3 p = body.position;
            p.y += lift - lifted;
            body.position = p;
            lifted = lift;

            if (t >= nextTick)
            {
                nextTick += interval;
                float radius = AbilityEffects.StormRadius(ability, t, radiusScale);
                StormTick(ability, head.position, radius, ability.damagePerSecond * interval);
            }

            yield return null;
        }

        // Back down exactly where the storm took off from (height only: the walk keeps its place).
        Vector3 end = body.position;
        end.y = baseY;
        body.position = end;

        IsChanneling = false;
        LocalChanneling = false;
        ChannelingIndex = -1;
    }

    /// <summary>One strike of the storm: every enemy inside the radius that the cloud can see takes the damage.</summary>
    private void StormTick(AbilitySO ability, Vector3 cloud, float radius, float amount)
    {
        struck.Clear();
        int count = Physics.OverlapSphereNonAlloc(cloud, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = overlapBuffer[i].GetComponentInParent<IDamageable>();

            if (target == null || !target.IsAlive || target.Faction == Faction.Neutral || !struck.Add(target)
                || !FactionUtility.IsHostile(MatchNet.LocalFaction, target.Faction))
            {
                continue;
            }

            // Out of the cloud's line of sight (behind a building or cover): spared, as in the original.
            Vector3 point = overlapBuffer[i].bounds.center;

            if (Physics.Linecast(cloud + Vector3.up * 0.8f, point, groundMask, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Hit(target, amount, point, (point - cloud).normalized);
        }
    }

    private void OnDestroy()
    {
        if (IsChanneling)
        {
            LocalChanneling = false;
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

        DamageArea(ability, point, ability.damage);
        AbilityEffects.ApplyMovement(ability, MatchNet.LocalFaction, point, ScaledRadius(ability));
    }

    private static float ScaledRadius(AbilitySO ability) =>
        PlayerUpgrades.Instance != null ? PlayerUpgrades.Instance.AbilityRadius(ability.radius) : ability.radius;

    private void DamageArea(AbilitySO ability, Vector3 centre, float baseDamage)
    {
        struck.Clear();

        if (baseDamage <= 0f)
        {
            return;
        }

        float radius = ScaledRadius(ability);
        int count = Physics.OverlapSphereNonAlloc(centre, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Collide);

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
                Hit(target, baseDamage, point, (point - centre).normalized);
            }
        }
    }

    private float castScale = 1f;

    private void Hit(IDamageable target, AbilitySO ability, Vector3 point, Vector3 direction) =>
        Hit(target, ability.damage * castScale, point, direction);

    private void Hit(IDamageable target, float baseDamage, Vector3 point, Vector3 direction)
    {
        float amount = PlayerUpgrades.Instance != null ? PlayerUpgrades.Instance.AbilityDamage(baseDamage) : baseDamage;
        DamageInfo info = new DamageInfo(amount, MatchNet.LocalFaction, gameObject, DamageType.Ability)
            .AtImpact(point, direction);

        target.TakeDamage(info);
    }
}
