#if UNITY_ANDROID
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes the existing HUD finger-sized on a phone by enlarging every canvas that sizes itself against a
/// reference screen. The HUD was laid out for a monitor, where a hotbar slot is a comfortable mouse target but
/// lands around a sixth of an inch on a phone — well under the ~0.3in a fingertip needs. Halving a canvas's
/// reference resolution doubles everything drawn on it, which brings a slot back to a tappable size.
///
/// <para>Nothing is edited on disk: this runs at play time on Android only, so the scenes the desktop build
/// loads are untouched.</para>
///
/// <para><b>Known limit, and it is why the real window pass is still owed:</b> doubling scales the big windows
/// too, so an inventory or equipment panel will be cramped on a small screen and will not fit at all in
/// portrait. Making those windows phone-shaped is a layout job, not a scale factor.</para>
///
/// <para>Canvases appear late (the death screen builds itself the first time you die), so this re-checks on a
/// slow cadence rather than once at startup, and remembers the ones it has already handled so a canvas is never
/// scaled twice.</para>
/// </summary>
public class TouchUiScale : MonoBehaviour
{
    /// <summary>How much bigger the existing HUD is drawn. Chosen to clear the ~0.3in minimum touch target for a
    /// hotbar slot on a typical phone; not measured on a device yet.</summary>
    const float Enlarge = 2f;

    /// <summary>Seconds between sweeps. Slow on purpose — finding every canvas is not a per-frame cost worth
    /// paying, and a screen that builds itself mid-session can wait a beat to be resized.</summary>
    const float SweepSeconds = 1f;

    readonly HashSet<int> done = new HashSet<int>();
    float nextSweep;

    void Update()
    {
        if (Time.unscaledTime < nextSweep) return;
        nextSweep = Time.unscaledTime + SweepSeconds;

        var scalers = Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None);
        for (int i = 0; i < scalers.Length; i++)
        {
            var s = scalers[i];
            if (s == null || s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            int id = s.GetInstanceID();
            if (!done.Add(id)) continue;
            s.referenceResolution = s.referenceResolution / Enlarge;
        }
    }
}
#endif
