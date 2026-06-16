using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The in-memory result of generation: per-cell tile choices for each layer, plus the
/// door / gate / spawn positions. No Unity scene objects — <c>MapGeneratorWindow</c> commits
/// this to real tilemaps + entities. Cell (0,0) is bottom-left; index = y*width + x.
/// </summary>
public class TownLayout
{
    public readonly int width, height;
    public readonly TileBase[] ground, road, decor, tree, overhead;   // per-cell, null = empty
    public readonly Dictionary<Vector2Int, TileBase> building = new Dictionary<Vector2Int, TileBase>(); // solid (→ Buildings)
    public readonly HashSet<Vector2Int> solid = new HashSet<Vector2Int>();   // blocks walking (buildings, boundary, trees)
    // The ACTUAL in-game collision footprint in HALF-CELL coords (each = ½ a world cell on each axis;
    // full cell (X,Y) → half-cells (2X,2Y),(2X+1,2Y),(2X,2Y+1),(2X+1,2Y+1)). Per-prop, resolved from
    // PropTemplate.CollisionFootprint() (Auto/Custom/None) plus the boundary. Distinct from `solid`,
    // which is the generator's full-cell layout constraint (road routing / connectivity). This set is
    // what gets pre-baked into the merged Collision object (half-size boxes); `solid` never collides.
    public readonly HashSet<Vector2Int> collision = new HashSet<Vector2Int>();
    public readonly HashSet<Vector2Int> protectedCells = new HashSet<Vector2Int>();   // stamped props + boundary: roads/carving NEVER tunnel through these
    public readonly List<Vector2Int> doors = new List<Vector2Int>();         // house entrance "approach" cells
    public readonly List<Vector2Int> npcSpawns = new List<Vector2Int>();
    public readonly List<Vector2Int> monsterSpawns = new List<Vector2Int>();
    public readonly List<Gate> gates = new List<Gate>();
    public Vector2Int center;

    public TownLayout(int w, int h)
    {
        width = w; height = h;
        int n = w * h;
        ground = new TileBase[n]; road = new TileBase[n]; decor = new TileBase[n];
        tree = new TileBase[n]; overhead = new TileBase[n];
    }

    public int Idx(int x, int y) => y * width + x;
    public int Idx(Vector2Int c) => c.y * width + c.x;
    public bool In(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
    public bool In(Vector2Int c) => In(c.x, c.y);
    public bool IsRoad(Vector2Int c) => In(c) && road[Idx(c)] != null;
    public bool Walkable(Vector2Int c) => In(c) && !solid.Contains(c);

    public void AddSolid(Vector2Int c) { if (In(c)) solid.Add(c); }
    /// <summary>Mark a HALF-cell as real in-game collision. Bounds are the half-grid (2× the full grid),
    /// NOT In() — In() is a full-cell check and would drop everything past the map midpoint (the
    /// "collision only in the lower-left quarter" bug).</summary>
    public void AddCollision(Vector2Int c)
    {
        if (c.x >= 0 && c.y >= 0 && c.x < width * 2 && c.y < height * 2) collision.Add(c);
    }
    /// <summary>Solid AND immovable: a stamped prop or boundary cell that carving must route around, never through.</summary>
    public void AddProtectedSolid(Vector2Int c) { if (In(c)) { solid.Add(c); protectedCells.Add(c); } }
    public void ClearSolid(Vector2Int c)
    {
        if (protectedCells.Contains(c)) return;   // never erase a placed prop / boundary
        solid.Remove(c); building.Remove(c); if (In(c)) tree[Idx(c)] = null;
    }
}

/// <summary>A gap in the boundary the player enters/leaves through — becomes a Portal + SpawnPoint.</summary>
public struct Gate
{
    public Vector2Int cell;      // the boundary-gap cell
    public Vector2Int outward;   // cardinal direction pointing off-map
    public string id;            // SpawnPoint id (e.g. "Gate_N")
}
