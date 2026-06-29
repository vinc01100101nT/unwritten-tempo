using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rule-based, hands-off foliage decorator for a baked prop (e.g. a Hunyuan house mesh). Instead of
/// hand-placing flowers, this scatters existing Pxltgr decor prefabs (Flower*, Grass*, Fern*, Bush*,
/// Mushroom*) onto the selected object's SURFACE by geometric rule, so the same one click decorates any
/// house — house #1 and house #500 — with seeded variation and zero per-house interaction.
///
/// Two automatic zones (no fragile "find the window" detection):
///  • GARDEN RING — clumps hugging the foot of the walls, seated on the ground (bounds-driven, like
///    <see cref="RecipeGenerator"/>). Reads as landscaping around the base.
///  • WALL ACCENTS — a few props mounted flat against vertical wall faces at low/mid height, oriented to
///    the wall normal (ivy/flowers climbing the wall). Skipped where the wall isn't vertical.
///
/// All decor is parented under a child named "<see cref="DecorParent"/>", colliders stripped (walk-through,
/// matching <see cref="PropPlacement.IsWalkThroughDecor"/>), so re-running just rebuilds that child.
/// </summary>
public static class MeshDecorator
{
    const string PropsDir = "Assets/Prefabs/Props";
    const string DecorParent = "AutoDecor";

    // Tunables — sensible chunky-village defaults. Counts scale with the wall perimeter so big and small
    // houses both look right from one click.
    const float RingSpacing      = 0.6f;   // target gap between garden clumps along the perimeter (world units)
    const float RingSkipChance    = 0.35f;  // randomly leave gaps so the ring isn't a solid hedge
    const float RingOutset        = 0.04f;  // push the clump just OUTSIDE the wall so it's not buried
    const float WallAccentPerMetre = 0.25f; // ivy/flower mounts per metre of perimeter
    const float WallMinHeightFrac  = 0.08f; // mount band: low on the wall …
    const float WallMaxHeightFrac  = 0.55f; // … up to mid-wall
    const float WallVerticalDot    = 0.5f;  // a face counts as "wall" only if its normal is this horizontal
    const float ScaleJitter        = 0.35f; // ±35% size variation

    [MenuItem("Tools/unwritten/World/Decorate Selected (Foliage)", false, 60)]
    static void DecorateSelected()
    {
        var target = Selection.activeGameObject;
        if (target == null || !target.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Decorate Foliage",
                "Select the house/prop INSTANCE in the scene (not a prefab in the Project) first.", "OK");
            return;
        }

        var flowers   = LoadPool("Flower");
        var groundFlora = LoadPool("Grass", "Fern", "Mushroom", "Bush");
        if (flowers.Count == 0 && groundFlora.Count == 0)
        {
            EditorUtility.DisplayDialog("Decorate Foliage",
                $"No decor prefabs found in {PropsDir} (Flower*, Grass*, Fern*, Bush*, Mushroom*).\n\n"
                + "Run Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", "OK");
            return;
        }

