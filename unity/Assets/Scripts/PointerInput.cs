using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// THE single gate for "where is the pointer and what did it just do" — the mouse's counterpart to
/// <see cref="Hotkeys"/>, which owns the same job for keys.
///
/// <para>Every world-input script used to carry its own private copy of these four reads (the player controller,
/// both skill targeters and the camera each had one), which meant the new-or-legacy backend dance was written out
/// five times and had already drifted: two copies answered the new backend alone where the others accepted either.
/// They are one body here now, so a change to how a click is read happens once.</para>
/// </summary>
public static class PointerInput
{
    /// <summary>Pointer position in screen pixels.</summary>
    public static Vector2 Position
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
            return Input.mousePosition;
        }
    }

    /// <summary>The player committed a point in the world this frame — a left click.</summary>
    public static bool PrimaryDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
            return Input.GetMouseButtonDown(0);
        }
    }

    /// <summary>Right button pressed this frame.</summary>
    public static bool SecondaryDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) return true;
#endif
            return Input.GetMouseButtonDown(1);
        }
    }

    /// <summary>Right button held — the camera orbit.</summary>
    public static bool SecondaryHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.isPressed) return true;
#endif
            return Input.GetMouseButton(1);
        }
    }

    /// <summary>Zoom intent this frame, in mouse-wheel notches (+ = away from you), times
    /// <see cref="GameSettings.ScrollSensitivity"/>. The AMOUNT is kept, not only the sign, so a trackpad's
    /// two-finger swipe — many small movements over many frames — zooms in proportion to how far it travelled
    /// instead of one full notch per frame. One frame is capped at one notch, which is also what keeps a backend
    /// reporting its native Windows range (~120 per notch) from leaping. Always 0 while the mouse is over the HUD.</summary>
    public static float ScrollAmount
    {
        get
        {
            // A WHEEL TURNED OVER A WINDOW BELONGS TO THE WINDOW. Every scrolling list, and anything else on screen,
            // gets it; the camera never zooms under an open window. The one gate for the whole HUD, so no window
            // has to remember to claim it.
            if (UiBlock.PointerOverUI()) return 0;
            float s;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) s = Mouse.current.scroll.ReadValue().y;
            else s = Input.mouseScrollDelta.y;
#else
            s = Input.mouseScrollDelta.y;
#endif
            return Mathf.Clamp(s, -1f, 1f) * GameSettings.ScrollSensitivity;
        }
    }
}
