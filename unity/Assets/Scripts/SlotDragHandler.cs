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
/// Drops: bag→bag = reorder · bag→matching equip slot = equip · equipped→bag = unequip. A cursor-
/// following ghost shows what's moving and every valid target lights a green halo while dragging.
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
        ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ghost.transform.SetParent(canvas.transform, false);
        ghost.transform.SetAsLastSibling();
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
        ResolveDrop(target);
        // Equip/Unequip/Reorder each Notify() the store → ProgressionWindows.RefreshAll re-renders.
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
