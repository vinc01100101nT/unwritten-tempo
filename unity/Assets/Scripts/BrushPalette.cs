using System.Collections.Generic;
using UnityEngine;

/// <summary>Where a palette entry paints: a placed PROP on top (Object), the autotiled modular WALL kit, or a
/// GROUND texture underneath. Replaces the old preset-brush model — you drag your own assets and pick the role.</summary>
public enum BrushRole { Object, Wall, Ground }

/// <summary>
/// One row in the palette: some assets you dragged in + the role that says where they go. IDs, swatch colours and
/// ground blend-channels are all DERIVED (see <see cref="BrushPalette.Resolve"/>) — you never hand-type them.
/// </summary>
[System.Serializable]
public class PaletteEntry
{
    public BrushRole role = BrushRole.Object;
    [Tooltip("Optional display name shown in the painter. Blank = derived from the first asset.")]
    public string label = "";
    [Tooltip("Optional swatch colour (hex). Blank = auto-generated from the label so every brush looks distinct.")]
    public string colorHex = "";

    [Tooltip("Object / Wall: the prefab(s) this brush places. Multiple = a group that scatters a random variant.")]
    public List<GameObject> prefabs = new List<GameObject>();

    [Tooltip("Ground: the texture (or material/sprite/ground prop) this slot paints. Order among Ground rows picks " +
             "its blend channel (first = the default underlayer, the next four = paintable overlays; max 5).")]
    public Object groundAsset;

    [Tooltip("Object: yaw (degrees) added on import so the mesh's real FRONT lines up with the painter's facing " +
             "arrow (e.g. a house whose front faces −Z needs 180). Per asset-source, not per painted instance.")]
    public float forwardOffset = 0f;
}

/// <summary>The fully-derived brush the painter manifest + the map importer consume — the ONE place ids, colours and
/// the ground blend-channel are computed from the raw <see cref="PaletteEntry"/> rows, so both sides always agree.</summary>
public class ResolvedBrush
{
    public string id, label, colorHex, groundTex = "";
    public BrushRole role;
    public int channel = -1;                         // Ground only: 0 = base underlayer, 1..4 = blend overlays
    public List<string> prefabNames = new List<string>();
    public float forwardOffset;
    public bool IsGround => role == BrushRole.Ground;
    public bool IsWall   => role == BrushRole.Wall;
}

/// <summary>
/// The Map Painter's palette: the assets you chose, tagged by role. Edited in the
/// <c>Tools ▸ unwritten ▸ World ▸ Brush Palette…</c> window, exported to the web painter as <c>prop-manifest.json</c>,
/// and read by <see cref="MapPainterImporter"/> on import. Ground rendering is a fixed 5-texture blend, so at most
/// five Ground rows are used — the extras are ignored (the window warns).
/// </summary>
[CreateAssetMenu(fileName = "BrushPalette", menuName = "unwritten/Brush Palette", order = 1)]
public class BrushPalette : ScriptableObject
{
    [Tooltip("World size of one painter grid cell (≈ one building plot).")]
    public float cellSize = 4f;
    // Props are placed at their NATIVE prefab size (the prefab owns its size, single source of truth) — no scale knob.
    public List<PaletteEntry> entries = new List<PaletteEntry>();

    public const int MaxGroundChannels = 5;   // GroundBlend.shader = 1 base + 4 blend layers

    /// <summary>Derive the final brush list: assign ground blend-channels by row order, generate stable ids and
    /// swatch colours, collect prefab names. The single source of truth both the exporter and importer read.</summary>
    public List<ResolvedBrush> Resolve()
    {
        var outList = new List<ResolvedBrush>();
        var usedIds = new HashSet<string>();
        int groundCount = 0;

        foreach (var e in entries)
        {
            if (e == null) continue;

            var rb = new ResolvedBrush { role = e.role, forwardOffset = e.forwardOffset };

            if (e.role == BrushRole.Ground)
            {
                if (groundCount >= MaxGroundChannels || e.groundAsset == null) continue;   // over the shader's 5 slots → dropped
                rb.channel = groundCount++;
                rb.groundTex = e.groundAsset.name;
                rb.label = string.IsNullOrWhiteSpace(e.label) ? e.groundAsset.name : e.label.Trim();
            }
            else
            {
                foreach (var pf in e.prefabs)
                    if (pf != null) rb.prefabNames.Add(pf.name);
                if (rb.prefabNames.Count == 0) continue;
                rb.label = string.IsNullOrWhiteSpace(e.label)
                    ? (e.role == BrushRole.Wall ? "Wall" : rb.prefabNames[0])
                    : e.label.Trim();
            }

            // Wall keeps the reserved "wall" id the painter + importer autotile on; everything else is name-derived + unique.
            rb.id = e.role == BrushRole.Wall ? "wall" : UniqueId(rb.label, usedIds);
            usedIds.Add(rb.id);
            rb.colorHex = string.IsNullOrWhiteSpace(e.colorHex) ? AutoColor(rb.id) : e.colorHex.Trim();
            outList.Add(rb);
        }
        return outList;
    }

    /// <summary>The painter ground-brush id bound to a blend channel (0 base … 4), or "" if that channel is unused.
    /// Lets the recipe generator's sidecar name ground cells to match whatever ground assets the author picked.</summary>
    public string GroundIdForChannel(int channel)
    {
        foreach (var rb in Resolve())
            if (rb.IsGround && rb.channel == channel) return rb.id;
        return "";
    }

    static string UniqueId(string label, HashSet<string> used)
    {
        string baseId = Sanitize(label);
        if (string.IsNullOrEmpty(baseId)) baseId = "brush";
        string id = baseId;
        for (int i = 2; used.Contains(id); i++) id = baseId + i;
        return id;
    }

    static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString().Trim('_');
    }

    // Deterministic pleasant swatch from the id's hash → stable per brush, distinct across brushes, no manual picking.
    static string AutoColor(string id)
    {
        int h = 0; foreach (char c in id) h = h * 31 + c;
        float hue = (h & 0x7fffffff) % 360 / 360f;
        Color c2 = Color.HSVToRGB(hue, 0.55f, 0.85f);
        return "#" + ColorUtility.ToHtmlStringRGB(c2);
    }
}
