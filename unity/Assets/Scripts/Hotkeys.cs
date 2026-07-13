using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
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
            if (input != null && input.isFocused) return true;
            var tmp = go.GetComponent<TMP_InputField>();      // chat + any TMP input field
            return tmp != null && tmp.isFocused;
        }
    }

    /// <summary>Key pressed this frame — suppressed while typing. Reads the new Input System when it's
    /// active AND the legacy manager (an OR, matching the per-key wrappers this replaced), so every
    /// Hotkeys-routed key works under either backend — the backend dance lives HERE, nowhere else.</summary>
    public static bool Down(KeyCode key)
    {
        if (Typing) return false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            var k = ToKey(key);
            if (k != Key.None && kb[k].wasPressedThisFrame) return true;
        }
#endif
        return Input.GetKeyDown(key);
    }

    /// <summary>Key held — suppressed while typing (so WASD doesn't walk the avatar while you type).</summary>
    public static bool Held(KeyCode key)
    {
        if (Typing) return false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            var k = ToKey(key);
            if (k != Key.None && kb[k].isPressed) return true;
        }
#endif
        return Input.GetKey(key);
    }

    /// <summary>Escape pressed this frame. Deliberately NOT typing-guarded: cancel/close-window must
    /// still work while a text box is focused (e.g. Esc closes the chat). The one shared front door for
    /// every panel's close-on-Escape, so the "Esc works while typing" rule can't fork per panel.</summary>
    public static bool CancelDown()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Escape);
    }

#if ENABLE_INPUT_SYSTEM
    // Legacy KeyCode → new-Input-System Key, for the keys this game binds. Key.None = no mapping
    // (the caller then relies on the legacy read alone).
    static Key ToKey(KeyCode k)
    {
        if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
        if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return Key.Digit1 + (k - KeyCode.Alpha1);
        if (k >= KeyCode.F1 && k <= KeyCode.F12) return Key.F1 + (k - KeyCode.F1);
        switch (k)
        {
            case KeyCode.Alpha0:       return Key.Digit0;
            case KeyCode.Space:        return Key.Space;
            case KeyCode.Escape:       return Key.Escape;
            case KeyCode.Return:       return Key.Enter;
            case KeyCode.Tab:          return Key.Tab;
            case KeyCode.LeftShift:    return Key.LeftShift;
            case KeyCode.RightShift:   return Key.RightShift;
            case KeyCode.LeftAlt:      return Key.LeftAlt;
            case KeyCode.RightAlt:     return Key.RightAlt;
            case KeyCode.LeftControl:  return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftBracket:  return Key.LeftBracket;
            case KeyCode.RightBracket: return Key.RightBracket;
            default:                   return Key.None;
        }
    }
#endif

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
