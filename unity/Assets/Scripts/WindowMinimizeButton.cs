using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The _ button in a window's title bar: minimises the window to its bar, or restores it. Acts on pointer DOWN and
/// claims the drag, for the same reason as <see cref="WindowCloseButton"/> — the bar under it is a drag handle,
/// and without this the press would become the bar's drag and the click would never arrive.
/// </summary>
public class WindowMinimizeButton : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public WindowMinimize target;

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || target == null) return;
        target.Toggle();
        UISounds.PlayClick();
    }

    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e) { }
}
