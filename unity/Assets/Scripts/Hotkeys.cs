using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Central gate for ALL gameplay / HUD key input. Every hotkey (hotbar 1–9, window toggles B/V/C/K/L/O/M,
/// skill keys, interact, movement) must query through here instead of calling <c>Input.GetKeyDown</c>/<c>GetKey</c>
/// directly — so the one <see cref="Typing"/> guard suppresses them all while the player is typing into a text
/// box, and there's a single place to change the input backend later.
///
/// <see cref="Typing"/> covers both input worlds this project uses:
///   • IMGUI dev tuners (EffectTuner/SequenceTuner filter boxes) via <c>GUIUtility.keyboardControl</c>.
///   • uGUI input fields via the focused EventSystem selection (future-proofing; no runtime uGUI input
///     fields exist today, but any we add will suppress hotkeys automatically).
///
/// Perf: the getter runs per-frame from hotkey handlers. In normal play <c>keyboardControl</c> is 0 and
/// nothing is selected, so it early-outs before any GetComponent; the lookup only runs while a selectable
/// is actually focused (rare), never during combat.
/// </summary>
public static class Hotkeys
{
    /// <summary>True while a text box owns the keyboard — every key query below returns false.</summary>
    public static bool Typing
    {
        get
        {
            if (GUIUtility.keyboardControl != 0) return true;   // IMGUI text field focused (dev tuners)
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var input = go.GetComponent<InputField>();
            return input != null && input.isFocused;
        }
    }

    /// <summary>Key pressed this frame — suppressed while typing.</summary>
    public static bool Down(KeyCode key) => !Typing && Input.GetKeyDown(key);

    /// <summary>Key held — suppressed while typing (so WASD doesn't walk the avatar while you type).</summary>
    public static bool Held(KeyCode key) => !Typing && Input.GetKey(key);

    /// <summary>Digit 1–9 for hotbar slot <paramref name="i"/> (0-based) pressed this frame. Suppressed while typing.</summary>
    public static bool DigitDown(int i)
    {
        if (i < 0 || i > 8 || Typing) return false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null) return kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame;
#endif
        return Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i));
    }
}
