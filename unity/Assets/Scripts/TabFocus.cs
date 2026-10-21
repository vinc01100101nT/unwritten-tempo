using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Tab moves the keyboard to the next text box on a screen, Shift+Tab to the previous, wrapping at both ends —
/// text boxes only, never a button. Attached once to a screen's canvas root, so every box that screen builds is reachable without
/// touching the mouse — nothing has to be numbered or listed by hand, and a row inserted into a form takes its
/// place in the walk on its own.
///
/// <para>The order IS the hierarchy order, which is the order the screen created its controls in, top to
/// bottom. That is the single source of the order: a hand-kept second list would drift the moment a field moved.</para>
///
/// <para>None of this comes for free. A focused text box takes every keystroke while it has the keyboard, and a
/// single-line box then throws a tab character away (TMP_InputField.KeyPressed), so without this a press of Tab
/// on a form does nothing whatsoever.</para>
/// </summary>
public class TabFocus : MonoBehaviour
{
    /// <summary>Give <paramref name="root"/> and everything under it Tab-to-the-next-box. Safe to call twice.</summary>
    public static void Attach(GameObject root)
    {
        if (root != null && root.GetComponent<TabFocus>() == null) root.AddComponent<TabFocus>();
    }

    // Reused every press so walking a form allocates nothing.
    static readonly List<Selectable> stops = new List<Selectable>();

    void Update()
    {
        if (!Hotkeys.TabDown()) return;

        var events = EventSystem.current;
        if (events == null) return;

        GetComponentsInChildren(false, stops);
        stops.RemoveAll(NotAStop);
        if (stops.Count == 0) return;

        var focused = events.currentSelectedGameObject;
        int at = -1;
        for (int i = 0; i < stops.Count; i++) if (stops[i].gameObject == focused) { at = i; break; }

        int step = Hotkeys.ShiftHeld ? -1 : 1;
        int next = at < 0 ? (step > 0 ? 0 : stops.Count - 1)
                          : (at + step + stops.Count) % stops.Count;

        Focus(stops[next], events);
        stops.Clear();
    }

    // ONLY TEXT BOXES are stops: a box shows it has the keyboard (its blinking marker), a button shows nothing, so
    // Tab landing on one left the player not knowing where they were.
    static bool NotAStop(Selectable s) => s == null || !s.IsInteractable() || s.GetComponent<TMP_InputField>() == null;   // perf-ok: only on a Tab press

    static void Focus(Selectable stop, EventSystem events)
    {
        // A text box needs its caret opened as well as being selected; anything else only needs the selection.
        var field = stop.GetComponent<TMP_InputField>();   // perf-ok: only runs on a Tab press, not per frame
        if (field != null) { field.Select(); field.ActivateInputField(); return; }
        events.SetSelectedGameObject(stop.gameObject);
    }
}
