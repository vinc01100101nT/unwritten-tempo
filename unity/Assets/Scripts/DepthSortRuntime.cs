using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// The single source of truth for the game's draw-order stack, applied automatically on Play.
/// Depth is split into fixed ORDER BANDS within the one Default sorting layer (sorting order is a
/// 16-bit value, so everything stays within ±32767):
///
///   Ground tilemap      −32000   behind everyone
///   Decor tilemap       −30000   under-foot decals (no collider)
///   ENTITIES         −25000..+25000   per-object by feet-Y (YDepthSorter): player/mobs/NPCs/props
///   Overhead tilemap   +30000     walk-under canopies/roofs (no collider)
///   FX               +31000..+32000   slash / click / target arrow / damage numbers
///
/// On every scene load this: points the camera transparency axis up (a tie-fallback), bands the
/// tilemaps by name, gives every in-scene unit a <see cref="YDepthSorter"/>, and bakes any still-raw
/// obstacle tilemaps into per-object props as a FALLBACK.
///
/// PERFORMANCE: the obstacle→prop bake is meant to run ONCE at author/generate time (call
/// <see cref="BakeObstacles"/> from an editor tool — the Map Generator does this), so the saved scene
/// already holds the prop objects and the play-time guard skips re-baking. Baked props are STATIC, so
/// each gets a fixed <c>sortingOrder</c> computed once from its foot-Y and carries NO per-frame
/// <see cref="YDepthSorter"/> — only moving units (player/mobs) sort every frame. Collision is NOT
/// generated here any more: it's pre-baked into a merged "Collision" tilemap (CompositeCollider2D) at
/// generate/stamp time. The bake only disables the raw tilemap's renderer; it never touches colliders.
/// </summary>
public static class DepthSortRuntime
{
    // ---- band constants (keep within ±32767) ---------------------------------
    public const int GroundOrder   = -32000;
    public const int RoadOrder     = -31000;   // walkable paths: above ground, below decor/entities
    public const int DecorOrder    = -30000;
    public const int ObstacleStopgapOrder = -28000;  // editor-preview order for the raw (un-baked) obstacle tilemap
    public const int EntityClamp   =  25000;
    public const int OverheadOrder =  30000;
    public const int FxSlash        = 31000;
    public const int FxClickMarker  = 31100;
    public const int FxTarget       = 31200;
    public const int FxPopup        = 32000;

    const float Precision = 50f;   // sortingOrder = -feetY * Precision

    static readonly Vector3 YAxis = new Vector3(0f, 1f, 0f);
    static readonly Vector3Int[] N4 = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

