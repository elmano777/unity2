using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The zipline (Deadlock's fast way along the lane): a rope strung from your own base towards the
/// front. Point the left controller at it and hold the grip, and you are pulled along it. The rope
/// only reaches as far as your side holds the lane: it grows when your minions push forward and
/// shrinks when the rival's minions push towards your base, so losing ground costs you reach.
/// It replaces the teleporter pads of the expanded map.
/// </summary>
/// <remarks>
/// Builds itself when the Lane loads (no scene change). Comfort: the rider keeps looking ahead and the
/// vignette closes while riding (<see cref="ComfortLocomotion.ExternalMotion01"/>). Keyboard fallback in
/// the editor: hold Z while looking along the rope.
/// </remarks>
public class LaneZipline : MonoBehaviour
{
    private const string LaneScene = "Lane";

    [Tooltip("Travel speed in m/s. Deadlock's zipline moves at a fixed 18 m/s on the inner lane.")]
    [SerializeField] private float rideSpeed = 18f;

    [Tooltip("Height of the rope above the floor, in metres.")]
    [SerializeField] private float ropeHeight = 6.5f;

    [Tooltip("How close the aim has to pass to the rope, in degrees, to latch on.")]
    [SerializeField] private float aimTolerance = 14f;

    [Tooltip("Where the rope starts, as a fraction of the way from your base to the rival's (0 = your patron).")]
    [SerializeField] private float startFraction = 0.04f;

    [Tooltip("The rope never reaches closer than this to your base, nor further than the max, however the lane goes.")]
    [SerializeField] private Vector2 reachRange = new Vector2(0.38f, 0.8f); // 0.38: always as far as your own Guardian (before, 0.12 barely left the base)

    [Tooltip("How fast the rope's free end grows or shrinks, in metres per second.")]
    [SerializeField] private float endSpeed = 5f;

    private static readonly Color Idle = new Color(0.45f, 0.9f, 1f, 0.55f);
    private static readonly Color Aimed = new Color(1f, 0.95f, 0.6f, 1f);
    private static readonly Color Locked = new Color(0.55f, 0.2f, 0.2f, 0.45f);
    private static readonly Color Denied = new Color(1f, 0.3f, 0.25f, 1f);

    [Tooltip("Where the rope ends near the rival's patron, as a fraction of the lane. Visible always, usable only up to the reach.")]
    [SerializeField] private float endFraction = 0.96f;

    [Tooltip("Rope nearer than this to where you stand can't be grabbed (stops re-grabbing it on arrival), in metres.")]
    [SerializeField] private float minRideDistance = 8f;

    [Tooltip("How close (horizontally) to the rope you must stand for the grip to grab it, in metres.")]
    [SerializeField] private float grabRadius = 10f; // the spawn is ~9 m from the rope

    [Tooltip("Metres of rope from your own patron that count as your base: from inside it the rope only takes you forward.")]
    [SerializeField] private float homeZone = 40f;

    private LineRenderer lockedRope;
    private bool aimedLocked;

    // The left hand stays up on the rope while riding.
    private Transform handVisual;
    private Vector3 handRestPosition;
    private Quaternion handRestRotation;
    private bool handOnRope;
    private Vector3 handRopePosition;
    private Quaternion handRopeRotation;

    private OVRCameraRig rig;
    private ComfortLocomotion locomotion;
    private InputAction hold;
    private LineRenderer rope;
    private Transform marker;

    private Vector3 homePoint;
    private Vector3 axis;
    private float laneLength;
    private bool haveLane;

    // Curved rope (straight, bend, straight) when the scene has a ZiplineRoute; distances are
    // always measured from this player's own patron, so the route is read backwards for Sapphire.
    private ZiplineRoute route;
    private bool routeReversed;
    private const float SampleStep = 2f;

    private float reach;
    private float reachTarget;
    private float nextScan;
    private bool riding;
    private bool wasRiding;
    private bool aimedAtRope;
    private float rideTarget;
    private Vector3 grabPoint;
    private bool needsRelease;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<LaneZipline>() == null)
        {
            new GameObject(nameof(LaneZipline)).AddComponent<LaneZipline>();
        }
    }

    private void Start()
    {
        // The zipline takes the teleporters' place.
        foreach (TeleportPad pad in FindObjectsOfType<TeleportPad>(true))
        {
            pad.gameObject.SetActive(false);
        }

        rig = FindObjectOfType<OVRCameraRig>();
        locomotion = FindObjectOfType<ComfortLocomotion>();

        hold = new InputAction("Zipline", InputActionType.Button);
        hold.AddBinding("<XRController>{LeftHand}/gripPressed");
        hold.AddBinding("<XRController>{LeftHand}/{GripButton}");
        hold.AddBinding("<Keyboard>/z");
        hold.Enable();

        GameObject go = new GameObject("ZiplineRope");
        go.transform.SetParent(transform, false);
        rope = go.AddComponent<LineRenderer>();
        rope.material = new Material(Shader.Find("Sprites/Default"));
        rope.positionCount = 2;
        rope.widthMultiplier = 0.09f; // a little wider than the steel cable of the route, so it glows over it
        rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rope.enabled = false;

        // The rest of the rope, up to the rival's patron: visible, but out of reach until the lane is won.
        GameObject lockedGo = new GameObject("ZiplineRopeLocked");
        lockedGo.transform.SetParent(transform, false);
        lockedRope = lockedGo.AddComponent<LineRenderer>();
        lockedRope.material = new Material(Shader.Find("Sprites/Default"));
        lockedRope.positionCount = 2;
        lockedRope.widthMultiplier = 0.075f;
        lockedRope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lockedRope.startColor = lockedRope.endColor = Locked;
        lockedRope.enabled = false;

        marker =GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
        Destroy(marker.GetComponent<Collider>());
        marker.SetParent(transform, false);
        marker.localScale = Vector3.one * 0.35f;
        marker.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Aimed };
        marker.gameObject.SetActive(false);
    }

    private void OnEnable() => Application.onBeforeRender += ApplyHand;

    private void OnDisable() => Application.onBeforeRender -= ApplyHand;

    private void LateUpdate() => ApplyHand();

    private void OnDestroy()
    {
        hold?.Dispose();

        if (locomotion != null)
        {
            locomotion.ExternalMotion01 = 0f;
        }
    }

    private void Update()
    {
        if (rig == null || rig.centerEyeAnchor == null)
        {
            return;
        }

        if (!haveLane && !FindLane())
        {
            return;
        }

        UpdateReach();
        DrawRope();
        Ride();
    }

    /// <summary>The rope follows the line from your patron to the rival's.</summary>
    private bool FindLane()
    {
        Structure home = null;
        Structure away = null;
        Faction local = MatchNet.LocalFaction;

        foreach (Structure s in Structure.Standing)
        {
            if (!s.IsPatron)
            {
                continue;
            }

            if (s.Faction == local) home = s;
            else away = s;
        }

        // With a route in the scene the patrons are not needed: the home end is the one nearest your
        // own patron, or, when it isn't standing (the practice only spawns the rival's), nearest to
        // where you spawned.
        route = FindObjectOfType<ZiplineRoute>();
        if (route != null && route.IsValid)
        {
            Vector3 reference;
            if (home != null) reference = home.transform.position;
            else if (Time.timeSinceLevelLoad > 1f) reference = rig.centerEyeAnchor.position;
            else return false;

            Vector3 a = route.PointAt(0f), b = route.PointAt(route.Length);
            a.y = b.y = reference.y = 0f;
            routeReversed = (b - reference).sqrMagnitude < (a - reference).sqrMagnitude;
            laneLength = route.Length;
            homePoint = routeReversed ? b : a;
            homePoint.y = rig.transform.position.y;
            axis = ((routeReversed ? a : b) - (routeReversed ? b : a)).normalized;
            haveLane = true;
            reach = reachTarget = Mathf.Lerp(reachRange.x, reachRange.y, 0.3f) * laneLength;
            return true;
        }

        route = null;

        if (home == null || away == null)
        {
            return false;
        }

        Vector3 delta = away.transform.position - home.transform.position;
        delta.y = 0f;
        laneLength = delta.magnitude;

        if (laneLength < 1f)
        {
            return false;
        }

        axis = delta / laneLength;
        homePoint = home.transform.position;
        homePoint.y = rig.transform.position.y;

        haveLane = true;
        reach = reachTarget = Mathf.Lerp(reachRange.x, reachRange.y, 0.3f) * laneLength;
        return true;
    }

    /// <summary>
    /// How much rope there is: up to the front line, which is the more retreated of your own most
    /// advanced minion and the rival's. When the rival pushes towards you the rope gets shorter.
    /// </summary>
    private void UpdateReach()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            Faction local = MatchNet.LocalFaction;
            float ally = 0.3f;
            float enemy = 1f;

            foreach (MinionAgent minion in FindObjectsOfType<MinionAgent>())
            {
                float s = Along(minion.transform.position) / laneLength;

                if (minion.Faction == local) ally = Mathf.Max(ally, s);
                else enemy = Mathf.Min(enemy, s);
            }

            float front = Mathf.Clamp(Mathf.Min(ally, enemy), reachRange.x, reachRange.y);
            reachTarget = front * laneLength;
        }

        reach = Mathf.MoveTowards(reach, reachTarget, endSpeed * Time.deltaTime);
    }

    private Vector3 RopePoint(float distanceAlong)
    {
        if (route != null)
        {
            return route.PointAt(routeReversed ? route.Length - distanceAlong : distanceAlong);
        }

        return homePoint + axis * distanceAlong + Vector3.up * ropeHeight;
    }

    /// <summary>Distance along the rope (from this player's patron) of the rope point nearest to <paramref name="p"/>.</summary>
    private float Along(Vector3 p)
    {
        if (route != null)
        {
            float s = route.Project(p);
            return routeReversed ? route.Length - s : s;
        }

        return Vector3.Dot(p - homePoint, axis);
    }

    /// <summary>The rope between two distances, sampled every couple of metres so bends stay round.</summary>
    private static void SetRopePositions(LineRenderer line, System.Func<float, Vector3> point, float from, float to)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt((to - from) / SampleStep) + 1);
        line.positionCount = n;

        for (int i = 0; i < n; i++)
        {
            line.SetPosition(i, point(Mathf.Lerp(from, to, i / (float)(n - 1))));
        }
    }

    /// <summary>
    /// Where the usable rope begins. A route starts over the spawn room, so it can be grabbed from
    /// its very start; the straight fallback keeps clear of the patron it starts at.
    /// </summary>
    private float StartDistance() => route != null ? 0.5f : startFraction * laneLength;

    private void DrawRope()
    {
        float start = StartDistance();
        rope.enabled = reach > start + 1f;

        if (!rope.enabled)
        {
            return;
        }

        float end = endFraction * laneLength;
        SetRopePositions(rope, RopePoint, start, reach);

        Color c = (aimedAtRope && !aimedLocked) || riding ? Aimed : Idle;
        rope.startColor = c;
        rope.endColor = c;

        lockedRope.enabled = end > reach + 1f;

        if (lockedRope.enabled)
        {
            SetRopePositions(lockedRope, RopePoint, reach, end);
        }
    }

    private void Ride()
    {
        float start = StartDistance();
        Transform head = rig.centerEyeAnchor;

        float hereNow = Along(head.position);

        // While riding the target was fixed on grabbing: no need to keep pointing at the rope.
        float aimedAlong = rideTarget;
        bool latched = riding;
        string refusal = null;

        if (!riding)
        {
            // No aiming (it was a headache in the headset): standing under or next to the rope is
            // enough. The ride goes the way you look along it: on to the end of the usable rope, or
            // back to your base.
            Vector3 under = RopePoint(Mathf.Clamp(hereNow, start, laneLength));
            Vector3 offset = under - head.position;
            offset.y = 0f;
            latched = offset.magnitude <= grabRadius && hereNow >= start - grabRadius && hereNow <= reach + grabRadius;

            if (latched)
            {
                float along = Mathf.Clamp(hereNow, start, laneLength);
                Vector3 tangent = RopePoint(Mathf.Min(along + 2f, laneLength)) - RopePoint(Mathf.Max(along - 2f, 0f));
                Vector3 look = head.forward;
                tangent.y = look.y = 0f;
                bool forward = Vector3.Dot(look, tangent) >= 0f;
                aimedAlong = forward ? reach : start;

                if (Mathf.Abs(aimedAlong - hereNow) < minRideDistance)
                {
                    latched = false;
                    refusal = forward ? "La cuerda no llega más allá. Avanza con tus minions." : "Ya estás al inicio de la cuerda.";
                }
                else if (!forward && hereNow < homeZone)
                {
                    // From the base only out of it, to the lane; coming back in from the lane is fine.
                    latched = false;
                    refusal = "Desde tu base la tirolesa solo va hacia adelante: mira hacia el carril.";
                }
            }
            else if (offset.magnitude <= grabRadius * 2f && hereNow <= reach)
            {
                refusal = "Ponte debajo de la cuerda y aprieta el grip izquierdo.";
            }

            grabPoint = under;
        }

        aimedAtRope = latched;
        aimedLocked = !riding && latched && aimedAlong > reach;
        marker.gameObject.SetActive(latched && rope.enabled);

        if (latched)
        {
            // The glowing bead sits on the rope right above you: "you can grab it here".
            marker.position = riding ? RopePoint(hereNow) : grabPoint;
            marker.GetComponent<Renderer>().sharedMaterial.color = aimedLocked ? Denied : Aimed;
        }

        bool pressed = hold.WasPressedThisFrame();

        if (!hold.IsPressed())
        {
            needsRelease = false;
        }

        // Past the front line the rope can be seen but not ridden: the lane has to be won first.
        if (pressed && aimedLocked)
        {
            refusal = "La cuerda aún no llega hasta allí. Avanza con tus minions.";
        }

        if (pressed && !riding && refusal != null)
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.5f, 0.15f);
            ExperienceCues.Play(ExperienceCues.NotReady, 0.4f);
            GameEvents.RaiseHint(refusal);
            CancelInvoke(nameof(ClearHint));
            Invoke(nameof(ClearHint), 3f);
        }

        bool canAct = MatchRunning() && !InputGate.Blocked;

        if (!riding)
        {
            // A fresh grab: holding the grip from before (or from the last ride) doesn't start one.
            if (latched && !aimedLocked && hold.IsPressed() && !needsRelease && canAct)
            {
                riding = true;
                rideTarget = aimedLocked ? reach : aimedAlong;
                aimedAlong = rideTarget;
            }
        }
        else
        {
            // The rope may shrink under the rider when the rival pushes: stop where it ends.
            rideTarget = Mathf.Min(rideTarget, Mathf.Max(reach, start));
            aimedAlong = rideTarget;

            bool arrived = Mathf.Abs(aimedAlong - hereNow) <= 0.8f;

            if (!hold.IsPressed() || !canAct || arrived)
            {
                riding = false;

                // Arrived with the grip still held: it has to be let go before the next ride.
                needsRelease = hold.IsPressed();
            }
        }

        if (riding && route != null)
        {
            // Slide along the curved rope towards the target, easing sideways onto the line.
            float next = Mathf.MoveTowards(hereNow, aimedAlong, rideSpeed * Time.deltaTime);
            Vector3 step = RopePoint(next) - RopePoint(hereNow);
            Vector3 lateral = RopePoint(hereNow) - head.position;
            step.y = lateral.y = 0f;
            rig.transform.position += step + lateral * Mathf.Clamp01(3f * Time.deltaTime);
            HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.12f, 0.05f);
        }
        else if (riding)
        {
            // Pull the head towards the target point on the rope.
            Vector3 target = homePoint + axis * aimedAlong;
            Vector3 toTarget = target - head.position;
            toTarget.y = 0f;

            if (toTarget.magnitude > 0.8f)
            {
                Vector3 step = toTarget.normalized * Mathf.Min(rideSpeed * Time.deltaTime, toTarget.magnitude);
                rig.transform.position += step;
            }

            HapticFeedback.Pulse(HapticFeedback.Hand.Left, 0.12f, 0.05f);
        }

        if (riding)
        {
            // The hand holds on to the rope just ahead of the rider until the button is let go.
            float here = Along(head.position);
            float sign = aimedAlong >= here ? 1f : -1f;
            float lo = 0f, hi = laneLength;
            Vector3 grab = RopePoint(Mathf.Clamp(here + sign * 1.5f, lo, hi));
            Vector3 tangent = RopePoint(Mathf.Clamp(here + sign * 3f, lo, hi)) - RopePoint(Mathf.Clamp(here, lo, hi));
            handRopePosition = grab;
            handRopeRotation = tangent.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(tangent.normalized, Vector3.up) : Quaternion.identity;
        }

        handOnRope = riding;
        ApplyHand();

        if (locomotion != null)
        {
            locomotion.ExternalMotion01 = riding ? 1f : 0f;
        }

        if (riding != wasRiding)
        {
            HapticFeedback.Pulse(HapticFeedback.Hand.Both, riding ? 0.45f : 0.2f, 0.08f);
            wasRiding = riding;

            // Latching on and letting go have their own sounds when the files are there.
            ExperienceCues.Play(riding ? ExperienceCues.ZiplineStart : "zipline_end", 0.7f);

            if (riding)
            {
                ExperienceCues.Play("zipline_latched", 0.6f);
            }
        }

        SetRideLoop(riding);
    }

    /// <summary>
    /// Pins the left hand's model to the rope while riding and gives it back to the controller when
    /// let go. Also runs just before render, after the rig has moved the anchors to the newest pose.
    /// </summary>
    private void ApplyHand()
    {
        if (handVisual == null)
        {
            if (rig == null || rig.leftControllerAnchor == null)
            {
                return;
            }

            ControllerHand hand = rig.leftControllerAnchor.GetComponentInChildren<ControllerHand>(true);

            if (hand == null)
            {
                return;
            }

            handVisual = hand.transform;
            handRestPosition = handVisual.localPosition;
            handRestRotation = handVisual.localRotation;
        }

        if (handOnRope)
        {
            handVisual.SetPositionAndRotation(handRopePosition, handRopeRotation);
        }
        else
        {
            handVisual.localPosition = handRestPosition;
            handVisual.localRotation = handRestRotation;
        }
    }

    private AudioSource rideLoop;

    /// <summary>The rope hum while riding: fades in and out so it never clicks on or off.</summary>
    private void SetRideLoop(bool on)
    {
        if (rideLoop == null)
        {
            AudioClip clip = ExperienceCues.Clip(ExperienceCues.ZiplineLoop);

            if (clip == null)
            {
                return;
            }

            rideLoop = new GameObject("ZiplineLoop").AddComponent<AudioSource>();
            rideLoop.transform.SetParent(rig.centerEyeAnchor, false);
            rideLoop.clip = clip;
            rideLoop.loop = true;
            rideLoop.spatialBlend = 0f;
            rideLoop.volume = 0f;
        }

        rideLoop.volume = Mathf.MoveTowards(rideLoop.volume, on ? 0.6f : 0f, 3f * Time.deltaTime);

        if (on && !rideLoop.isPlaying)
        {
            rideLoop.Play();
        }
        else if (!on && rideLoop.volume <= 0f && rideLoop.isPlaying)
        {
            rideLoop.Pause();
        }
    }

    private void ClearHint() => GameEvents.RaiseHint(string.Empty);

    private static bool MatchRunning() =>
        MatchManager.Instance == null || MatchManager.Instance.State == MatchState.Playing;

    /// <summary>Finds where along the rope the aim passes closest, if it passes close enough.</summary>
    private bool TryAim(Transform aimer, float start, out float along)
    {
        along = 0f;

        if (reach <= start + 1f)
        {
            return false;
        }

        if (route != null)
        {
            // Against the whole curved rope, read from this player's end.
            float end = endFraction * laneLength;
            float from = routeReversed ? route.Length - end : start;
            float to = routeReversed ? route.Length - start : end;
            Ray aim = new Ray(aimer.position, aimer.forward);
            float angleToRope = route.ClosestToRay(aim, from, to, out float s);

            if (angleToRope > aimTolerance)
            {
                return false;
            }

            along = routeReversed ? route.Length - s : s;
            return true;
        }

        // Aim against the whole rope, so the locked part can be told apart from the usable part.
        Vector3 a = RopePoint(start);
        Vector3 b = RopePoint(endFraction * laneLength);
        Ray ray = new Ray(aimer.position, aimer.forward);

        // Closest point on the rope segment to the aim ray.
        Vector3 d1 = ray.direction;
        Vector3 d2 = (b - a).normalized;
        Vector3 r = ray.origin - a;
        float dd = Vector3.Dot(d1, d2);
        float denom = 1f - dd * dd;
        float t2 = denom > 0.0001f ? (Vector3.Dot(d1, r) * dd - Vector3.Dot(d2, r)) / denom : 0f;
        t2 = Mathf.Clamp(t2, 0f, (b - a).magnitude);
        Vector3 onRope = a + d2 * t2;

        Vector3 toPoint = onRope - ray.origin;
        float angle = Vector3.Angle(d1, toPoint);

        if (angle > aimTolerance || toPoint.sqrMagnitude < 1f)
        {
            return false;
        }

        along = start + t2;
        return true;
    }
}
