using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drag-to-sort + drag-to-equip for the new progression windows. Lives on the same GameObject as an
/// <see cref="ItemSlot"/> (added by <see cref="ProgressionWindows"/>); operates on the
/// <see cref="InventoryStore"/> / <see cref="Character"/> model rather than mutating slot
/// <see cref="ItemSlot.Item"/>s, so it never desyncs from the data on the next refresh.
///
/// Drops: bag→bag = reorder · bag→matching equip slot = equip · equipped→bag = unequip · bag→THE WORLD = put it
/// down on the ground. A cursor-following ghost shows what's moving and every valid target lights a green halo
/// while dragging.
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
    Color sourceIconColor;
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
        ItemSlot.HighlightDropTargets(slot);    // green halos on every valid target

        // Floating ghost icon: a copy of this slot's sprite that follows the cursor above the windows.
        ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        ghost.transform.SetParent(canvas.transform, false);
        UiLayers.Raise(ghost.transform, UiLayers.Layer.DragGhost);
        var gi = ghost.GetComponent<Image>();
        gi.raycastTarget = false;
        gi.preserveAspect = true;
        gi.sprite = slot.iconImage != null ? slot.iconImage.sprite : null;
        gi.color = new Color(1f, 1f, 1f, 0.9f);
        var grt = (RectTransform)ghost.transform;
        grt.sizeDelta = new Vector2(48, 48);
        grt.position = e.position;

        // Fade the source icon so it reads as "lifted out".
        if (slot.iconImage != null)
        {
            sourceIconColor = slot.iconImage.color;
            slot.iconImage.color = new Color(sourceIconColor.r, sourceIconColor.g, sourceIconColor.b, 0.3f);
        }
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
        ItemSlot.ClearDropTargets();
        if (slot != null && slot.iconImage != null) slot.iconImage.color = sourceIconColor;
        if (ghost != null) { Destroy(ghost); ghost = null; }

        var target = SlotUnder(e);
        if (target == null && NothingUnder(e)) { DropIntoTheWorld(e); return; }
        ResolveDrop(target);
        // Equip/Unequip/Reorder each Notify() the store → ProgressionWindows.RefreshAll re-renders.
    }

    /// <summary>Let go over the world: put it down where you are standing, through the same door the right-click
    /// menu's Drop uses — so a stack still asks how many, and a worn piece, a quest item and the Ring are still
    /// refused with the reason shown there.</summary>
    void DropIntoTheWorld(PointerEventData e)
    {
        // A WORN piece is on your body, not in the bag: there is no bag position to name, so there is nothing to
        // ask the server for. Taking it off is what the menu's Unequip is for.
        if (slot == null || !slot.IsBag || slot.boundEntry == null) return;
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

        if (slot.IsBag)
        {
            if (slot.boundEntry == null) return;
            if (!target.IsBag)                       // bag → equip slot
            {
                if (slot.boundEntry.IsEquip && target.accepts == slot.Item.slot)
                    store.Equip(slot.boundEntry);
            }
            else                                     // bag → bag (reorder; null entry = empty cell → append)
            {
                store.Reorder(slot.boundEntry, target.boundEntry);
            }
        }
        else if (target.IsBag)                       // equipped → bag (unequip)
        {
            store.Unequip(slot.accepts);
        }
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
