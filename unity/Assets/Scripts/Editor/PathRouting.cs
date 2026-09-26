using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the <b>path network</b> for a generated map: a set of connected polylines (in world XZ) that meander with
/// random turns. There is no centre meeting point: a road runs straight between the places it links (edge to edge,
/// portal to portal, portal to another road) and wanders left and right on the way. This is author/generate-time
/// only (called from <see cref="RecipeGenerator"/>), so it lives in Editor and favours clarity over per-frame speed.
///
/// The same network feeds three things, which is the whole point of computing it once:
/// <list type="bullet">
/// <item><b>Ground</b> — <see cref="GroundBlendBuilder"/> bakes per-vertex distance-to-nearest-segment into the
/// mesh's vertex red channel (the feathered path weight the GroundBlend shader paints the path tile with).</item>
/// <item><b>Placement</b> — StreetRows / Roadside walk the main route (<see cref="Walk"/>) and offset props
/// perpendicular to it, so houses/carts curve to line the road.</item>
/// <item><b>Clearance</b> — <see cref="MinDistanceXZ"/> rejects any solid prop sitting on the path, so a route is
/// never blocked.</item>
/// </list>
/// </summary>
public static class PathRouting
{
    /// <summary>One continuous route as a polyline of world-XZ points (≥2 points; segments are consecutive pairs).</summary>
    public class Route
    {
        public readonly List<Vector2> pts = new List<Vector2>();
        public float Length
        {
            get { float L = 0f; for (int i = 1; i < pts.Count; i++) L += Vector2.Distance(pts[i - 1], pts[i]); return L; }
        }
    }

    /// <summary>
    /// Generate <paramref name="count"/> meandering routes across a <paramref name="sizeX"/>×<paramref name="sizeZ"/>
    /// plot centred on <paramref name="center"/>, each from one edge to the opposite edge. <paramref name="wander"/>
    /// (0..1) controls how far each route drifts off the straight line; <paramref name="smoothness"/> (0..3 Chaikin
    /// passes) rounds the angular corners into curves (0 = the sharp faceted look). All randomness flows from
    /// <paramref name="seed"/> (reproducible). Returns an empty list when <paramref name="count"/> ≤ 0 (a pathless map).
    /// </summary>
    public static List<Route> Build(Vector2 center, float sizeX, float sizeZ, int count, float wander, int seed,
                                    int smoothness = 0)
    {
        var routes = new List<Route>();
        if (count <= 0) return routes;

        var rng = new System.Random(seed * 7919 + 13);
        float halfX = sizeX * 0.5f, halfZ = sizeZ * 0.5f;
        float inset = Mathf.Min(sizeX, sizeZ) * 0.04f;   // start/end a touch inside the edge, not exactly on it

        for (int i = 0; i < count; i++)
        {
            // Alternate the dominant axis so multiple routes actually cross instead of stacking parallel:
            // even routes run left↔right (vary along Z), odd routes run bottom↔top (vary along X).
            Vector2 a, b;
            if ((i & 1) == 0)
            {
                a = new Vector2(center.x - halfX + inset, center.y + Range(rng, -halfZ + inset, halfZ - inset));
                b = new Vector2(center.x + halfX - inset, center.y + Range(rng, -halfZ + inset, halfZ - inset));
            }
            else
            {
                a = new Vector2(center.x + Range(rng, -halfX + inset, halfX - inset), center.y - halfZ + inset);
                b = new Vector2(center.x + Range(rng, -halfX + inset, halfX - inset), center.y + halfZ - inset);
            }
            routes.Add(MeanderRoute(a, b, center, sizeX, sizeZ, wander, smoothness, rng));
        }
        return routes;
    }

