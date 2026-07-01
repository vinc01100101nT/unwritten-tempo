using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// THE single source of truth for "is the pointer over UI right now?" — used by every world-input script so a
/// click on any UI never leaks to the 3D world (moving, targeting, attacking a mob behind the panel).
///
/// It folds together the two UI systems the game uses:
/// 1. <b>uGUI</b> (Canvas windows) — <see cref="EventSystem.IsPointerOverGameObject"/>. For this to be true the
///    window must have a raycast-target graphic under the cursor; <see cref="UiKit.ClickShield"/> guarantees
///    that across a whole window.
/// 2. <b>IMGUI</b> (OnGUI dev overlays like the Effect Tuner / DevSettings) — the EventSystem CANNOT see these,
///    so each overlay calls <see cref="MarkImguiHot"/> in its OnGUI while the cursor is over its window; that
///    pulse is honored here for a frame (OnGUI runs after Update, so we accept last frame's mark too).
/// </summary>
public static class UiBlock
{
    static int hotFrame = -1;

    /// <summary>Call from an IMGUI overlay's OnGUI when the cursor is over its window this frame.</summary>
    public static void MarkImguiHot() => hotFrame = Time.frameCount;

    /// <summary>True if an IMGUI overlay flagged the cursor over it this frame or last (Update runs before
    /// OnGUI, so the mark for the current frame lands a frame late — accept both).</summary>
    public static bool ImguiHot => hotFrame >= Time.frameCount - 1;

    /// <summary>The gate every world-input script should check: true when the pointer is over ANY UI (uGUI
    /// window or IMGUI overlay), so the click should not also reach the world.</summary>
    public static bool PointerOverUI()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return true;
        return ImguiHot;
    }
}
