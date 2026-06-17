using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fills the currently-open stage with nature from <c>Assets/Prefabs/LowPolyNature</c> — trees, bushes,
/// grass, flowers, rocks, mushrooms, stumps — by raycasting onto the <c>Ground</c>-layer terrain and
/// dropping weighted-random props on the surface. Density is area-based, so it adapts to any ground size,
/// and the result is re-runnable (clears the previous <c>Nature_Scatter</c> first), leaving every placed
/// prop hand-editable afterward (generate-then-hand-finish).
///
/// Colliders are set per category for correct 2.5D movement + pathfinding (obstacles = non-ground
/// colliders, see <see cref="Pathfinder3DConfig"/>):
///   • trees / stumps → a cheap TRUNK capsule (walk under the canopy, path around the trunk)
///   • rocks          → a snug box (solid)
///   • grass / flowers / mushrooms / bushes / branches → NO collider (decorative, walk through)
/// All scatter sits on the Default layer so the terrain height-ray never mistakes a treetop for ground.
/// </summary>
public static class NatureScatter
{
    const string PrefabDir = "Assets/Prefabs/LowPolyNature";
    const string Container = "Nature_Scatter";

    // Instances per square world-unit, scaled by Density. Tune here.
    const float Density   = 1.0f;
    const float DenGrass    = 0.15f;
    const float DenFlowers  = 0.04f;
    const float DenTrees    = 0.03f;
    const float DenBushes   = 0.03f;
    const float DenRocks    = 0.02f;
    const float DenMushroom = 0.012f;
    const float DenStumps   = 0.004f;

    const float TreeSpacing   = 2.5f;   // min gap between trees (units)
    const float TreeMaxSlope  = 30f;    // don't plant trees on steeper-than-this faces (degrees)
    const float SpawnClearR   = 3.0f;   // keep solids this far from the terrain centre (player spawn)
    const float Inset         = 1.5f;   // pull scatter in from the ground edge

