using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AN INVISIBLE NAME ON A PART OF THE SCREEN the tutorial guide may point at — put on the element where the window
/// builds it, never looked up by its name or position. While the element is on screen it is in <see cref="Live"/>; a
/// window that closes switches its rows off and so takes them out.
///
/// <para><see cref="station"/> says which station's window a station row belongs to, so a guide for the smith never
/// lights the same-named row at the loom.</para>
/// </summary>
public class GuideMarker : MonoBehaviour
{
    public GuideSpot spot;
    public string itemId = "";
    public CraftStation station = CraftStation.None;

    static readonly List<GuideMarker> live = new List<GuideMarker>(32);

    /// <summary>Every marker whose element is switched on right now.</summary>
    public static IReadOnlyList<GuideMarker> Live => live;

    void OnEnable() => live.Add(this);
    void OnDisable() => live.Remove(this);

    // Looked up once each — the guide asks every frame while it is up.
    ScrollRect scroll;
    Button button;
    bool looked;

    /// <summary>The scrolling list this part sits in, or null.</summary>
    public ScrollRect Scroll { get { Look(); return scroll; } }

    /// <summary>The button this part is, or null.</summary>
    public Button Button { get { Look(); return button; } }

    void Look()
    {
        if (looked) return;
        looked = true;
        scroll = GetComponentInParent<ScrollRect>();
        button = GetComponent<Button>();
    }

    /// <summary>Put a marker on <paramref name="go"/>.</summary>
    public static GuideMarker Put(GameObject go, GuideSpot spot, string itemId = "", CraftStation station = CraftStation.None)
    {
        if (go == null || spot == GuideSpot.None) return null;
        var m = go.GetComponent<GuideMarker>() ?? go.AddComponent<GuideMarker>();
        m.spot = spot;
        m.itemId = itemId ?? "";
        m.station = station;
        return m;
    }

    /// <summary>The newest live marker of this kind, item and station — the row a window built last, so a row being
    /// replaced this frame never wins over the one replacing it.</summary>
    public static GuideMarker Find(GuideSpot spot, string itemId, CraftStation station)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var m = live[i];
            if (m == null || m.spot != spot) continue;
            if (NeedsItem(spot) && m.itemId != itemId) continue;
            if (NeedsStation(spot) && m.station != station) continue;
            return m;
        }
        return null;
    }

    static bool NeedsItem(GuideSpot s) =>
        s == GuideSpot.StationForgeRow || s == GuideSpot.StationPieceRow || s == GuideSpot.StationFoldButton
        || s == GuideSpot.BagRow;

    static bool NeedsStation(GuideSpot s) => s != GuideSpot.BagRow && s != GuideSpot.None;
}
