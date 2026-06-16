using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3D port of <see cref="Pathfinder"/> for the 2.5D world — the SAME grid A*, just run over the
/// XZ plane against 3D colliders instead of Physics2D. Grid cells are (X, Z). Obstacles are
/// sampled at a fixed <see cref="ProbeY"/> height, so the flat ground plane is ignored and only
/// props that stick up count as walls. Units (anything with a <see cref="CharacterController"/>)
/// are passed through, so characters route AROUND scenery but THROUGH each other — matching the
/// 2D design. Open ground returns a straight line (one spherecast), keeping direct moves snappy.
///
/// Kept separate from Pathfinder.cs so the 2D game is untouched (additive 2.5D migration).
/// </summary>
public static class Pathfinder3D
{
    /// <summary>Grid resolution in world units (two cells per 1-unit tile, like the 2D version).</summary>
    public static float CellSize = 0.5f;
    public static int MaxExpansions = 6000;
    public static LayerMask ObstacleMask = ~0;

    /// <summary>World-Y at which obstacles are sampled — above the ground, through the body of
    /// standing props. Set to the agent's mid-height (~0.5 for a 1-unit-tall sprite). In height-aware
    /// mode this is RELATIVE to the sampled ground height under each cell.</summary>
    public static float ProbeY = 0.5f;

    // ---- elevation (opt-in) ---------------------------------------------------
    /// <summary>When true, the grid samples the ground height under each cell and only lets you step
    /// between cells whose heights differ by <= <see cref="MaxStep"/> — so ramps/stairs are walkable
    /// and cliffs are not. Default false keeps the original flat-XZ behaviour (no per-cell raycasts).
    /// Set via <see cref="Pathfinder3DConfig"/>.</summary>
    public static bool HeightAware = false;

    /// <summary>Layers that count as walkable GROUND (terrain tiers + ramps) for height sampling.
    /// Must be set (non-zero) when <see cref="HeightAware"/> is on, or every cell reads as void.</summary>
    public static LayerMask GroundMask = 0;

    /// <summary>Max walkable height change between adjacent cells (a step/stair/ramp rise). Larger
    /// deltas are cliffs and block the move.</summary>
    public static float MaxStep = 0.6f;

    const float RayTop = 60f;   // height we drop ground-probe rays from
    static readonly System.Collections.Generic.Dictionary<long, float> groundCache
        = new System.Collections.Generic.Dictionary<long, float>();

    // Reset the elevation statics at every Play start so they can't leak between scenes when
    // "Enter Play Mode" domain reload is disabled — each scene's Pathfinder3DConfig (or its absence)
    // is then authoritative. Flat scenes (no config) get the default flat behaviour.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetElevationStatics()
    {
        HeightAware = false;
        GroundMask = 0;
        ObstacleMask = ~0;
        groundCache.Clear();
    }

    public static string LastOutcome = "";

    static readonly Collider[] overlapBuf = new Collider[16];
    static readonly RaycastHit[] castBuf = new RaycastHit[16];

