using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimises a window to its blue title bar and back. Minimised, the window's edge and body stop drawing, every
/// part of the body except the title bar is switched off, and the window stops catching clicks — so only the bar is
/// left on screen, still draggable. Restoring switches back on exactly the parts that were on before, so a part the
/// window had hidden itself stays hidden.
///
/// Put on the window root by <see cref="UiKit.DressWindow"/>; toggled by <see cref="WindowMinimizeButton"/>.
/// </summary>
[DisallowMultipleComponent]
public class WindowMinimize : MonoBehaviour
{
    public Image edge;
    public Image body;
    public RectTransform header;

    public bool Minimized { get; private set; }

    readonly List<GameObject> hidden = new List<GameObject>();

    public void Toggle()
    {
        if (edge == null || body == null) return;
        Minimized = !Minimized;
        edge.enabled = !Minimized;
        body.enabled = !Minimized;

        // The whole-window click catcher (UiKit.ClickShield) — the bar keeps catching its own clicks.
        var shield = edge.transform.Find("ClickShield");
        if (shield != null && shield.TryGetComponent<Image>(out var s)) s.raycastTarget = !Minimized;

        if (Minimized)
        {
            hidden.Clear();
            foreach (Transform child in body.transform)
                if (child != header && child.gameObject.activeSelf) { hidden.Add(child.gameObject); child.gameObject.SetActive(false); }
        }
        else
        {
            for (int i = 0; i < hidden.Count; i++) if (hidden[i] != null) hidden[i].SetActive(true);
            hidden.Clear();
        }
    }
}
