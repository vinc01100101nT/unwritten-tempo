using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The ✕ button in a window header — hides the target window and plays the UI
/// close foley (matching the Esc / hotkey-toggle close behaviour).
///
/// It also implements the drag interfaces as no-ops. That's deliberate: the header
/// above it is a <see cref="WindowDragHandle"/> (an ancestor <c>IDragHandler</c>),
/// and without this the EventSystem would route the press gesture up to the header's
/// drag and swallow the button's click — so clicking the ✕ would do nothing. By
/// "claiming" the drag here, the gesture stays on the button and the click fires
/// reliably (and dragging the ✕ correctly does NOT move the window).
///
/// ⚠️ Unity requires every MonoBehaviour to live in a file whose name matches the
/// class name. This used to share <c>WindowChrome.cs</c> with
/// <see cref="WindowDragHandle"/>; because that filename matched neither class,
/// Unity couldn't resolve the scripts and both serialized as the generic
/// <c>fileID: 11500000</c> — which is why the ✕ did nothing at runtime. Keep one
/// MonoBehaviour per correctly-named file. (Re-run Tools ▸ unwritten ▸ Scene Setup
/// ▸ Build UI Shell after touching this so the prefab re-binds the script refs.)
/// </summary>
public class WindowCloseButton : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>The window root to hide on click.</summary>
    public GameObject window;

    // Every ✕ on an open window. A button is enabled exactly while its window is up, so this holds the open windows
    // and nothing else; it changes only when a window opens or closes, never per frame.
    static readonly System.Collections.Generic.List<WindowCloseButton> shown = new System.Collections.Generic.List<WindowCloseButton>(16);

    void OnEnable() => shown.Add(this);
    void OnDisable() => shown.Remove(this);

    /// <summary>Close every open window, exactly as clicking each one's ✕ would.</summary>
    public static void CloseEveryWindow()
    {
        bool any = false;
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            var w = shown[i].window;
            if (w == null || !UiAnim.IsOpen(w)) continue;
            UiAnim.Close(w);
            any = true;
        }
        if (any) UISounds.PlayClose();
    }

    // Close on pointer DOWN, not click. A click only fires when press+release land on the same object
    // WITHOUT crossing the EventSystem drag threshold — nudge the mouse a few pixels between press and
    // release and the gesture becomes a drag, eligibleForClick flips false, and the click is silently
    // lost. That was the intermittent "✕ sometimes does nothing". Acting on down can't be stolen by a drag.
    public void OnPointerDown(PointerEventData e)
    {
        if (window == null) return;
        // Route through the state owner (matches Esc). A bare SetActive(false) left UiAnim.open stuck true,
        // so IsOpen() reported "already open" and the window (e.g. chat) never reopened.
        UiAnim.Close(window);
        UISounds.PlayClose();
    }

    // No-ops: present only so the header's drag handler can't steal our click.
    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e) { }
}
