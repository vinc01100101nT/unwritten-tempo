using UnityEngine;

/// <summary>
/// Shared look/behaviour for the IMGUI dev overlays (EffectTuner F8, SequenceTuner F9, …). Central choke point
/// so every dev window is OPAQUE (never see-through over the game) and consistent — fix the style here once
/// instead of per-window. New dev windows: call <see cref="FillWindow"/> as the first line of their window fn.
/// </summary>
public static class DevGui
{
    static Texture2D bg;

    // Solid charcoal panel fill (opaque). Cached 1×1, never GC'd.
    static Texture2D PanelBg
    {
        get
        {
            if (bg == null)
            {
                bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                bg.SetPixel(0, 0, new Color(0.12f, 0.12f, 0.14f, 1f));
                bg.Apply();
            }
            return bg;
        }
    }

    /// <summary>Paint an opaque background over the whole window. Call FIRST inside the GUILayout.Window callback
    /// (coords are window-local, so pass the window's own width/height).</summary>
    public static void FillWindow(float width, float height) => FillRect(new Rect(0, 0, width, height));

    /// <summary>Paint an opaque charcoal fill over an arbitrary rect (screen-space) — e.g. a backing drawn
    /// behind a window before GUILayout.Window so the world/HUD never bleeds through.</summary>
    public static void FillRect(Rect r)
    {
        var prev = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(r, PanelBg, ScaleMode.StretchToFill);
        GUI.color = prev;
    }
}