    /// <summary>
    /// A straight, plaza-centred <b>cardinal cross</b>: two dead-straight routes through the exact map centre — one
    /// E↔W and one N↔S — so a road runs from the central plaza out to each of the four edge midpoints (N/E/S/W) and
    /// nowhere else. No hub jitter, no wander, no extra spokes. The two routes cross exactly at <paramref name="center"/>,
    /// which is where the plaza sits. Used when a recipe's <c>PathStyle</c> is <c>Cardinal</c>.
    /// </summary>
    public static List<Route> BuildCardinal(Vector2 center, float sizeX, float sizeZ)
    {
        var routes = new List<Route>();
        float halfX = sizeX * 0.5f, halfZ = sizeZ * 0.5f;
        float inset = Mathf.Min(sizeX, sizeZ) * 0.04f;   // stop a touch inside the edge (matches Build)

        // East↔West, dead straight at z = center.y, through the centre.
        var ew = new Route();
        ew.pts.Add(new Vector2(center.x - halfX + inset, center.y));
        ew.pts.Add(center);
        ew.pts.Add(new Vector2(center.x + halfX - inset, center.y));
        routes.Add(ew);

        // North↔South, dead straight at x = center.x, through the centre (so it lines up with the south gate).
        var ns = new Route();
        ns.pts.Add(new Vector2(center.x, center.y - halfZ + inset));
        ns.pts.Add(center);
        ns.pts.Add(new Vector2(center.x, center.y + halfZ - inset));
        routes.Add(ns);

        return routes;
    }

    /// <summary>
    /// Path network for a map whose roads must reach its <b>warp doorways</b> (world-XZ positions on the plot rim,
    /// e.g. a portal's edge+offset). The first two doorways are joined by one meandering road; every further doorway
    /// gets its own meandering road that joins the network at a random point along an existing road. A map with a
    /// single doorway gets one road from it to a random spot in the middle half of the plot. Used by
    /// <see cref="RecipeGenerator"/> when the World Creator passes a map's portal positions; falls back to
    /// <see cref="Build"/> for portal-less maps. Returns empty when there are no exits.
    /// </summary>
    public static List<Route> BuildToExits(Vector2 center, float sizeX, float sizeZ, IReadOnlyList<Vector2> exits,
                                           float wander, int seed, int smoothness = 0)
    {
        var routes = new List<Route>();
        if (exits == null || exits.Count == 0) return routes;

        var rng = new System.Random(seed * 7919 + 13);
        if (exits.Count == 1)
        {
            Vector2 end = center + new Vector2(Range(rng, -0.25f, 0.25f) * sizeX, Range(rng, -0.25f, 0.25f) * sizeZ);
            routes.Add(MeanderRoute(exits[0], end, center, sizeX, sizeZ, wander, smoothness, rng));
            return routes;
        }

        routes.Add(MeanderRoute(exits[0], exits[1], center, sizeX, sizeZ, wander, smoothness, rng));
        for (int i = 2; i < exits.Count; i++)
        {
            // Join somewhere in the middle 60% of a random existing road, so branches meet the network in different places.
            var host = routes[rng.Next(routes.Count)];
            Vector2 join = PointAt(host, Range(rng, 0.2f, 0.8f));
            routes.Add(MeanderRoute(exits[i], join, center, sizeX, sizeZ, wander, smoothness, rng));
        }
        return routes;
    }

