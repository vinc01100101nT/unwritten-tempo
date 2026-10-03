using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Dragging a bag row. Lives on the same GameObject as an <see cref="ItemSlot"/> (added by
/// <see cref="InventoryList"/>); operates on the <see cref="InventoryStore"/> rather than on the row, so it never
/// desyncs from the data on the next refresh.
///
/// Drops: onto another row = reorder · onto THE WORLD = put it down on the ground. A cursor-following ghost moves
/// with the drag.
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

        ItemTooltip.Suppressed = true;          // no tooltips flickering over slots mid-drag
        ItemTooltip.HideTip();

        // Floating ghost that follows the cursor above the windows.
        ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        ghost.transform.SetParent(canvas.transform, false);
        UiLayers.Raise(ghost.transform, UiLayers.Layer.DragGhost);
        var gi = ghost.GetComponent<Image>();
        gi.raycastTarget = false;
        gi.preserveAspect = true;
        gi.color = new Color(1f, 1f, 1f, 0.9f);
        var grt = (RectTransform)ghost.transform;
        grt.sizeDelta = new Vector2(48, 48);
        grt.position = e.position;
    }

    public void OnDrag(PointerEventData e)
    {
        if (ghost != null) ghost.transform.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;

        ItemTooltip.Suppressed = false;
        if (ghost != null) { Destroy(ghost); ghost = null; }

        // LET GO OVER THE HOTBAR: a usable item goes on it by kind, a piece of gear as that exact piece.
        if (HotbarSlotTarget.SlotUnder(e) >= 0)
        {
            HotbarSlotTarget.DropOnto(e, BarEntryFor(slot != null ? slot.boundEntry : null));
            return;
        }

        var target = SlotUnder(e);
        if (target == null && NothingUnder(e)) { DropIntoTheWorld(e); return; }
        ResolveDrop(target);
        // Reorder Notify()s the store → ProgressionWindows.RefreshAll re-renders.
    }

    /// <summary>What a bag row stands for on the hotbar: one exact piece of gear (worn or carried), a usable item by its
    /// kind, or nothing — a material or a quest item has nothing to press.</summary>
    static string BarEntryFor(BagEntry entry)
    {
        if (entry == null || entry.def == null) return "";
        if (entry.IsEquip) return entry.instance != null ? HotbarEntry.Gear(entry.instance.copyId) : "";
        return entry.def.IsUsable ? HotbarEntry.Item(entry.def.id) : "";
    }

    /// <summary>Let go over the world: put it down where you are standing, through the same door the right-click
    /// menu's Drop uses — so a stack still asks how many, and a worn piece, a quest item and the Ring are still
    /// refused with the reason shown there.</summary>
    void DropIntoTheWorld(PointerEventData e)
    {
        // A WORN piece is on your body, not in the bag: the store refuses to drop it, and taking it off is what
        // the menu's Unequip is for.
        if (slot == null || slot.boundEntry == null) return;
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

    void ResolveDrop(ItemSlot target)
    {
        var store = InventoryStore.Instance;
        if (store == null || slot == null || slot.Item == null || target == null || target == slot) return;
        if (slot.boundEntry == null) return;

        // Onto another row: reorder (a null entry = an empty row → append).
        store.Reorder(slot.boundEntry, target.boundEntry);
    }

    // The ItemSlot under the cursor at drop time (the ghost is raycast-transparent, so it won't shadow it).
    static ItemSlot SlotUnder(PointerEventData e)
    {
        if (EventSystem.current == null) return null;
        hits.Clear();
        EventSystem.current.RaycastAll(e, hits);
        foreach (var h in hits)
        {
            var s = h.gameObject.GetComponentInParent<ItemSlot>();
            if (s != null) return s;
        }
        return null;
    }
}