    // Ground surface Y under a world XZ point (raycast straight down on the ground layers), or NaN if
    // there's no ground there (a hole/void — treated as unwalkable).
    static float SampleGroundYWorld(Vector2 xz)
    {
        if (Physics.Raycast(new Vector3(xz.x, RayTop, xz.y), Vector3.down, out var hit, RayTop * 2f, GroundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return float.NaN;
    }

    static float GroundYCell(int x, int y)
    {
        long k = Key(x, y);
        if (groundCache.TryGetValue(k, out float gy)) return gy;
        gy = SampleGroundYWorld(Center(x, y));
        groundCache[k] = gy;
        return gy;
    }

    static readonly (int dx, int dy)[] N8 =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    };
    const float Sqrt2 = 1.41421356f;

    // A unit (CharacterController) is never a wall; everything else solid is. Triggers are
    // excluded by the query; the flat ground is excluded by sampling at ProbeY (above it).
    static bool IsWall(Collider c) => c != null && !(c is CharacterController);

    static bool Blocked(Vector2 center, float radius)
    {
        float py = ProbeY;
        if (HeightAware)
        {
            float gy = SampleGroundYWorld(center);
            if (float.IsNaN(gy)) return true;   // no ground under this cell = void = unwalkable
            py = gy + ProbeY;                   // probe obstacles relative to the local ground height
        }
        Vector3 p = new Vector3(center.x, py, center.y);
        int n = Physics.OverlapSphereNonAlloc(p, Mathf.Max(0.01f, radius), overlapBuf, ObstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (IsWall(overlapBuf[i])) return true;
        return false;
    }

    public static bool HasClearPath(Vector2 a, Vector2 b, float radius)
    {
        Vector3 a3 = new Vector3(a.x, ProbeY, a.y);
        Vector2 d2 = b - a;
        float dist = d2.magnitude;
        if (dist < 1e-4f) return !Blocked(a, radius);

        Vector3 dir = new Vector3(d2.x, 0f, d2.y) / dist;
        int n = Physics.SphereCastNonAlloc(a3, Mathf.Max(0.01f, radius), dir, castBuf, dist, ObstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (IsWall(castBuf[i].collider)) return false;
        return true;
    }

    static long Key(int x, int y) => ((long)x << 32) | (uint)y;
    static Vector2 Center(int x, int y) => new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
    static int Floor(float v) => Mathf.FloorToInt(v / CellSize);

    static float Heur(int x, int y, int gx, int gy)
    {
        int dx = Mathf.Abs(x - gx), dy = Mathf.Abs(y - gy);
        return (dx + dy) + (Sqrt2 - 2f) * Mathf.Min(dx, dy);   // octile distance
    }

    /// <summary>Route on the XZ plane from <paramref name="from"/> to <paramref name="to"/>
    /// (both = (X, Z)) keeping <paramref name="radius"/> clearance. Returns waypoints (excluding
    /// the start, ending at the goal / closest reachable cell), or null if truly stuck.</summary>
    public static List<Vector2> FindPath(Vector2 from, Vector2 to, float radius)
    {
        if (HeightAware) groundCache.Clear();   // heights are per-query (terrain may have changed)

        // Flat LOS fast-path: a straight spherecast can't see a cliff edge (no collider in the air),
        // so skip it in height-aware mode and let the grid (with the step-gate) route honestly.
        if (!HeightAware && HasClearPath(from, to, radius))
        {
            LastOutcome = "clear-LOS";
            return new List<Vector2> { to };
        }

        int sx = Floor(from.x), sy = Floor(from.y);
        int gx = Floor(to.x), gy = Floor(to.y);

        if (Blocked(Center(gx, gy), radius) && !FindNearbyOpen(ref gx, ref gy, radius))
        {
            LastOutcome = "goal-cell-walled-off";
            return null;
        }

        var came = new Dictionary<long, long>();
        var g = new Dictionary<long, float>();
        var closed = new HashSet<long>();
        var open = new List<(float f, long key, int x, int y)>();

        long startKey = Key(sx, sy);
        long goalKey = Key(gx, gy);
        g[startKey] = 0f;
        HeapPush(open, (Heur(sx, sy, gx, gy), startKey, sx, sy));

        bool found = false;
        int expansions = 0;
        long bestKey = startKey;
        float bestH = Heur(sx, sy, gx, gy);

        while (open.Count > 0 && expansions < MaxExpansions)
        {
            var cur = HeapPop(open);
            if (!closed.Add(cur.key)) continue;
            expansions++;
            if (cur.x == gx && cur.y == gy) { found = true; break; }

            float hcur = Heur(cur.x, cur.y, gx, gy);
            if (hcur < bestH) { bestH = hcur; bestKey = cur.key; }

            float cg = g[cur.key];
            foreach (var (dx, dy) in N8)
            {
                int nx = cur.x + dx, ny = cur.y + dy;
                long nk = Key(nx, ny);
                if (closed.Contains(nk)) continue;

                // Elevation step-gate: only walk to a neighbour whose ground height is within MaxStep
                // (a stair/ramp rise). A bigger delta (or a void) is a cliff — not a valid move.
                if (HeightAware)
                {
                    float gn = GroundYCell(nx, ny);
                    if (float.IsNaN(gn) || Mathf.Abs(gn - GroundYCell(cur.x, cur.y)) > MaxStep) continue;
                }

                bool isStart = nx == sx && ny == sy;
                if (!isStart && Blocked(Center(nx, ny), radius)) continue;
                if (dx != 0 && dy != 0 &&
                    (Blocked(Center(cur.x + dx, cur.y), radius) || Blocked(Center(cur.x, cur.y + dy), radius)))
                    continue;

                float ng = cg + ((dx != 0 && dy != 0) ? Sqrt2 : 1f);
                if (g.TryGetValue(nk, out float old) && ng >= old) continue;

                g[nk] = ng;
                came[nk] = cur.key;
                HeapPush(open, (ng + Heur(nx, ny, gx, gy), nk, nx, ny));
            }
        }

        long endKey = found ? goalKey : bestKey;
        if (!found && endKey == startKey)
        {
            LastOutcome = $"no-route (searched {expansions})";
            return null;
        }

        var cells = new List<Vector2>();
        long k = endKey;
        while (true)
        {
            cells.Add(Center((int)(k >> 32), (int)(uint)k));
            if (k == startKey) break;
            k = came[k];
        }
        cells.Reverse();

        Vector2 endPoint = found ? to : Center((int)(endKey >> 32), (int)(uint)endKey);
        var pts = new List<Vector2>(cells.Count + 1) { from };
        for (int i = 1; i < cells.Count - 1; i++) pts.Add(cells[i]);
        pts.Add(endPoint);
        // Smoothing uses straight-line LOS, which (like the fast-path above) can't see cliffs — so in
        // height-aware mode keep the grid waypoints (the controller's slope handling smooths motion).
        var smoothed = HeightAware ? pts.GetRange(1, pts.Count - 1) : Smooth(pts, radius);
        LastOutcome = found ? $"routed ({smoothed.Count} wp)" : $"partial ({smoothed.Count} wp)";
        return smoothed;
    }

    static bool FindNearbyOpen(ref int gx, ref int gy, float radius)
    {
        for (int r = 1; r <= 4; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    if (!Blocked(Center(gx + dx, gy + dy), radius)) { gx += dx; gy += dy; return true; }
                }
        return false;
    }

    static List<Vector2> Smooth(List<Vector2> pts, float radius)
    {
        var outp = new List<Vector2>();
        if (pts.Count == 0) return outp;
        if (pts.Count <= 2) { outp.Add(pts[pts.Count - 1]); return outp; }

        int anchor = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            if (i == pts.Count - 1) { outp.Add(pts[i]); break; }
            if (!HasClearPath(pts[anchor], pts[i + 1], radius)) { outp.Add(pts[i]); anchor = i; }
        }
        return outp;
    }

    static void HeapPush(List<(float f, long key, int x, int y)> h, (float f, long key, int x, int y) item)
    {
        h.Add(item);
        int i = h.Count - 1;
        while (i > 0)
        {
            int p = (i - 1) / 2;
            if (h[p].f <= h[i].f) break;
            (h[p], h[i]) = (h[i], h[p]);
            i = p;
        }
    }

    static (float f, long key, int x, int y) HeapPop(List<(float f, long key, int x, int y)> h)
    {
        var root = h[0];
        int last = h.Count - 1;
        h[0] = h[last];
        h.RemoveAt(last);

        int i = 0, n = h.Count;
        while (true)
        {
            int l = 2 * i + 1, r = 2 * i + 2, s = i;
            if (l < n && h[l].f < h[s].f) s = l;
            if (r < n && h[r].f < h[s].f) s = r;
            if (s == i) break;
            (h[s], h[i]) = (h[i], h[s]);
            i = s;
        }
        return root;
    }
}
