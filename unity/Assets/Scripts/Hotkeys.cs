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
    // ---- on-screen buttons that stand in for a key --------------------------------------------------------------
    // The status box's window buttons "press" a key rather than calling each window: every
    // window already listens for its key here, so a button needs no second way in. A press made during one frame is
    // answered for the WHOLE of the next frame, then gone — so every reader sees it exactly once, whichever order the
    // scripts that read keys run in. Started each frame by VirtualPressClock, on every platform.

    static readonly System.Collections.Generic.HashSet<KeyCode> pendingPress = new System.Collections.Generic.HashSet<KeyCode>();
    static readonly System.Collections.Generic.HashSet<KeyCode> framePress = new System.Collections.Generic.HashSet<KeyCode>();

    /// <summary>An on-screen button pressed <paramref name="key"/>: <see cref="Down"/> answers it next frame.</summary>
    public static void PressOnScreen(KeyCode key) => pendingPress.Add(key);

    /// <summary>True for the frame an on-screen button's press is answered in.</summary>
    public static bool PressedOnScreen(KeyCode key) => framePress.Contains(key);

    /// <summary>Last frame's on-screen presses become this frame's; nothing older survives. Once per frame, first.</summary>
    public static void BeginFrame()
    {
        framePress.Clear();
        framePress.UnionWith(pendingPress);
        pendingPress.Clear();
    }

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
        if (PressedOnScreen(key)) return true;   // an on-screen button counts as the key being struck
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
        // The status box's System button presses Escape on screen; it reaches the same door as the key.
        if (PressedOnScreen(KeyCode.Escape)) return true;
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

    /// <summary>Up arrow pressed this frame gives +1, Down gives -1, neither 0. Not typing-guarded, for the same
    /// reason as <see cref="TabDown"/>: it steps through a list while a text box has the keyboard — the chat's name
    /// box walks the people you have messaged with it.</summary>
    public static int ArrowStep()
    {
        if (NoKeyboard) return 0;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.upArrowKey.wasPressedThisFrame) return 1;
        if (kb != null && kb.downArrowKey.wasPressedThisFrame) return -1;
#endif
        if (Input.GetKeyDown(KeyCode.UpArrow)) return 1;
        if (Input.GetKeyDown(KeyCode.DownArrow)) return -1;
        return 0;
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
        if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return Key.Numpad0 + (k - KeyCode.Keypad0);
        switch (k)
        {
            // Every key the hotbar's third row may be given (GameKeys) is mapped, so a player's own key is read the
            // same way as every built-in one.
            case KeyCode.BackQuote:      return Key.Backquote;
            case KeyCode.Minus:          return Key.Minus;
            case KeyCode.Equals:         return Key.Equals;
            case KeyCode.Backslash:      return Key.Backslash;
            case KeyCode.Semicolon:      return Key.Semicolon;
            case KeyCode.Quote:          return Key.Quote;
            case KeyCode.Comma:          return Key.Comma;
            case KeyCode.Period:         return Key.Period;
            case KeyCode.Slash:          return Key.Slash;
            case KeyCode.CapsLock:       return Key.CapsLock;
            case KeyCode.Insert:         return Key.Insert;
            case KeyCode.Delete:         return Key.Delete;
            case KeyCode.Home:           return Key.Home;
            case KeyCode.End:            return Key.End;
            case KeyCode.PageUp:         return Key.PageUp;
            case KeyCode.PageDown:       return Key.PageDown;
            case KeyCode.UpArrow:        return Key.UpArrow;
            case KeyCode.DownArrow:      return Key.DownArrow;
            case KeyCode.LeftArrow:      return Key.LeftArrow;
            case KeyCode.RightArrow:     return Key.RightArrow;
            case KeyCode.KeypadEnter:    return Key.NumpadEnter;
            case KeyCode.KeypadPeriod:   return Key.NumpadPeriod;
            case KeyCode.KeypadDivide:   return Key.NumpadDivide;
            case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
            case KeyCode.KeypadMinus:    return Key.NumpadMinus;
            case KeyCode.KeypadPlus:     return Key.NumpadPlus;
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
}
