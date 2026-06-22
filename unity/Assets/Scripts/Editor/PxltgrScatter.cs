using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fills the currently-open field with nature from the <b>Pxltgr</b> pack (<c>Assets/Prefabs/Props</c>) —
/// trees, stumps, rocks, bushes, ferns, grass, flowers, mushrooms — by raycasting onto the <c>Ground</c>-layer
/// floor and dropping weighted-random props seated on the surface by their measured bounds. Replaces the
/// retired <c>NatureScatter</c> (LowPolySimpleNature). Density is area-based so it adapts to any field size,
/// and it's re-runnable (clears the previous <c>Nature_Scatter</c> first), leaving every prop hand-editable.
///
/// Props are placed at <b>×0.3</b> (×a little jitter) — the same scale the Pxltgr town uses — so the field and
/// town read at one consistent size. Solids (trees/stumps/rocks) KEEP their prefab MeshColliders so they block
/// movement + routing (the pathfinder treats anything off the Ground layer as a wall), exactly like the town
/// baker; decorative cover (grass/flowers/ferns/mushrooms/bushes) is stripped of colliders so you walk through.
/// All scatter sits on the Default layer so the terrain height-ray never mistakes a treetop for ground.
/// </summary>
public static class PxltgrScatter
{
    const string PropsDir  = "Assets/Prefabs/Props";
    const string Container = "Nature_Scatter";
    const float  PropScale = 0.3f;   // matches the Pxltgr town prop scale

    // Instances per square world-unit, scaled by Density. Tune here.
    const float Density     = 1.0f;
    const float DenTrees    = 0.020f;
    const float DenStumps   = 0.004f;
    const float DenRocks    = 0.010f;
    const float DenBush     = 0.008f;
    const float DenGrass    = 0.070f;
    const float DenFlowers  = 0.030f;
    const float DenFern     = 0.020f;
    const float DenMushroom = 0.010f;

    const float TreeSpacing = 2.5f;   // min gap between trees (units)
    const float SpawnClearR = 6.0f;   // keep solids this far from the field centre (player spawn)
    const float Inset       = 2.0f;   // pull scatter in from the ground edge

