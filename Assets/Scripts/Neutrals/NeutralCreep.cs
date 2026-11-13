using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A neutral creature of a camp beside the lane. It belongs to nobody: either player can kill it
/// for its souls, which go to whoever landed the killing blow. It never leaves its spot; hit it and
/// it (and the rest of its camp) spits back at the attacker for a few seconds.
/// </summary>
/// <remarks>
/// Like a minion, it only thinks on the host (<see cref="Health.IsReplica"/>): the joining player's
/// copy shows the hits the host reports through <see cref="NetworkNeutralActions"/>. The model has
/// no skeleton, so its life comes from a slow hover and a lunge when it attacks.
/// </remarks>
[RequireComponent(typeof(Health))]
public class NeutralCreep : MonoBehaviour
{
    [Header("Reward")]
    [Tooltip("Prefab spawned for each soul it drops. Needs a SoulPickup.")]
    [SerializeField] private GameObject soulPrefab;

    [Tooltip("Souls dropped in total when it dies.")]
    [SerializeField] private int soulValue = 18;

    [Tooltip("How many separate pickups the reward is split into.")]
    [SerializeField] private int soulDrops = 2;

    [Header("Retaliation")]
    [Tooltip("Damage of each spit at the hero that attacked the camp. It never lands a killing blow.")]
    [SerializeField] private float attackDamage = 8f;

    [SerializeField] private float attackInterval = 1.7f;

    [Tooltip("Farthest it can reach its attacker, in metres. Step back past this and it gives up.")]
    [SerializeField] private float attackRange = 13f;

    [Tooltip("Seconds it stays angry after the last hit it took.")]
    [SerializeField] private float aggroSeconds = 5f;

    [Tooltip("Layers that block its line of fire: buildings and cover.")]
    [SerializeField] private LayerMask sightBlockers;

    [Header("Looks")]
    [Tooltip("Child holding the mesh: it hovers, turns to face its attacker and lunges when it spits.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Where the spit leaves from. Defaults to a point at chest height.")]
    [SerializeField] private Transform muzzle;

    [Tooltip("Line drawn from the creature to what it hit, for a moment.")]
    [SerializeField] private LineRenderer bolt;

    [SerializeField] private float boltSeconds = 0.18f;
    [SerializeField] private float hoverHeight = 0.12f;
    [SerializeField] private float hoverSpeed = 1.3f;

    [Tooltip("Transparent material (Deadlock/MinionFade) swapped onto the body when it dies.")]
    [SerializeField] private Material deathFadeMaterial;

    [SerializeField] private float deathFadeDuration = 0.6f;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    private Health health;
    private Vector3 visualBasePosition;
    private Quaternion restRotation;
    private Faction aggroSide = Faction.Neutral;
    private float aggroUntil;
    private float nextAttackTime;
    private float nextSearchTime;
    private Health prey;
    private float boltHideTime;
    private float lungeTime = -10f;
    private float phase;
    private bool dead;

    /// <summary>Raised when it spits, with the point it hit. The network copy and the sounds listen.</summary>
    public event System.Action<Vector3> Attacked;

    /// <summary>The camp this creature belongs to; told when it is hit so the whole camp turns on the attacker.</summary>
    public NeutralCamp Camp { get; set; }

    public bool IsAlive => health != null && health.IsAlive;

    private void Awake()
    {
        health = GetComponent<Health>();
        restRotation = transform.rotation;
        phase = Random.value * 10f;

        if (visualRoot != null)
        {
            visualBasePosition = visualRoot.localPosition;
        }

        if (bolt != null)
        {
            bolt.enabled = false;
        }

        if (sightBlockers.value == 0)
        {
            sightBlockers = LayerMask.GetMask("Ground");
        }
    }

    private void Start()
    {
        restRotation = transform.rotation;
    }

    private void OnEnable()
    {
        health.Killed += OnKilled;
        GameEvents.DamageDealt += OnAnyDamage;
    }

