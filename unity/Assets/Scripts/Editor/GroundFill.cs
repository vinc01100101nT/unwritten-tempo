using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click <b>ground filler</b> for a hand-authored map. Instead of clicking ground tiles one-by-one in the
/// Prop Palette, this measures everything you've already placed in the ACTIVE scene, then carpets a rectangle
/// of <c>ground_grass</c> tiles under it (with a margin) — same bounds-driven, GPU-instanced, collider-free
/// tiling the town baker uses (<see cref="FantasyTownBuilder"/>), plus the single flat floor collider on the
/// Ground layer that the player snaps to and the pathfinder rays against.
///
/// Unlike "Build Fantasy Town", this does NOT touch your hand-placed houses/trees — it only adds the ground.
/// Safe to re-run: it deletes the previous <c>Ground_Fill</c> root first and re-carpets to the current size.
/// </summary>
public static class GroundFill
{
    const string PropsDir = "Assets/Prefabs/Props";
    const string RootName = "Ground_Fill";

    const float TileTarget = 2.5f;   // world-unit size per ground tile (a few big tiles, not thousands of tiny)
    const float Margin     = 3f;     // extra ground past the outermost prop, each side
    const int   MaxTiles   = 1600;   // safety budget on tile count
    const int   Seed       = 73;

    [MenuItem("Tools/unwritten/World/Fill Ground Under Map", priority = 212)]
    static void Fill()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Fill Ground", "Exit Play mode first, then re-run.", "OK"); return; }

        var grassSrc = Prefab("ground_grass");
        if (grassSrc == null)
        { EditorUtility.DisplayDialog("Fill Ground",
            $"No 'ground_grass' prefab in {PropsDir}.\nRun Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        { EditorUtility.DisplayDialog("Fill Ground", "No active scene to fill.", "OK"); return; }

        int groundLayer = WorldBuildUtil.EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Fill Ground", "No free layer slot for 'Ground'. Free one in Project Settings ▸ Tags and Layers.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        // Remove a previous fill so re-running just re-sizes (and so we don't measure our own old ground).
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == RootName) Object.DestroyImmediate(r);

        // ── Area = combined footprint of everything you've placed (skip ground-layer objects) + margin ──────
        var area = new Bounds();
        bool has = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.layer == groundLayer) continue;                 // don't fit to existing ground
            foreach (var rend in root.GetComponentsInChildren<Renderer>())
            {
                if (!has) { area = rend.bounds; has = true; } else area.Encapsulate(rend.bounds);
            }
        }
        if (!has)
        { EditorUtility.DisplayDialog("Fill Ground",
            "Nothing placed to fill under. Place some props first, then re-run.", "OK"); return; }

        float bx = area.center.x, bz = area.center.z;
        float wantX = area.size.x + Margin * 2f, wantZ = area.size.z + Margin * 2f;

        // ── Size one tile to TileTarget world units (decoupled from the tile mesh's native size) ────────────
        var nativeB = MeasureScaled(grassSrc, 1f);
        float tileScale = TileTarget / Mathf.Max(0.01f, nativeB.size.x, nativeB.size.z);
        var gb = MeasureScaled(grassSrc, tileScale);
        float tileW = Mathf.Max(0.05f, gb.size.x), tileD = Mathf.Max(0.05f, gb.size.z);
        float topY = gb.max.y, offX = gb.center.x, offZ = gb.center.z;   // pivot→bounds offsets at this scale

        int cols = Mathf.Max(1, Mathf.CeilToInt(wantX / tileW));
        int rows = Mathf.Max(1, Mathf.CeilToInt(wantZ / tileD));
        if ((long)cols * rows > MaxTiles)
        {
            float k = Mathf.Sqrt(MaxTiles / (float)(cols * rows));
            cols = Mathf.Max(1, Mathf.FloorToInt(cols * k));
            rows = Mathf.Max(1, Mathf.FloorToInt(rows * k));
        }
        float gridX = cols * tileW, gridZ = rows * tileD;

        // ── Lay the carpet (a little soil/stone scatter so it isn't flat, like the town) ────────────────────
        var rng    = new System.Random(Seed);
        var soil   = Prefab("ground_soil");
        var stones = Prefab("ground_stones");
        var rootGO = new GameObject(RootName);
        float startX = bx - gridX * 0.5f + tileW * 0.5f;
        float startZ = bz - gridZ * 0.5f + tileD * 0.5f;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float cx = startX + i * tileW, cz = startZ + j * tileD;
                GameObject src = grassSrc;
                if (soil   != null && rng.NextDouble() < 0.07) src = soil;
                else if (stones != null && rng.NextDouble() < 0.04) src = stones;
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(src);
                tile.transform.localScale = Vector3.one * tileScale;
                tile.transform.position = new Vector3(cx - offX, -topY, cz - offZ);  // bounds centre → cell, top → y=0
                foreach (var col in tile.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                tile.transform.SetParent(rootGO.transform, true);
            }
        WorldBuildUtil.SetLayerRecursive(rootGO, groundLayer);

        // One flat floor collider (the walkable surface: player snap + pathfinder ground rays).
        var floor = new GameObject("Ground_Floor") { layer = groundLayer };
        floor.transform.SetParent(rootGO.transform, false);
        var fbox = floor.AddComponent<BoxCollider>();
        fbox.center = new Vector3(bx, -0.5f, bz);                 // top at y=0 (tile surface)
        fbox.size   = new Vector3(gridX + 2f, 1f, gridZ + 2f);

        // Make sure routing knows the Ground layer = walkable, everything else = wall (add only if missing).
        if (Object.FindObjectOfType<Pathfinder3DConfig>() == null)
        {
            var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
            cfg.heightAware = true; cfg.groundMask = groundMask; cfg.maxStep = 0.6f;
        }

        Physics.SyncTransforms();
        Selection.activeGameObject = rootGO;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Fill Ground] Carpeted {cols}×{rows} = {cols * rows} tiles over ~{gridX:0.#}×{gridZ:0.#} units. Ctrl+S to save.");
    }

    /// <summary>Instantiate the prefab at a scale, measure its world renderer bounds, then discard.
    /// IMPORTANT: keep the prefab's OWN rotation — these ground meshes carry a baked −90°X correction that
    /// lays them flat. Resetting to identity measures the tile standing on edge, so its Z extent collapses to
    /// the mesh THICKNESS and the per-row grid step shrinks to ~0 → tiles pile up and z-fight. Measure as
    /// it will actually be placed (the loop below also leaves the prefab rotation untouched).</summary>
    static Bounds MeasureScaled(GameObject prefab, float scale)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.position = Vector3.zero;
        inst.transform.localScale = Vector3.one * scale;
        bool has = PropPlacement.TryWorldBounds(inst, out var b);
        if (!has) b = new Bounds(Vector3.zero, Vector3.one * scale);   // safety: never a zero-size tile
        Object.DestroyImmediate(inst);
        return b;
    }

    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.prefab");
}
