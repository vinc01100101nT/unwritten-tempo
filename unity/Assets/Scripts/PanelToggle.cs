using System;
using UnityEngine;

/// <summary>
/// Shows/hides UI panels with hotkeys. Lives on the always-active UI Canvas (a
/// hidden panel can't listen for its own re-open key, so the listener must sit on
/// something that stays enabled). Panels toggle independently — so you can have
/// the Bag and Character windows open at once to drag between them — and the
/// close-all key (default Esc) hides everything.
/// </summary>
public class PanelToggle : MonoBehaviour
{
    [Serializable]
    public class Binding
    {
        public string name;          // for readability in the Inspector
        public KeyCode key;
        public GameObject panel;
    }

    public Binding[] panels = new Binding[0];
    public KeyCode closeAllKey = KeyCode.Escape;

    void Update()
    {
        foreach (var b in panels)
        {
            if (b.panel != null && Input.GetKeyDown(b.key))
                b.panel.SetActive(!b.panel.activeSelf);
        }

        if (closeAllKey != KeyCode.None && Input.GetKeyDown(closeAllKey))
            foreach (var b in panels)
                if (b.panel != null) b.panel.SetActive(false);
    }
}
