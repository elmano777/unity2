using TMPro;
using UnityEngine;

/// <summary>
/// The shop in each base (RF-11), laid out like the shop of the game it is based on: a paper board
/// with a coloured tab per category on the side, the items of the open tab grouped in blocks by
/// tier with the price on each block, and a dark panel with the details of the chosen item.
/// </summary>
/// <remarks>
/// Everything is bought by shooting, the same gesture that collects souls, so there is no laser or
/// menu in the middle of the fight. Shooting a tab opens it; the first shot at an item selects it
/// and shows what it does, and a second shot within <see cref="confirmWindow"/> buys it. A held
/// trigger only selects (<see cref="ShopCard"/> ignores the rest of a burst), so automatic fire
/// never buys by accident. Only the board of the player's own side sells; the rival's is sealed.
/// The board is local to each machine: the wallet is per player and <see cref="PlayerUpgrades"/>
/// sends the host what it must apply (health and armour).
/// </remarks>
public class ShopBoard : MonoBehaviour
{
    [Tooltip("Side whose base this board stands in.")]
    [SerializeField] private Faction faction = Faction.Player;

    [SerializeField] private ShopItemSO[] items;

    [Tooltip("One card per item, same order as the items.")]
    [SerializeField] private ShopCard[] cards;

    [Tooltip("One page per tab (the blocks and cards of that category), same order as the tabs.")]
    [SerializeField] private GameObject[] pages;

    [SerializeField] private ShopTab[] tabs;

    [Header("Labels")]
    [SerializeField] private TMP_Text soulsLabel;
    [SerializeField] private TMP_Text detailTitle;
    [SerializeField] private TMP_Text detailBody;
    [SerializeField] private TMP_Text detailHint;

    [Tooltip("Big icon of the selected item in the detail panel; hidden while nothing is selected.")]
    [SerializeField] private Renderer detailIcon;

    [Tooltip("Icon material per item, same order as the items.")]
    [SerializeField] private Material[] iconMaterials;

    [Tooltip("Covers the cards on the rival's board.")]
    [SerializeField] private GameObject sealedOverlay;