    // Chaikin corner-cutting: each pass replaces every corner with two points at ¼ and ¾ of its edges, rounding
    // the polyline toward a curve. The first/last points (the anchors) are preserved so routes still reach them.
    static void Chaikin(List<Vector2> pts, int iterations)
    {
        for (int it = 0; it < iterations && pts.Count >= 3; it++)
        {
            var outPts = new List<Vector2>(pts.Count * 2) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 p = pts[i], q = pts[i + 1];
                outPts.Add(Vector2.Lerp(p, q, 0.25f));
                outPts.Add(Vector2.Lerp(p, q, 0.75f));
            }
            outPts.Add(pts[pts.Count - 1]);
            pts.Clear();
            pts.AddRange(outPts);
        }
    }

    // One route a→b: meandering waypoints, then optional Chaikin rounding (a and b stay anchored).
    static Route MeanderRoute(Vector2 a, Vector2 b, Vector2 center, float sizeX, float sizeZ, float wander,
                              int smoothness, System.Random rng)
    {
        var r = new Route();
        r.pts.Add(a);
        Meander(r.pts, a, b, center, sizeX, sizeZ, wander, rng);
        r.pts.Add(b);
        Chaikin(r.pts, smoothness);
        return r;
    }

    // Append interior waypoints along a→b that swing to one side of the straight line, then — at random waypoints —
    // turn across to the other side. Each waypoint sits 40–100% of the full drift off the line, so a road can never
    // come out near-straight by chance. Waypoints advance monotonically along a→b, so the road never doubles back.
    // The offset is tapered by sin(πt) so the road leaves a and arrives at b head-on, and every waypoint is kept
    // EdgeKeep units inside the plot.
    static void Meander(List<Vector2> pts, Vector2 a, Vector2 b, Vector2 center, float sizeX, float sizeZ,
                        float wander, System.Random rng)
    {
        const float EdgeKeep = 4f;
        Vector2 delta = b - a;
        float len = delta.magnitude;
        if (len < 1e-3f) return;
        Vector2 dir = delta / len;
        Vector2 perp = new Vector2(-dir.y, dir.x);

        int segs = Mathf.Clamp(Mathf.RoundToInt(len / 8f), 2, 10);   // a waypoint roughly every ~8 world units
        float maxDrift = Mathf.Clamp01(wander) * Mathf.Min(len * 0.45f, Mathf.Min(sizeX, sizeZ) / 3f);
        float minX = center.x - sizeX * 0.5f + EdgeKeep, maxX = center.x + sizeX * 0.5f - EdgeKeep;
        float minZ = center.y - sizeZ * 0.5f + EdgeKeep, maxZ = center.y + sizeZ * 0.5f - EdgeKeep;

        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
        for (int s = 1; s < segs; s++)
        {
            float t = (s + Range(rng, -0.3f, 0.3f)) / segs;   // jittered spacing so turns don't fall on a grid
            if (s > 1 && rng.NextDouble() < 0.4) side = -side;   // a turn across the line, at a random waypoint
            float off = side * maxDrift * Range(rng, 0.4f, 1f);
            Vector2 p = Vector2.Lerp(a, b, t) + perp * (off * Mathf.Sin(t * Mathf.PI));
            pts.Add(new Vector2(Mathf.Clamp(p.x, minX, maxX), Mathf.Clamp(p.y, minZ, maxZ)));
        }
    }

    // The point a given fraction (0..1) of the way along a route's length.
    static Vector2 PointAt(Route route, float fraction)
    {
        float target = route.Length * Mathf.Clamp01(fraction);
        for (int i = 1; i < route.pts.Count; i++)
        {
            float seg = Vector2.Distance(route.pts[i - 1], route.pts[i]);
            if (target <= seg && seg > 1e-6f) return Vector2.Lerp(route.pts[i - 1], route.pts[i], target / seg);
            target -= seg;
        }
        return route.pts[route.pts.Count - 1];
    }

    /// <summary>Shortest distance from world-XZ <paramref name="p"/> to the nearest segment of any route
    /// (float.PositiveInfinity if there are no routes). Used for vertex path-weight and prop clearance.</summary>
    public static float MinDistanceXZ(Vector2 p, IReadOnlyList<Route> routes)
    {
        float best = float.PositiveInfinity;
        if (routes == null) return best;
        for (int r = 0; r < routes.Count; r++)
        {
            var pts = routes[r].pts;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = DistToSegment(p, pts[i - 1], pts[i]);
                if (d < best) best = d;
            }
        }
        return best;
    }

    /// <summary>Walk along <paramref name="route"/> emitting a point every <paramref name="spacing"/> world units
    /// with the unit travel direction there — for placing props that follow the road. Skipped if the route is too
    /// short or spacing is non-positive.</summary>
    public static IEnumerable<(Vector2 pos, Vector2 dir)> Walk(Route route, float spacing)
    {
        if (route == null || route.pts.Count < 2 || spacing <= 0f) yield break;
        float next = spacing * 0.5f;   // start half a step in so we don't sit right on the edge
        for (int i = 1; i < route.pts.Count; i++)
        {
            Vector2 a = route.pts[i - 1], b = route.pts[i];
            Vector2 seg = b - a;
            float segLen = seg.magnitude;
            if (segLen < 1e-4f) continue;
            Vector2 dir = seg / segLen;
            while (next <= segLen)
            {
                yield return (a + dir * next, dir);
                next += spacing;
            }
            next -= segLen;   // carry the remainder into the next segment
        }
    }

    // Point→segment distance in 2D (XZ).
    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
}