    [MenuItem("Tools/unwritten/Scatter Nature (fill map)", priority = 6)]
    static void Scatter()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Scatter Nature", "Exit Play mode first, then re-run.", "OK"); return; }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Scatter Nature", "No 'Ground' layer in this project. Build a Nature Stage first.", "OK"); return; }
        int groundMask = 1 << groundLayer;

        if (!TryGetGroundBounds(groundLayer, out var bounds))
        { EditorUtility.DisplayDialog("Scatter Nature",
            "No ground found on the 'Ground' layer in the open scene.\n\n" +
            "Run Tools ▸ unwritten ▸ New Nature Stage (low-poly ground) first, then re-run this.", "OK"); return; }

        var buckets = LoadBuckets();
        if (buckets.Values.All(l => l.Count == 0))
        { EditorUtility.DisplayDialog("Scatter Nature",
            $"No prefabs in {PrefabDir}.\n\nRun Build Prefabs ▸ Low Poly Nature first.", "OK"); return; }

        Physics.SyncTransforms();   // make terrain colliders queryable for the drop rays

        // Fresh container (re-runnable).
        var prev = GameObject.Find(Container);
        if (prev != null) Object.DestroyImmediate(prev);
        var root = new GameObject(Container);
        Undo.RegisterCreatedObjectUndo(root, "Scatter Nature");

        int seed = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
        Random.InitState(seed);

        float area = bounds.size.x * bounds.size.z;
        Vector3 center = bounds.center;
        var treePositions = new List<Vector3>();
        int total = 0;

        // Solids first (trees, then rocks/stumps), so spacing/clear checks settle before decor.
        total += Place(buckets["tree"],     Count(area, DenTrees),    root, groundMask, bounds, center, Category.Tree,   treePositions);
        total += Place(buckets["stump"],    Count(area, DenStumps),   root, groundMask, bounds, center, Category.Stump,  null);
        total += Place(buckets["rock"],     Count(area, DenRocks),    root, groundMask, bounds, center, Category.Rock,   null);
        total += Place(buckets["bush"],     Count(area, DenBushes),   root, groundMask, bounds, center, Category.Decor,  null);
        total += Place(buckets["grass"],    Count(area, DenGrass),    root, groundMask, bounds, center, Category.Decor,  null);
        total += Place(buckets["flowers"],  Count(area, DenFlowers),  root, groundMask, bounds, center, Category.Decor,  null);
        total += Place(buckets["mushroom"], Count(area, DenMushroom), root, groundMask, bounds, center, Category.Decor,  null);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log($"[unwritten] Scattered {total} nature props onto {bounds.size.x:0}×{bounds.size.z:0} terrain (seed {seed}).");
        EditorUtility.DisplayDialog("Scatter Nature — done",
            $"Placed {total} props under '{Container}'.\n\n" +
            "Press Play to walk it — you'll path around trees/rocks and walk through grass.\n\n" +
            "Re-run for a new layout, tweak the density consts at the top of NatureScatter.cs, or " +
            "hand-move/delete individual props (they keep their prefab links).", "OK");
    }

    [MenuItem("Tools/unwritten/Clear Nature Scatter", priority = 7)]
    static void Clear()
    {
        var prev = GameObject.Find(Container);
        if (prev == null) { Debug.Log("[unwritten] No Nature_Scatter to clear."); return; }
        Undo.DestroyObjectImmediate(prev);
        EditorSceneManager.MarkSceneDirty(prev.scene);
    }

    enum Category { Tree, Stump, Rock, Decor }

    static int Count(float area, float density) => Mathf.Max(0, Mathf.RoundToInt(area * density * Density));

    static int Place(List<GameObject> pool, int count, GameObject root, int groundMask, Bounds b,
                     Vector3 center, Category cat, List<Vector3> spacingList)
    {
        if (pool == null || pool.Count == 0 || count <= 0) return 0;
        int placed = 0, attempts = 0, maxAttempts = count * 12;

        while (placed < count && attempts < maxAttempts)
        {
            attempts++;
            float x = Random.Range(b.min.x + Inset, b.max.x - Inset);
            float z = Random.Range(b.min.z + Inset, b.max.z - Inset);

            if (!Physics.Raycast(new Vector3(x, b.max.y + 50f, z), Vector3.down, out var hit, 200f,
                                 groundMask, QueryTriggerInteraction.Ignore))
                continue;

            // Keep solid props off the player spawn (terrain centre) and off steep tree faces.
            bool solid = cat != Category.Decor;
            if (solid && new Vector2(hit.point.x - center.x, hit.point.z - center.z).magnitude < SpawnClearR)
                continue;
            if (cat == Category.Tree)
            {
                if (Vector3.Angle(hit.normal, Vector3.up) > TreeMaxSlope) continue;
                if (spacingList != null && spacingList.Any(p => (p - hit.point).sqrMagnitude < TreeSpacing * TreeSpacing))
                    continue;
            }

            var src = pool[Random.Range(0, pool.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            go.transform.position = hit.point;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ConfigureCollider(go, cat);
            SetLayerRecursive(go, 0);   // Default — never Ground

            go.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            go.transform.localScale = Vector3.one * Random.Range(0.85f, 1.2f);

            spacingList?.Add(hit.point);
            placed++;
        }
        return placed;
    }

    // ---- colliders ------------------------------------------------------------

    static void ConfigureCollider(GameObject go, Category cat)
    {
        StripColliders(go);
        if (cat == Category.Decor) return;   // grass/flowers/mushrooms/bushes — walk through

        if (!TryWorldBounds(go, out var wb)) return;
        Vector3 localCenter = wb.center - go.transform.position;   // scale=1, rot=identity here

        if (cat == Category.Rock)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = localCenter;
            bc.size = wb.size * 0.85f;
        }
        else // Tree / Stump → trunk capsule at the base
        {
            var cap = go.AddComponent<CapsuleCollider>();
            float r = Mathf.Clamp(Mathf.Min(wb.size.x, wb.size.z) * 0.15f, 0.06f, 0.5f);
            float h = Mathf.Max(wb.size.y * 0.5f, r * 2f);
            cap.radius = r;
            cap.height = h;
            cap.center = new Vector3(localCenter.x, (wb.min.y - go.transform.position.y) + h * 0.5f, localCenter.z);
        }
    }

    static void StripColliders(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
    }

    // ---- helpers --------------------------------------------------------------

    static Dictionary<string, List<GameObject>> LoadBuckets()
    {
        var keys = new[] { "tree", "stump", "rock", "bush", "grass", "flowers", "mushroom", "branch" };
        var map = keys.ToDictionary(k => k, k => new List<GameObject>());
        // branch is decorative ground litter → fold into grass-style decor
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            string name = go.name.ToLowerInvariant();
            var key = keys.FirstOrDefault(k => name.StartsWith(k));
            if (key == null) continue;
            (key == "branch" ? map["grass"] : map[key]).Add(go);
        }
        return map;
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

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }
}