    private void OnDisable()
    {
        health.Killed -= OnKilled;
        GameEvents.DamageDealt -= OnAnyDamage;
    }

    private void OnAnyDamage(IDamageable target, DamageInfo info)
    {
        if (!ReferenceEquals(target, health) || info.source == Faction.Neutral)
        {
            return;
        }

        if (Camp != null)
        {
            Camp.Alert(info.source);
        }
        else
        {
            Provoke(info.source);
        }
    }

    /// <summary>Turns the creature on the hero of <paramref name="side"/> for a few seconds. Host only.</summary>
    public void Provoke(Faction side)
    {
        if (dead || health.IsReplica || side == Faction.Neutral)
        {
            return;
        }

        if (Time.time >= aggroUntil)
        {
            // A beat before the first spit, so the player sees it turn before it hurts.
            nextAttackTime = Time.time + 0.6f + Random.value * 0.5f;
        }

        aggroSide = side;
        aggroUntil = Time.time + aggroSeconds;
    }

    private void Update()
    {
        if (dead)
        {
            return;
        }

        Animate();

        if (bolt != null && bolt.enabled && Time.time >= boltHideTime)
        {
            bolt.enabled = false;
        }

        if (health.IsReplica || Time.time >= aggroUntil || MatchManager.IsOver)
        {
            if (!health.IsReplica)
            {
                Turn(restRotation);
            }

            return;
        }

        if (Time.time >= nextSearchTime || prey == null)
        {
            nextSearchTime = Time.time + 0.5f;
            prey = FindHero(aggroSide);
        }

        if (prey == null || !prey.IsAlive)
        {
            return;
        }

        Vector3 from = MuzzlePoint;
        Vector3 to = prey.transform.position;
        Vector3 flat = to - transform.position;
        flat.y = 0f;

        if (flat.sqrMagnitude > 0.01f)
        {
            Turn(Quaternion.LookRotation(flat));
        }

        if (Time.time < nextAttackTime || Vector3.Distance(from, to) > attackRange
            || Physics.Linecast(from, to, sightBlockers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        nextAttackTime = Time.time + attackInterval;

        // It wears the hero down but never finishes them: a kill belongs to the duel, not to a camp.
        if (prey.CurrentHealth > attackDamage + 1f)
        {
            // A neutral source hurts nobody (see FactionUtility), so the hit is dealt as the other side's.
            DamageInfo info = new DamageInfo(attackDamage, FactionUtility.Opposite(prey.Faction), gameObject, DamageType.Minion)
                .AtImpact(to, (to - from).normalized);
            prey.TakeDamage(info);
        }

        ShowAttack(to);
        Attacked?.Invoke(to);
    }

    /// <summary>Draws a spit at <paramref name="point"/>: the bolt, the lunge and the sound. Used by both machines.</summary>
    public void ShowAttack(Vector3 point)
    {
        lungeTime = Time.time;

        if (health != null && health.IsReplica)
        {
            Vector3 flat = point - transform.position;
            flat.y = 0f;

            if (flat.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(flat);
            }
        }

        if (bolt != null)
        {
            bolt.positionCount = 2;
            bolt.SetPosition(0, MuzzlePoint);
            bolt.SetPosition(1, point);
            bolt.enabled = true;
            boltHideTime = Time.time + boltSeconds;
        }

        ExperienceCues.PlayAt("neutral_attack", MuzzlePoint, 0.5f);
    }

    private Vector3 MuzzlePoint => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.2f;

    private void Turn(Quaternion target)
    {
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 240f * Time.deltaTime);
    }

    /// <summary>A slow hover, and a quick push forward and back when it spits.</summary>
    private void Animate()
    {
        if (visualRoot == null)
        {
            return;
        }

        float hover = Mathf.Sin((Time.time + phase) * hoverSpeed) * hoverHeight;
        float t = (Time.time - lungeTime) / 0.3f;
        float lunge = t >= 0f && t < 1f ? Mathf.Sin(t * Mathf.PI) * 0.35f : 0f;
        visualRoot.localPosition = visualBasePosition + Vector3.up * hover + Vector3.forward * lunge;
    }

    /// <summary>The nearest living hero body of the given side.</summary>
    private Health FindHero(Faction side)
    {
        Health best = null;
        float bestDistance = float.MaxValue;

        foreach (Health candidate in FindObjectsOfType<Health>())
        {
            if (!candidate.IsAlive || candidate.Faction != side)
            {
                continue;
            }

            string layer = LayerMask.LayerToName(candidate.gameObject.layer);

            if (layer != "PlayerBody" && layer != "Rival")
            {
                continue;
            }

            float distance = (candidate.transform.position - transform.position).sqrMagnitude;

            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void OnKilled(DamageInfo info)
    {
        dead = true;

        if (bolt != null)
        {
            bolt.enabled = false;
        }

        foreach (Collider body in GetComponentsInChildren<Collider>())
        {
            body.enabled = false;
        }

        ExperienceCues.PlayAt("minion_death", transform.position + Vector3.up, 0.6f);

        // Souls are created once, by the host; the rival's copy receives them over the network.
        if (!health.IsReplica)
        {
            DropSouls(info.source == Faction.Neutral ? Faction.Player : info.source);
        }

        StartCoroutine(FadeOut());
    }

    private void DropSouls(Faction killer)
    {
        if (soulPrefab == null)
        {
            return;
        }

        int drops = Mathf.Max(1, soulDrops);
        int perDrop = Mathf.Max(1, soulValue / drops);

        for (int i = 0; i < drops; i++)
        {
            Vector3 offset = drops == 1 ? Vector3.zero : Quaternion.Euler(0f, 360f / drops * i, 0f) * Vector3.forward * 0.45f;
            Vector3 position = transform.position + offset + Vector3.up * 0.9f;

            MatchNet.Spawn(soulPrefab, position, Quaternion.identity, spawned =>
            {
                if (spawned.TryGetComponent(out SoulPickup pickup))
                {
                    pickup.SetValue(perDrop);
                    pickup.SetOwner(killer);
                }
            });

            GameEvents.RaiseSoulDropped(position);
        }
    }

    /// <summary>Fades the body out like a dying minion and, on the host, removes the creature.</summary>
    private IEnumerator FadeOut()
    {
        var parts = new List<(Renderer renderer, int index, MaterialPropertyBlock block)>();

        if (deathFadeMaterial != null)
        {
            foreach (Renderer renderer in (visualRoot != null ? visualRoot : transform).GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                Material[] originals = renderer.sharedMaterials;
                var faded = new Material[originals.Length];

                for (int i = 0; i < originals.Length; i++)
                {
                    faded[i] = deathFadeMaterial;
                    var block = new MaterialPropertyBlock();

                    if (originals[i] != null)
                    {
                        if (originals[i].HasProperty(BaseMapId) && originals[i].GetTexture(BaseMapId) != null)
                        {
                            block.SetTexture(BaseMapId, originals[i].GetTexture(BaseMapId));
                        }

                        block.SetColor(BaseColorId, originals[i].HasProperty(BaseColorId) ? originals[i].GetColor(BaseColorId) : Color.white);
                    }

                    parts.Add((renderer, i, block));
                }

                renderer.sharedMaterials = faded;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        float elapsed = 0f;

        while (elapsed < deathFadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / deathFadeDuration);

            foreach (var part in parts)
            {
                part.block.SetFloat(AlphaId, alpha);
                part.renderer.SetPropertyBlock(part.block, part.index);
            }

            yield return null;
        }

        // The host removes the networked creature; the copy stays invisible until that arrives.
        if (!health.IsReplica)
        {
            Destroy(gameObject);
        }
    }
}
