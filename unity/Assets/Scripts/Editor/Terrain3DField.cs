using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared, generic helpers for building MERGED, TIERED, RAMP-GATED Kenney terrain — the look proven in
/// <see cref="TerrainSandbox"/>, factored out so the town baker (<see cref="Town3DBaker"/>) reuses the
/// exact same heightfield / ramp / emit logic instead of forking it.
///
/// A "tier" is an integer height level; the world top of a cell is <c>baseY + tier*tierStep</c>. Blocks
/// are oversized by <see cref="Overlap"/> so their rounded shoulders fuse into one lawn (memory
/// <c>kenney-3d-terrain-overlap</c>). Each walkable cell gets a FLAT box collider at its tier top; tier
/// edges are impassable cliffs and the only way up is a generated grass RAMP placed on a spanning tree
/// of the tier regions (so every area stays reachable).
/// </summary>
public static class Terrain3DField
{
    public const float Overlap = 1.3f;   // >1 so neighbours overlap and the dirt grooves close

    static readonly Vector2Int[] N4 =
        { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    // ---- heightmap -----------------------------------------------------------

    /// <summary>Quantised-Perlin tier per cell, row-major (index = y*w + x), values in 0..tiers-1.</summary>
    public static int[] BuildHeightmap(int w, int h, int tiers, float noiseScale, int seed)
    {
        tiers = Mathf.Max(1, tiers);
        var rng = new System.Random(seed);
        float nox = (float)rng.NextDouble() * 100f, noz = (float)rng.NextDouble() * 100f;
        var tier = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = Mathf.PerlinNoise(nox + x * noiseScale, noz + y * noiseScale);
                tier[y * w + x] = Mathf.Clamp(Mathf.FloorToInt(n * tiers), 0, tiers - 1);
            }
        return tier;
    }

    // ---- ramp planning (union-find spanning tree over tier regions) ----------