    /// <summary>Per-object entity order from a feet/base world-Y: lower on screen = higher = in front.</summary>
    public static int OrderForY(float y)
        => Mathf.Clamp(Mathf.RoundToInt(-y * Precision), -EntityClamp, EntityClamp);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SetupScene();
        SceneManager.sceneLoaded += (s, m) => SetupScene();
    }

    static void SetupScene()
    {
        SetupCameraAxis();
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            BandTilemap(tm);
        BakeObstacleProps();
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            if (IsUnit(sr)) EnsureUnitSorter(sr);
    }

    public static void SetupCameraAxis()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = YAxis;
        }
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = YAxis;
    }

    public static void BandTilemap(Tilemap tm)
    {
        var tr = tm != null ? tm.GetComponent<TilemapRenderer>() : null;
        if (tr == null) return;
        string n = tm.name.ToLowerInvariant();

        if (IsCollisionLayer(n)) return;   // the pre-baked collision layer renders nothing; never band it

        if (NameHas(n, "road", "street", "cobble", "path"))
            Set(tr, RoadOrder, TilemapRenderer.Mode.Chunk);
        else if (NameHas(n, "ground", "floor", "water", "grass", "base", "background", "terrain"))
            Set(tr, GroundOrder, TilemapRenderer.Mode.Chunk);
        else if (NameHas(n, "decor", "decal", "overlay", "detail", "underfoot"))
            Set(tr, DecorOrder, TilemapRenderer.Mode.Chunk);
        else if (NameHas(n, "overhead", "canopy", "roof", "treetop", "foreground", "above"))
            Set(tr, OverheadOrder, TilemapRenderer.Mode.Chunk);
        else if (IsObstacleName(n))
            Set(tr, ObstacleStopgapOrder, TilemapRenderer.Mode.Chunk);   // editor preview only; baked away at Play
    }

    public static void EnsureUnitSorter(SpriteRenderer sr)
    {
        if (sr != null && !sr.TryGetComponent<YDepthSorter>(out _))
            sr.gameObject.AddComponent<YDepthSorter>();
    }

    public static bool IsUnit(SpriteRenderer sr)
    {
        var go = sr.gameObject;
        return go.GetComponent<PlayerController2D>() != null
            || go.GetComponent<MonsterAI>() != null
            || go.GetComponent<Interactable>() != null
            || go.GetComponent<CharacterAnimator2D>() != null;
    }

    // ---- obstacle tilemap → per-object props ---------------------------------

    /// <summary>
    /// Pre-bake entry point: bake every still-raw obstacle/building tilemap in the open scene(s) into
    /// grouped, foot-sorted prop objects. Call this ONCE from an editor tool (the Map Generator does)
    /// so the saved scene holds the props and play-time never has to. Idempotent — already-baked
    /// tilemaps (renderer disabled) are skipped, so it doubles as the play-time fallback.
    /// </summary>
    public static void BakeObstacles() => BakeObstacleProps();

    // Bake every obstacle-ish tilemap into grouped, foot-sorted prop objects, then switch the raw
    // tilemap renderer off so the props are the only thing drawing. Colliders are NOT created or
    // touched here any more — collision is the pre-baked "Collision" tilemap (CompositeCollider2D).
    static void BakeObstacleProps()
    {
        Transform root = null;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            string n = tm.name.ToLowerInvariant();
            if (IsCollisionLayer(n)) continue;          // never bake the collision layer into visuals
            if (!IsObstacleName(n)) continue;

            var tr = tm.GetComponent<TilemapRenderer>();
            if (tr != null && !tr.enabled) continue;    // already baked (guard against re-runs)

            if (root == null)
            {
                root = new GameObject("ObstacleProps").transform;
                // Keep baked props in the SAME scene as the tilemap, so an additively-loaded map's
                // props unload with that map (not with whatever scene happens to be active).
                SceneManager.MoveGameObjectToScene(root.gameObject, tm.gameObject.scene);
            }

            var grid = tm.GetComponentInParent<Grid>();
            Vector2 cell = grid != null ? (Vector2)grid.cellSize : Vector2.one;
            bool solid = NameHas(n, "building", "house", "wall", "solid", "fence");

            foreach (var cluster in FindClusters(tm))
                BuildProp(tm, cluster, cell, root, solid);

            if (tr != null) tr.enabled = false;         // raw tilemap no longer draws; the props do
        }
    }

    // 4-connected flood fill over painted cells → clusters (each = one prop).
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

    static void BuildProp(Tilemap tm, List<Vector3Int> cluster, Vector2 cell, Transform root, bool solid)
    {
        // foot/base of the cluster = bottom edge of its lowest row
        int minCellY = int.MaxValue;
        foreach (var c in cluster) if (c.y < minCellY) minCellY = c.y;
        float bottomY = 0f, bMinX = float.MaxValue, bMaxX = float.MinValue;
        foreach (var c in cluster)
            if (c.y == minCellY)
            {
                Vector3 w = tm.GetCellCenterWorld(c);
                bottomY = w.y - cell.y * 0.5f;
                bMinX = Mathf.Min(bMinX, w.x - cell.x * 0.5f);
                bMaxX = Mathf.Max(bMaxX, w.x + cell.x * 0.5f);
            }

        var parent = new GameObject(solid ? "Building" : "Tree");
        parent.transform.SetParent(root, true);
        parent.transform.position = new Vector3((bMinX + bMaxX) * 0.5f, bottomY, 0f);

        // One renderer per cell, no colliders (collision is the pre-baked Collision tilemap).
        var renderers = new List<SpriteRenderer>(cluster.Count);
        foreach (var c in cluster)
        {
            var child = new GameObject($"Cell_{c.x}_{c.y}");
            child.transform.SetParent(parent.transform, true);
            child.transform.position = tm.GetCellCenterWorld(c);
            var sr = child.AddComponent<SpriteRenderer>();
            var sprite = tm.GetSprite(c);
            if (sprite != null) sr.sprite = sprite;
            renderers.Add(sr);
        }

        // The prop never moves, so its depth never changes: compute the foot-sorted order ONCE and bake
        // it in — no per-frame YDepthSorter. The moving player keeps its own YDepthSorter and sorts
        // correctly against these fixed orders (same -footY*Precision scale). This is the big per-frame
        // CPU saving vs sorting every static tree/house every LateUpdate.
        float footY = float.MaxValue;
        foreach (var sr in renderers) if (sr.sprite != null) footY = Mathf.Min(footY, sr.bounds.min.y);
        if (footY == float.MaxValue) footY = bottomY;
        int order = OrderForY(footY);
        foreach (var sr in renderers) sr.sortingOrder = order;
    }

    static bool IsObstacleName(string n)
        => NameHas(n, "obstacle", "obstruction", "wall", "prop", "tree", "house",
                      "building", "foliage", "bush", "fence", "solid");

    /// <summary>The dedicated pre-baked collision tilemap (renders nothing; holds the CompositeCollider2D).</summary>
    public static bool IsCollisionLayer(string n) => n.Contains("collision");

    static void Set(TilemapRenderer tr, int order, TilemapRenderer.Mode mode)
    {
        tr.sortingOrder = order;
        tr.mode = mode;
    }

    static bool NameHas(string n, params string[] keys)
    {
        foreach (var k in keys) if (n.Contains(k)) return true;
        return false;
    }
}
