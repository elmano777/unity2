using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One per connected player. The owner copies its headset and controller poses into network
/// variables; the other machine draws them as the rival: a body under the head, two hands and
/// the rival hero's weapon (RF-09).
/// </summary>
/// <remarks>
/// The host fights from the Amber end (Faction.Player) and the joining player from the Sapphire
/// end (Faction.Enemy), so the owner also moves its own rig to its spawn point and puts its body
/// and weapon on the right side. The host runs the minions, towers, souls and damage (Avance 3);
/// each player's body here is a capsule under the head with hit points the host owns, so minions
/// can hurt it and the rival's shots can land on it.
/// </remarks>
public class NetworkPlayer : NetworkBehaviour
{
    [Tooltip("Heroes in the same order on every machine; the selected one is sent as an index.")]
    [SerializeField] private HeroDefinitionSO[] heroes;

    [Header("Rival visuals (hidden for the owner)")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Transform head;
    [SerializeField] private Transform body;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private TMPro.TextMeshPro nameLabel;

    [Tooltip("How fast the rival's visuals catch up with the received pose. Higher = snappier.")]
    [SerializeField] private float smoothing = 18f;

    [Header("Hero body")]
    [Tooltip("Primitive head and body, hidden once the hero's model stands in for them.")]
    [SerializeField] private Renderer[] placeholders;

    [Tooltip("Height the hero's model is scaled to, feet to the top of the head, in metres.")]
    [SerializeField] private float heroHeight = 1.85f;

    [Tooltip("Layers the hero's feet are put down on, straight below the rival's head.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Body (what minions, towers and the rival can hit)")]
    [Tooltip("Hit points of this player, replicated by NetworkHealth. Lives on the Hitbox child.")]
    [SerializeField] private Health bodyHealth;

    [Tooltip("Capsule that follows the head, a little below it, so the player has a body to shoot at.")]
    [SerializeField] private Transform bodyHitbox;

    [SerializeField] private float maxHealth = 300f;

    [Tooltip("Seconds a player stays down before coming back at their end of the lane.")]
    [SerializeField] private float respawnSeconds = 5f;

    private readonly NetworkVariable<Vector3> headPosition = Owned(Vector3.zero);
    private readonly NetworkVariable<Quaternion> headRotation = Owned(Quaternion.identity);
    private readonly NetworkVariable<Vector3> leftPosition = Owned(Vector3.zero);
    private readonly NetworkVariable<Quaternion> leftRotation = Owned(Quaternion.identity);
    private readonly NetworkVariable<Vector3> rightPosition = Owned(Vector3.zero);
    private readonly NetworkVariable<Quaternion> rightRotation = Owned(Quaternion.identity);
    private readonly NetworkVariable<int> heroIndex = Owned(-1);

    private OVRCameraRig rig;
    private GameObject weaponVisual;
    private Transform heroBody;

    private static NetworkVariable<T> Owned<T>(T value) =>
        new NetworkVariable<T>(value, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>Host = Amber end, anyone who joins = Sapphire end.</summary>
    public Faction Side => OwnerClientId == NetworkManager.ServerClientId ? Faction.Player : Faction.Enemy;

    public override void OnNetworkSpawn()
    {
        heroIndex.OnValueChanged += OnHeroChanged;

        SetUpBody();

        if (IsOwner)
        {
            rig = FindObjectOfType<OVRCameraRig>();
            visualRoot.SetActive(false);
            MoveRigToSpawn();
            ApplyLocalSide();
            ReplaceRigBody();
            HookOwnWeapons();
            heroIndex.Value = FindHeroIndex();
        }
        else
        {
            ShowHero(heroIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        heroIndex.OnValueChanged -= OnHeroChanged;
        UnhookOwnWeapons();

        if (bodyHealth != null)
        {
            bodyHealth.HealthChanged -= OnBodyHealthChanged;
            bodyHealth.Killed -= OnBodyKilled;
        }
    }

    /// <summary>
    /// Gives the player a body to hit. The host decides its side and hit points (NetworkHealth then
    /// copies them to the other screen); the layer tells the weapons apart: your own body is on
    /// PlayerBody, which your gun ignores, and the rival's is on Rival, which it hits.
    /// </summary>
    private void SetUpBody()
    {
        if (bodyHealth == null)
        {
            return;
        }

        int layer = LayerMask.NameToLayer(IsOwner ? "PlayerBody" : "Rival");

        if (layer >= 0)
        {
            bodyHealth.gameObject.layer = layer;
        }

        if (IsServer)
        {
            bodyHealth.Configure(Side, maxHealth);
            bodyHealth.Killed += OnBodyKilled;
        }

        bodyHealth.HealthChanged += OnBodyHealthChanged;
    }

    /// <summary>The Lane's own PlayerBody (the camera capsule from the single-player scene) would be a second body.</summary>
    private void ReplaceRigBody()
    {
        if (rig == null)
        {
            return;
        }

        foreach (Health legacy in rig.GetComponentsInChildren<Health>(true))
        {
            if (legacy.TryGetComponent(out Collider collider))
            {
                collider.enabled = false;
            }
        }
    }

    private void OnBodyHealthChanged(float current, float max)
    {
        bool alive = bodyHealth.IsAlive;

        if (IsOwner)
        {
            // Nobody shoots while down.
            if (rig != null)
            {
                foreach (Weapon weapon in rig.GetComponentsInChildren<Weapon>(true))
                {
                    weapon.enabled = alive;
                }
            }
        }
        else if (visualRoot != null)
        {
            visualRoot.SetActive(alive);
        }
    }

    private void OnBodyKilled(DamageInfo info)
    {
        StartCoroutine(RespawnAfterDelay());
    }

    private System.Collections.IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnSeconds);

        if (!IsSpawned)
        {
            yield break;
        }

        bodyHealth.Revive();
        RespawnClientRpc();
    }

    [ClientRpc]
    private void RespawnClientRpc()
    {
        if (IsOwner)
        {
            MoveRigToSpawn();
        }
    }

    // ---- Shots: the rival sees a tracer when the other player fires ----

    private Weapon[] ownWeapons = System.Array.Empty<Weapon>();

    private void HookOwnWeapons()
    {
        if (rig == null)
        {
            return;
        }

        ownWeapons = rig.GetComponentsInChildren<Weapon>(true);

        foreach (Weapon weapon in ownWeapons)
        {
            weapon.Shot += OnOwnShot;
        }
    }

    private void UnhookOwnWeapons()
    {
        foreach (Weapon weapon in ownWeapons)
        {
            if (weapon != null)
            {
                weapon.Shot -= OnOwnShot;
            }
        }
    }

    private void OnOwnShot(Vector3 origin, Vector3 end)
    {
        if (IsSpawned)
        {
            ShotServerRpc(origin, end);
        }
    }

    // Cosmetic and frequent: a lost tracer is not worth waiting for.
    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void ShotServerRpc(Vector3 origin, Vector3 end)
    {
        ShotClientRpc(origin, end);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ShotClientRpc(Vector3 origin, Vector3 end)
    {
        if (!IsOwner)
        {
            DrawRivalTracer(origin, end);
        }
    }

    private static Material tracerMaterial;

    private static void DrawRivalTracer(Vector3 origin, Vector3 end)
    {
        if (tracerMaterial == null)
        {
            tracerMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.7f, 0.25f, 0.9f) };
        }

        var tracer = new GameObject("RivalTracer");
        LineRenderer line = tracer.AddComponent<LineRenderer>();
        line.material = tracerMaterial;
        line.positionCount = 2;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        line.widthMultiplier = 0.025f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(tracer, 0.06f);
    }

    private void PlaceHitbox(Vector3 headWorld)
    {
        if (bodyHitbox != null)
        {
            bodyHitbox.position = headWorld + Vector3.down * 0.35f;
        }
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsOwner)
        {
            WriteLocalPose();
        }
        else
        {
            FollowRemotePose();
        }
    }

