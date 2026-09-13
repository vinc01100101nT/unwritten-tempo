using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The interaction half of one bag row: it holds the item the row shows and owns hovering it (the item tooltip, or
/// the live equip→stat-delta preview) and clicking it. <see cref="InventoryList"/> adds one to every row, with a
/// <see cref="SlotDragHandler"/> beside it for dragging. It draws nothing — the row's text cells do that.
/// </summary>
[RequireComponent(typeof(Image))]
public class ItemSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] Item item;                  // serialized so seeded gear survives into Play
    public Item Item => item;

    // Present for equipment carrying refine/cards; null for plain bag items (potions/materials).
    [SerializeField] EquipmentInstance instance;
    public EquipmentInstance Instance => instance;

    /// <summary>The live <see cref="BagEntry"/> this row currently shows (set each refresh; null on an empty row).
    /// SlotDragHandler uses it to reorder and drop against the store rather than against the row.</summary>
    public BagEntry boundEntry;

    /// <summary>The click handling for this row, set by the list that owns it.</summary>
    public System.Action<PointerEventData> clickOverride;
    /// <summary>When true, hovering an equipment instance shows the live equip→stat-delta preview
    /// against <see cref="previewSlot"/> (instead of the plain item tooltip).</summary>
    public bool previewOnHover;
    public EquipSlotType previewSlot;

    // Closing a window (X / B-C toggle / Esc) deactivates its rows. uGUI does NOT fire
    // OnPointerExit on a row that's disabled out from under the cursor, so a tooltip raised by
    // OnPointerEnter would otherwise stay stuck on the always-on tooltip layer. Hide the shared
    // tooltip on disable so closing always dismisses it.
    void OnDisable()
    {
        ItemTooltip.HideTip();
    }

    public void SetItem(Item newItem)
    {
        item = newItem;
        instance = null;             // a plain item clears any prior instance data
    }

    /// <summary>Hold a full equipment instance (def + refine + cards). Use <see cref="SetItem"/> for plain
    /// bag items.</summary>
    public void SetInstance(EquipmentInstance inst)
    {
        instance = inst;
        item = inst != null ? inst.def : null;
    }

    public void OnPointerClick(PointerEventData e)
    {
        // EVERY slot the game builds is given its own click handler by the window that made it, so the line
        // below has always been the end of this method — everything that used to follow it could never run.
        // What was there was a second, older way to use, equip and unequip straight out of a SQUARE on the
        // screen, which reached none of the character's real belongings. Its sibling — the one that put a
        // picked-up item into a square — was losing loot for exactly that reason. Removed rather than left
        // sitting here looking like a working path into somebody's bag.
        if (clickOverride != null) clickOverride(e);
        ItemTooltip.HideTip();   // whatever it described may have moved/vanished
    }

    /// <summary>Hovering a filled row shows the item's tooltip (instance-aware, so equipment shows its +N /
    /// cards / rarity).</summary>
    public void OnPointerEnter(PointerEventData e)
    {
        if (instance != null)
        {
            if (previewOnHover && Character.Instance != null) ItemTooltip.ShowForPreview(instance, previewSlot);
            else ItemTooltip.ShowFor(instance);
        }
        else if (item != null) ItemTooltip.ShowFor(item);
    }

    public void OnPointerExit(PointerEventData e)
    {
        ItemTooltip.HideTip();
    }


    // DELIBERATELY GONE: a way to "add an item to the bag" from here. It put the item into the first empty
    // SQUARE of the window, which is a picture of the bag and not the bag — nothing ever read a square back
    // into the character, and the next repaint drew over it from the real list. Loot collected that way was
    // therefore lost, silently, every time. Anything giving somebody an item goes to InventoryStore.Give,
    // which is the one door into what a character is actually carrying.
}
