using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// THE WINDOW THAT CAME LAST HAS THE KEYBOARD, AND ENTER SAYS YES TO IT — the one place both are decided, for every
/// window in the game. Every affirming button (OK, Enter, Enter World, Create, Confirm, Accept, Upload, Play…) is made
/// through <see cref="UiKit.MarkPrimary"/>, which hands it here, so a new window gets this without being told.
///
/// <para><b>Focus.</b> The moment an affirming button appears — its window opened — the keyboard goes to it, and its
/// window goes on top of the stack. When it goes away, the keyboard goes back to what held it before.</para>
///
/// <para><b>Enter</b>, in order: a button holding the keyboard is pressed; typing in a box presses the affirming button
/// of THAT box's window (Enter in the chat line, which has none, is left to the chat); otherwise the affirming button
/// of the window that came last is pressed. This presses the button itself and takes the press away from Unity's own
/// event handling, so one press can never do two things. Screens that read Enter themselves ask
/// <see cref="TakenThisFrame"/> first. Runs before every other script.</para>
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class EnterKey : MonoBehaviour
{
    // Affirming buttons showing now, the one that appeared last at the end.
    static readonly List<Button> shown = new List<Button>(8);
    // What held the keyboard just before each of them took it, index for index.
    static readonly List<GameObject> heldBefore = new List<GameObject>(8);
    static readonly List<Button> inWindow = new List<Button>(4);
    static EnterKey driver;
    static int takenOnFrame = -1;
    static EventSystem tamed;

    /// <summary>True on the frame Enter pressed a button — nothing else may answer that press.</summary>
    public static bool TakenThisFrame => takenOnFrame == Time.frameCount;

    /// <summary>Make <paramref name="button"/> an affirming button. Called by <see cref="UiKit.MarkPrimary"/>.</summary>
    public static void Mark(Button button)
    {
        if (button == null || button.GetComponent<Watch>() != null) return;
        button.gameObject.AddComponent<Watch>();
        if (driver == null && Application.isPlaying)
        {
            var go = new GameObject("EnterKey") { hideFlags = HideFlags.DontSaveInEditor };
            DontDestroyOnLoad(go);
            driver = go.AddComponent<EnterKey>();
        }
    }

    // Tells the stack when an affirming button appears and goes. It finds its button itself: being added runs
    // OnEnable at once, before anything handed to it afterwards could have arrived.
    sealed class Watch : MonoBehaviour
    {
        Button button;
        void OnEnable() { if (button == null) button = GetComponent<Button>(); Appeared(button); }
        void OnDisable() => Went(button);
    }

    static void Appeared(Button b)
    {
        if (b == null || !Application.isPlaying) return;
        int at = shown.IndexOf(b);
        if (at >= 0) { shown.RemoveAt(at); heldBefore.RemoveAt(at); }
        var events = EventSystem.current;
        var held = events != null ? events.currentSelectedGameObject : null;
        heldBefore.Add(held);
        shown.Add(b);
        if (events != null) events.SetSelectedGameObject(b.gameObject);
    }

    static void Went(Button b)
    {
        int at = shown.IndexOf(b);
        if (at < 0) return;
        var before = heldBefore[at];
        shown.RemoveAt(at);
        heldBefore.RemoveAt(at);
        var events = EventSystem.current;
        if (events == null) return;
        var now = events.currentSelectedGameObject;
        // Only when the keyboard was still on the button going away: anything that took it since keeps it.
        if (now != null && now != b.gameObject && now.activeInHierarchy) return;
        var back = before != null && before.activeInHierarchy ? before
                 : shown.Count > 0 ? shown[shown.Count - 1].gameObject : null;
        events.SetSelectedGameObject(back);
        // A box handed back the keyboard is put back into typing mode, as it was.
        var field = back != null ? back.GetComponent<TMPro.TMP_InputField>() : null;
        if (field != null) field.ActivateInputField();
    }

    void Update()
    {
        var events = EventSystem.current;
        if (events == null) return;
        // UNITY'S OWN KEYBOARD HANDLING IS SWITCHED OFF, for every event system the game ever has: on its own it pressed
        // whatever button held the keyboard on Enter (and Space), moved the keyboard with the arrow keys, and ran after
        // this — so one Enter did two things. Keys reach buttons through here only; Tab walks the text boxes (TabFocus). Mouse clicks and
        // typing into boxes are untouched.
        if (!ReferenceEquals(events, tamed)) { events.sendNavigationEvents = false; tamed = events; }
        // THE KEYBOARD NEVER SITS NOWHERE while a window with a yes is up: a button Unity let go of (it greys a button
        // out and drops the keyboard from it — Enter World, until the list has loaded) is handed it back.
        if (events.currentSelectedGameObject == null)
        {
            var top = TopShown();
            if (top != null) events.SetSelectedGameObject(top.gameObject);
        }

        if (!Hotkeys.EnterDown()) return;

        var focused = events.currentSelectedGameObject;
        Button press = null;

        // A YES HOLDING THE KEYBOARD is what they mean. Any other button holds it only because it was CLICKED (New,
        // then back to the list) — the click already did its job, so Enter goes to the window's yes instead.
        var focusedButton = focused != null ? focused.GetComponent<Button>() : null;
        bool meant = focusedButton != null && shown.Contains(focusedButton);
        if (meant && focusedButton.isActiveAndEnabled && focusedButton.IsInteractable())
            press = focusedButton;
        else if (focusedButton != null)
        {
            press = OnlyAffirmingIn(WindowOf(focused.transform));
            if (press == null) { var top = TopShown(); if (top != null) press = OnlyAffirmingIn(WindowOf(top.transform)); }
        }
        else if (Hotkeys.Typing)
        {
            if (focused == null) return;
            press = OnlyAffirmingIn(WindowOf(focused.transform));
            if (press == null) return;   // the chat line, and any box whose window has no single yes
        }
        else
        {
            var top = TopShown();
            if (top != null) press = OnlyAffirmingIn(WindowOf(top.transform));
        }
        if (press == null) return;

        takenOnFrame = Time.frameCount;
        press.onClick.Invoke();
    }

    // The affirming button of the window that came last, still showing and pressable; null when there is none.
    static Button TopShown()
    {
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            var b = shown[i];
            if (b == null) { shown.RemoveAt(i); heldBefore.RemoveAt(i); continue; }
            if (b.isActiveAndEnabled && b.IsInteractable()) return b;
        }
        return null;
    }

    // The window's affirming button, when it shows exactly one; a list of them (Play on every quest) is not guessed at.
    static Button OnlyAffirmingIn(Transform window)
    {
        if (window == null) return null;
        inWindow.Clear();
        for (int i = 0; i < shown.Count; i++)
        {
            var b = shown[i];
            if (b != null && b.isActiveAndEnabled && b.IsInteractable() && WindowOf(b.transform) == window) inWindow.Add(b);
        }
        return inWindow.Count == 1 ? inWindow[0] : null;
    }

    // The window a piece belongs to: its ancestor sitting directly on a canvas.
    static Transform WindowOf(Transform t)
    {
        while (t != null && t.parent != null)
        {
            if (t.parent.GetComponent<Canvas>() != null) return t;   // perf-ok: only on an Enter press
            t = t.parent;
        }
        return null;
    }
}
