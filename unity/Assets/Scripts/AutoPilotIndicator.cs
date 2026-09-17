using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A tiny "AUTO-PILOT" tag pinned near the bottom of the screen, shown while the player's auto-pilot AI is
/// fighting for them. Builds its OWN Screen-Space-Overlay canvas at runtime so it needs no scene wiring —
/// <see cref="Player3DController"/> spawns one in Awake (mirroring how it spawns its TargetIndicator)
/// and toggles it via <see cref="Show"/>. Non-interactive: no raycaster, text raycast off.
/// </summary>
public class AutoPilotIndicator : MonoBehaviour
{
    TMP_Text label;

    public static AutoPilotIndicator Create()
    {
        var go = new GameObject("AutoPilotIndicator") { hideFlags = HideFlags.DontSaveInEditor };
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;                       // above the world, fine under modal panels
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        UiKit.ApplyTextCrispness(scaler);

        var ind = go.AddComponent<AutoPilotIndicator>();
        ind.Build();
        ind.Show(false);
        return ind;
    }

    void Build()
    {
        label = UiKit.MakeText("Label", transform, "AUTO-PILOT  —  press S to stop", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.085f);   // bottom-centre, above the HUD bars
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420, 34);
        label.color = new Color(1f, 0.5f, 0.4f);
        label.raycastTarget = false;
    }

    public void Show(bool on) { if (label != null && label.enabled != on) label.enabled = on; }
}
