using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where rain water can stand, as plain numbers in and numbers out, so every rule here can be checked without a map.
/// Still water is flat, and a still water surface reflects exactly like a mirror laid at its own height. Rain stands
/// in two places. On level ground (the heights at which the ground is flat, <see cref="AddFlatTriangles"/>) it lies as
/// a thin sheet, <see cref="FilmDepth"/> deep, in scattered puddles — more of them where the ground rises next to it
/// (<see cref="Runoff"/>), because that is where rain running downhill ends up. Rain on sloping ground runs downhill
/// and collects in the hollows (<see cref="Watershed"/>): each hollow gets the rain of all the slope that drains into it,
/// fills from its lowest point, and once full spills over its lowest rim into the next hollow down — two that fill to
/// the rim between them become one lake. So a small hollow under a big slope fills fast and deep, a wide one under a
/// little slope barely wets, and every lake's level follows from how much rain has fallen (<see cref="Rainfall"/>),
/// which every player works out the same from the shared world clock. The edge of the map holds water in; ground that
/// is level everywhere (a town) sends no rain anywhere and keeps its puddles.
/// </summary>
public static class WaterPooling
{
    /// <summary>How close a surface must be to a water height to count as lying on it. Half the smallest gap measured
    /// between two flat heights of any built map (3 mm: a road's ground and its border strip), so no surface can match
    /// the wrong one.</summary>
    public const float HeightTolerance = 0.0015f;

    /// <summary>Two flat triangles closer in height than this are the same water surface.</summary>
    public const float SameHeight = 0.0001f;

    /// <summary>A flat height with less ground than this, in square world units, is a sliver and holds no water.</summary>
    public const float MinWaterArea = 1f;

    /// <summary>The up-component of a surface normal at or above which the surface counts as level (about 3.6°).</summary>
    public const float FlatNormalY = 0.998f;

    /// <summary>Side of one square of the runoff map, in world units.</summary>
    public const float RunoffCell = 0.5f;
    /// <summary>How far from rising ground water still gathers, in world units.</summary>
    public const float RunoffReach = 3f;
    /// <summary>A rise this tall next to a spot sends it the most water; a smaller rise sends proportionally less.</summary>
    public const float RunoffFullRise = 0.3f;
    /// <summary>How much the most runoff raises the puddle pattern at a spot, on the pattern's own 0..1 scale.</summary>
    public const float RunoffPull = 0.35f;

    /// <summary>How deep, in world units, the sheet of water on level ground stands. Its edge is where the ground next
    /// to the level has risen this far, so it follows the ground's own curve.</summary>
    public const float FilmDepth = 0.01f;
    /// <summary>How clear a lake is, in world units: what lies under it shows through by e^(−depth ÷ this)
    /// (Beer–Lambert) — muddy rain water, so a thing 4 cm down shows at about a third and one at chest depth (16 cm)
    /// is gone.</summary>
    public const float Clarity = 0.04f;

    /// <summary>A lake shallower than this, or smaller than <see cref="MinWaterArea"/>, is no lake.</summary>
    public const float MinLakeDepth = 0.005f;
    /// <summary>Seconds of rain between each rise of the lakes.</summary>
    public const float StepSeconds = 30f;
    /// <summary>Seconds each rise takes to fill in, rather than jumping.</summary>
    public const float RiseSeconds = 3f;
    /// <summary>How much rain falls in each step of the heaviest rain, in world units of water on the ground (a lighter
    /// rain falls its share of this).</summary>
    public const float RainPerStep = 0.004f;
    /// <summary>The most rain one wet spell leaves standing, in world units: ten steps of the heaviest rain, five
    /// minutes. More rain than this runs off the map.</summary>
    public const float MostRain = 0.04f;
    /// <summary>Seconds for the most rain to drain away once it stops.</summary>
    public const float DrainSeconds = 60f;

    /// <summary>Side of one square of the world the on-screen test works in, in world units.</summary>
    public const float ChunkSize = 8f;

    /// <summary>One flat height being gathered: its total level ground and the chunks of the world it lies in.</summary>
    public sealed class FlatHeight
    {
        public float y;
        /// <summary>The highest level in this group — above <see cref="y"/> only once <see cref="JoinFilms"/> has
        /// joined levels that one sheet of water covers.</summary>
        public float top;
        public float area;
        public readonly Dictionary<long, Bounds> chunks = new Dictionary<long, Bounds>();
    }

