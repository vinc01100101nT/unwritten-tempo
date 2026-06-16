using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The draggable icon inside an <see cref="ItemSlot"/>. While dragging it floats
/// above the panels and follows the cursor; the actual move/swap is done by the
/// target slot's <see cref="ItemSlot.OnDrop"/>, then the icon snaps back to its
/// home slot (slots own their icons; only the item data moves). Empty slots
/// (no item) don't drag.
/// </summary>
[RequireComponent(typeof(Image), typeof(CanvasGroup))]
public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public ItemSlot slot;

    RectTransform rt;
    CanvasGroup cg;
    Canvas canvas;
    Vector2 aMin, aMax, pivot, oMin, oMax;

    void Awake()
    {
        rt = (RectTransform)transform;
        cg = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();
        aMin = rt.anchorMin; aMax = rt.anchorMax; pivot = rt.pivot;
        oMin = rt.offsetMin; oMax = rt.offsetMax;
    }

    bool CanDrag => slot != null && slot.Item != null;

    public void OnBeginDrag(PointerEventData e)
    {
        if (!CanDrag) return;
        ItemTooltip.Suppressed = true;          // no tooltips popping while we drag over slots
        ItemTooltip.HideTip();
        ItemSlot.HighlightAcceptingEquip(slot.Item);   // glow the slots this fits
        rt.SetParent(canvas.transform, true);   // float above the panels
        rt.SetAsLastSibling();
        cg.blocksRaycasts = false;              // let the slot underneath receive the drop
    }

    public void OnDrag(PointerEventData e)
    {
        if (cg.blocksRaycasts) return;          // didn't actually start a drag (empty slot)
        rt.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        ItemTooltip.Suppressed = false;
        ItemSlot.ClearHighlights();
        cg.blocksRaycasts = true;
        rt.SetParent(slot.transform, false);    // snap home; data already moved via OnDrop
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.offsetMin = oMin; rt.offsetMax = oMax;
        rt.localScale = Vector3.one;
    }
}
