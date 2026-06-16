using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dependency-free grid A* over the live 2D physics world — the shared routing brain
/// behind every <see cref="PathAgent"/> (players, allies, monsters). No NavMesh bake,
/// no external package, and NO scene object or layer setup required: it samples the
/// colliders that are already in the world.
///
/// What counts as a wall? A cell is blocked only by a STATIC (no <see cref="Rigidbody2D"/>),
/// NON-trigger collider — i.e. painted walls, crates, tilemap colliders and the map
/// boundary. Dynamic bodies (the player, monsters, allies) and triggers (portals, pickups,
/// interactables) never block, so units route AROUND scenery but THROUGH each other.
///
/// <see cref="FindPath"/> returns a short list of world-space waypoints, string-pulled so
/// motion looks natural instead of zig-zagging along the grid. Open ground is a no-op:
/// if there's clear line of sight to the goal it just returns the goal (no search cost),
/// which is why direct moves still feel snappy.
/// </summary>
public static class Pathfinder
{
    /// <summary>Grid resolution in world units. The world is 1 tile = 1 unit (PPU 16),
    /// so 0.5 gives two cells per tile — fine enough to weave between 1-tile crates.</summary>
    public static float CellSize = 0.5f;

    /// <summary>Hard cap on A* node expansions. If a goal is walled off the search bails
    /// here and <see cref="FindPath"/> returns null (the agent then falls back to a straight
    /// line, exactly the old behaviour — it never freezes).</summary>
    public static int MaxExpansions = 6000;

    /// <summary>Which layers may contain walls. Default = everything; the static/non-trigger
    /// test below does the real filtering, so you normally never touch this.</summary>
    public static LayerMask ObstacleMask = ~0;

    /// <summary>Diagnostic: how the most recent <see cref="FindPath"/> resolved.</summary>
    public static string LastOutcome = "";

    // Reusable query buffers (Unity is single-threaded on the main thread).
    static readonly List<Collider2D> overlapBuf = new List<Collider2D>(8);
    static readonly List<RaycastHit2D> castBuf = new List<RaycastHit2D>(8);

    static readonly (int dx, int dy)[] N8 =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    const float Sqrt2 = 1.41421356f;

    static ContactFilter2D Filter()
    {
        var f = new ContactFilter2D { useTriggers = false };   // triggers (portals/pickups) never block
        f.SetLayerMask(ObstacleMask);
        return f;
    }

    /// <summary>Is <paramref name="c"/> a wall? Everything solid blocks EXCEPT a moving unit,
    /// and a "unit" is defined directly as "something with a <see cref="PathAgent"/>" rather than
    /// guessed from its Rigidbody2D. So houses, trees, crates, edge/tilemap/composite colliders —
    /// however they're set up — all block; only players/monsters/allies (which carry a PathAgent)
    /// are passed through. Triggers are already excluded by the query filter.</summary>
    static bool IsWall(Collider2D c)
    {
        if (c == null) return false;
        var body = c.attachedRigidbody;
        if (body == null) return true;                     // a static collider with no body → wall
        return body.GetComponent<PathAgent>() == null;     // has a body but isn't a pathfinding unit → wall
    }

    /// <summary>Would an agent of <paramref name="radius"/> centred at <paramref name="center"/>
    /// overlap a wall? Moving units (dynamic bodies) are ignored.</summary>
    static bool Blocked(Vector2 center, float radius)
    {
        int n = Physics2D.OverlapCircle(center, Mathf.Max(0.01f, radius), Filter(), overlapBuf);
        for (int i = 0; i < n; i++)
            if (IsWall(overlapBuf[i]))
                return true;
        return false;
    }

    /// <summary>Can a disc of <paramref name="radius"/> slide straight from a to b without
    /// hitting a static wall? Used both for the open-ground fast path and path smoothing.</summary>
    public static bool HasClearPath(Vector2 a, Vector2 b, float radius)
    {
        Vector2 d = b - a;
        float dist = d.magnitude;
        if (dist < 1e-4f) return !Blocked(a, radius);

        int n = Physics2D.CircleCast(a, Mathf.Max(0.01f, radius), d / dist, Filter(), castBuf, dist);
        for (int i = 0; i < n; i++)
            if (IsWall(castBuf[i].collider))
                return false;
        return true;
    }

    static long Key(int x, int y) => ((long)x << 32) | (uint)y;
    static Vector2 Center(int x, int y) => new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
    static int Floor(float v) => Mathf.FloorToInt(v / CellSize);

