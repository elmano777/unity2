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
/// and weapon on the right side. Minions and damage are still local to each machine; syncing
/// them is Avance 3.
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

    private readonly NetworkVariable<Vector3> headPosition = Owned(Vector3.zero);
    private readonly NetworkVariable<Quaternion> headRotation = Owned(Quaternion.identity);
    private readonly NetworkVariable<Vector3> leftPosition = Owned(Vector3.zero);
    private readonly NetworkVariable<Quaternion> leftRotation = Owned(Quaternion.identity);
    private readonly NetworkVariable<Vector3> rightPosition = Owned(Vector3.zero);
    private readonly NetworkVariable<Quaternion> rightRotation = Owned(Quaternion.identity);
    private readonly NetworkVariable<int> heroIndex = Owned(-1);

    private OVRCameraRig rig;
    private GameObject weaponVisual;

    private static NetworkVariable<T> Owned<T>(T value) =>
        new NetworkVariable<T>(value, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>Host = Amber end, anyone who joins = Sapphire end.</summary>
    public Faction Side => OwnerClientId == NetworkManager.ServerClientId ? Faction.Player : Faction.Enemy;

    public override void OnNetworkSpawn()
    {
        heroIndex.OnValueChanged += OnHeroChanged;

        if (IsOwner)
        {
            rig = FindObjectOfType<OVRCameraRig>();
            visualRoot.SetActive(false);
            MoveRigToSpawn();
            ApplyLocalSide();
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
    }

    private void FollowRemotePose()
    {
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

        Follow(head, headPosition.Value, headRotation.Value, t);
        Follow(leftHand, leftPosition.Value, leftRotation.Value, t);
        Follow(rightHand, rightPosition.Value, rightRotation.Value, t);

        // The body hangs under the head and only turns with it around Y, like a torso would.
        if (body != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            Quaternion yaw = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : body.rotation;
            body.SetPositionAndRotation(head.position + Vector3.down * 0.75f, yaw);
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

        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            DestroyImmediate(behaviour);
        }

        foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(collider);
        }

        holder.SetActive(true);
        weaponVisual = holder;
    }
}
