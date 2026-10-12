using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A tiny "AUTO-COMBAT" tag pinned near the bottom of the screen, shown while auto-combat is hunting for the player. Built at runtime on the main screen layer (<see cref="UiKit.HudCanvas"/>), so it grows with the
/// screen like the rest of the HUD and needs no scene wiring — <see cref="Player3DController"/> spawns one in Awake
/// (mirroring how it spawns its TargetIndicator) and toggles it via <see cref="Show"/>. Non-interactive: text raycast off.
/// </summary>
public class AutoPilotIndicator : MonoBehaviour
{
    TMP_Text label;

    public static AutoPilotIndicator Create()
    {
        var go = new GameObject("AutoPilotIndicator", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        var hud = UiKit.HudCanvas;
        if (hud != null)
        {
            var rt = (RectTransform)go.transform;
            rt.SetParent(hud.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();   // part of the screen, under every window
        }

        var ind = go.AddComponent<AutoPilotIndicator>();
        ind.Build();
        ind.Show(false);
        return ind;
    }

    void Build()
    {
        // Sizes on the main layer's 1280-wide plan — the same size on screen it had on its own 1920-wide layer.
        label = UiKit.MakeText("Label", transform, "AUTO-COMBAT  —  press " + GameKeys.Label(GameKeys.AutoPilot) + " to stop",
                               13, FontStyle.Bold, TextAnchor.MiddleCenter);
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.085f);   // bottom-centre, above the HUD bars
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280, 23);
        label.color = new Color(1f, 0.5f, 0.4f);
        label.raycastTarget = false;
    }

    public void Show(bool on) { if (label != null && label.enabled != on) label.enabled = on; }
}
