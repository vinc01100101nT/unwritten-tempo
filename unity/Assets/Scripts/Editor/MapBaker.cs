using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools ▸ unwritten ▸ Bake Map (props + collision).
///
/// The performance-first, one-click pre-bake for a HAND-PAINTED map (the generator does this
/// automatically). It turns the open scene's Obstacles/Buildings tilemaps into:
///   • a single merged <b>Collision</b> tilemap (CompositeCollider2D) — buildings block their whole
///     footprint, trees block only their bottom row (trunk), so you still walk under canopies;
///   • static, foot-sorted prop objects (via <see cref="DepthSortRuntime.BakeObstacles"/>) with fixed
///     sort orders and no per-frame components.
///
/// After this the scene needs zero on-Play generation. Run it again after editing obstacle tiles.
/// </summary>
public static class MapBaker
{
    static readonly Vector3Int[] N4 = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

    [MenuItem("Tools/unwritten/Bake Map (props + collision)")]
    public static void Bake()
    {
        var grid = Object.FindFirstObjectByType<Grid>();
        if (grid == null)
        {
            EditorUtility.DisplayDialog("Bake Map",
                "No Grid in the open scene. Open a map scene, then run this again.", "OK");
            return;
        }

        // 1. Derive the blocking FULL cells from the still-raw obstacle/building tilemaps.
        var blockFull = new HashSet<Vector2Int>();
        var sourceTilemaps = new List<Tilemap>();
        foreach (var tm in grid.GetComponentsInChildren<Tilemap>(true))
        {
            string n = tm.name.ToLowerInvariant();
            if (n.Contains("collision")) continue;
            bool building = n.Contains("building") || n.Contains("house") || n.Contains("wall") ||
                            n.Contains("fence") || n.Contains("solid");
            bool tree = n.Contains("tree") || n.Contains("obstacle") || n.Contains("foliage") || n.Contains("bush");
            if (!building && !tree) continue;
            sourceTilemaps.Add(tm);

            foreach (var cluster in FindClusters(tm))
            {
                if (building)
                {
                    foreach (var c in cluster) blockFull.Add(new Vector2Int(c.x, c.y));
                }
                else   // tree: bottom row only (trunk) → walk under the canopy
                {
                    int minY = int.MaxValue;
                    foreach (var c in cluster) minY = Mathf.Min(minY, c.y);
                    foreach (var c in cluster) if (c.y == minY) blockFull.Add(new Vector2Int(c.x, c.y));
                }
            }
        }

        if (sourceTilemaps.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Map",
                "No Obstacles/Buildings tilemap with tiles found. Paint trees/houses on a tilemap " +
                "named \"Obstacles\" or \"Buildings\", then run this.", "OK");
            return;
        }

        // 2. Materialise merged half-cell collision (full blocking cells → their 4 half-cells each).
        var collision = CollisionLayerTools.GetOrCreate(grid);
        CollisionLayerTools.Clear(collision);   // idempotent re-bake
        if (blockFull.Count > 0)
        {
            var halfCells = new List<Vector2Int>();
            foreach (var c in blockFull) halfCells.AddRange(PropTemplate.HalfCellsOf(c));
            CollisionLayerTools.Paint(grid, collision, halfCells);
        }

        // 3. Drop any old per-tilemap colliders so collision lives ONLY on the merged Collision object.
        foreach (var tm in sourceTilemaps)
            foreach (var col in tm.GetComponents<Collider2D>())
                col.enabled = false;

        // 4. Bake the visuals into static foot-sorted props (disables the raw tilemap renderers).
        DepthSortRuntime.BakeObstacles();

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Debug.Log($"[unwritten] Baked map → {blockFull.Count} blocking cell(s) merged into one Collision object " +
                  $"+ {sourceTilemaps.Count} tilemap(s) baked into static foot-sorted props. Zero on-Play generation. " +
                  "(To revert: delete 'ObstacleProps' + 'Collision' and re-enable the tilemap renderers.)");
    }

    // 4-connected flood fill over painted cells → one cluster per object.
    static List<List<Vector3Int>> FindClusters(Tilemap tm)
    {
        tm.CompressBounds();
        var visited = new HashSet<Vector3Int>();
        var clusters = new List<List<Vector3Int>>();

        foreach (var start in tm.cellBounds.allPositionsWithin)
        {
            if (!tm.HasTile(start) || visited.Contains(start)) continue;
            var cluster = new List<Vector3Int>();
            var q = new Queue<Vector3Int>();
            q.Enqueue(start); visited.Add(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                cluster.Add(c);
                foreach (var d in N4)
                {
                    var nb = c + d;
                    if (tm.HasTile(nb) && visited.Add(nb)) q.Enqueue(nb);
                }
            }
            clusters.Add(cluster);
        }
        return clusters;
    }
}
