using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shared chrome behavior for a HUD window root, completing the locked "shared window rules":
/// it REMEMBERS its on-screen position between sessions (PlayerPrefs) and brings itself to the
/// FRONT when clicked anywhere. Title-bar DRAGGING is handled by the existing
/// <see cref="WindowDragHandle"/> on the header — this composes with it rather than replacing it.
///
/// JS/TS analogy: a small controller that, on mount, restores the window's saved x/y from
/// localStorage and raises its z-index on click; on unmount, persists x/y again.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WindowFrame : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("Unique key for saving this window's position. Blank = use the GameObject name.")]
    public string persistKey = "";

    RectTransform rt;
    string KeyX => "win_" + (string.IsNullOrEmpty(persistKey) ? name : persistKey) + "_x";
    string KeyY => "win_" + (string.IsNullOrEmpty(persistKey) ? name : persistKey) + "_y";

    void Awake() => rt = (RectTransform)transform;

    void OnEnable()
    {
        if (rt == null) rt = (RectTransform)transform;
        if (PlayerPrefs.HasKey(KeyX))
            rt.anchoredPosition = new Vector2(PlayerPrefs.GetFloat(KeyX), PlayerPrefs.GetFloat(KeyY));
        KeepOnScreen();
        UiLayers.Raise(transform, UiLayers.Layer.Window);   // a freshly-opened window comes to the front
    }

    // A REMEMBERED PLACE CAN BE OFF THE SCREEN NOW: the window may have grown since it was saved (the Skills window
    // doubled in width), or the screen may be smaller. So the window is slid back inside its canvas — only as far as
    // it sticks out, so a window that already fits does not move at all.
    void KeepOnScreen()
    {
        var parent = rt.parent as RectTransform;
        if (parent == null || rt.anchorMin != rt.anchorMax) return;
        Rect area = parent.rect;
        Vector2 size = rt.rect.size;
        Vector2 anchor = new Vector2(Mathf.Lerp(area.xMin, area.xMax, rt.anchorMin.x), Mathf.Lerp(area.yMin, area.yMax, rt.anchorMin.y));
        Vector2 lo = area.min - anchor + Vector2.Scale(rt.pivot, size);
        Vector2 hi = area.max - anchor - Vector2.Scale(Vector2.one - rt.pivot, size);
        Vector2 p = rt.anchoredPosition;
        p.x = lo.x <= hi.x ? Mathf.Clamp(p.x, lo.x, hi.x) : (lo.x + hi.x) * 0.5f;
        p.y = lo.y <= hi.y ? Mathf.Clamp(p.y, lo.y, hi.y) : (lo.y + hi.y) * 0.5f;
        rt.anchoredPosition = p;
    }

    void OnDisable() => Save();
    void OnApplicationQuit() => Save();

    void Save()
    {
        if (rt == null) return;
        var p = rt.anchoredPosition;
        PlayerPrefs.SetFloat(KeyX, p.x);
        PlayerPrefs.SetFloat(KeyY, p.y);
    }

    public void OnPointerDown(PointerEventData e) => UiLayers.Raise(transform, UiLayers.Layer.Window);
}
