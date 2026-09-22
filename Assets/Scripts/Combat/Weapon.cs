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

    private float nextFireTime;
    private float tracerHideTime;

    /// <summary>The action actually listened to: the assigned reference or the default bindings.</summary>
    private InputAction activeFireAction;
    private bool ownsFireAction;

    /// <summary>True while the weapon is off cooldown and can fire.</summary>
    public bool CanFire => Time.time >= nextFireTime;

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

        return true;
    }

    private void ApplyHit(RaycastHit hit, Vector3 direction)
    {
        // GetComponentInParent so a collider on a child mesh still finds the Health on the root.
        IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

        if (target != null && target.IsAlive)
        {
            DamageInfo info = new DamageInfo(damage, owner, gameObject)
                .AtImpact(hit.point, direction);

            target.TakeDamage(info);
        }

        if (impactEffect != null)
        {
            GameObject spawned = Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(spawned, 2f);
        }
    }

    private void PlayFeedback(Vector3 origin, Vector3 endPoint)
    {
        if (audioSource != null && fireClip != null)
        {
            audioSource.PlayOneShot(fireClip);
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

        HapticFeedback.Pulse(hapticHand, hapticAmplitude, hapticDuration);
    }
}
