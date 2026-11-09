using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's gun: hitscan shooting with cooldown, plus the sound and vibration that confirm
/// the shot landed (RF-06). Hits anything that implements <see cref="IDamageable"/>, which is how
/// the same trigger pull farms minions, collects souls and damages towers.
/// </summary>
/// <remarks>
/// Driven by the Input System directly rather than by XRI's activate event, so the only XR thing
/// this file touches is <see cref="HapticFeedback"/>. That keeps it portable to Unity 2022, where
/// XRI's interactor API differs. Attach an XR Grab Interactable on the prefab for the grabbing
/// itself — that is scene setup, not code.
/// </remarks>
public class Weapon : MonoBehaviour
{
    [Header("Ballistics")]
    [Tooltip("Empty transform at the tip of the barrel. Its forward axis is the firing direction.")]
    [SerializeField] private Transform muzzle;

    [SerializeField] private float damage = 20f;

    [Tooltip("Seconds between shots. 0.15 gives roughly 6-7 shots per second.")]
    [SerializeField] private float fireInterval = 0.15f;

    [SerializeField] private float range = 60f;

    [Tooltip("Layers a shot can hit. Leave out the player's own body to avoid self-hits.")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Identity")]
    [Tooltip("Side this weapon shoots for, stamped onto every DamageInfo it produces.")]
    [SerializeField] private Faction owner = Faction.Player;

    [Header("Input")]
    [Tooltip("Trigger action that fires. Leave empty to use the built-in right trigger binding below.")]
    [SerializeField] private InputActionReference fireAction;

    [Tooltip("Used when no fire action is assigned: the right controller trigger (Oculus XR plugin " +
             "layouts), plus the left mouse button so the lane can be tested in the editor without a headset.")]
    [SerializeField] private bool useDefaultBindings = true;

    [Tooltip("Keep firing every fireInterval while the trigger is held, instead of once per pull.")]
    [SerializeField] private bool automatic = true;

    [Tooltip("Which controller vibrates on each shot.")]
    [SerializeField] private HapticFeedback.Hand hapticHand = HapticFeedback.Hand.Right;

    [SerializeField] private float hapticAmplitude = 0.35f;
    [SerializeField] private float hapticDuration = 0.06f;

    [Header("Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireClip;

    [Tooltip("Particle system at the muzzle, played on each shot.")]
    [SerializeField] private ParticleSystem muzzleFlash;

    [Tooltip("Spawned at the impact point on a hit. Destroys itself.")]
    [SerializeField] private GameObject impactEffect;

    [Tooltip("Line renderer used as a tracer. Optional.")]
    [SerializeField] private LineRenderer tracer;

    [SerializeField] private float tracerDuration = 0.04f;

    [Tooltip("World-space particle system with a stretched renderer. One particle per shot flies from " +
             "the muzzle to the impact point, so the bullet reads as travelling. Optional.")]
    [SerializeField] private ParticleSystem bulletStreak;

    [Tooltip("How fast the visible bullet travels, in metres per second. Damage stays instant.")]
    [SerializeField] private float bulletSpeed = 140f;

    /// <summary>Raised after every shot with where it started and where it ended, so the rival's screen can draw it.</summary>
    public event System.Action<Vector3, Vector3> Shot;

    private float nextFireTime;
    private float tracerHideTime;

    /// <summary>The action actually listened to: the assigned reference or the default bindings.</summary>
    private InputAction activeFireAction;
    private bool ownsFireAction;

    /// <summary>True while the weapon is off cooldown and can fire.</summary>
    public bool CanFire => Time.time >= nextFireTime;

    /// <summary>Side this weapon shoots for. Set at runtime when the online rival plays the Sapphire side.</summary>
    public Faction Owner
    {
        get => owner;
        set => owner = value;
    }

    /// <summary>Damage of one bullet, after shop upgrades.</summary>
    public float Damage => damage;

    /// <summary>Adds flat damage per bullet. Used by the shop's damage item.</summary>
    public void AddDamage(float amount)
    {
        damage = Mathf.Max(1f, damage + amount);
    }

    /// <summary>Multiplies the time between shots: below 1 fires faster. Used by the shop's fire-rate item.</summary>
    public void ScaleFireInterval(float factor)
    {
        fireInterval = Mathf.Max(0.04f, fireInterval * Mathf.Max(0.1f, factor));
    }

    private void Awake()
    {
        if (muzzle == null)
        {
            muzzle = transform;
        }

        if (tracer != null)
        {
            tracer.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (fireAction != null && fireAction.action != null)
        {
            activeFireAction = fireAction.action;
        }
        else if (useDefaultBindings)
        {
            // The project runs on the Oculus XR plugin with OVRCameraRig, not XRI, so there is no
            // XRI input actions asset to reference. These bindings resolve against the Touch
            // controller layouts the Oculus plugin registers with the Input System.
            activeFireAction = new InputAction("Fire", InputActionType.Button);
            activeFireAction.AddBinding("<XRController>{RightHand}/triggerPressed");
            activeFireAction.AddBinding("<XRController>{RightHand}/{TriggerButton}");
            activeFireAction.AddBinding("<Mouse>/leftButton");
            ownsFireAction = true;
        }

        if (activeFireAction != null)
        {
            activeFireAction.performed += OnFirePerformed;
            activeFireAction.Enable();
        }
    }

    private void OnDisable()
    {
        if (activeFireAction == null)
        {
            return;
        }

        activeFireAction.performed -= OnFirePerformed;

        if (ownsFireAction)
        {
            activeFireAction.Dispose();
            ownsFireAction = false;
        }

        activeFireAction = null;
    }

    private void Update()
    {
        if (automatic && activeFireAction != null && activeFireAction.IsPressed())
        {
            TryFire();
        }

        if (tracer != null && tracer.enabled && Time.time >= tracerHideTime)
        {
            tracer.enabled = false;
        }
    }

    private void OnFirePerformed(InputAction.CallbackContext context)
    {
        TryFire();
    }

    /// <summary>
    /// Fires if the cooldown has elapsed. Public so the tutorial and, later, a networked input
    /// handler can drive the weapon without going through the local input action.
    /// </summary>
    /// <returns>True when a shot actually went out.</returns>
    public bool TryFire()
    {
        if (!CanFire)
        {
            return false;
        }

        nextFireTime = Time.time + fireInterval;

        Vector3 origin = muzzle.position;
        Vector3 direction = muzzle.forward;
        Vector3 endPoint = origin + direction * range;

        // QueryTriggerInteraction.Collide so soul pickups can use trigger colliders and still be shot.
        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Collide))
        {
            endPoint = hit.point;
            ApplyHit(hit, direction);
        }

        PlayFeedback(origin, endPoint);
        GameEvents.RaiseShotFired();
        Shot?.Invoke(origin, endPoint);

        return true;
    }

    private void ApplyHit(RaycastHit hit, Vector3 direction)
    {
        // GetComponentInParent so a collider on a child mesh still finds the Health on the root.
        IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

        if (target != null && target.IsAlive)
        {
            // The shop's passive bonuses (close, long range, minions) and the weapon investment apply here.
            float amount = PlayerUpgrades.Instance != null
                ? PlayerUpgrades.Instance.WeaponDamage(damage, hit.distance, hit.collider)
                : damage;

            DamageInfo info = new DamageInfo(amount, owner, gameObject)
                .AtImpact(hit.point, direction);

            target.TakeDamage(info);
        }

        PlayImpactSound(hit, target);

        if (impactEffect != null)
        {
            GameObject spawned = Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(spawned, 2f);
        }
    }

    private AudioClip[] heroFire = System.Array.Empty<AudioClip>();
    private string heroFireFor;

    /// <summary>
    /// The shot of the hero in use: files in Resources/Weapons/&lt;hero name&gt; (a few variants, one at random each time).
    /// The clip set on the weapon stays as the fallback when there are none.
    /// </summary>
    private AudioClip PickFireClip()
    {
        HeroDefinitionSO hero = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;
        string name = hero != null ? hero.heroName : null;

        if (name != heroFireFor)
        {
            heroFireFor = name;
            heroFire = string.IsNullOrEmpty(name) ? System.Array.Empty<AudioClip>() : Resources.LoadAll<AudioClip>("Weapons/" + name);
        }

        return heroFire.Length > 0 ? heroFire[Random.Range(0, heroFire.Length)] : fireClip;
    }

    private static float nextImpactSound;

    /// <summary>
    /// What the bullet hit decides how it sounds: a body, a structure's metal, a shielded structure
    /// (which takes no damage yet, so the player hears why) or plain ground and walls. Souls keep their own sound.
    /// </summary>
    private static void PlayImpactSound(RaycastHit hit, IDamageable target)
    {
        if (Time.time < nextImpactSound || target is SoulPickup)
        {
            return;
        }

        nextImpactSound = Time.time + 0.05f;
        Structure structure = hit.collider.GetComponentInParent<Structure>();
        string cue;

        if (structure != null)
        {
            cue = structure.IsLocked ? "impact_shield" : "impact_metal";
        }
        else if (target != null)
        {
            cue = "impact_flesh";
        }
        else
        {
            cue = "impact_concrete";
        }

        ExperienceCues.PlayAt(cue, hit.point, 0.5f);
    }

    private void PlayFeedback(Vector3 origin, Vector3 endPoint)
    {
        AudioClip shot = PickFireClip();

        if (audioSource != null && shot != null)
        {
            audioSource.PlayOneShot(shot);
        }

        if (muzzleFlash != null)
        {
            muzzleFlash.Play();
        }

        if (tracer != null)
        {
            tracer.positionCount = 2;
            tracer.SetPosition(0, origin);
            tracer.SetPosition(1, endPoint);
            tracer.enabled = true;
            tracerHideTime = Time.time + tracerDuration;
        }

        if (bulletStreak != null && bulletSpeed > 0f)
        {
            // Emit rather than Play: every shot gets its own particle, so rapid fire shows several
            // bullets in the air at once, each dying exactly where its ray hit.
            Vector3 travel = endPoint - origin;
            var emit = new ParticleSystem.EmitParams
            {
                position = origin,
                velocity = travel.normalized * bulletSpeed,
                startLifetime = Mathf.Max(0.01f, travel.magnitude / bulletSpeed),
                applyShapeToPosition = false,
            };
            bulletStreak.Emit(emit, 1);
        }

        HapticFeedback.Pulse(hapticHand, hapticAmplitude, hapticDuration);
    }
}