    private void WriteLocalPose()
    {
        if (rig == null)
        {
            return;
        }

        Transform left = rig.leftControllerAnchor != null ? rig.leftControllerAnchor : rig.leftHandAnchor;
        Transform right = rig.rightControllerAnchor != null ? rig.rightControllerAnchor : rig.rightHandAnchor;

        headPosition.Value = rig.centerEyeAnchor.position;
        headRotation.Value = rig.centerEyeAnchor.rotation;
        leftPosition.Value = left.position;
        leftRotation.Value = left.rotation;
        rightPosition.Value = right.position;
        rightRotation.Value = right.rotation;

        PlaceHitbox(rig.centerEyeAnchor.position);
    }

    private void FollowRemotePose()
    {
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

        Follow(head, headPosition.Value, headRotation.Value, t);
        Follow(leftHand, leftPosition.Value, leftRotation.Value, t);
        Follow(rightHand, rightPosition.Value, rightRotation.Value, t);
        PlaceHitbox(head.position);

        // The body hangs under the head and only turns with it around Y, like a torso would.
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);

        if (body != null)
        {
            Quaternion yaw = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : body.rotation;
            body.SetPositionAndRotation(head.position + Vector3.down * 0.75f, yaw);
        }

        // The hero stands on the floor under the head rather than hanging from it, so it stays
        // upright and full height while the player leans or crouches.
        if (heroBody != null)
        {
            Quaternion yaw = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : heroBody.rotation;
            heroBody.SetPositionAndRotation(FloorUnder(head.position), yaw);
        }

        // Hero name floats over the rival's head and always faces this player.
        Camera viewer = Camera.main;

