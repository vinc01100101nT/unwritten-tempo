using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A UI slot that holds one <see cref="Item"/>. A bag slot (accepts = None) takes
/// anything; an equipment slot only takes items whose <see cref="Item.slot"/>
/// matches. Shows the item's icon, or an optional greyed placeholder when empty.
/// Dropping a <see cref="DraggableItem"/> here moves/swaps the item.
/// </summary>
[RequireComponent(typeof(Image))]
public class ItemSlot : MonoBehaviour, IDropHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("None = bag slot (holds any item). Otherwise only items of this type fit.")]
    public EquipSlotType accepts = EquipSlotType.None;

    public Image iconImage;

    [Tooltip("Shown when the slot is empty (e.g. a greyed equip-slot silhouette). Optional.")]
    public Sprite emptyPlaceholder;
    [Tooltip("Tint for the empty-slot placeholder — a faint gold watermark, not a full-opacity icon.")]
    public Color emptyTint = new Color(0.784f, 0.659f, 0.384f, 0.16f);

    [Header("Visual channels (optional — wired by the HUD widget builder; safe to leave null)")]
    [Tooltip("Border Image whose COLOR = rarity (the sole rarity signal).")]
    public Image rarityBorder;
    [Tooltip("Soft rarity glow (Legendary etc.); tinted by rarity, hidden for Common.")]
    public Image rarityGlow;
    [Tooltip("\"+N\" enhancement badge; hidden at +0. Never tints the rarity border.")]
    public TMP_Text upgradeBadge;
    [Tooltip("Card sockets as ◇ (filled) / □ (empty) in a NEUTRAL color; hidden when no sockets.")]
    public TMP_Text cardPips;
    [Tooltip("Cyan INNER ring shown while this slot is selected (never a rarity color).")]
    public GameObject selectRing;
    [Tooltip("Cyan OUTER bloom shown while hovered (never a rarity color).")]
    public GameObject hoverGlow;
    [Tooltip("Procedural gold slot-silhouette (SlotWatermark); shown only while empty AND no override sprite is set.")]
    public GameObject emptyWatermark;
    [Tooltip("Green halo shown while this slot is a valid drop target during a drag (SlotDragHandler).")]
    public GameObject dropHighlight;

    [SerializeField] Item item;                  // serialized so seeded gear survives into Play
    public Item Item => item;

    // Present for equipment carrying refine/cards; null for plain bag items (potions/materials).
    [SerializeField] EquipmentInstance instance;
    public EquipmentInstance Instance => instance;

    /// <summary>For bag slots in the new windows: the live <see cref="InventoryStore.Entry"/> this
    /// cell currently shows (set each refresh). null on equip slots and empty cells. SlotDragHandler
    /// uses it to reorder/equip against the store rather than mutating slot <see cref="Item"/>s.</summary>
    public InventoryStore.Entry boundEntry;

    // Neutral pip color — card slots are NEVER rarity-tinted (locked channel rule).
    static readonly Color PipColor = new Color(0.62f, 0.66f, 0.72f, 1f);
    // Faint cool slate frame for an empty slot (cooler than the warm rarity tints, fits the charcoal theme).
    static readonly Color EmptyBorderColor = new Color(0.32f, 0.34f, 0.40f, 1f);

    // ---- New-window interaction hooks (set by the Inventory/Equipment windows) ----
    /// <summary>If set, REPLACES the built-in (old-bag) click handling — the new windows route
    /// clicks here instead of the legacy drag/equip/consume logic.</summary>
    public System.Action<PointerEventData> clickOverride;
    /// <summary>When true, hovering an equipment instance shows the live equip→stat-delta preview
    /// against <see cref="previewSlot"/> (instead of the plain item tooltip).</summary>
    public bool previewOnHover;
    public EquipSlotType previewSlot;

    /// <summary>Raised whenever the held item changes (drag/swap/consume). Equip slots
    /// listen to push the new item into <see cref="Character"/>.</summary>
    public event System.Action Changed;

    void Awake()
    {
        if (hoverGlow != null) hoverGlow.SetActive(false);
        if (selectRing != null) selectRing.SetActive(false);
        if (dropHighlight != null) dropHighlight.SetActive(false);
        Refresh();
    }

    // Closing a window (X / B-V-C toggle / Esc) deactivates its slots. uGUI does NOT fire
    // OnPointerExit on a slot that's disabled out from under the cursor, so a tooltip raised by
    // OnPointerEnter would otherwise stay stuck on the always-on tooltip layer. Clear our hover
    // visuals and hide the shared tooltip on disable so closing always dismisses it.
    void OnDisable()
    {
        if (hoverGlow != null) hoverGlow.SetActive(false);
        if (dropHighlight != null) dropHighlight.SetActive(false);
        ItemTooltip.HideTip();
    }

    public bool Accepts(Item item)
    {
        if (item == null) return true;
        if (accepts == EquipSlotType.None) return true;   // bag holds anything
        return item.slot == accepts;
    }

    public void SetItem(Item newItem)
    {
        item = newItem;
        instance = null;             // a plain item clears any prior instance data
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Place a full equipment instance (def + refine + cards) — drives the +N badge and
    /// card pips on top of the icon/rarity. Use <see cref="SetItem"/> for plain bag items.</summary>
    public void SetInstance(EquipmentInstance inst)
    {
        instance = inst;
        item = inst != null ? inst.def : null;
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Selection state = cyan inner ring (set by the owning window; never a rarity color).</summary>
    public void SetSelected(bool on)
    {
        if (selectRing != null) selectRing.SetActive(on);
    }

    void Refresh()
    {
        if (iconImage != null)
        {
            if (Item != null && Item.icon != null)
            {
                iconImage.sprite = Item.icon;
                iconImage.color = Color.white;
                iconImage.enabled = true;
            }
            else if (emptyPlaceholder != null)
            {
                iconImage.sprite = emptyPlaceholder;
                iconImage.color = emptyTint;   // faint gold watermark — slot identity without reading as a real item
                iconImage.enabled = true;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }
        }
        // Procedural slot silhouette: shown only when truly empty AND no override sprite was wired
        // (a real emptyPlaceholder sprite takes precedence — same drop-in pattern as the rest of the kit).
        if (emptyWatermark != null)
            emptyWatermark.SetActive(Item == null && emptyPlaceholder == null);
        RefreshChannels();
    }

    // Drive the independent visual channels — border=rarity, +N=enhancement, ◇=card slots — from
    // whatever is held. Each ref is optional, so a slot wired only with an icon still works.
    void RefreshChannels()
    {
        bool has = item != null;
        Rarity rarity = instance != null ? instance.Rarity : (item != null ? item.rarity : Rarity.Common);

        if (rarityBorder != null)
        {
            // Keep the frame drawn (and raycastable) even when empty so the cell reads as a slot and can be
            // selected/dropped onto — a faint cool slate when empty, the rarity color once filled.
            rarityBorder.enabled = true;
            rarityBorder.color = has ? Rarities.Border(rarity) : EmptyBorderColor;
        }
        if (rarityGlow != null)
        {
            Color g = has ? Rarities.Glow(rarity) : new Color(0f, 0f, 0f, 0f);
            rarityGlow.color = g;
            rarityGlow.enabled = has && g.a > 0f;
        }
        if (upgradeBadge != null)
        {
            int up = instance != null ? instance.upgrade : 0;
            bool show = up > 0;
            upgradeBadge.gameObject.SetActive(show);
            if (show) upgradeBadge.text = "+" + up;
        }
        if (cardPips != null)
        {
            int slots = instance != null ? instance.CardSlots : (item != null ? item.cardSlots : 0);
            bool show = slots > 0;
            cardPips.gameObject.SetActive(show);
            if (show)
            {
                cardPips.color = PipColor;
                cardPips.text = Pips(instance != null ? instance.FilledSockets : 0, slots);
            }
        }
    }

    // ◇ = filled socket, □ = empty (shapes distinguish them; color stays neutral).
    static string Pips(int filled, int slots)
    {
        var sb = new System.Text.StringBuilder(slots);
        for (int i = 0; i < slots; i++) sb.Append(i < filled ? '◇' : '□');
        return sb.ToString();
    }

    public void OnDrop(PointerEventData e)
    {
        var dragged = e.pointerDrag != null ? e.pointerDrag.GetComponent<DraggableItem>() : null;
        if (dragged != null && dragged.slot != null) TryMove(dragged.slot, this);
    }

    /// <summary>
    /// Right-click does the obvious thing for what's under the cursor:
    ///  • bag consumable → use it (heal, then remove);
    ///  • bag equipment  → auto-equip into its matching slot;
    ///  • equipped item   → unequip back to a free bag slot.
    /// </summary>
    public void OnPointerClick(PointerEventData e)
    {
        if (clickOverride != null) { clickOverride(e); return; }   // new windows handle it themselves
        if (e.button != PointerEventData.InputButton.Right || item == null) return;

        if (accepts == EquipSlotType.None)                      // a bag slot
        {
            if (item.healAmount > 0)                            // consumable → use
            {
                if (Character.Instance != null) Character.Instance.Heal(item.healAmount);
                SetItem(null);
            }
            else if (item.slot != EquipSlotType.None)           // equipment → equip
            {
                var target = FindEquipSlot(item.slot);
                if (target != null) TryMove(this, target);
            }
        }
        else                                                    // an equip slot → unequip
        {
            var bag = FindEmptyBagSlot();
            if (bag != null) TryMove(this, bag);
        }
        ItemTooltip.HideTip();   // whatever it described may have moved/vanished
    }

    /// <summary>Hovering a filled slot lights the cyan bloom and shows the item's tooltip
    /// (instance-aware, so equipment shows its +N / cards / rarity).</summary>
    public void OnPointerEnter(PointerEventData e)
    {
        if (hoverGlow != null) hoverGlow.SetActive(true);
        if (instance != null)
        {
            if (previewOnHover && Character.Instance != null) ItemTooltip.ShowForPreview(instance, previewSlot);
            else ItemTooltip.ShowFor(instance);
        }
        else if (item != null) ItemTooltip.ShowFor(item);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (hoverGlow != null) hoverGlow.SetActive(false);
        ItemTooltip.HideTip();
    }

    /// <summary>Move (or swap) an item from one slot to another, honoring equip-type rules.</summary>
    public static bool TryMove(ItemSlot from, ItemSlot to)
    {
        if (from == null || to == null || from == to || from.Item == null) return false;
        Item a = from.Item, b = to.Item;
        if (!to.Accepts(a)) return false;                  // target won't take it
        if (b != null && !from.Accepts(b)) return false;   // swap would put b somewhere invalid
        from.SetItem(b);                                   // b may be null (plain move)
        to.SetItem(a);
        return true;
    }

    // ---- highlighting + slot lookup (used while dragging / right-click equip) ----

    static readonly Color HighlightColor = new Color(0.55f, 1f, 0.55f, 1f);

    /// <summary>Tint this slot's frame to flag it as a valid drop target.</summary>
    public void SetHighlight(bool on)
    {
        var bg = GetComponent<Image>();
        if (bg != null) bg.color = on ? HighlightColor : Color.white;
    }

    static ItemSlot[] AllSlots() =>
        Object.FindObjectsByType<ItemSlot>(FindObjectsInactive.Include);

    /// <summary>Light up every equip slot that would accept this item (called on drag start).</summary>
    public static void HighlightAcceptingEquip(Item dragged)
    {
        if (dragged == null || dragged.slot == EquipSlotType.None) return;
        foreach (var s in AllSlots())
            if (s.accepts == dragged.slot) s.SetHighlight(true);
    }

    public static void ClearHighlights()
    {
        foreach (var s in AllSlots()) s.SetHighlight(false);
    }

    // ---- drag drop-target highlighting (new windows; non-destructive green halo) ----

    public bool IsBag => accepts == EquipSlotType.None;

    /// <summary>Toggle the green "you can drop here" halo (separate channel — never touches the
    /// rarity border, unlike the legacy <see cref="SetHighlight"/>).</summary>
    public void SetDropHighlight(bool on)
    {
        if (dropHighlight != null) dropHighlight.SetActive(on && isActiveAndEnabled);
    }

    /// <summary>Light every slot that would accept what's being dragged from <paramref name="source"/>:
    /// a bag item can drop on any bag cell (reorder) and on equip slots of its type; an equipped item
    /// can drop on any bag cell (unequip).</summary>
    public static void HighlightDropTargets(ItemSlot source)
    {
        if (source == null || source.Item == null) return;
        bool fromBag = source.IsBag;
        EquipSlotType itemSlot = source.Item.slot;   // None for non-equippables
        foreach (var s in AllSlots())
        {
            if (s == source) continue;
            bool ok = fromBag
                ? (s.IsBag || (itemSlot != EquipSlotType.None && s.accepts == itemSlot))
                : s.IsBag;   // equipped → only bag cells (to unequip)
            if (ok) s.SetDropHighlight(true);
        }
    }

    public static void ClearDropTargets()
    {
        foreach (var s in AllSlots()) s.SetDropHighlight(false);
    }

    static ItemSlot FindEquipSlot(EquipSlotType type)
    {
        foreach (var s in AllSlots())
            if (s.accepts == type) return s;
        return null;
    }

    static ItemSlot FindEmptyBagSlot()
    {
        foreach (var s in AllSlots())
            if (s.accepts == EquipSlotType.None && s.Item == null) return s;
        return null;
    }

    /// <summary>Puts <paramref name="item"/> into the first free bag slot. Returns false if the bag is full.</summary>
    public static bool TryAddToBag(Item item)
    {
        var slot = FindEmptyBagSlot();
        if (slot == null) return false;
        slot.SetItem(item);
        return true;
    }
}
