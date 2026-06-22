using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared <b>Pxltgr grass-plot</b> builder — the one place that lays a flat tiled ground out of the pack's
/// <c>ground_*</c> prefabs (in <c>Assets/Prefabs/Props</c>, sliced by <see cref="FbxPropExtractor"/>). Used by
/// both <see cref="PxltgrFieldBuilder"/> (the open field) and <see cref="RegionBuilder"/> (the bare town hub),
/// so the field and town share ONE consistent ground look now that the LowPolySimpleNature pack is gone.
///
/// The plot is grass with a sparse soil/stone speckle (and an optional central paved road), GPU-instanced as
/// collider-free tiles over a SINGLE flat floor <see cref="BoxCollider"/> on the Ground layer — that floor is
/// the walkable surface the player snaps to and the pathfinder rays against (cheap; see PERFORMANCE.md). Tiles
/// are sized to <paramref name="tileTarget"/> world units regardless of the mesh's native size, so a handful
/// of big tiles cover the plot instead of thousands of tiny ones.
///
/// IMPORTANT: the ground meshes carry a baked −90°X correction that lays them flat, so we never reset their
/// rotation — measuring/placing them as-authored (this mirrors the note in the old <c>GroundFill</c>).
/// </summary>
public static class PxltgrGround
{
    const string PropsDir = "Assets/Prefabs/Props";
    public const float DefaultTileTarget = 2.5f;
    const int MaxTiles = 1600;   // safety budget on tile count

    /// <summary>
    /// Lay a grass plot ~<paramref name="sizeX"/>×<paramref name="sizeZ"/> world units centred on
    /// (<paramref name="center"/>.x, <paramref name="center"/>.z), surface at y=0, under a new root in
    /// <paramref name="scene"/> on <paramref name="groundLayer"/>. Adds a flat floor collider + a
    /// <see cref="Pathfinder3DConfig"/> (only if the scene lacks one). Returns the ground root, or
    /// <c>null</c> if the <c>ground_grass</c> prefab is missing (caller should show a dialog).
    /// </summary>
    /// <param name="roadHalf">Half-width of an optional central paved road strip along X (0 = no road).</param>
    public static GameObject LayPlot(Scene scene, string rootName, Vector3 center, float sizeX, float sizeZ,
                                     int groundLayer, int seed = 73, float roadHalf = 0f,
                                     float tileTarget = DefaultTileTarget)
    {
        var grassSrc = Prefab("ground_grass");
        if (grassSrc == null) return null;

        // Size one tile to tileTarget world units (decoupled from the mesh's native size).
        var nativeB = MeasureScaled(grassSrc, 1f);
        float tileScale = tileTarget / Mathf.Max(0.01f, nativeB.size.x, nativeB.size.z);
        var gb = MeasureScaled(grassSrc, tileScale);
        float tileW = Mathf.Max(0.05f, gb.size.x), tileD = Mathf.Max(0.05f, gb.size.z);
        float topY = gb.max.y, offX = gb.center.x, offZ = gb.center.z;   // pivot→bounds offsets at this scale

        int cols = Mathf.Max(2, Mathf.CeilToInt(sizeX / tileW));
        int rows = Mathf.Max(2, Mathf.CeilToInt(sizeZ / tileD));
        if ((long)cols * rows > MaxTiles)
        {
            float k = Mathf.Sqrt(MaxTiles / (float)(cols * rows));
            cols = Mathf.Max(2, Mathf.FloorToInt(cols * k));
            rows = Mathf.Max(2, Mathf.FloorToInt(rows * k));
        }
        float gridX = cols * tileW, gridZ = rows * tileD;

        var rng    = new System.Random(seed);
        var road   = roadHalf > 0f ? Prefab("ground_road") : null;
        var soil   = Prefab("ground_soil");
        var stones = Prefab("ground_stones");

        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        float startX = center.x - gridX * 0.5f + tileW * 0.5f;
        float startZ = center.z - gridZ * 0.5f + tileD * 0.5f;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float cx = startX + i * tileW, cz = startZ + j * tileD;
                GameObject src = grassSrc;
                if (road != null && Mathf.Abs(cz - center.z) <= roadHalf) src = road;      // central street
                else if (soil   != null && rng.NextDouble() < 0.07) src = soil;            // dirt patches
                else if (stones != null && rng.NextDouble() < 0.04) src = stones;
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(src);
                tile.transform.localScale = Vector3.one * tileScale;     // keep the prefab's baked flat rotation
                tile.transform.position = new Vector3(cx - offX, -topY, cz - offZ);   // bounds centre → cell, top → y=0
                foreach (var col in tile.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                tile.transform.SetParent(root.transform, true);
            }
        WorldBuildUtil.SetLayerRecursive(root, groundLayer);

        AddWalkableFloor(scene, root, center, gridX, gridZ, groundLayer);
        return root;
    }

    /// <summary>
    /// Add the single flat floor <see cref="BoxCollider"/> (the walkable surface the player snaps to and the
    /// pathfinder rays against, with its top at y=0) plus a scene-scoped <see cref="Pathfinder3DConfig"/> if the
    /// scene lacks one. Shared by the tiled <see cref="LayPlot"/> and the meshed <c>PxltgrGroundBlend</c> so both
    /// ground styles route identically. <paramref name="sizeX"/>/<paramref name="sizeZ"/> is the plot footprint.
    /// </summary>
    public static void AddWalkableFloor(Scene scene, GameObject root, Vector3 center,
                                        float sizeX, float sizeZ, int groundLayer)
    {
        var floor = new GameObject("Ground_Floor") { layer = groundLayer };
        floor.transform.SetParent(root.transform, false);
        var fbox = floor.AddComponent<BoxCollider>();
        fbox.center = new Vector3(center.x, -0.5f, center.z);    // top at y=0 (tile surface)
        fbox.size   = new Vector3(sizeX + 2f, 1f, sizeZ + 2f);

        // Routing: Ground layer = walkable, everything else = wall. Add only if THIS scene lacks a config
        // (scene-scoped, not FindObjectOfType — the builders open the map additively, so another loaded
        // scene's config must not make us skip adding one here).
        bool hasConfig = false;
        foreach (var r in scene.GetRootGameObjects())
            if (r.GetComponentInChildren<Pathfinder3DConfig>(true) != null) { hasConfig = true; break; }
        if (!hasConfig)
        {
            var cfgGO = new GameObject("Pathfinder3DConfig");
            SceneManager.MoveGameObjectToScene(cfgGO, scene);
            var cfg = cfgGO.AddComponent<Pathfinder3DConfig>();
            cfg.heightAware = true; cfg.groundMask = 1 << groundLayer; cfg.maxStep = 0.6f;
        }

        Physics.SyncTransforms();   // so surface raycasts hit the floor we just made
    }

    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.prefab");

    // Instantiate at a scale, measure world renderer bounds as-placed (rotation untouched — see class note), discard.
    static Bounds MeasureScaled(GameObject prefab, float scale)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.position = Vector3.zero;
        inst.transform.localScale = Vector3.one * scale;
        bool has = PropPlacement.TryWorldBounds(inst, out var b);
        if (!has) b = new Bounds(Vector3.zero, Vector3.one * scale);
        Object.DestroyImmediate(inst);
        return b;
    }
}