    /// <summary>For each low cell that should host a ramp, the outward direction toward its higher
    /// neighbour. Guarantees every tier region is reachable (spanning tree). Verbatim logic from
    /// <see cref="TerrainSandbox"/>.</summary>
    public static Dictionary<Vector2Int, Vector2Int> PlanRamps(int[] tier, int w, int h)
    {
        int Idx(int x, int y) => y * w + x;
        bool In(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < w && c.y < h;

        var region = new int[w * h];
        for (int i = 0; i < region.Length; i++) region[i] = -1;
        int regionCount = 0;
        var queue = new Queue<Vector2Int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (region[Idx(x, y)] != -1) continue;
                int id = regionCount++;
                region[Idx(x, y)] = id; queue.Enqueue(new Vector2Int(x, y));
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var d in N4)
                    {
                        var nb = c + d;
                        if (!In(nb) || region[Idx(nb.x, nb.y)] != -1) continue;
                        if (tier[Idx(nb.x, nb.y)] != tier[Idx(c.x, c.y)]) continue;
                        region[Idx(nb.x, nb.y)] = id; queue.Enqueue(nb);
                    }
                }
            }

        var uf = new int[regionCount];
        for (int i = 0; i < regionCount; i++) uf[i] = i;
        int Find(int a) { while (uf[a] != a) a = uf[a] = uf[uf[a]]; return a; }

        var ramps = new Dictionary<Vector2Int, Vector2Int>();
        var lowCells = new HashSet<Vector2Int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new Vector2Int(x, y);
                foreach (var d in N4)
                {
                    var nb = c + d;
                    if (!In(nb)) continue;
                    if (tier[Idx(nb.x, nb.y)] != tier[Idx(c.x, c.y)] + 1) continue;   // c is exactly one tier lower
                    int ra = Find(region[Idx(c.x, c.y)]), rb = Find(region[Idx(nb.x, nb.y)]);
                    if (ra == rb) continue;
                    if (lowCells.Contains(c)) continue;
                    uf[ra] = rb;
                    ramps[c] = d; lowCells.Add(c);
                }
            }
        return ramps;
    }

    // ---- emit ----------------------------------------------------------------

    /// <summary>An overlap-scaled grass block whose top face sits at <paramref name="top"/> (body buried
    /// to <paramref name="baseSlab"/> below). Collider-free + painted with the terrain skin + static.</summary>
    public static GameObject EmitBlock(GameObject block, Bounds b, Transform parent, Vector3 centerXZ,
                                       float top, float baseSlab, float cellSize)
    {
        float fx = Mathf.Max(0.01f, b.size.x), fy = Mathf.Max(0.01f, b.size.y), fz = Mathf.Max(0.01f, b.size.z);
        float height = baseSlab + top;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(block);
        go.transform.SetParent(parent, true);
        go.transform.localScale = new Vector3(cellSize / fx * Overlap, height / fy, cellSize / fz * Overlap);
        go.transform.position = new Vector3(centerXZ.x, -baseSlab, centerXZ.z);   // base buried, top at `top`
        Strip(go);
        Terrain3DArt.Apply(go);
        MakeStatic(go);
        return go;
    }

    /// <summary>A flat walk/stand box collider on <paramref name="layer"/> at a tier top.</summary>
    public static void EmitFlatCollider(Transform parent, Vector3 centerXZ, float top, float cellSize, int layer)
    {
        var go = new GameObject("c"); go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(centerXZ.x, top - 0.1f, centerXZ.z); go.layer = layer;
        go.AddComponent<BoxCollider>().size = new Vector3(cellSize, 0.2f, cellSize);
        MakeStatic(go);
    }

    /// <summary>A solid grass WEDGE rising one <paramref name="tierStep"/> toward <paramref name="dir"/>,
    /// from the low cell at <paramref name="lowTop"/>. Oversized so it fuses into both lawns; thick so the
    /// body sinks into the hill (only the grass walkway shows). Carries a blocking-free Ground collider.
    /// Geometry identical to <see cref="TerrainSandbox.PlaceRamp"/>.</summary>
    public static void EmitRamp(GameObject block, Bounds b, Transform parent, Vector3 lowCenter,
                                float lowTop, Vector2Int dir, float tierStep, float cellSize,
                                int layer, float rampThick = 0.6f)
    {
        float fx = Mathf.Max(0.01f, b.size.x), fy = Mathf.Max(0.01f, b.size.y), fz = Mathf.Max(0.01f, b.size.z);
        float run = cellSize, rise = tierStep;
        float angleRad = Mathf.Atan2(rise, run);
        float angle = angleRad * Mathf.Rad2Deg, cosA = Mathf.Cos(angleRad);
        float length = Mathf.Sqrt(run * run + rise * rise) * Overlap;
        float width = cellSize * Overlap;

        Vector3 scale; Quaternion rot;
        if (dir.x != 0)
        {
            scale = new Vector3(length / fx, rampThick / fy, width / fz);
            rot = Quaternion.Euler(0f, 0f, dir.x > 0 ? angle : -angle);
        }
        else
        {
            scale = new Vector3(width / fx, rampThick / fy, length / fz);
            rot = Quaternion.Euler(dir.y > 0 ? -angle : angle, 0f, 0f);
        }
        var root = new GameObject("ramp").transform;
        root.SetParent(parent, true);
        root.SetPositionAndRotation(
            new Vector3(lowCenter.x, lowTop + rise * 0.5f - rampThick * 0.5f * cosA, lowCenter.z), rot);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(block);
        go.transform.SetParent(root, false);
        go.transform.localScale = scale;
        go.transform.localPosition = -Vector3.Scale(b.center, scale);
        Strip(go);
        Terrain3DArt.Apply(go);
        var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
        go.layer = layer;
        MakeStatic(go);
    }

    // ---- small helpers -------------------------------------------------------

    public static Bounds MeasureBounds(GameObject prefab)
    {
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity; tmp.transform.localScale = Vector3.one;
        var rends = tmp.GetComponentsInChildren<Renderer>();
        Bounds bb = rends.Length > 0 ? rends[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        for (int i = 1; i < rends.Length; i++) bb.Encapsulate(rends[i].bounds);
        Object.DestroyImmediate(tmp);
        return bb;
    }

    /// <summary>Drop a transform onto the nearest ground surface on <paramref name="mask"/>, plus a foot offset.</summary>
    public static void SnapToGround(Transform t, LayerMask mask, float footOffsetY)
    {
        Vector3 p = t.position;
        if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, mask, QueryTriggerInteraction.Ignore))
            t.position = new Vector3(p.x, hit.point.y + footOffsetY, p.z);
    }

    static void Strip(GameObject go) { foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); }
    static void MakeStatic(GameObject go) => GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
}