        int placed = Decorate(target, flowers, groundFlora);
        Debug.Log($"[unwritten] MeshDecorator placed {placed} foliage props on '{target.name}'.");
    }

    /// <summary>Scatter foliage onto <paramref name="target"/>. Deterministic per object name. Returns count.
    /// Reusable from a bake pipeline so the whole flow stays zero-touch.</summary>
    public static int Decorate(GameObject target, List<GameObject> flowers, List<GameObject> groundFlora)
    {
        // Rebuild: nuke any previous AutoDecor child so re-running is idempotent.
        var prev = target.transform.Find(DecorParent);
        if (prev != null) Object.DestroyImmediate(prev.gameObject);

        var parent = new GameObject(DecorParent);
        Undo.RegisterCreatedObjectUndo(parent, "Decorate Foliage");
        parent.transform.SetParent(target.transform, false);

        var rng = new System.Random(target.name.GetHashCode());
        int mask = PropPlacement.GroundMask();

        // We raycast against the house surface; add temporary MeshColliders where missing, remove ours after.
        var tempColliders = EnsureTempColliders(target);
        Physics.SyncTransforms();

        if (!PropPlacement.TryWorldBounds(target, out var b)) { CleanupTemp(tempColliders); return 0; }

        int placed = 0;
        placed += PlaceGardenRing(target, parent.transform, b, groundFlora.Count > 0 ? groundFlora : flowers, rng, mask);
        placed += PlaceWallAccents(target, parent.transform, b, flowers.Count > 0 ? flowers : groundFlora, rng);

        CleanupTemp(tempColliders);
        Physics.SyncTransforms();
        return placed;
    }

    // ── GARDEN RING ─────────────────────────────────────────────────────────────────────────────────────
    // Walk the footprint perimeter; at each step cast horizontally INWARD to find the wall, then seat a clump
    // just outside that wall on the ground. Hugging beats a fixed rectangle (which floats clumps off corners).
    static int PlaceGardenRing(GameObject target, Transform parent, Bounds b, List<GameObject> pool,
                               System.Random rng, int mask)
    {
        if (pool.Count == 0) return 0;
        Vector2 c = new Vector2(b.center.x, b.center.z);
        float reach = Mathf.Max(b.extents.x, b.extents.z) + 1f;   // start rays outside the widest wall
        float perimeter = 2f * (b.size.x + b.size.z);
        int samples = Mathf.Max(8, Mathf.RoundToInt(perimeter / RingSpacing));
        float baseY = b.min.y + b.size.y * 0.05f;                 // low band — find the foot of the wall
        int placed = 0;

        for (int i = 0; i < samples; i++)
        {
            if (rng.NextDouble() < RingSkipChance) continue;
            float ang = (i / (float)samples) * Mathf.PI * 2f + (float)(rng.NextDouble() - 0.5) * 0.15f;
            Vector2 outward = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector3 origin = new Vector3(c.x + outward.x * reach, baseY, c.y + outward.y * reach);
            Vector3 dir = new Vector3(-outward.x, 0f, -outward.y);

            if (!RaycastTarget(target, origin, dir, reach + 0.5f, out var hit)) continue;
            Vector2 wall = new Vector2(hit.point.x, hit.point.z) + outward * RingOutset;
            var go = Spawn(pool[rng.Next(pool.Count)], parent, rng);
            PropPlacement.PlaceOnGround(go, new Vector3(wall.x, 0f, wall.y), mask);
            placed++;
        }
        return placed;
    }

    // ── WALL ACCENTS ────────────────────────────────────────────────────────────────────────────────────
    // Cast inward at random heights; where we hit a near-vertical face, mount a prop flat against it facing
    // out along the surface normal (climbing ivy / wall flowers). Non-vertical hits (roof) are skipped.
    static int PlaceWallAccents(GameObject target, Transform parent, Bounds b, List<GameObject> pool,
                                System.Random rng)
    {
        if (pool.Count == 0) return 0;
        Vector2 c = new Vector2(b.center.x, b.center.z);
        float reach = Mathf.Max(b.extents.x, b.extents.z) + 1f;
        float perimeter = 2f * (b.size.x + b.size.z);
        int target_n = Mathf.Max(2, Mathf.RoundToInt(perimeter * WallAccentPerMetre));
        int placed = 0, attempts = 0, max = target_n * 8;

        while (placed < target_n && attempts < max)
        {
            attempts++;
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float hFrac = Mathf.Lerp(WallMinHeightFrac, WallMaxHeightFrac, (float)rng.NextDouble());
            Vector2 outward = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float y = b.min.y + b.size.y * hFrac;
            Vector3 origin = new Vector3(c.x + outward.x * reach, y, c.y + outward.y * reach);
            Vector3 dir = new Vector3(-outward.x, 0f, -outward.y);

            if (!RaycastTarget(target, origin, dir, reach + 0.5f, out var hit)) continue;
            if (Mathf.Abs(hit.normal.y) > (1f - WallVerticalDot)) continue;   // skip non-vertical (roof/eaves)

            var go = Spawn(pool[rng.Next(pool.Count)], parent, rng);
            // Seat against the wall: mount point + a hair off the surface, facing out along the normal.
            go.transform.position = hit.point + hit.normal * RingOutset;
            go.transform.rotation = Quaternion.LookRotation(hit.normal, Vector3.up);
            placed++;
        }
        return placed;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────
    static GameObject Spawn(GameObject prefab, Transform parent, System.Random rng)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        float s = 1f + (float)(rng.NextDouble() * 2 - 1) * ScaleJitter;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
        go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        go.transform.SetParent(parent, true);
        WorldBuildUtil.SetLayerRecursive(go, 0);        // Default layer — never Ground
        PropPlacement.StripColliders(go);               // walk-through decor
        Undo.RegisterCreatedObjectUndo(go, "Decorate Foliage");
        return go;
    }

    /// <summary>Raycast that only counts hits on <paramref name="target"/> or its children (ignores ground,
    /// neighbours, our own temp colliders on other objects).</summary>
    static bool RaycastTarget(GameObject target, Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default; bool found = false; float bestD = float.MaxValue;
        var hits = Physics.RaycastAll(origin, dir.normalized, dist, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (!h.collider.transform.IsChildOf(target.transform)) continue;
            if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
        }
        return found;
    }

    static List<Collider> EnsureTempColliders(GameObject target)
    {
        var added = new List<Collider>();
        foreach (var mf in target.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                added.Add(mc);
            }
        return added;
    }

    static void CleanupTemp(List<Collider> temp)
    {
        foreach (var c in temp) if (c != null) Object.DestroyImmediate(c);
    }

    static List<GameObject> LoadPool(params string[] prefixes)
    {
        var list = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (var pre in prefixes)
                if (file.StartsWith(pre, System.StringComparison.OrdinalIgnoreCase))
                { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (pf != null) list.Add(pf); break; }
        }
        return list;
    }
}