    [Header("Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip selectClip;
    [SerializeField] private AudioClip buyClip;
    [SerializeField] private AudioClip refuseClip;

    [Header("Timing")]
    [Tooltip("Seconds after selecting an item in which a second shot buys it.")]
    [SerializeField] private float confirmWindow = 2f;

    [Tooltip("Seconds a purchase or refusal message stays in the detail panel.")]
    [SerializeField] private float messageSeconds = 2.5f;

    private int activeTab;
    private int selected = -1;
    private float armedUntil;
    private float messageUntil;
    private string message;
    private bool? wasMine;

    /// <summary>Items on sale, in card order.</summary>
    public ShopItemSO[] Items => items;

    /// <summary>The item shown in the detail panel, or -1.</summary>
    public int Selected => selected;

    /// <summary>The open tab.</summary>
    public int ActiveTab => activeTab;

    /// <summary>True when this is the board of the local player's side, the only one that sells.</summary>
    public bool IsMine => faction == MatchNet.LocalFaction;

    private void OnEnable()
    {
        GameEvents.WalletChanged += OnWalletChanged;
        ShowTab(activeTab);
    }

    private void OnDisable()
    {
        GameEvents.WalletChanged -= OnWalletChanged;
    }

    private void Update()
    {
        // MatchNet's side is only known once the room is joined, and messages and the confirm
        // window run out on their own: keep the board honest a few times a second.
        if (Time.frameCount % 10 == 0)
        {
            Refresh();
        }
    }

    private void OnWalletChanged(int souls) => Refresh();

    /// <summary>Opens tab <paramref name="index"/>. Called by the tab that was shot.</summary>
    public void ShowTab(int index)
    {
        if (pages == null || pages.Length == 0)
        {
            return;
        }

        int previousTab = activeTab;
        activeTab = Mathf.Clamp(index, 0, pages.Length - 1);

        // A page-turn sound when a tab is shot (not when the board first turns on).
        if (activeTab != previousTab && Time.timeSinceLevelLoad > 2f)
        {
            // One sound per category panel (weapon, vitality, spirit), in the same order as the tabs.
            string[] panels = { "shop_tab_weapon", "shop_tab_vitality", "shop_tab_spirit" };
            ExperienceCues.Play(activeTab < panels.Length ? panels[activeTab] : "shop_tab_weapon", 0.6f);
        }

        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null)
            {
                pages[i].SetActive(i == activeTab);
            }
        }

        if (tabs != null)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] != null)
                {
                    tabs[i].SetOpen(i == activeTab);
                }
            }
        }

        // The selection belongs to the page it was made on.
        if (selected >= 0 && !IsOnActivePage(selected))
        {
            selected = -1;
            armedUntil = 0f;
        }

        Refresh();
    }

    /// <summary>
    /// A shot reached a card: the first one selects the item, a second one within the confirm
    /// window buys it.
    /// </summary>
    public void OnCardShot(int index)
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

        if (selected == index && Time.time <= armedUntil)
        {
            Buy(index);
            return;
        }

        selected = index;
        armedUntil = Time.time + confirmWindow;
        messageUntil = 0f;

        // A soft page sound when an item is picked; the clip set on the board is the fallback.
        string hover = "shop_select";

        if (ExperienceCues.Clip(hover) != null)
        {
            ExperienceCues.Play(hover, 0.6f);
        }
        else
        {
            Play(selectClip);
        }

        HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.2f, 0.05f);
        Refresh();
    }

    private void Buy(int index)
    {
        PlayerUpgrades upgrades = PlayerUpgrades.Instance;

        if (upgrades == null)
        {
            return;
        }

        ShopItemSO item = items[index];
        armedUntil = 0f;

        if (upgrades.TryBuy(item, out PlayerUpgrades.Refusal refusal))
        {
            Say($"¡Comprado: {item.itemName}!", ExperienceCues.Clip("shop_buy") != null ? null : buyClip);
            ExperienceCues.Play("shop_buy", 0.7f);
            HapticFeedback.Pulse(HapticFeedback.Hand.Right, 0.6f, 0.12f);
            return;
        }

        switch (refusal)
        {
            case PlayerUpgrades.Refusal.MaxedOut:
                Say("Ya tienes el máximo de este objeto.", refuseClip);
                break;

            case PlayerUpgrades.Refusal.NotEnoughSouls:
                int missing = item.cost - (PlayerWallet.Instance != null ? PlayerWallet.Instance.Souls : 0);
                Say($"Te faltan {missing} almas.", refuseClip);
                break;
        }
    }

    private void Say(string text, AudioClip clip)
    {
        message = text;
        messageUntil = Time.time + messageSeconds;
        Play(clip);
        Refresh();
    }

    private void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private bool IsOnActivePage(int index)
    {
        return cards != null && index < cards.Length && cards[index] != null && pages != null
            && activeTab < pages.Length && pages[activeTab] != null
            && cards[index].transform.IsChildOf(pages[activeTab].transform);
    }

    private void Refresh()
    {
        PlayerWallet wallet = PlayerWallet.Instance;
        PlayerUpgrades upgrades = PlayerUpgrades.Instance;
        int souls = wallet != null ? wallet.Souls : 0;
        bool mine = IsMine;
        bool armed = selected >= 0 && Time.time <= armedUntil;

        if (wasMine != mine)
        {
            wasMine = mine;

            if (sealedOverlay != null)
            {
                sealedOverlay.SetActive(!mine);
            }
        }

        if (soulsLabel != null)
        {
            soulsLabel.text = mine ? souls.ToString() : "—";
        }

        if (cards != null && items != null)
        {
            for (int i = 0; i < cards.Length && i < items.Length; i++)
            {
                ShopItemSO item = items[i];

                if (cards[i] == null || item == null)
                {
                    continue;
                }

                int count = upgrades != null ? upgrades.Stacks(item) : 0;
                bool maxed = item.maxStacks > 0 && count >= item.maxStacks;
                cards[i].Show(count, maxed, mine && souls >= item.cost, i == selected, i == selected && armed);
            }
        }

        RefreshDetail(upgrades, souls, mine, armed);
    }

    private void RefreshDetail(PlayerUpgrades upgrades, int souls, bool mine, bool armed)
    {
        bool showMessage = Time.time < messageUntil && !string.IsNullOrEmpty(message);
        bool hasItem = mine && selected >= 0 && items != null && selected < items.Length && items[selected] != null;

        if (detailIcon != null)
        {
            detailIcon.gameObject.SetActive(hasItem);

            if (hasItem && iconMaterials != null && selected < iconMaterials.Length && iconMaterials[selected] != null)
            {
                detailIcon.sharedMaterial = iconMaterials[selected];
            }
        }

        if (!mine)
        {
            SetDetail("TIENDA DEL RIVAL", "Aquí no puedes comprar.\nTu tienda está en tu base.", showMessage ? message : "");
            return;
        }

        if (!hasItem)
        {
            SetDetail("ELIGE UN OBJETO",
                "Dispara a un objeto para ver qué hace.\nDispara a una pestaña para cambiar de categoría.",
                showMessage ? message : $"Tienes {souls} almas");
            return;
        }

        ShopItemSO item = items[selected];
        int count = upgrades != null ? upgrades.Stacks(item) : 0;
        bool maxed = item.maxStacks > 0 && count >= item.maxStacks;
        string stacks = item.maxStacks > 0 ? $"Tienes {count} de {item.maxStacks}" : $"Tienes {count}";
        string body = $"{item.description}\n\n<size=85%>{item.cost} almas\n{stacks}</size>";

        string hint;

        if (showMessage)
        {
            hint = message;
        }
        else if (maxed)
        {
            hint = "Ya tienes el máximo";
        }
        else if (souls < item.cost)
        {
            hint = $"Te faltan {item.cost - souls} almas";
        }
        else
        {
            hint = armed ? "¡Dispara otra vez para comprar!" : "Dispara dos veces para comprar";
        }

        SetDetail(item.itemName.ToUpperInvariant(), body, hint);
    }

    private void SetDetail(string title, string body, string hint)
    {
        if (detailTitle != null) detailTitle.text = title;
        if (detailBody != null) detailBody.text = body;
        if (detailHint != null) detailHint.text = hint;
    }
}
