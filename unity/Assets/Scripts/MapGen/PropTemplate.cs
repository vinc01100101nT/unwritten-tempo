using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// A captured multi-cell OBJECT footprint — the tiles of one hand-painted prop (house, tree,
/// well, statue, market stall, sign…), recorded per source layer and normalised so the
/// bottom-left of its bounding box is cell (0,0). The town generator stamps these as whole
/// units instead of placing loose single tiles.
///
/// This is the ONE generic object the generator places — there is deliberately no separate
/// "house" or "tree" type. WHAT a prop is comes from its captured layers (Buildings/wall/tree
/// → solid; Ground/Decor/Overhead → walkable trim); WHERE it may go comes from
/// <see cref="placement"/>. So a new kind of object never needs new code — capture it, tag it.
///
/// Author with Tools ▸ unwritten ▸ Capture Prop Template; verify with Stamp Prop Template.
/// (Formerly <c>HouseTemplate</c>; existing assets migrate automatically — same script GUID,
/// and <c>doorCell</c> carries over to <see cref="anchorCell"/>.)
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Prop Template", fileName = "PropTemplate")]
public class PropTemplate : ScriptableObject
{
    [Serializable]
    public struct Cell
    {
        [Tooltip("Source tilemap name (e.g. Ground, Buildings, Decor, Overhead). Re-stamped " +
                 "to the same-named layer so DepthSortRuntime bands/bakes it correctly.")]
        public string layer;

        [Tooltip("Cell offset from the template origin (bottom-left of the bounding box).")]
        public Vector2Int pos;

        [Tooltip("The tile painted at this cell.")]
        public TileBase tile;
    }

    [Header("Placement")]
    [Tooltip("Where the generator may place this prop:\n" +
             "• Lot — building plots; gets a path carved to the road network.\n" +
             "• OpenGround — scattered in open space (trees, rocks).\n" +
             "• PlazaCenter — single town centrepiece (well/statue).\n" +
             "• Roadside — hugging an existing road (lamps/signs/stalls).")]
    public PropPlacement placement = PropPlacement.Lot;

    [Tooltip("Relative frequency vs other props sharing this placement (higher = more common).")]
    [Min(0f)] public float weight = 1f;

    [Header("Footprint")]
    [Tooltip("Footprint size in cells (width, height).")]
    public Vector2Int size;

    [Tooltip("Anchor / entrance cell (formerly 'door'): a Lot prop carves a path from the cell " +
             "just outside this to the nearest road. Defaults to bottom-centre on capture.")]
    [FormerlySerializedAs("doorCell")]
    public Vector2Int anchorCell;

    [Tooltip("Every painted cell across all captured layers, normalised to a (0,0) origin.")]
    public List<Cell> cells = new List<Cell>();

    [Header("Collision")]
    [Tooltip("How this prop blocks movement:\n" +
             "• Auto — derived: building/wall cells block fully, tree/obstacle cells block only their " +
             "bottom row (the trunk, so you walk under the canopy).\n" +
             "• Custom — exactly the half-cells you clicked in the Inspector grid (or painted on a 'Collision' layer).\n" +
             "• None — nothing blocks (the painted cells are kept, so you can switch back to Custom).")]
    public PropCollision collisionMode = PropCollision.Auto;

    [Tooltip("HALF-cells (relative to the origin) that block movement, used when Collision Mode = Custom. " +
             "A half-cell is ½ a world cell on each axis, so a 3×3 prop is edited as a 6×6 grid — thin " +
             "objects can block part of a cell. Filled by clicking the Inspector grid, or a 'Collision' " +
             "capture layer (painted full cells expand to their 4 half-cells).")]
    public List<Vector2Int> collisionCells = new List<Vector2Int>();

    /// <summary>
    /// The local HALF-cell offsets that should block movement, resolved for the current
    /// <see cref="collisionMode"/>. Pre-baked into a merged collision shape at generate/stamp time —
    /// never recomputed at runtime. Auto: building cells full + tree cells' bottom row (trunk), each
    /// full cell expanded to its four half-cells.
    /// </summary>
    public List<Vector2Int> CollisionFootprint()
    {
        var result = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>();

        if (collisionMode == PropCollision.None) return result;

        if (collisionMode == PropCollision.Custom)
        {
            foreach (var c in collisionCells) if (seen.Add(c)) result.Add(c);
            return result;
        }

        // Auto — trees block only their lowest row (trunk); buildings block their whole footprint.
        // Each blocking ART cell (full) expands to its four half-cells.
        int treeMinY = int.MaxValue;
        foreach (var cell in cells)
            if (IsTreeLayer(Lower(cell.layer))) treeMinY = Mathf.Min(treeMinY, cell.pos.y);

        foreach (var cell in cells)
        {
            string n = Lower(cell.layer);
            bool block = IsBuildingLayer(n) || (IsTreeLayer(n) && cell.pos.y == treeMinY);
            if (!block) continue;
            foreach (var h in HalfCellsOf(cell.pos))
                if (seen.Add(h)) result.Add(h);
        }
        return result;
    }

    /// <summary>The four half-cells covering a full-cell offset (x,y): (2x,2y),(2x+1,2y),(2x,2y+1),(2x+1,2y+1).</summary>
    public static IEnumerable<Vector2Int> HalfCellsOf(Vector2Int fullCell)
    {
        int x = fullCell.x * 2, y = fullCell.y * 2;
        yield return new Vector2Int(x, y);
        yield return new Vector2Int(x + 1, y);
        yield return new Vector2Int(x, y + 1);
        yield return new Vector2Int(x + 1, y + 1);
    }

    static string Lower(string s) => (s ?? "").ToLowerInvariant();
    static bool IsBuildingLayer(string n) =>
        n.Contains("building") || n.Contains("house") || n.Contains("wall") ||
        n.Contains("fence") || n.Contains("solid");
    static bool IsTreeLayer(string n) =>
        n.Contains("tree") || n.Contains("obstacle") || n.Contains("foliage") || n.Contains("bush");

    /// <summary>The distinct layer (tilemap) names this template paints into.</summary>
    public IEnumerable<string> Layers
    {
        get
        {
            var seen = new HashSet<string>();
            foreach (var c in cells)
                if (seen.Add(c.layer)) yield return c.layer;
        }
    }
}

/// <summary>Where the generator may place a <see cref="PropTemplate"/>.</summary>
public enum PropPlacement { Lot, OpenGround, PlazaCenter, Roadside }

/// <summary>How a prop blocks movement. See <see cref="PropTemplate.collisionMode"/>.</summary>
public enum PropCollision { Auto, Custom, None }
