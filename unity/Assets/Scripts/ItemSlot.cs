using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

    [Tooltip("Shown when the slot is empty (e.g. a greyed equip icon). Optional.")]
    public Sprite emptyPlaceholder;

    [SerializeField] Item item;                  // serialized so seeded gear survives into Play
    public Item Item => item;

    /// <summary>Raised whenever the held item changes (drag/swap/consume). Equip slots
    /// listen to push the new item into <see cref="Character"/>.</summary>
    public event System.Action Changed;

    void Awake() => Refresh();

    public bool Accepts(Item item)
    {
        if (item == null) return true;
        if (accepts == EquipSlotType.None) return true;   // bag holds anything
        return item.slot == accepts;
    }

    public void SetItem(Item newItem)
    {
        item = newItem;
        Refresh();
        Changed?.Invoke();
    }

    void Refresh()
    {
        if (iconImage == null) return;
        if (Item != null && Item.icon != null)
        {
            iconImage.sprite = Item.icon;
            iconImage.color = Color.white;
            iconImage.enabled = true;
        }
        else if (emptyPlaceholder != null)
        {
            iconImage.sprite = emptyPlaceholder;
            iconImage.color = Color.white;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
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

    /// <summary>Hovering a filled slot shows the item's details tooltip.</summary>
    public void OnPointerEnter(PointerEventData e)
    {
        if (item != null) ItemTooltip.ShowFor(item);
    }

    public void OnPointerExit(PointerEventData e) => ItemTooltip.HideTip();

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
        Object.FindObjectsByType<ItemSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);

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
}
