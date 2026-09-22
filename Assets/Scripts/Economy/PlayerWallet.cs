using UnityEngine;

/// <summary>
/// The player's soul balance — the currency the whole progression loop runs on (RF-07, RF-11).
/// Souls come in from <see cref="SoulPickup"/> and go out at the shop.
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    [Tooltip("Souls the player starts a ritual with.")]
    [SerializeField] private int startingSouls;

    /// <summary>Souls currently held.</summary>
    public int Souls { get; private set; }

    /// <summary>Running total collected this match, including souls already spent.</summary>
    public int LifetimeSouls { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        Souls = startingSouls;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        GameEvents.RaiseWalletChanged(Souls);
    }

    /// <summary>Adds souls and tells the HUD and the stats recorder about it.</summary>
    public void Add(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Souls += amount;
        LifetimeSouls += amount;

        GameEvents.RaiseSoulsCollected(amount);
        GameEvents.RaiseWalletChanged(Souls);
    }

    /// <summary>True when the player could afford something costing <paramref name="cost"/>.</summary>
    public bool CanAfford(int cost) => Souls >= cost;

    /// <summary>
    /// Deducts <paramref name="cost"/> if the player can afford it.
    /// Returns false and changes nothing otherwise, so callers can show a "not enough souls" hint.
    /// </summary>
    public bool TrySpend(int cost)
    {
        if (cost < 0 || !CanAfford(cost))
        {
            return false;
        }

        Souls -= cost;
        GameEvents.RaiseWalletChanged(Souls);
        return true;
    }
}
