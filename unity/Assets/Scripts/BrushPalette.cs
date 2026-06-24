using System.Collections.Generic;
using UnityEngine;

/// <summary>Is a brush a placed PROP (house/tree/wall) or a GROUND texture (path/dirt/water)? Splits the painter
/// into two layers: ground paint underneath, objects on top — and means ground brushes have no footprint problem.</summary>
public enum BrushKind { Object, GroundPaint }

/// <summary>
/// One painter brush. The <b>single source of truth</b> shared by the Map Painter (via the exported manifest) and
/// the recipe generator: a colour-coded category that resolves to real Pxltgr prefabs (object brushes) or a
/// <c>ground_*</c> texture (ground brushes). New assets are folded in by the palette window's Auto-fill.
/// </summary>
[System.Serializable]
public class MapBrush
{
    [Tooltip("Stable key written into map JSON (e.g. 'houses'). Lowercase, no spaces.")]
    public string id = "houses";
    public string label = "Houses";
    [Tooltip("Swatch colour shown in the painter, hex like #CC3B3B.")]
    public string colorHex = "#CC3B3B";
    public BrushKind kind = BrushKind.Object;

    [Tooltip("Object brushes: which Pxltgr category these prefabs belong to (auto-fill classifies by this).")]
    public PropCategory category = PropCategory.None;
    [Tooltip("Resolved prefab names in Assets/Prefabs/Props (filled by Auto-fill; overrides Category when set). " +
             "This is the editable source the recipe + manifest both read.")]
    public List<string> prefabNames = new List<string>();

    [Tooltip("GroundPaint brushes only: the ground_* texture prop this paints (e.g. ground_road). " +
             "Empty = placeholder with no asset yet (e.g. water).")]
    public string groundTex = "";

    [Tooltip("Object brushes: yaw (degrees) added on import to align the prefab's real FRONT with the painter's " +
             "facing arrow. The painter arrow = 'where I want the front'; meshes whose local front isn't +Z need a " +
             "correction here (e.g. Pxltgr houses front −Z → 180). Per prop-pack, not per painted instance.")]
    public float forwardOffset = 0f;
}

/// <summary>
/// The brush set for the Map Painter pipeline (Assets ▸ Create ▸ unwritten ▸ Brush Palette, or the
/// <c>Tools ▸ unwritten ▸ World ▸ Brush Palette…</c> window). Edited in Unity, exported to the web painter as
/// <c>prop-manifest.json</c>, and read by <see cref="RecipeGenerator"/> so category→prefab lists live in ONE place.
/// </summary>
[CreateAssetMenu(fileName = "BrushPalette", menuName = "unwritten/Brush Palette", order = 1)]
public class BrushPalette : ScriptableObject
{
    [Tooltip("World size of one painter grid cell (≈ one building plot).")]
    public float cellSize = 4f;
    [Tooltip("Scale prefabs are measured at for the manifest and placed at by the importer (matches the recipe's 0.3).")]
    public float defaultScale = 0.3f;
    public List<MapBrush> brushes = new List<MapBrush>();

    /// <summary>The brush whose <see cref="MapBrush.category"/> matches <paramref name="c"/> and has resolved
    /// prefab names, or null. Used by the recipe generator to read the palette as the source of truth.</summary>
    public MapBrush FindByCategory(PropCategory c)
    {
        if (brushes == null) return null;
        foreach (var b in brushes)
            if (b != null && b.kind == BrushKind.Object && b.category == c && b.prefabNames != null && b.prefabNames.Count > 0)
                return b;
        return null;
    }
}
