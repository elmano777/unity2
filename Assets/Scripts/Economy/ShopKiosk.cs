using TMPro;
using UnityEngine;

/// <summary>
/// A shop booth in the street (RF-11). Its shop board sits behind a roll-up shutter: walking up to
/// the booth raises the shutter and the board can be shot as usual (<see cref="ShopBoard"/>);
/// walking away lowers it again, so the player buys on the way and keeps going. The tower booth
/// only trades while its side's first tower stands; once that falls it stays shut and the base
/// store is the only one left. The rival's booths never open for this player.
/// </summary>
public class ShopKiosk : MonoBehaviour
{
    [Tooltip("Side whose booth this is.")]
    [SerializeField] private Faction faction = Faction.Player;

    [Tooltip("The shop board inside the booth: hidden while the shutter is down.")]
    [SerializeField] private GameObject content;

    [Tooltip("Roll-up shutter in front of the board, pivoted at its top edge: rolls up by shrinking in Y.")]
    [SerializeField] private Transform shutter;
    [SerializeField] private float shutterOpenScale = 0.06f;

    [Tooltip("Horizontal metres from the booth at which the shutter goes up.")]
    [SerializeField] private float openRadius = 6f;

    [Tooltip("Booth beside a tower: closes for good when that tower falls.")]
    [SerializeField] private bool needsTower;
    [SerializeField] private int towerTier = 1;

    [Header("Feedback")]
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Light lamp;
    [SerializeField] private float lampOpenIntensity = 1.6f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;

    private const float ShutterSpeed = 2.2f;

    private float openAmount;
    private bool wasOpen;
    private bool towerSeen;
    private bool towerFallen;
    private Transform head;

    /// <summary>True while the shutter is (or is going) up.</summary>
    public bool IsOpen => wasOpen;

    /// <summary>False once the tower this booth depends on has fallen.</summary>
    public bool IsTrading => !towerFallen;

    private void Start()
    {
        ApplyShutter(0f);

        if (content != null)
        {
            content.SetActive(false);
        }
    }

    private void Update()
    {
        if (needsTower && !towerFallen)
        {
            WatchTower();
        }

        bool mine = faction == MatchNet.LocalFaction;
        bool near = PlayerDistance() <= openRadius;
        bool open = mine && !towerFallen && near;

        if (open && !wasOpen)
        {
            ExperienceCues.Play("shop_open", 0.6f);

            if (audioSource != null && openClip != null)
            {
                audioSource.PlayOneShot(openClip);
            }
        }

        wasOpen = open;
        openAmount = Mathf.MoveTowards(openAmount, open ? 1f : 0f, ShutterSpeed * Time.deltaTime);
        ApplyShutter(openAmount);

        if (content != null && content.activeSelf != openAmount > 0.02f)
        {
            content.SetActive(openAmount > 0.02f);
        }

        if (lamp != null)
        {
            lamp.intensity = Mathf.Lerp(lampOpenIntensity * 0.25f, lampOpenIntensity, openAmount);
        }

        if (statusLabel != null)
        {
            statusLabel.text = !mine ? "TIENDA DEL RIVAL"
                : towerFallen ? "CERRADA · TU TORRE CAYÓ"
                : open ? "ABIERTO"
                : "ACÉRCATE PARA COMPRAR";
        }
    }

    /// <summary>
    /// Finds this side's tower of <see cref="towerTier"/> among the standing structures. Towers are
    /// spawned when the duel starts (and practice keeps the player's own as scenery), so a booth
    /// that has never seen its tower stays open; one that saw it and then lost it closes.
    /// </summary>
    private void WatchTower()
    {
        bool found = false;

        foreach (Structure s in Structure.Standing)
        {
            if (s != null && s.Faction == faction && s.Tier == towerTier && !s.IsPatron && s.IsStanding)
            {
                found = true;
                break;
            }
        }

        if (found)
        {
            towerSeen = true;
        }
        else if (towerSeen)
        {
            towerFallen = true;
            ExperienceCues.Play("shop_closed", 0.6f);
        }
    }

    private float PlayerDistance()
    {
        if (head == null)
        {
            Camera cam = Camera.main;

            if (cam == null)
            {
                return float.MaxValue;
            }

            head = cam.transform;
        }

        Vector3 d = head.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    private void ApplyShutter(float amount)
    {
        if (shutter == null)
        {
            return;
        }

        Vector3 s = shutter.localScale;
        s.y = Mathf.Lerp(1f, shutterOpenScale, Mathf.SmoothStep(0f, 1f, amount));
        shutter.localScale = s;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, openRadius);
    }
}