    static float Heur(int x, int y, int gx, int gy)
    {
        int dx = Mathf.Abs(x - gx), dy = Mathf.Abs(y - gy);
        // Octile distance: exact cost over an 8-connected grid, so A* stays admissible.
        return (dx + dy) + (Sqrt2 - 2f) * Mathf.Min(dx, dy);
    }

    /// <summary>
    /// Route from <paramref name="from"/> to <paramref name="to"/> keeping <paramref name="radius"/>
    /// clearance from walls. Returns the waypoints to walk (excluding the start, ending at the
    /// goal), or null if no route exists (caller should fall back to a straight line).
    /// </summary>
    public static List<Vector2> FindPath(Vector2 from, Vector2 to, float radius)
    {
        // Open ground: just go straight. Keeps the direct, snappy feel and costs one cast.
        if (HasClearPath(from, to, radius))
        {
            LastOutcome = "clear-LOS (no wall between me and goal)";
            return new List<Vector2> { to };
        }

        int sx = Floor(from.x), sy = Floor(from.y);
        int gx = Floor(to.x), gy = Floor(to.y);

        // Target standing against a wall (its cell is blocked)? Aim for the nearest open
        // cell beside it so we still route right up to it.
        if (Blocked(Center(gx, gy), radius) && !FindNearbyOpen(ref gx, ref gy, radius))
        {
            LastOutcome = "goal-cell-walled-off";
            return null;
        }

        var came = new Dictionary<long, long>();
        var g = new Dictionary<long, float>();
        var closed = new HashSet<long>();
        var open = new List<(float f, long key, int x, int y)>();   // tiny binary min-heap

        long startKey = Key(sx, sy);
        long goalKey = Key(gx, gy);
        g[startKey] = 0f;
        HeapPush(open, (Heur(sx, sy, gx, gy), startKey, sx, sy));

        bool found = false;
        int expansions = 0;

        // Track the reachable node that got CLOSEST to the goal. If the goal is walled off, we
        // return a PARTIAL route to this node (walk as far toward the goal as we can) instead of
        // giving up — that's what stops a unit from beelining straight through the wall/boundary.
        long bestKey = startKey;
        float bestH = Heur(sx, sy, gx, gy);

        while (open.Count > 0 && expansions < MaxExpansions)
        {
            var cur = HeapPop(open);
            if (!closed.Add(cur.key)) continue;   // stale heap entry (lazy decrease-key)
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

                // The start cell may itself sample "blocked" (you can stand right against a
                // wall) — always allow leaving it; every other cell must be clear.
                bool isStart = nx == sx && ny == sy;
                if (!isStart && Blocked(Center(nx, ny), radius)) continue;

                // No diagonal squeeze through a blocked corner.
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

        // Reached the goal → route there. Walled off → route to the closest cell we could reach.
        long endKey = found ? goalKey : bestKey;

        // We couldn't even step off the start cell toward the goal — genuinely stuck. Let the
        // caller decide (hold position); there's nowhere closer to walk to.
        if (!found && endKey == startKey)
        {
            LastOutcome = $"no-route (searched {expansions} cells) — already as close as possible";
            return null;
        }

        // Walk cameFrom back to the start, collecting cell centres.
        var cells = new List<Vector2>();
        long k = endKey;
        while (true)
        {
            cells.Add(Center((int)(k >> 32), (int)(uint)k));
            if (k == startKey) break;
            k = came[k];
        }
        cells.Reverse();

        // Smooth. End on the EXACT goal if we reached it; otherwise end on the closest reachable
        // cell centre (NEVER the unreachable goal), so the unit walks up to the wall and stops on
        // the inside instead of trying to push through it.
        Vector2 endPoint = found ? to : Center((int)(endKey >> 32), (int)(uint)endKey);
        var pts = new List<Vector2>(cells.Count + 1) { from };
        for (int i = 1; i < cells.Count - 1; i++) pts.Add(cells[i]);
        pts.Add(endPoint);
        var smoothed = Smooth(pts, radius);
        LastOutcome = found
            ? $"routed ({smoothed.Count} waypoints)"
            : $"partial → closest reachable ({smoothed.Count} waypoints, searched {expansions} cells)";
        return smoothed;
    }

    // Spiral outward from a blocked goal cell to the nearest walkable one (within 4 cells).
    static bool FindNearbyOpen(ref int gx, ref int gy, float radius)
    {
        for (int r = 1; r <= 4; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;   // ring only
                    if (!Blocked(Center(gx + dx, gy + dy), radius)) { gx += dx; gy += dy; return true; }
                }
        return false;
    }

    // String-pulling: drop any waypoint we can see past. Returns points AFTER the start
    // (pts[0]) and always ends on the final point.
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

    // ---- tiny binary min-heap keyed by f-score -------------------------------

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