        if (nameLabel != null && viewer != null)
        {
            Vector3 labelPosition = head.position + Vector3.up * 0.35f;
            Vector3 away = labelPosition - viewer.transform.position;
            nameLabel.transform.SetPositionAndRotation(labelPosition,
                away.sqrMagnitude > 0.001f ? Quaternion.LookRotation(away) : nameLabel.transform.rotation);
        }
    }

    private Vector3 FloorUnder(Vector3 headPosition)
    {
        if (Physics.Raycast(headPosition, Vector3.down, out RaycastHit hit, 3f, groundMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        // No floor found: assume someone of the hero's height standing up.
        return headPosition + Vector3.down * (heroHeight - 0.15f);
    }

    private static void Follow(Transform target, Vector3 position, Quaternion rotation, float t)
    {
        if (target == null)
        {
            return;
        }

        target.SetPositionAndRotation(
            Vector3.Lerp(target.position, position, t),
            Quaternion.Slerp(target.rotation, rotation, t));
    }

    private void MoveRigToSpawn()
    {
        if (rig == null)
        {
            return;
        }

        string spawnName = Side == Faction.Player ? "SpawnPoint_Amber" : "SpawnPoint_Sapphire";
        GameObject spawn = GameObject.Find(spawnName);

        if (spawn == null)
        {
            Debug.LogWarning($"[NetworkPlayer] {spawnName} not found; staying where the rig is.");
            return;
        }

        // Keep the rig's height: PlayerSpawnHeight owns Y while tracking settles.
        Vector3 position = spawn.transform.position;
        position.y = rig.transform.position.y;
        rig.transform.SetPositionAndRotation(position, spawn.transform.rotation);
    }

    /// <summary>The joining player fights for Sapphire: their body and gun switch side locally.</summary>
    private void ApplyLocalSide()
    {
        if (Side == Faction.Player || rig == null)
        {
            return;
        }

        foreach (Health health in rig.GetComponentsInChildren<Health>(true))
        {
            health.Configure(Faction.Enemy, health.MaxHealth);
        }

        foreach (Weapon weapon in rig.GetComponentsInChildren<Weapon>(true))
        {
            weapon.Owner = Faction.Enemy;
        }
    }

    private int FindHeroIndex()
    {
        HeroDefinitionSO selected = GameSession.Instance != null ? GameSession.Instance.SelectedHero : null;
        int index = selected != null ? System.Array.IndexOf(heroes, selected) : -1;

        // Straight into the Lane from the editor: show the first hero rather than a bare body.
        return index >= 0 ? index : 0;
    }

    private void OnHeroChanged(int previous, int current)
    {
        if (!IsOwner)
        {
            ShowHero(current);
        }
    }

    private void ShowHero(int index)
    {
        if (heroes == null || index < 0 || index >= heroes.Length || heroes[index] == null)
        {
            return;
        }

        HeroDefinitionSO hero = heroes[index];

        if (nameLabel != null)
        {
            nameLabel.text = hero.heroName;
        }

        ShowHeroBody(hero);

        if (weaponVisual != null)
        {
            Destroy(weaponVisual);
        }

        if (hero.weaponPrefab == null || rightHand == null)
        {
            return;
        }

        // Built under an inactive holder so the copy's Weapon never enables: otherwise it would
        // listen to this machine's trigger and fire the rival's gun.
        var holder = new GameObject("RivalWeapon");
        holder.SetActive(false);
        holder.transform.SetParent(rightHand, false);

        GameObject copy = Instantiate(hero.weaponPrefab, holder.transform);
        MakeVisualOnly(copy);

        holder.SetActive(true);
        weaponVisual = holder;
    }

    /// <summary>
    /// Stands the hero's model in for the primitive head and body. The models are static (no
    /// skeleton), so the body only turns with the head while the synced hands and gun move on
    /// their own. The model's own gun is hidden so the rival doesn't carry two.
    /// </summary>
    private void ShowHeroBody(HeroDefinitionSO hero)
    {
        if (heroBody != null)
        {
            Destroy(heroBody.gameObject);
            heroBody = null;
        }

        bool hasModel = hero.previewModel != null;

        foreach (Renderer placeholder in placeholders)
        {
            if (placeholder != null)
            {
                placeholder.enabled = !hasModel;
            }
        }

        if (!hasModel)
        {
            return;
        }

        var holder = new GameObject("RivalHero");
        holder.SetActive(false);
        holder.transform.SetParent(visualRoot.transform, false);

        GameObject copy = Instantiate(hero.previewModel, holder.transform);
        MakeVisualOnly(copy);

        foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.EndsWith(".gun"))
            {
                renderer.enabled = false;
            }
        }

        holder.SetActive(true);

        // Scale to heroHeight and move the model so its feet are centred on the holder.
        Bounds bounds = new Bounds();
        bool first = true;

        foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled)
            {
                continue;
            }

            if (first)
            {
                bounds = renderer.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!first && bounds.size.y > 0.01f)
        {
            float scale = heroHeight / bounds.size.y;
            Vector3 feet = holder.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            copy.transform.localScale *= scale;
            copy.transform.localPosition = (copy.transform.localPosition - feet) * scale;
        }

        heroBody = holder.transform;
    }

    /// <summary>Strips a copied prefab down to what it looks like: no scripts, nothing to hit.</summary>
    private static void MakeVisualOnly(GameObject copy)
    {
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            DestroyImmediate(behaviour);
        }

        foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(collider);
        }
    }
}
