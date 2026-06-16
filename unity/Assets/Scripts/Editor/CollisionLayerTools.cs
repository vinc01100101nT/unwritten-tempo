using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the single, pre-baked <b>Collision</b> object that holds a town's whole collision shape at
/// HALF-CELL resolution — the performance-first replacement for on-Play colliders.
///
/// Collision is stored in HALF-cells (each = ½ a world cell on each axis), so a 3×3 prop is edited as a
/// 6×6 grid and thin objects can block just part of a cell. Each blocking row-run becomes one
/// <see cref="BoxCollider2D"/> on ONE "Collision" GameObject backed by a STATIC <see cref="Rigidbody2D"/>
/// — i.e. plain static colliders (the proven, reliable pattern: each box blocks on its own, and a
/// static body is a Pathfinder wall). It renders nothing but shows as green collider outlines in the
/// Scene view, so you can SEE collision while authoring.
///
/// We deliberately do NOT use a CompositeCollider2D: a composite generated in the editor doesn't keep
/// its geometry when the scene is loaded at Play (it regenerates, and the result was empty → pass-through).
/// Greedy horizontal run-merging already keeps the box count low, so plain boxes stay cheap.
/// </summary>
public static class CollisionLayerTools
{
    public const string ObjectName = "Collision";

    /// <summary>Find or create the Collision object under <paramref name="grid"/> (static body, plain boxes).</summary>
    public static GameObject GetOrCreate(Grid grid)
    {
        foreach (Transform child in grid.transform)
            if (child.name == ObjectName && child.GetComponent<Rigidbody2D>() != null)
                return child.gameObject;

        var go = new GameObject(ObjectName);
        go.transform.SetParent(grid.transform, false);
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;   // static body → blocks dynamic player + Pathfinder wall
        return go;
    }

    /// <summary>Remove all box colliders (for an idempotent re-bake). Keeps the object + body.</summary>
    public static void Clear(GameObject collision)
    {
        foreach (var box in collision.GetComponents<BoxCollider2D>())
            Object.DestroyImmediate(box);
    }

    /// <summary>
    /// Add the half-cells as static box colliders. Half-cells are greedily merged into horizontal RUNS
    /// first (one box per contiguous row span instead of one per cell), so a solid wall or boundary
    /// becomes a few long boxes — cheap, and reliable since every box blocks on its own.
    /// </summary>
    public static void Paint(Grid grid, GameObject collision, IEnumerable<Vector2Int> halfCells)
    {
        // Group half-cells by row, then split each row into runs of consecutive columns.
        var byRow = new Dictionary<int, List<int>>();
        foreach (var hc in new HashSet<Vector2Int>(halfCells))
        {
            if (!byRow.TryGetValue(hc.y, out var xs)) byRow[hc.y] = xs = new List<int>();
            xs.Add(hc.x);
        }

        Vector3 baseWorld = collision.transform.position;
        Vector2 cs = grid.cellSize;
        float halfW = cs.x * 0.5f, halfH = cs.y * 0.5f;

        foreach (var kv in byRow)
        {
            var xs = kv.Value;
            xs.Sort();
            int i = 0;
            while (i < xs.Count)
            {
                int start = xs[i], end = xs[i];
                while (i + 1 < xs.Count && xs[i + 1] == end + 1) end = xs[++i];
                i++;
                int len = end - start + 1;

                Vector3 c0 = HalfCellCenter(grid, new Vector2Int(start, kv.Key));
                Vector3 c1 = HalfCellCenter(grid, new Vector2Int(end, kv.Key));
                var box = collision.AddComponent<BoxCollider2D>();
                box.size = new Vector2(halfW * len, halfH);
                box.offset = (Vector2)((c0 + c1) * 0.5f - baseWorld);
            }
        }
    }

    /// <summary>World-space centre of a half-cell (cellSize ÷ 2 on each axis), aligned to the grid.</summary>
    public static Vector3 HalfCellCenter(Grid grid, Vector2Int hc)
    {
        Vector2 cs = grid.cellSize;
        int fx = FloorDiv(hc.x, 2), fy = FloorDiv(hc.y, 2);   // the full cell this half-cell sits in
        int qx = hc.x - fx * 2, qy = hc.y - fy * 2;           // quadrant within it (0 = low half, 1 = high)
        Vector3 fc = grid.GetCellCenterWorld(new Vector3Int(fx, fy, 0));
        float ox = (qx == 0 ? -0.25f : 0.25f) * cs.x;
        float oy = (qy == 0 ? -0.25f : 0.25f) * cs.y;
        return fc + new Vector3(ox, oy, 0f);
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
