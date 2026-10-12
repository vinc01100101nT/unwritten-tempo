using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid A* for the 2.5D world — run over the
/// XZ plane against 3D colliders instead of Physics2D. Grid cells are (X, Z). Obstacles are
/// sampled at a fixed <see cref="ProbeY"/> height, so the flat ground plane is ignored and only
/// props that stick up count as walls. Units (anything with a <see cref="CharacterController"/>)
/// are passed through, so characters route AROUND scenery but THROUGH each other — matching the
/// 2D design. Open ground returns a straight line (one spherecast), keeping direct moves snappy.
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
    /// <summary>When true, the grid samples the ground under each cell — its height and how steep it is — and only
    /// lets a route step between cells a body could walk between (<see cref="GroundElevation.Walkable"/>, the same
    /// rule every body's controller walks by) — so slopes and stairs are walkable and cliffs are not. Default false
    /// keeps the original flat-XZ behaviour (no per-cell raycasts). Set via <see cref="Pathfinder3DConfig"/>.</summary>
    public static bool HeightAware = false;

    /// <summary>Layers that count as walkable GROUND (terrain tiers + ramps) for height sampling.
    /// Must be set (non-zero) when <see cref="HeightAware"/> is on, or every cell reads as void.</summary>
    public static LayerMask GroundMask = 0;

    const float RayTop = 60f;   // height we drop ground-probe rays from
    // Per cell: x = ground height (NaN = nothing underneath), y = how steep it is there (a tangent, see GroundElevation.SlopeTan).
    static readonly System.Collections.Generic.Dictionary<long, Vector2> groundCache
        = new System.Collections.Generic.Dictionary<long, Vector2>();

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
        areaGrids.Clear();
    }

    public static string LastOutcome = "";

    static readonly Collider[] overlapBuf = new Collider[16];
    static readonly RaycastHit[] castBuf = new RaycastHit[16];

    // Reused A* working sets — cleared at the top of each FindPath instead of newly allocated every query.
    // Pathfinding is single-threaded (main-thread only), so sharing these is safe and kills the steady GC
    // churn of many mobs re-routing on a cadence.
    static readonly Dictionary<long, long> came = new Dictionary<long, long>();
    static readonly Dictionary<long, float> gScore = new Dictionary<long, float>();
    static readonly HashSet<long> closed = new HashSet<long>();
    static readonly List<(float f, long key, int x, int y)> open = new List<(float f, long key, int x, int y)>();
    // Reused result-building scratch, so route reconstruction allocates nothing per query either.
    static readonly List<Vector2> cellsScratch = new List<Vector2>();
    static readonly List<Vector2> ptsScratch = new List<Vector2>();

    // Ground surface Y under a world XZ point (raycast straight down on the ground layers), or NaN if
    // there's no ground there (a hole/void — treated as unwalkable).
    static float SampleGroundYWorld(Vector2 xz) => SampleGround(xz, out float y, out _) ? y : float.NaN;

    // The ground under a world XZ point: its height and how steep it is there. False when there is none.
    static bool SampleGround(Vector2 xz, out float y, out float slopeTan)
    {
        if (Physics.Raycast(new Vector3(xz.x, RayTop, xz.y), Vector3.down, out var hit, RayTop * 2f, GroundMask, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            slopeTan = GroundElevation.SlopeTan(hit.normal);
            return true;
        }
        y = float.NaN; slopeTan = 0f;
        return false;
    }

    static Vector2 GroundCell(int x, int y)
    {
        long k = Key(x, y);
        if (groundCache.TryGetValue(k, out var g)) return g;
        g = SampleGround(Center(x, y), out float gy, out float slope) ? new Vector2(gy, slope) : new Vector2(float.NaN, 0f);
        groundCache[k] = g;
        return g;
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
        if (!HeightAware) return BlockedAt(center, radius, ProbeY);
        float gy = SampleGroundYWorld(center);
        if (float.IsNaN(gy)) return true;   // no ground under this cell = void = unwalkable
        return BlockedAt(center, radius, gy + ProbeY);   // probe obstacles relative to the local ground height
    }

    // Is anything solid within radius of this point, at this height?
    static bool BlockedAt(Vector2 center, float radius, float py)
    {
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

    /// <summary>Is the straight segment a→b walkable in one shot? Dispatches by mode: a flat SphereCast when the
    /// world is flat, or a height-aware march (below) when elevation matters. Used by both the straight-line
    /// fast-path and the route smoother, so open ground takes a direct line in EITHER mode.</summary>
    static bool LineClear(Vector2 a, Vector2 b, float radius)
        => HeightAware ? HasClearWalkableLine(a, b, radius) : HasClearPath(a, b, radius);

    /// <summary>Height-aware straight-line test. A flat SphereCast can't see a cliff edge (there's no collider in
    /// the air over a drop), which is why height-aware mode used to refuse to straight-line or smooth at all —
    /// and so the agent stair-stepped cell-to-cell even across clearly open, level ground (the "drift"). Instead
    /// we MARCH the segment in small steps and accept it only when the ground stays continuous (no void, no rise
    /// between samples that a body could not walk, see <see cref="GroundElevation.Walkable"/>) AND no obstacle blocks
    /// the swept body. Cliffs/steps
    /// still fail and fall back to the grid; flat open ground goes perfectly straight.</summary>
    static bool HasClearWalkableLine(Vector2 a, Vector2 b, float radius)
    {
        Vector2 d = b - a;
        float dist = d.magnitude;
        if (dist < 1e-4f) return !Blocked(a, radius);
        Vector2 dir = d / dist;

        float stepLen = Mathf.Max(0.05f, CellSize * 0.5f);
        int steps = Mathf.CeilToInt(dist / stepLen);

        if (!SampleGround(a, out float prevGy, out float prevSlope)) return false;   // start over a void

        float prevAlong = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float along = Mathf.Min(dist, i * stepLen);
            Vector2 p = a + dir * along;
            if (!SampleGround(p, out float gy, out float slope)) return false;   // hole / off the edge
            if (!GroundElevation.Walkable(gy - prevGy, along - prevAlong, prevSlope, slope)) return false;   // cliff, ledge or too steep
            if (Blocked(p, radius)) return false;           // prop / wall in the way
            prevGy = gy; prevSlope = slope; prevAlong = along;
        }
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

    /// <summary>Route on the XZ plane from <paramref name="from"/> to <paramref name="to"/> (both = (X, Z))
    /// keeping <paramref name="radius"/> clearance, writing the waypoints (excluding the start, ending at the
    /// goal / closest reachable cell) into the caller-owned <paramref name="result"/>. Returns true if a path
    /// was produced; false (with <paramref name="result"/> left empty) if truly stuck.
    ///
    /// Allocation-free in the steady state: the result list and every internal working set / scratch list are
    /// reused, so a field of mobs can repath on a cadence without feeding the GC (the per-frame combat garbage
    /// the profiler pinned on MonsterAI3D.Update was these result Lists).</summary>
    public static bool FindPath(Vector2 from, Vector2 to, float radius, List<Vector2> result, int maxExpansions = 0)
    {
        result.Clear();

        // Mobs pass a smaller cap than the player (their routes are short — aggro/leash are a few units),
        // so a blocked goal can't burn the full 6000-cell budget on a tiny chase. <=0 → the global default.
        int cap = maxExpansions > 0 ? maxExpansions : MaxExpansions;

        if (HeightAware) groundCache.Clear();   // heights are per-query (terrain may have changed)

        // Straight-line fast-path: if the direct segment is walkable (mode-aware — a flat spherecast when flat,
        // a ground-continuity march when height-aware), take it. This is what keeps a click on open ground a
        // single straight move instead of a grid staircase, in BOTH flat and height-aware scenes.
        if (LineClear(from, to, radius))
        {
            LastOutcome = "clear-LOS";
            result.Add(to);
            return true;
        }

        int sx = Floor(from.x), sy = Floor(from.y);
        int gx = Floor(to.x), gy = Floor(to.y);

        if (Blocked(Center(gx, gy), radius) && !FindNearbyOpen(ref gx, ref gy, radius))
        {
            LastOutcome = "goal-cell-walled-off";
            return false;
        }

        // NO WAY THERE AT ALL — known in nanoseconds instead of found out by searching every cell that can be
        // reached until the budget runs out (MEASURED: 52–57 ms from a cliff top to a creature below it, asked
        // again every 0.3 s while chasing). Head straight at the goal and stop at the last spot still in our own
        // area: the edge above it, or the foot of the cliff below it — where the search would have ended anyway.
        if (KnownApart(sx, sy, gx, gy, out AreaGrid area, out ushort own))
        {
            ApproachWithin(area, own, from, to, result);
            LastOutcome = "partial-other-area";
            return true;
        }

        came.Clear(); gScore.Clear(); closed.Clear(); open.Clear();

        long startKey = Key(sx, sy);
        long goalKey = Key(gx, gy);
        gScore[startKey] = 0f;
        HeapPush(open, (Heur(sx, sy, gx, gy), startKey, sx, sy));

        bool found = false;
        int expansions = 0;
        long bestKey = startKey;
        float bestH = Heur(sx, sy, gx, gy);

        while (open.Count > 0 && expansions < cap)
        {
            var cur = HeapPop(open);
            if (!closed.Add(cur.key)) continue;
            expansions++;
            if (cur.x == gx && cur.y == gy) { found = true; break; }

            float hcur = Heur(cur.x, cur.y, gx, gy);
            if (hcur < bestH) { bestH = hcur; bestKey = cur.key; }

            float cg = gScore[cur.key];
            foreach (var (dx, dy) in N8)
            {
                int nx = cur.x + dx, ny = cur.y + dy;
                long nk = Key(nx, ny);
                if (closed.Contains(nk)) continue;

                // Elevation gate: only to a neighbour a body could walk to — the same rule its controller walks by.
                // A void, a ledge or ground too steep is not a valid move.
                if (HeightAware)
                {
                    Vector2 gn = GroundCell(nx, ny), gc = GroundCell(cur.x, cur.y);
                    float run = (dx != 0 && dy != 0) ? CellSize * Sqrt2 : CellSize;
                    if (float.IsNaN(gn.x) || !GroundElevation.Walkable(gn.x - gc.x, run, gc.y, gn.y)) continue;
                }

                bool isStart = nx == sx && ny == sy;
                if (!isStart && Blocked(Center(nx, ny), radius)) continue;
                if (dx != 0 && dy != 0 &&
                    (Blocked(Center(cur.x + dx, cur.y), radius) || Blocked(Center(cur.x, cur.y + dy), radius)))
                    continue;

                float ng = cg + ((dx != 0 && dy != 0) ? Sqrt2 : 1f);
                if (gScore.TryGetValue(nk, out float old) && ng >= old) continue;

                gScore[nk] = ng;
                came[nk] = cur.key;
                HeapPush(open, (ng + Heur(nx, ny, gx, gy), nk, nx, ny));
            }
        }

        long endKey = found ? goalKey : bestKey;
        if (!found && endKey == startKey)
        {
            LastOutcome = "no-route";   // constant string — no per-query interpolation alloc
            return false;
        }

        // Reconstruct the cell chain into reused scratch (no per-call List alloc).
        cellsScratch.Clear();
        long k = endKey;
        while (true)
        {
            cellsScratch.Add(Center((int)(k >> 32), (int)(uint)k));
            if (k == startKey) break;
            k = came[k];
        }
        cellsScratch.Reverse();

        Vector2 endPoint = found ? to : Center((int)(endKey >> 32), (int)(uint)endKey);
        ptsScratch.Clear();
        ptsScratch.Add(from);
        for (int i = 1; i < cellsScratch.Count - 1; i++) ptsScratch.Add(cellsScratch[i]);
        ptsScratch.Add(endPoint);

        // Pull the grid route taut. The smoother's line-of-sight test is mode-aware (LineClear), so it can
        // straighten runs over flat ground even in height-aware scenes while still keeping the waypoints needed
        // to hug a cliff/step edge — no more cell-to-cell stair-stepping across open ground.
        Smooth(ptsScratch, radius, result);

        LastOutcome = found ? "routed" : "partial";
        return true;
    }

    // ---- connected areas ---------------------------------------------------------
    //
    // Every cell of a map, labelled once — when the map loads — with which connected area it belongs to, by the
    // same rules the search walks by: ground under it, nothing solid in it, and a step to its neighbour a body could
    // walk (GroundElevation.Walkable) —
    // its neighbour. Two cells with different labels can never be joined by a route, so a search between them is
    // skipped. The labelling is deliberately MORE permissive than the search (the thinnest possible body, and
    // diagonal steps allowed past corners), so "different areas" is only ever said when it is certainly true;
    // when in doubt the search runs exactly as it always did.

    sealed class AreaGrid
    {
        public int x0, y0, nx, ny;
        public ushort[] label;   // 0 = not walkable (solid, or nothing underneath)
        // Every walkable cell, grouped by label (KeeperMath.GroupByLabel): area L's cells are byLabel[firstOf[L]] up to
        // byLabel[firstOf[L + 1]]. Built once at load so a random landing is one lookup — 4 bytes per walkable cell.
        public int[] byLabel;
        public int[] firstOf;
        public ushort At(int x, int y)
        {
            x -= x0; y -= y0;
            return x < 0 || y < 0 || x >= nx || y >= ny ? (ushort)0 : label[y * nx + x];
        }
        public bool Holds(int x, int y) => x >= x0 && y >= y0 && x < x0 + nx && y < y0 + ny;
    }

    static readonly List<AreaGrid> areaGrids = new List<AreaGrid>();

    /// <summary>Label the connected areas of the ground inside <paramref name="bounds"/> (a whole map). One-off, at
    /// load: MEASURED 13 ms and 28.8 KB for a 60×60 map. Returns what to hand <see cref="ForgetAreas"/> when the map
    /// goes; null when the labelling was skipped (too many areas to tell apart), which only disables the shortcut.</summary>
    public static object LabelAreas(Bounds bounds)
    {
        var g = new AreaGrid { x0 = Floor(bounds.min.x), y0 = Floor(bounds.min.z) };
        g.nx = Floor(bounds.max.x) - g.x0 + 1;
        g.ny = Floor(bounds.max.z) - g.y0 + 1;
        int n = g.nx * g.ny;
        g.label = new ushort[n];
        var height = new float[n];
        var slope = new float[n];
        var open = new bool[n];
        for (int y = 0; y < g.ny; y++)
            for (int x = 0; x < g.nx; x++)
            {
                int i = y * g.nx + x;
                Vector2 c = Center(g.x0 + x, g.y0 + y);
                float gy = 0f;
                if (HeightAware && !SampleGround(c, out gy, out slope[i])) continue;
                height[i] = gy;
                open[i] = !BlockedAt(c, 0.01f, HeightAware ? gy + ProbeY : ProbeY);
            }

        var queue = new int[n];
        ushort next = 0;
        for (int start = 0; start < n; start++)
        {
            if (!open[start] || g.label[start] != 0) continue;
            if (next == ushort.MaxValue) return null;
            next++;
            int head = 0, tail = 0;
            g.label[start] = next;
            queue[tail++] = start;
            while (head < tail)
            {
                int cur = queue[head++];
                int cx = cur % g.nx, cy = cur / g.nx;
                foreach (var (dx, dy) in N8)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= g.nx || y >= g.ny) continue;
                    int i = y * g.nx + x;
                    if (!open[i] || g.label[i] != 0) continue;
                    if (HeightAware && !GroundElevation.Walkable(height[i] - height[cur],
                            (dx != 0 && dy != 0) ? CellSize * Sqrt2 : CellSize, slope[cur], slope[i])) continue;
                    g.label[i] = next;
                    queue[tail++] = i;
                }
            }
        }
        g.byLabel = KeeperMath.GroupByLabel(g.label, next, out g.firstOf);
        areaGrids.Add(g);
        return g;
    }

    /// <summary>A random walkable spot on the same connected stretch of ground as <paramref name="from"/> — so whoever
    /// lands there can always walk back — where a body <paramref name="radius"/> wide fits, picked evenly among that
    /// stretch's cells, at the ground's height. FALSE when <paramref name="from"/> is not on labelled ground (a map with
    /// no labels, or a spot off the walkable ground) or no fitting cell turned up in a few tries. Asked once per use
    /// (a Fly Wing), never per frame: each try is one lookup, plus the fitting test and a ground probe.</summary>
    public static bool RandomSpotInArea(Vector3 from, float radius, out Vector3 spot)
    {
        spot = from;
        int sx = Floor(from.x), sy = Floor(from.z);
        for (int a = 0; a < areaGrids.Count; a++)
        {
            var g = areaGrids[a];
            if (!g.Holds(sx, sy)) continue;
            ushort own = g.At(sx, sy);
            if (own == 0 || g.firstOf == null || own + 1 >= g.firstOf.Length) return false;
            int first = g.firstOf[own], count = g.firstOf[own + 1] - first;
            if (count <= 0) return false;
            for (int attempt = 0; attempt < RandomSpotTries; attempt++)
            {
                int at = g.byLabel[first + CombatRng.Range(0, count)];
                int gx = g.x0 + at % g.nx, gy = g.y0 + at / g.nx;
                // The labels were worked out for the thinnest body; a real one may not fit, so it is nudged to the
                // nearest cell that does — and kept only if that is still the same stretch of ground.
                if (Blocked(Center(gx, gy), radius) && !FindNearbyOpen(ref gx, ref gy, radius)) continue;
                if (g.At(gx, gy) != own) continue;
                Vector2 c = Center(gx, gy);
                if (!SampleGround(c, out float y, out _)) continue;
                spot = new Vector3(c.x, y, c.y);
                return true;
            }
            return false;
        }
        return false;
    }

    // How many cells a random landing tries before giving up — a fitting cell is the rule, so this is rarely reached.
    const int RandomSpotTries = 8;

    /// <summary>Drop a map's labels when the map goes, so a later map set down in the same place is not judged by them.</summary>
    public static void ForgetAreas(object labels)
    {
        if (labels is AreaGrid g) areaGrids.Remove(g);
    }

    // Are these two cells in one labelled map but in areas no route joins? A cell that is not walkable (label 0) is
    // never judged: the search has its own ways of starting from, or aiming near, such a cell.
    static bool KnownApart(int sx, int sy, int gx, int gy, out AreaGrid area, out ushort own)
    {
        for (int i = 0; i < areaGrids.Count; i++)
        {
            var g = areaGrids[i];
            if (!g.Holds(sx, sy) || !g.Holds(gx, gy)) continue;
            own = g.At(sx, sy);
            ushort theirs = g.At(gx, gy);
            area = g;
            return own != 0 && theirs != 0 && own != theirs;
        }
        area = null; own = 0;
        return false;
    }

    // Walk the straight line toward the goal and stop at the last point still in our own area — which is where we
    // already stand when we are at the edge, so a chase waits there instead of walking off it.
    static void ApproachWithin(AreaGrid area, ushort own, Vector2 from, Vector2 to, List<Vector2> result)
    {
        Vector2 d = to - from;
        float dist = d.magnitude;
        float step = CellSize * 0.5f;
        Vector2 last = from;
        for (float along = step; along < dist; along += step)
        {
            Vector2 p = from + d * (along / dist);
            ushort here = area.At(Floor(p.x), Floor(p.y));
            if (here == own) last = p;
            else if (here != 0) break;
        }
        result.Add(last);
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

    // Pull the route taut: keep a waypoint only where the straight line to the next-next point is blocked.
    // Appends the kept waypoints to <paramref name="outp"/> (caller-owned) — no allocation.
    static void Smooth(List<Vector2> pts, float radius, List<Vector2> outp)
    {
        if (pts.Count == 0) return;
        if (pts.Count <= 2) { outp.Add(pts[pts.Count - 1]); return; }

        int anchor = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            if (i == pts.Count - 1) { outp.Add(pts[i]); break; }
            if (!LineClear(pts[anchor], pts[i + 1], radius)) { outp.Add(pts[i]); anchor = i; }
        }
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
