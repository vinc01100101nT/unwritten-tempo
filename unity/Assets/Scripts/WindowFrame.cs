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
        UiLayers.Raise(transform, UiLayers.Layer.Window);   // a freshly-opened window comes to the front
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
