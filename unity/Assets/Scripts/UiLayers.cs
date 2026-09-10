using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one place that decides WHAT FLOATS ABOVE WHAT on the HUD.
///
/// <para>Unity draws children in order, so the last child of a canvas is the thing on top. Every floating
/// piece of the interface used to shout <c>SetAsLastSibling</c> for itself, and whoever shouted last won —
/// which is a race, not an order. A menu opened by a right-click lost that race to the window underneath it,
/// because the same click raised the window; before that the hotbar's tip lost it to its own cells, and the
/// buff card lost it to the icons. Three separate fixes for one missing idea.</para>
///
/// <para>So the order lives here, once, as <see cref="Layer"/>. A floating thing calls
/// <see cref="Raise"/> with the layer it belongs to and this puts it in the right place relative to
/// everything else that is up — no matter what order things opened in. Nothing is re-parented, so every
/// object keeps the coordinate space and the anchoring it already had.</para>
///
/// <para><b>Adding a new floating thing:</b> call <see cref="Raise"/> when it appears, and name its layer.
/// There is no other way to come to the front, and forgetting is what this class exists to prevent.</para>
/// </summary>
public static class UiLayers
{
    /// <summary>Low to high — later members draw on top of earlier ones.</summary>
    public enum Layer
    {
        /// <summary>A draggable panel with a title bar. The bag, the character sheet, the skill book.</summary>
        Window = 0,

        /// <summary>Something raised BY a window and belonging to it: the right-click menu, a dialog.</summary>
        Menu = 1,

        /// <summary>Explains whatever is under the cursor, and is never interacted with.</summary>
        Tooltip = 2,

        /// <summary>The icon that follows the cursor mid-drag. Above everything it might be dropped on.</summary>
        DragGhost = 3,

        /// <summary>Covers the whole screen while the world is not ready. Above all of it.</summary>
        Loading = 4,
    }

    struct Entry
    {
        public Transform t;
        public Layer layer;
    }

    // Small and only touched when something opens or a drag starts — never per frame.
    static readonly List<Entry> tracked = new List<Entry>(16);
    static readonly List<Entry> ordering = new List<Entry>(16);

    /// <summary>Bring <paramref name="t"/> to the front OF ITS LAYER, and keep every higher layer above it.
    /// Call this every time the thing appears; calling it twice is harmless.</summary>
    public static void Raise(Transform t, Layer layer)
    {
        if (t == null) return;

        bool known = false;
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            if (tracked[i].t == null) { tracked.RemoveAt(i); continue; }   // destroyed since last time
            if (tracked[i].t != t) continue;
            tracked[i] = new Entry { t = t, layer = layer };
            known = true;
        }
        if (!known) tracked.Add(new Entry { t = t, layer = layer });

        // This one goes up first, so within its own layer the most recent thing is the front one.
        t.SetAsLastSibling();

        // Then everything ABOVE it goes up again, lowest layer first, so the declared order holds however
        // the pieces happened to open. Only siblings under the same parent can be ordered against each other.
        var parent = t.parent;
        ordering.Clear();
        for (int i = 0; i < tracked.Count; i++)
        {
            var e = tracked[i];
            if (e.t == null || e.t == t || e.t.parent != parent) continue;
            if (e.layer <= layer) continue;
            ordering.Add(e);
        }
        for (int a = 1; a < ordering.Count; a++)          // insertion sort: the list is tiny and never grows
        {
            var key = ordering[a];
            int b = a - 1;
            while (b >= 0 && ordering[b].layer > key.layer) { ordering[b + 1] = ordering[b]; b--; }
            ordering[b + 1] = key;
        }
        for (int i = 0; i < ordering.Count; i++) ordering[i].t.SetAsLastSibling();
    }

    /// <summary>Stop tracking something that is gone for good. Optional — a destroyed object is dropped on
    /// the next <see cref="Raise"/> anyway — but it keeps the list honest for anything long-lived.</summary>
    public static void Forget(Transform t)
    {
        for (int i = tracked.Count - 1; i >= 0; i--)
            if (tracked[i].t == null || tracked[i].t == t) tracked.RemoveAt(i);
    }
}