    /// <summary>Add every exactly-level triangle of one mesh, placed by <paramref name="toWorld"/>, to
    /// <paramref name="into"/>: grouped by height, with its area and the chunks it covers.</summary>
    public static void AddFlatTriangles(Vector3[] verts, int[] tris, Matrix4x4 toWorld, List<FlatHeight> into)
    {
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector3 a = toWorld.MultiplyPoint3x4(verts[tris[t]]);
            Vector3 b = toWorld.MultiplyPoint3x4(verts[tris[t + 1]]);
            Vector3 c = toWorld.MultiplyPoint3x4(verts[tris[t + 2]]);
            if (Mathf.Abs(a.y - b.y) > SameHeight || Mathf.Abs(a.y - c.y) > SameHeight) continue;

            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            if (area <= 0f) continue;

            FlatHeight h = null;
            for (int i = 0; i < into.Count; i++)
                if (Mathf.Abs(into[i].y - a.y) <= SameHeight) { h = into[i]; break; }
            if (h == null) { h = new FlatHeight { y = a.y, top = a.y }; into.Add(h); }
            h.area += area;

            Vector3 centre = (a + b + c) / 3f;
            long key = ChunkKey(centre.x, centre.z);
            var box = new Bounds(a, Vector3.zero);
            box.Encapsulate(b);
            box.Encapsulate(c);
            if (h.chunks.TryGetValue(key, out var have)) { have.Encapsulate(box); h.chunks[key] = have; }
            else h.chunks[key] = box;
        }
    }

    static long ChunkKey(float x, float z)
    {
        long cx = Mathf.FloorToInt(x / ChunkSize), cz = Mathf.FloorToInt(z / ChunkSize);
        return (cx << 32) ^ (cz & 0xffffffffL);
    }

    /// <summary>Keep the flat heights that hold water — at least <see cref="MinWaterArea"/> of ground — largest first,
    /// at most <paramref name="max"/> of them.</summary>
    public static void KeepWaterHeights(List<FlatHeight> heights, int max)
    {
        heights.RemoveAll(h => h.area < MinWaterArea);
        heights.Sort((p, q) => q.area.CompareTo(p.area));
        if (heights.Count > max) heights.RemoveRange(max, heights.Count - max);
    }

    /// <summary>The sheets of water on level ground: levels closer together than <see cref="FilmDepth"/> lie under one
    /// sheet (a cliff top's grass 4 mm over the ground, a road's border strip 3 mm over it), which covers them all —
    /// its floor the lowest level, its top the highest. Most ground first.</summary>
    public static List<FlatHeight> JoinFilms(List<FlatHeight> levels)
    {
        var sorted = new List<FlatHeight>(levels);
        sorted.Sort((p, q) => p.y.CompareTo(q.y));
        var films = new List<FlatHeight>();
        foreach (var l in sorted)
        {
            var last = films.Count > 0 ? films[films.Count - 1] : null;
            if (last != null && l.y - last.top <= FilmDepth)
            {
                last.top = l.y;
                last.area += l.area;
                foreach (var kv in l.chunks)
                {
                    if (last.chunks.TryGetValue(kv.Key, out var have)) { have.Encapsulate(kv.Value); last.chunks[kv.Key] = have; }
                    else last.chunks[kv.Key] = kv.Value;
                }
                continue;
            }
            var film = new FlatHeight { y = l.y, top = l.y, area = l.area };
            foreach (var kv in l.chunks) film.chunks[kv.Key] = kv.Value;
            films.Add(film);
        }
        films.Sort((p, q) => q.area.CompareTo(p.area));
        return films;
    }

    /// <summary>The index of the water height <paramref name="y"/> lies on — the nearest within
    /// <see cref="HeightTolerance"/> — or -1 when it lies on none.</summary>
    public static int MatchHeight(float y, float[] heights, int count)
    {
        int best = -1;
        float bestGap = HeightTolerance;
        for (int i = 0; i < count; i++)
        {
            float gap = Mathf.Abs(y - heights[i]);
            if (gap <= bestGap) { best = i; bestGap = gap; }
        }
        return best;
    }

    /// <summary>The highest ground under the centre of every square of a grid (<paramref name="nx"/> ×
    /// <paramref name="nz"/> squares of <paramref name="cell"/>, lowest corner at <paramref name="originX"/>,
    /// <paramref name="originZ"/>), from one mesh's triangles placed by <paramref name="toWorld"/>. Squares no
    /// triangle covers keep what they held, so start the grid at <see cref="float.NegativeInfinity"/> and feed every
    /// ground mesh in turn.</summary>
    public static void RaiseToGround(Vector3[] verts, int[] tris, Matrix4x4 toWorld,
                                     float originX, float originZ, float cell, int nx, int nz, float[] grid)
    {
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector3 a = toWorld.MultiplyPoint3x4(verts[tris[t]]);
            Vector3 b = toWorld.MultiplyPoint3x4(verts[tris[t + 1]]);
            Vector3 c = toWorld.MultiplyPoint3x4(verts[tris[t + 2]]);
            float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(det) < 1e-9f) continue;

            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - originX) / cell - 0.5f));
            int i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - originX) / cell - 0.5f));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - originZ) / cell - 0.5f));
            int j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - originZ) / cell - 0.5f));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = originX + (i + 0.5f) * cell, pz = originZ + (j + 0.5f) * cell;
                    float wa = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                    float wb = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                    float wc = 1f - wa - wb;
                    if (wa < 0f || wb < 0f || wc < 0f) continue;
                    float y = wa * a.y + wb * b.y + wc * c.y;
                    int k = j * nx + i;
                    if (y > grid[k]) grid[k] = y;
                }
        }
    }

    /// <summary>Which squares of the ground grid lie on a sheet's level ground: a square gets sheet index + 1 in
    /// <paramref name="levelOf"/> when an exactly-level triangle at that sheet's height covers its centre and is the
    /// top ground there (<paramref name="grid"/>, from <see cref="RaiseToGround"/>). A slope that only passes through
    /// a sheet's height is never level ground, so it holds no sheet. Feed every ground mesh in turn.</summary>
    public static void MarkLevelGround(Vector3[] verts, int[] tris, Matrix4x4 toWorld, List<FlatHeight> films, int filmCount,
                                       float[] grid, float originX, float originZ, float cell, int nx, int nz, byte[] levelOf)
    {
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector3 a = toWorld.MultiplyPoint3x4(verts[tris[t]]);
            Vector3 b = toWorld.MultiplyPoint3x4(verts[tris[t + 1]]);
            Vector3 c = toWorld.MultiplyPoint3x4(verts[tris[t + 2]]);
            if (Mathf.Abs(a.y - b.y) > SameHeight || Mathf.Abs(a.y - c.y) > SameHeight) continue;
            int film = -1;
            for (int i = 0; i < filmCount && film < 0; i++)
                if (a.y >= films[i].y - SameHeight && a.y <= films[i].top + SameHeight) film = i;
            if (film < 0) continue;
            float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(det) < 1e-9f) continue;

            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - originX) / cell - 0.5f));
            int i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - originX) / cell - 0.5f));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - originZ) / cell - 0.5f));
            int j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - originZ) / cell - 0.5f));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = originX + (i + 0.5f) * cell, pz = originZ + (j + 0.5f) * cell;
                    float wa = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                    float wb = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                    if (wa < 0f || wb < 0f || 1f - wa - wb < 0f) continue;
                    int k = j * nx + i;
                    if (Mathf.Abs(a.y - grid[k]) <= SameHeight) levelOf[k] = (byte)(film + 1);
                }
        }
    }

    /// <summary>How much rain runs to each square of a ground grid that holds water, 0..1: for the rising ground
    /// within <see cref="RunoffReach"/>, the most of (its rise over <see cref="RunoffFullRise"/>, capped at 1) × (how
    /// near it is). Flat ground with nothing rising near it gets 0; the foot of a slope or a cliff gets the most.
    /// Only squares whose ground lies on one of the <paramref name="count"/> water <paramref name="heights"/> receive
    /// (nothing else can hold the water); every square with ground sends. Squares with no ground
    /// (<see cref="float.NegativeInfinity"/>) neither send nor receive.</summary>
    public static void Runoff(float[] grid, int nx, int nz, float cell, float[] heights, int count, float[] result)
    {
        int reach = Mathf.CeilToInt(RunoffReach / cell);
        var offI = new List<int>(); var offJ = new List<int>(); var offNear = new List<float>();
        for (int dj = -reach; dj <= reach; dj++)
            for (int di = -reach; di <= reach; di++)
            {
                float near = 1f - Mathf.Sqrt(di * di + dj * dj) * cell / RunoffReach;
                if (near <= 0f) continue;
                offI.Add(di); offJ.Add(dj); offNear.Add(near);
            }

        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                float here = grid[k], best = 0f;
                if (!float.IsNegativeInfinity(here) && MatchHeight(here, heights, count) >= 0)
                    for (int o = 0; o < offI.Count; o++)
                    {
                        int ni = i + offI[o], nj = j + offJ[o];
                        if (ni < 0 || ni >= nx || nj < 0 || nj >= nz) continue;
                        float rise = grid[nj * nx + ni] - here;
                        if (!(rise > 0f)) continue;
                        float w = Mathf.Min(1f, rise / RunoffFullRise) * offNear[o];
                        if (w > best) best = w;
                    }
                result[k] = best;
            }
    }

    /// <summary>Rain fallen over <paramref name="seconds"/> at <paramref name="intensity"/> (0..1, 1 = the heaviest):
    /// a step of <see cref="RainPerStep"/> × intensity every <see cref="StepSeconds"/> — the first at once — each
    /// filling in over <see cref="RiseSeconds"/>.</summary>
    public static float RainOver(double seconds, float intensity)
    {
        if (seconds <= 0 || intensity <= 0f) return 0f;
        int step = (int)System.Math.Floor(seconds / StepSeconds);
        float into = Mathf.Clamp01((float)(seconds - step * (double)StepSeconds) / RiseSeconds);
        return RainPerStep * Mathf.Clamp01(intensity) * (step + into);
    }

    /// <summary>How much rain stands on the map now, in world units of water, from the weather in force since
    /// <paramref name="since"/> (its rain <paramref name="intensity"/>, 0 = dry) and the weather before it (from
    /// <paramref name="beforeSince"/>, <paramref name="beforeIntensity"/>) — all on the shared world clock, so every
    /// player gets the same. Rain adds up to <see cref="MostRain"/>; dry weather drains it over
    /// <see cref="DrainSeconds"/>. A place that always holds water (<paramref name="held"/>, 0..1) never drains below
    /// that share of one step.</summary>
    public static float Rainfall(double now, float intensity, double since, float beforeIntensity, double beforeSince, float held)
    {
        float standing = beforeIntensity > 0f ? Mathf.Min(MostRain, RainOver(since - beforeSince, beforeIntensity)) : 0f;
        float rain = intensity > 0f
            ? Mathf.Min(MostRain, standing + RainOver(now - since, intensity))
            : Mathf.Max(0f, standing - MostRain * (float)((now - since) / DrainSeconds));
        return Mathf.Max(rain, RainPerStep * Mathf.Clamp01(held));
    }



    /// <summary>The squares of a ground grid (<see cref="RaiseToGround"/>) from lowest to highest, skipping squares with
    /// no ground — the order every lake is filled in.</summary>
    public static int[] LowestFirst(float[] grid)
    {
        var order = new List<int>(grid.Length);
        for (int k = 0; k < grid.Length; k++) if (!float.IsNegativeInfinity(grid[k])) order.Add(k);
        order.Sort((a, b) => grid[a].CompareTo(grid[b]));
        return order.ToArray();
    }

    /// <summary>One lake on a ground grid: its lowest ground, its level, and the squares whose ground lies below it.</summary>
    public sealed class Lake
    {
        public float bottom;
        public float level;
        public readonly List<int> cells = new List<int>();
    }

    /// <summary>A map's ground as hollows inside hollows, worked out once per map: filling it from its lowest ground up,
    /// every lowest point starts a hollow, and where the water of two hollows would meet (a saddle) they become one
    /// bigger hollow above it. Each hollow knows how many sloping squares drain down into it (its catchment), where it
    /// spills when full (its lowest rim) and which hollow that spill runs down into, and every ground height below it,
    /// sorted, so the level a volume of water reaches is one search.</summary>
    public sealed class Watershed
    {
        internal readonly List<Hollow> hollows = new List<Hollow>();
        internal readonly List<int> roots = new List<int>();
    }

    internal sealed class Hollow
    {
        public float bottom, birth, spill = float.PositiveInfinity;
        public int parent = -1, saddle = -1, spillsInto = -1;
        public readonly List<int> kids = new List<int>();
        public readonly List<int> own = new List<int>();
        public int catchment;
        public float[] heights;
        public double[] sums;
        public double stored;
        public bool pooled;
        public float level;
    }

    /// <summary>Build the hollows of a ground grid (<see cref="RaiseToGround"/>). <paramref name="sheds"/>[k] says whether
    /// rain on square k runs off (sloping ground) — rain on level ground stays there as its sheet of puddles. Running
    /// water always takes the steepest way down, square by square.</summary>
    public static Watershed BuildWatershed(float[] grid, int nx, int nz, bool[] sheds)
    {
        var ws = new Watershed();
        int n = grid.Length;
        var order = LowestFirst(grid);
        var root = new int[n];
        var hollowOf = new int[n];
        var ownerOf = new int[n];
        for (int k = 0; k < n; k++) { root[k] = -1; ownerOf[k] = -1; }
        var touching = new List<int>(4);
        foreach (int c in order)
        {
            float h = grid[c];
            touching.Clear();
            int ci = c % nx, cj = c / nx;
            for (int d = 0; d < 4; d++)
            {
                int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                int k = nj * nx + ni;
                if (root[k] < 0) continue;
                int r = Find(root, k);
                if (!touching.Contains(r)) touching.Add(r);
            }
            int atId;
            if (touching.Count == 0)
            {
                ws.hollows.Add(new Hollow { bottom = h, birth = h });
                atId = ws.hollows.Count - 1;
                root[c] = c;
                hollowOf[c] = atId;
            }
            else if (touching.Count == 1)
            {
                root[c] = touching[0];
                atId = hollowOf[touching[0]];
            }
            else
            {
                var at = new Hollow { bottom = float.PositiveInfinity, birth = h };
                ws.hollows.Add(at);
                atId = ws.hollows.Count - 1;
                foreach (int r in touching)
                {
                    var kid = ws.hollows[hollowOf[r]];
                    kid.spill = h;
                    kid.saddle = c;
                    kid.parent = atId;
                    at.kids.Add(hollowOf[r]);
                    if (kid.bottom < at.bottom) at.bottom = kid.bottom;
                    root[r] = c;
                }
                root[c] = c;
                hollowOf[c] = atId;
            }
            ws.hollows[atId].own.Add(c);
            ownerOf[c] = atId;
        }

        // Where the rain of each sloping square ends up: the hollow its steepest way down reaches.
        for (int c = 0; c < n; c++)
            if (sheds[c] && ownerOf[c] >= 0) ws.hollows[ownerOf[Down(grid, nx, nz, c)]].catchment++;

        // Where each hollow spills when full: from its rim, down the steepest way on the far side, into the first hollow
        // there (the hollow around both when no ground falls away beyond the rim).
        for (int i = 0; i < ws.hollows.Count; i++)
        {
            var w = ws.hollows[i];
            if (w.saddle < 0) continue;
            int si = w.saddle % nx, sj = w.saddle / nx, beyond = -1;
            for (int d = 0; d < 4; d++)
            {
                int ni = si + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = sj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                int k = nj * nx + ni;
                if (ownerOf[k] < 0 || grid[k] > grid[w.saddle] || Inside(ws, ownerOf[k], i)) continue;
                if (beyond < 0 || grid[k] < grid[beyond]) beyond = k;
            }
            w.spillsInto = beyond < 0 ? w.parent : ownerOf[Down(grid, nx, nz, beyond)];
            if (Inside(ws, w.spillsInto, i)) w.spillsInto = w.parent;
        }
        for (int i = 0; i < ws.hollows.Count; i++)
        {
            var w = ws.hollows[i];
            if (float.IsPositiveInfinity(w.spill)) ws.roots.Add(i);
            // Water that spills into a hollow finds the lowest of the hollows inside it first.
            w.kids.Sort((a, b) => ws.hollows[a].bottom.CompareTo(ws.hollows[b].bottom));
        }

        // Every hollow's ground heights, sorted, with running sums: kids are always made before their parent.
        for (int i = 0; i < ws.hollows.Count; i++)
        {
            var w = ws.hollows[i];
            var all = new List<float>(w.own.Count);
            foreach (int c in w.own) all.Add(grid[c]);
            foreach (int kid in w.kids) all.AddRange(ws.hollows[kid].heights);
            all.Sort();
            w.heights = all.ToArray();
            w.sums = new double[w.heights.Length + 1];
            for (int k = 0; k < w.heights.Length; k++) w.sums[k + 1] = w.sums[k] + w.heights[k];
        }
        return ws;
    }

    // The square the steepest way down from square c ends on: step to the lowest lower neighbour until none is lower.
    static int Down(float[] grid, int nx, int nz, int c)
    {
        for (int guard = 0; guard < grid.Length; guard++)
        {
            int ci = c % nx, cj = c / nx, next = -1;
            float lowest = grid[c];
            for (int d = 0; d < 4; d++)
            {
                int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                int k = nj * nx + ni;
                if (grid[k] < lowest) { lowest = grid[k]; next = k; }
            }
            if (next < 0) return c;
            c = next;
        }
        return c;
    }

    // Is hollow x the hollow `of` or one inside it?
    static bool Inside(Watershed ws, int x, int of)
    {
        for (; x >= 0; x = ws.hollows[x].parent) if (x == of) return true;
        return false;
    }

    // Water, in (world units × squares), a hollow holds up to level L.
    static double VolumeTo(Hollow w, float level)
    {
        if (float.IsPositiveInfinity(level)) return double.PositiveInfinity;
        int k = UpperBound(w.heights, level);
        return k * (double)level - w.sums[k];
    }

    // The level a hollow's water stands at when it holds this much.
    static float LevelFor(Hollow w, double volume)
    {
        if (volume <= 0) return w.bottom;
        int lo = 1, hi = w.heights.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            double level = (volume + w.sums[mid]) / mid;
            if (mid < w.heights.Length && level > w.heights[mid]) lo = mid + 1; else hi = mid;
        }
        return (float)((volume + w.sums[lo]) / lo);
    }

    static int UpperBound(float[] a, float v)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (a[mid] < v) lo = mid + 1; else hi = mid; }
        return lo;
    }

    // Pour water into a hollow whose `stored` already counts all the water inside it: into the hollows inside it,
    // lowest first, each overflowing into the next, and into its own pool above them once every one is full. Returns
    // what did not fit — it spills over the hollow's rim.
    static double Pour(Watershed ws, Hollow w, double water)
    {
        if (water <= 0) return 0;
        if (w.kids.Count > 0 && !w.pooled)
        {
            foreach (int kid in w.kids)
            {
                if (water <= 0) break;
                double left = Pour(ws, ws.hollows[kid], water);
                w.stored += water - left;
                water = left;
            }
            if (water <= 0) return 0;
            w.pooled = true;
        }
        double room = VolumeTo(w, w.spill) - w.stored;
        double take = System.Math.Min(water, room);
        w.stored += take;
        w.level = LevelFor(w, w.stored);
        return water - take;
    }

    /// <summary>The lakes <paramref name="rain"/> (world units of water fallen, <see cref="Rainfall"/>) leaves on a
    /// ground grid: each hollow gets the rain of its own sloping squares, poured in from the smallest hollows up, and
    /// what overflows a hollow runs down the slope into the next. Largest first; a lake shallower than
    /// <see cref="MinLakeDepth"/> or smaller than <see cref="MinWaterArea"/> is left out.</summary>
    public static List<Lake> Fill(Watershed ws, float[] grid, float cell, float rain)
    {
        foreach (var w in ws.hollows) { w.stored = 0; w.pooled = false; w.level = w.bottom; }
        // It rains everywhere at once: every hollow takes the rain of its own catchment, and whatever a full hollow
        // cannot hold runs on down to the next.
        if (rain > 0f)
            for (int i = 0; i < ws.hollows.Count; i++)
                Spill(ws, i, (double)ws.hollows[i].catchment * rain);

        var lakes = new List<Lake>();
        var stack = new Stack<int>(ws.roots);
        while (stack.Count > 0)
        {
            var w = ws.hollows[stack.Pop()];
            if (w.kids.Count > 0 && !w.pooled) { foreach (int kid in w.kids) stack.Push(kid); continue; }
            if (w.stored <= 0 || w.level - w.bottom < MinLakeDepth) continue;
            var lake = new Lake { bottom = w.bottom, level = w.level };
            Collect(ws, w, grid, lake);
            if (lake.cells.Count * cell * cell >= MinWaterArea) lakes.Add(lake);
        }
        lakes.Sort((p, q) => q.cells.Count.CompareTo(p.cells.Count));
        return lakes;
    }

    // Pour water into hollow `at`; what it cannot hold spills over its rim and runs down into the hollow its spill
    // reaches, and so on — into the hollow around it once the one below is full as well (their water meets and rises
    // together). Every hollow around the one that took water counts it too.
    static void Spill(Watershed ws, int at, double water)
    {
        for (int guard = 0; water > 1e-12 && at >= 0 && guard < 4 * ws.hollows.Count + 16; guard++)
        {
            var w = ws.hollows[at];
            double left = Pour(ws, w, water);
            double took = water - left;
            for (int up = w.parent; up >= 0 && took > 0; up = ws.hollows[up].parent) ws.hollows[up].stored += took;
            water = left;
            if (water <= 1e-12) break;
            int next = w.spillsInto;
            at = next >= 0 && !IsFull(ws.hollows[next]) ? next : w.parent;
        }
    }

    static bool IsFull(Hollow w) => w.stored >= VolumeTo(w, w.spill) - 1e-9;

    static int Find(int[] root, int k)
    {
        int r = k;
        while (root[r] != r) r = root[r];
        while (root[k] != r) { int next = root[k]; root[k] = r; k = next; }
        return r;
    }

    static void Collect(Watershed ws, Hollow w, float[] grid, Lake lake)
    {
        foreach (int c in w.own) if (grid[c] < lake.level) lake.cells.Add(c);
        foreach (int kid in w.kids) Collect(ws, ws.hollows[kid], grid, lake);
    }

    /// <summary>Where each lake of <paramref name="upper"/> (the lakes at a little more rain) stood at the rain just
    /// below it (<paramref name="lower"/>), so its surface can glide between the two instead of jumping: water only
    /// rises with rain, so every lower lake lies inside one upper lake — the one <paramref name="bodyOf"/> marks on its
    /// first square (body index + 1, upper lake k being body <paramref name="firstLake"/> + k). An upper lake with no
    /// lower lake inside it starts just under its own lowest ground, so it grows from nothing. Fills
    /// <paramref name="from"/>[firstLake + k].</summary>
    public static void StartLevels(List<Lake> lower, List<Lake> upper, int upperCount, byte[] bodyOf, int firstLake, float[] from)
    {
        for (int k = 0; k < upperCount; k++) from[firstLake + k] = upper[k].bottom - 2f * HeightTolerance;
        foreach (var l in lower)
        {
            if (l.cells.Count == 0) continue;
            int body = bodyOf[l.cells[0]] - 1;
            if (body < firstLake || body >= firstLake + upperCount) continue;
            from[body] = Mathf.Min(upper[body - firstLake].level, Mathf.Max(from[body], l.level));
        }
    }

    /// <summary>A 0..1 value stored the way an 8-bit texture holds it, so what is drawn and what is asked agree.</summary>
    public static float AsStored(float v) => Mathf.Round(Mathf.Clamp01(v) * 255f) / 255f;

    /// <summary>Read a grid of values at world (<paramref name="x"/>, <paramref name="z"/>) the way a GPU reads a
    /// texture with smooth filtering: between the four nearest square centres, clamped at the edges.</summary>
    public static float SampleSmooth(float[] grid, int nx, int nz, float originX, float originZ, float cell, float x, float z)
    {
        if (nx <= 0 || nz <= 0) return 0f;
        float gx = (x - originX) / cell - 0.5f, gz = (z - originZ) / cell - 0.5f;
        int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
        float fx = gx - i, fz = gz - j;
        int i0 = Mathf.Clamp(i, 0, nx - 1), i1 = Mathf.Clamp(i + 1, 0, nx - 1);
        int j0 = Mathf.Clamp(j, 0, nz - 1), j1 = Mathf.Clamp(j + 1, 0, nz - 1);
        float low  = Mathf.Lerp(grid[j0 * nx + i0], grid[j0 * nx + i1], fx);
        float high = Mathf.Lerp(grid[j1 * nx + i0], grid[j1 * nx + i1], fx);
        return Mathf.Lerp(low, high, fz);
    }
}
