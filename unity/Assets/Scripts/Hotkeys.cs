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
    /// <summary>Nobody is at the keyboard of a server, so no key is ever down there. Checked by every reader
    /// below, which is the whole point of this being the one gate: the answer is given ONCE and no input site
    /// can forget it.
    ///
    /// <para>Not merely tidy — <b>required</b>. <see cref="Typing"/> asks <c>GUIUtility.keyboardControl</c>, and a
    /// dedicated server build strips the graphics stack that owns it, so touching it there crashes the process
    /// outright rather than returning a value.</para></summary>
    static bool NoKeyboard => GameServer.IsServer;

    /// <summary>True while a text box owns the keyboard — every key query below returns false.</summary>
    public static bool Typing
    {
        get
        {
            if (NoKeyboard) return false;
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
        if (NoKeyboard || Typing) return false;
#if UNITY_ANDROID
        if (TouchControls.VirtualDown(key)) return true;   // an on-screen button counts as the key being struck
#endif
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
        if (NoKeyboard || Typing) return false;
#if UNITY_ANDROID
        if (TouchControls.VirtualHeld(key)) return true;
#endif
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
        if (NoKeyboard) return false;
#if UNITY_ANDROID
        // The Android Back gesture already arrives here as Escape; this adds the on-screen route to the same door.
        if (TouchControls.VirtualDown(KeyCode.Escape)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Escape);
    }

    /// <summary>Tab pressed this frame. Deliberately NOT typing-guarded, the same way <see cref="CancelDown"/>
    /// isn't: stepping to the next box on a form is exactly the thing you do WHILE typing, so the guard that
    /// silences gameplay keys must not silence this one. Read by <see cref="TabFocus"/>.</summary>
    public static bool TabDown()
    {
        if (NoKeyboard) return false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.tabKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Tab);
    }

    /// <summary>Either Shift held. Not typing-guarded for the same reason as <see cref="TabDown"/> — it is only
    /// ever read alongside a key that has to work inside a text box.</summary>
    public static bool ShiftHeld
    {
        get
        {
            if (NoKeyboard) return false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) return true;
#endif
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
    }

    static int cancelHandledFrame = -1;

    /// <summary>Say that this frame's Escape was USED (a window closed, an aim was cancelled, …). Every
    /// Escape consumer calls this in the branch where it actually acts, so <see cref="CancelHandledThisFrame"/>
    /// can tell "Escape did something" from "Escape went unanswered". Escape falling all the way through is
    /// what opens the System window (<see cref="SystemWindow"/>), so this flag is what keeps closing a window
    /// from popping the System menu in the same keypress.</summary>
    public static void MarkCancelHandled() => cancelHandledFrame = Time.frameCount;

    /// <summary>True when something already acted on this frame's Escape. Read it from <c>LateUpdate</c> —
    /// every consumer marks in <c>Update</c>, which always runs first.</summary>
    public static bool CancelHandledThisFrame => cancelHandledFrame == Time.frameCount;

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
        if (i < 0 || i > 8 || NoKeyboard || Typing) return false;
#if UNITY_ANDROID
        if (TouchControls.VirtualDown((KeyCode)((int)KeyCode.Alpha1 + i))) return true;
#endif
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null) return kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame;
#endif
        return Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i));
    }
}
