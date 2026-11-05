using TMPro;
using UnityEngine;

/// <summary>
/// The shop in each base (RF-11): a stand with one plaque per item. The player buys by shooting a
/// plaque, the same gesture that collects souls, so the shop needs no laser or menu in the middle
/// of the fight. Each plaque shows the price, what it does and how many the player owns.
/// </summary>
/// <remarks>
/// Only the kiosk of the player's own side sells; the rival's says so. Purchases are local (the
/// wallet is per player) and <see cref="PlayerUpgrades"/> sends what the host must know.
/// </remarks>
public class ShopKiosk : MonoBehaviour
{
    [Tooltip("Side whose base this kiosk stands in.")]
    [SerializeField] private Faction faction = Faction.Player;

    [SerializeField] private ShopItemSO[] items;

    [Tooltip("One label per item, same order: name, price, effect and stacks.")]
    [SerializeField] private TMP_Text[] itemLabels;

    [Tooltip("Line under the plaques: souls held, or why the last purchase failed.")]
    [SerializeField] private TMP_Text statusLabel;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip buyClip;
    [SerializeField] private AudioClip refuseClip;

    [SerializeField] private Color affordable = new Color(1f, 0.85f, 0.45f, 1f);
    [SerializeField] private Color tooExpensive = new Color(0.55f, 0.55f, 0.6f, 1f);
    [SerializeField] private Color owned = new Color(0.45f, 1f, 0.6f, 1f);

    [SerializeField] private float messageSeconds = 2.5f;

    private float messageUntil;

    /// <summary>Items on sale, in plaque order.</summary>
    public ShopItemSO[] Items => items;

    private bool IsMine => faction == MatchNet.LocalFaction;

    private void OnEnable()
    {
        GameEvents.WalletChanged += OnWalletChanged;
        Refresh();
    }

    private void OnDisable()
    {
        GameEvents.WalletChanged -= OnWalletChanged;
    }

    private void Update()
    {
        // MatchNet's side is only known once the room is joined; keep the text honest until then,
        // and put the souls line back when a purchase message expires.
        if (Time.frameCount % 15 == 0)
        {
            Refresh();
        }
    }

    private void OnWalletChanged(int souls) => Refresh();

    /// <summary>Buys item <paramref name="index"/>. Called by the plaque that was shot.</summary>
    public void Buy(int index)
    {
        if (items == null || index < 0 || index >= items.Length || items[index] == null)
        {
            return;
        }

        if (!IsMine)
        {
            Say("Esta es la tienda del rival.", refuseClip);
            return;
        }

        PlayerUpgrades upgrades = PlayerUpgrades.Instance;

        if (upgrades == null)
        {
            return;
        }

        ShopItemSO item = items[index];

        if (upgrades.TryBuy(item, out PlayerUpgrades.Refusal refusal))
        {
            Say($"¡{item.itemName}!", buyClip);
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.5f, 0.1f);
            return;
        }

        switch (refusal)
        {
            case PlayerUpgrades.Refusal.MaxedOut:
                Say("Ya tienes el máximo de ese objeto.", refuseClip);
                break;

            case PlayerUpgrades.Refusal.NotEnoughSouls:
                int missing = item.cost - (PlayerWallet.Instance != null ? PlayerWallet.Instance.Souls : 0);
                Say($"Te faltan {missing} almas.", refuseClip);
                break;
        }
    }

    private void Say(string message, AudioClip clip)
    {
        messageUntil = Time.time + messageSeconds;

        if (statusLabel != null)
        {
            statusLabel.text = message;
        }

        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }

        Refresh();
    }

    private void Refresh()
    {
        PlayerWallet wallet = PlayerWallet.Instance;
        PlayerUpgrades upgrades = PlayerUpgrades.Instance;
        int souls = wallet != null ? wallet.Souls : 0;

        if (statusLabel != null && Time.time >= messageUntil)
        {
            statusLabel.text = IsMine ? $"Tienes {souls} almas · dispara a un objeto para comprarlo" : "Tienda del rival";
        }

        if (items == null || itemLabels == null)
        {
            return;
        }

        for (int i = 0; i < items.Length && i < itemLabels.Length; i++)
        {
            ShopItemSO item = items[i];
            TMP_Text label = itemLabels[i];

            if (item == null || label == null)
            {
                continue;
            }

            int count = upgrades != null ? upgrades.Stacks(item) : 0;
            bool maxed = item.maxStacks > 0 && count >= item.maxStacks;
            string stacksText = item.maxStacks > 0 ? $"{count}/{item.maxStacks}" : count.ToString();
            string price = maxed ? "MÁXIMO" : $"{item.cost} almas";

            label.text = $"<b>{item.itemName}</b>\n<size=70%>{item.description}</size>\n{price}   <size=70%>{stacksText}</size>";
            label.color = maxed ? owned : (IsMine && souls >= item.cost ? affordable : tooExpensive);
        }
    }
}
