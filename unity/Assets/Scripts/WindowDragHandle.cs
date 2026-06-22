using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The draggable title bar of a themed HUD window (Bag / Character / Skills /
/// Outfit / Calendar): mousedown+drag the header → translate the window. No
/// per-frame cost — it only reacts to pointer events. Wired up by
/// <c>UIShellBuilder.BuildWindowHeader</c>.
///
/// ⚠️ Unity requires every MonoBehaviour to live in a file whose name matches the
/// class name. This used to share <c>WindowChrome.cs</c> with
/// <see cref="WindowCloseButton"/>; because that filename matched neither class,
/// Unity couldn't resolve the scripts and both serialized as the generic
/// <c>fileID: 11500000</c> — so at runtime the header drag AND the ✕ close button
/// were missing-script no-ops. Keep one MonoBehaviour per correctly-named file.
///
/// JS/TS analogy: an event listener attached to a DOM node —
/// "mousedown+drag the header → translate the window".
/// </summary>
public class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    /// <summary>The window root to move (defaults to our parent's rect).</summary>
    public RectTransform window;
    Canvas canvas;

    void Awake()
    {
        if (window == null) window = transform.parent as RectTransform;
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        // Lift the grabbed window above its siblings so overlapping windows
        // (e.g. Bag + Character open at once) come to the front when you grab one.
        if (window != null) window.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData e)
    {
        if (window == null) return;
        // e.delta is in screen pixels; anchoredPosition is in canvas (reference)
        // units, so divide by the scaler's factor. Shifting anchoredPosition
        // translates the window while preserving its (screen-relative) size, even
        // though the panels are stretch-anchored.
        float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
        window.anchoredPosition += e.delta / scale;
    }
}
