using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Dragging a box — in the bag, on the body, in the Keeper's storage. Lives on the same GameObject as an
/// <see cref="ItemSlot"/> (every <see cref="ItemBox"/> has both); it decides nothing about what a drop MEANS. Whatever
/// it is let go over answers that (<see cref="IItemDropTarget"/>): another box, a grid, the body in the equipment panel.
///
/// Drops: onto the hotbar = put it there · onto a drop target = what that target says · onto THE WORLD = put it down
/// on the ground (only a thing in the bag). A cursor-following ghost moves with the drag, and
/// <see cref="ItemDrag"/> tells anything listening what is being held.
///
/// <para><b>Letting go over the world means dropping it</b>, and "the world" means the cursor is over no window
/// at all — not merely outside the bag. Releasing onto your character sheet or your skill book does nothing,
/// because throwing a sword on the floor is not what somebody dragging it onto another window meant to do.</para>
/// </summary>
[RequireComponent(typeof(ItemSlot))]
public class SlotDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    ItemSlot slot;
    Canvas canvas;
    GameObject ghost;
    bool dragging;

    static readonly List<RaycastResult> hits = new List<RaycastResult>();

    void Awake()
    {
        slot = GetComponent<ItemSlot>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (slot == null || slot.Item == null || canvas == null) return;   // empty cell → no drag
        dragging = true;
        ItemDrag.Begin(slot.boundEntry);

        ItemTooltip.Suppressed = true;          // no tooltips flickering over slots mid-drag
        ItemTooltip.HideTip();

        // The one drag picture every drag in the game shows (DragPicture).
        ghost = DragPicture.Begin((RectTransform)transform, canvas, e.position);
    }

    public void OnDrag(PointerEventData e)
    {
        DragPicture.Move(ghost, e.position);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;
        ItemDrag.End();

        ItemTooltip.Suppressed = false;
        DragPicture.End(ref ghost, (RectTransform)transform);

        // LET GO OVER THE HOTBAR: a usable item goes on it by kind, a piece of gear as that exact piece.
        if (HotbarSlotTarget.SlotUnder(e) >= 0)
        {
            HotbarSlotTarget.DropOnto(e, BarEntryFor(slot != null ? slot.boundEntry : null));
            return;
        }

        if (DropTargetTakes(e)) return;
        if (NothingUnder(e)) DropIntoTheWorld(e);
    }

    // The first thing under the cursor that takes the drop — a box, a grid, the body. The box being dragged is not one.
    bool DropTargetTakes(PointerEventData e)
    {
        if (EventSystem.current == null) return false;
        hits.Clear();
        EventSystem.current.RaycastAll(e, hits);
        foreach (var h in hits)
        {
            var target = h.gameObject.GetComponentInParent<IItemDropTarget>();
            if (target == null) continue;
            if ((target as Component) == GetComponent<ItemBox>()) return true;   // let go where it was: nothing moves
            return target.TakeDrop(slot, e);
        }
        return false;
    }

    void OnDisable()
    {
        // A box switched off mid-drag (its window closed) must not leave the drag, or the ghost, behind.
        if (!dragging) return;
        dragging = false;
        ItemDrag.End();
        ItemTooltip.Suppressed = false;
        DragPicture.End(ref ghost, (RectTransform)transform);
    }

    /// <summary>What a bag row stands for on the hotbar: one exact piece of gear (worn or carried), a usable item by its
    /// kind, or nothing — a material or a quest item has nothing to press.</summary>
    static string BarEntryFor(BagEntry entry)
    {
        if (entry == null || entry.def == null) return "";
        if (entry.IsEquip) return entry.instance != null ? HotbarEntry.Gear(entry.instance.copyId) : "";
        return entry.def.IsUsable ? HotbarEntry.Item(entry.def.id) : "";
    }

    /// <summary>Let go over the world: put it down where you are standing — a stack asks how many, and a quest item is
    /// refused with the reason shown. Only a thing IN THE BAG: a worn piece is on your body (take it off first), and a
    /// stored one is the Keeper's to hand back.</summary>
    void DropIntoTheWorld(PointerEventData e)
    {
        if (slot == null || slot.boundEntry == null || InventoryStore.Instance == null) return;
        if (!InventoryStore.Instance.Entries.Contains(slot.boundEntry)) return;
        DropAmountPrompt.DropFromScreen(slot.boundEntry, e.position, canvas, null);
    }

    // Is the cursor over NO user interface at all — i.e. over the world. The ghost is raycast-transparent, so it
    // cannot shadow whatever is really underneath.
    static bool NothingUnder(PointerEventData e)
    {
        if (EventSystem.current == null) return false;
        hits.Clear();
        EventSystem.current.RaycastAll(e, hits);
        return hits.Count == 0;
    }
}
