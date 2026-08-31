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
///
/// <para>On Android the finger answers instead, from <see cref="TouchControls"/> — which is the whole reason this
/// gate exists: touch support is one branch here rather than five branches scattered across the game.</para>
/// </summary>
public static class PointerInput
{
    /// <summary>Pointer position in screen pixels.</summary>
    public static Vector2 Position
    {
        get
        {
#if UNITY_ANDROID
            if (TouchControls.HasTouch) return TouchControls.WorldPointer;
#endif
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
            return Input.mousePosition;
        }
    }

    /// <summary>The player committed a point in the world this frame — a left click, or on a phone a finger
    /// lifted from a tap or an aim.</summary>
    public static bool PrimaryDown
    {
        get
        {
#if UNITY_ANDROID
            if (TouchControls.HasTouch) return TouchControls.WorldCommit;
#endif
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
            return Input.GetMouseButtonDown(0);
        }
    }

    /// <summary>Right button pressed this frame. Always false on a phone: there is no second button, and the
    /// things it cancels are reached by the hardware Back gesture, which arrives as Escape.</summary>
    public static bool SecondaryDown
    {
        get
        {
#if UNITY_ANDROID
            if (TouchControls.HasTouch) return false;
#endif
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) return true;
#endif
            return Input.GetMouseButtonDown(1);
        }
    }

    /// <summary>Right button held — the camera orbit on the desktop. Always false on a phone, where the orbit is
    /// a one-finger drag reported as <see cref="TouchControls.OrbitDelta"/> instead of as a held button.</summary>
    public static bool SecondaryHeld
    {
        get
        {
#if UNITY_ANDROID
            if (TouchControls.HasTouch) return false;
#endif
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.isPressed) return true;
#endif
            return Input.GetMouseButton(1);
        }
    }

    /// <summary>Zoom intent as +1 / -1 / 0. Only the SIGN, never the magnitude: scroll units differ wildly per
    /// backend and per mouse driver (the new backend reports ~120 per notch on Windows), so one notch means one
    /// step everywhere. A phone's pinch is reported in the same units for the same reason.</summary>
    public static int ScrollDir
    {
        get
        {
#if UNITY_ANDROID
            if (TouchControls.HasTouch) return TouchControls.ZoomDir;
#endif
            float s;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) s = Mouse.current.scroll.ReadValue().y;
            else s = Input.mouseScrollDelta.y;
#else
            s = Input.mouseScrollDelta.y;
#endif
            return s > 0f ? 1 : s < 0f ? -1 : 0;
        }
    }
}