    // Explicit prefab lists (LOD0 only where the pack ships LODs) so we never scatter LOD1/LOD2 duplicates.
    static readonly string[] Trees  = { "Oak_tree1", "Oak_tree2", "Oak_tree3", "Birch_tree1", "Birch_tree2",
                                        "Birch_tree3", "Pine_tree1", "Pine_tree2", "Pine_tree3", "Deciduous_tree1",
                                        "Deciduous_tree2", "Deciduous_tree3", "Deciduous_tree4", "Willow_tree1", "Willow_tree2" };
    static readonly string[] Stumps = { "Deciduous_tree_stump_LOD0", "Pine_stump_LOD0" };
    static readonly string[] Rocks  = { "Rock1", "Rock2", "Rock3", "Stone1_LOD0", "Stone2_LOD0", "Stone3_LOD0" };
    static readonly string[] Bushes = { "Bush1" };
    static readonly string[] Grass  = { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" };
    static readonly string[] Flowers= { "Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6",
                                        "Flower7", "Flower8", "Flower9", "Sunflower" };
    static readonly string[] Ferns  = { "Fern1", "Fern2", "Fern3" };
    static readonly string[] Mush   = { "Mushroom1", "Mushroom2", "Mushroom3", "Mushroom4", "Mushroom5" };

    enum Category { Tree, Stump, Rock, Decor }

    [MenuItem("Tools/unwritten/World/Scatter Nature (Pxltgr, fill map)", priority = 210)]
    static void Scatter()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Scatter Nature", "Exit Play mode first, then re-run.", "OK"); return; }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Scatter Nature", "No 'Ground' layer in this project. Build a field first (New Field (Pxltgr grass)).", "OK"); return; }
        int groundMask = 1 << groundLayer;

        if (!TryGetGroundBounds(groundLayer, out var bounds))
        { EditorUtility.DisplayDialog("Scatter Nature",
            "No ground found on the 'Ground' layer in the open scene.\n\n" +
            "Run Tools ▸ unwritten ▸ World ▸ New Field (Pxltgr grass) first, then re-run this.", "OK"); return; }

        if (Load(Trees).Count == 0 && Load(Grass).Count == 0)
        { EditorUtility.DisplayDialog("Scatter Nature",
            $"No Pxltgr nature prefabs in {PropsDir}.\n\nRun Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK"); return; }

        Physics.SyncTransforms();   // make the floor collider queryable for the drop rays

        var prev = GameObject.Find(Container);
        if (prev != null) Object.DestroyImmediate(prev);
        var root = new GameObject(Container);
        Undo.RegisterCreatedObjectUndo(root, "Scatter Nature (Pxltgr)");

        int seed = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
        Random.InitState(seed);

        float area = bounds.size.x * bounds.size.z;
        Vector3 center = bounds.center;
        var treePositions = new List<Vector3>();
        int total = 0;

        // Solids first (trees, then stumps/rocks) so spacing/clear checks settle before decor.
        total += Place(Load(Trees),   Count(area, DenTrees),    root, groundMask, bounds, center, Category.Tree,  treePositions);
        total += Place(Load(Stumps),  Count(area, DenStumps),   root, groundMask, bounds, center, Category.Stump, null);
        total += Place(Load(Rocks),   Count(area, DenRocks),    root, groundMask, bounds, center, Category.Rock,  null);
        total += Place(Load(Bushes),  Count(area, DenBush),     root, groundMask, bounds, center, Category.Decor, null);
        total += Place(Load(Grass),   Count(area, DenGrass),    root, groundMask, bounds, center, Category.Decor, null);
        total += Place(Load(Flowers), Count(area, DenFlowers),  root, groundMask, bounds, center, Category.Decor, null);
        total += Place(Load(Ferns),   Count(area, DenFern),     root, groundMask, bounds, center, Category.Decor, null);
        total += Place(Load(Mush),    Count(area, DenMushroom), root, groundMask, bounds, center, Category.Decor, null);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log($"[unwritten] Scattered {total} Pxltgr nature props onto {bounds.size.x:0}×{bounds.size.z:0} field (seed {seed}).");
        EditorUtility.DisplayDialog("Scatter Nature — done",
            $"Placed {total} Pxltgr props under '{Container}'.\n\n" +
            "Press Play to walk it — you'll path around trees/rocks and walk through grass.\n\n" +
            "Re-run for a new layout, tweak the density consts at the top of PxltgrScatter.cs, or " +
            "hand-move/delete individual props (they keep their prefab links).", "OK");
    }

    [MenuItem("Tools/unwritten/World/Clear Nature Scatter", priority = 211)]
    static void Clear()
    {
        var prev = GameObject.Find(Container);
        if (prev == null) { Debug.Log("[unwritten] No Nature_Scatter to clear."); return; }
        Undo.DestroyObjectImmediate(prev);
        EditorSceneManager.MarkSceneDirty(prev.scene);
    }

    static int Count(float area, float density) => Mathf.Max(0, Mathf.RoundToInt(area * density * Density));

    static int Place(List<GameObject> pool, int count, GameObject root, int groundMask, Bounds b,
                     Vector3 center, Category cat, List<Vector3> spacingList)
    {
        if (pool == null || pool.Count == 0 || count <= 0) return 0;
        int placed = 0, attempts = 0, maxAttempts = count * 12;
        bool solid = cat != Category.Decor;

        while (placed < count && attempts < maxAttempts)
        {
            attempts++;
            float x = Random.Range(b.min.x + Inset, b.max.x - Inset);
            float z = Random.Range(b.min.z + Inset, b.max.z - Inset);

            if (!Physics.Raycast(new Vector3(x, b.max.y + 50f, z), Vector3.down, out var hit, 200f,
                                 groundMask, QueryTriggerInteraction.Ignore))
                continue;

            // Keep solid props off the player spawn (field centre), and trees spaced out.
            if (solid && new Vector2(hit.point.x - center.x, hit.point.z - center.z).magnitude < SpawnClearR)
                continue;
            if (cat == Category.Tree && spacingList != null &&
                spacingList.Any(p => (p - hit.point).sqrMagnitude < TreeSpacing * TreeSpacing))
                continue;

            var src = pool[Random.Range(0, pool.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            go.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            go.transform.localScale = Vector3.one * PropScale * Random.Range(0.85f, 1.2f);

            // Seat the prop by its measured bounds (feet on the ground), robust to the off-pivot extracted meshes.
            if (TryWorldBounds(go, out var wb))
                go.transform.position = new Vector3(hit.point.x - wb.center.x, hit.point.y - wb.min.y, hit.point.z - wb.center.z);
            else
                go.transform.position = hit.point;

            WorldBuildUtil.SetLayerRecursive(go, 0);   // Default — never Ground (so it's not mistaken for floor)
            if (!solid)   // decorative cover shouldn't block movement or routing
                foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

            spacingList?.Add(hit.point);
            placed++;
        }
        return placed;
    }

    static List<GameObject> Load(string[] names)
    {
        var list = new List<GameObject>();
        foreach (var n in names)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{n}.prefab");
            if (go != null) list.Add(go);
        }
        return list;
    }

    static bool TryGetGroundBounds(int groundLayer, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.gameObject.layer != groundLayer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    static bool TryWorldBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }
}
