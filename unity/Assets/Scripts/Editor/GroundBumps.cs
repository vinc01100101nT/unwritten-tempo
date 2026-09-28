using UnityEngine;

/// <summary>
/// The rolling mounds, or flat levels, a biome can give its ground (<see cref="BiomeRow.bumpHeight"/>,
/// <see cref="BiomeRow.bumpLevels"/>): the heights for every vertex of the ground mesh, and the one rule that keeps
/// them walkable. Plain numbers in, plain numbers out, so the whole shape
/// can be checked without building a map.
///
/// <para>Heights are a row-major grid of <c>(nx+1)×(nz+1)</c> values, index <c>j*(nx+1)+i</c>, in the same order and
/// triangulation <see cref="GroundBlendBuilder.LayPlotSampled"/> lays its vertices.</para>
/// </summary>
public static class GroundBumps
{
    /// <summary>The spacing, in world units, the shape is worked out at before it is read back at the ground's vertices
    /// (ground drawn coarser than this is worked out on a finer grid). On the vertices alone, a level's edge and the foot
    /// of its ramp could only follow the grid's own lines, so they ran in straight zigzags that puddles traced exactly;
    /// worked out finer, a vertex just past an edge gets the height the smooth ground has there.</summary>
    public const float FineSpacing = 0.2f;

    /// <summary>The widest spacing, in world units, a shaped ground is drawn with. A level's edge can be no rounder than
    /// the triangles drawing it: at 0.8 the water on a level still ended in 0.8-unit steps along its rim, because a sheet
    /// of water ends exactly where the drawn ground drops away.</summary>
    public const float DrawnSpacing = 0.25f;

    /// <summary>How softly, in world units, every edge of the shape is rounded once its slopes are limited — the
    /// spread of the blur that turns the sharp crease at a ramp's top and foot into a curve. A blur never makes a slope
    /// steeper, and it leaves the middle of a level exactly level.</summary>
    public const float EdgeRounding = 0.5f;

    /// <summary>The ground's shape for a biome: rolling mounds, or — with <paramref name="levels"/> of 2 or more — that
    /// many flat levels joined by slopes, from 0 up to <paramref name="height"/>, hills about <paramref name="width"/>
    /// across. Never steeper than <paramref name="maxSlope"/>: where it would be, the higher side is cut back into a
    /// slope a creature can walk (<see cref="LimitSlope"/>), so every level keeps its height and only its edges give.</summary>
    public static float[] Make(int nx, int nz, float sizeX, float sizeZ, float originX, float originZ,
                               int seed, float height, float width, int levels, float maxSlope)
    {
        int per = Mathf.Max(1, Mathf.RoundToInt(sizeX / nx / FineSpacing));
        int fx = nx * per, fz = nz * per;
        float fdx = sizeX / fx, fdz = sizeZ / fz, dx = sizeX / nx, dz = sizeZ / nz;
        var fine = Heights(fx, fz, sizeX, sizeZ, originX, originZ, seed, height, width);
        if (levels >= 2) Terrace(fine, levels, height);
        float allowed = maxSlope;
        float[] h = null;
        for (int tries = 0; tries < 6; tries++)
        {
            var limited = (float[])fine.Clone();
            LimitSlope(limited, fx, fz, fdx, fdz, allowed);
            Soften(limited, fx, fz, fdx, fdz, EdgeRounding);
            h = AtVertices(limited, fx, nx, nz, per);
            float steepest = SteepestSlope(h, nx, nz, dx, dz);
            if (steepest <= maxSlope) return h;
            // A slope limited on the fine points can still run a little steeper across a drawn triangle: tighten and redo.
            allowed *= maxSlope / steepest * 0.98f;
        }
        CapSlope(h, nx, nz, dx, dz, maxSlope);
        return h;
    }

    /// <summary>Blur a height grid (<paramref name="nx"/>+1 × <paramref name="nz"/>+1 points, spacing
    /// <paramref name="dx"/> × <paramref name="dz"/>) with a bell curve <paramref name="spread"/> wide, clamped at the
    /// edges. The weights add up to exactly one, so ground that is level everywhere within reach stays level.</summary>
    public static void Soften(float[] h, int nx, int nz, float dx, float dz, float spread)
    {
        if (spread <= 0f) return;
        int row = nx + 1;
        var tmp = new float[h.Length];
        Pass(h, tmp, true);
        Pass(tmp, h, false);

        void Pass(float[] from, float[] to, bool alongX)
        {
            float step = alongX ? dx : dz;
            int reach = Mathf.CeilToInt(3f * spread / step);
            var w = new float[reach * 2 + 1];
            float sum = 0f;
            for (int k = -reach; k <= reach; k++) { float d = k * step / spread; w[k + reach] = Mathf.Exp(-0.5f * d * d); sum += w[k + reach]; }
            for (int k = 0; k < w.Length; k++) w[k] /= sum;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float acc = 0f;
                    for (int k = -reach; k <= reach; k++)
                    {
                        int si = alongX ? Mathf.Clamp(i + k, 0, nx) : i, sj = alongX ? j : Mathf.Clamp(j + k, 0, nz);
                        acc += from[sj * row + si] * w[k + reach];
                    }
                    to[j * row + i] = acc;
                }
        }
    }

    /// <summary>The fine grid's heights at the ground's own vertices (every <paramref name="step"/>-th fine point).</summary>
    public static float[] AtVertices(float[] fine, int fx, int nx, int nz, int step)
    {
        var h = new float[(nx + 1) * (nz + 1)];
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
                h[j * (nx + 1) + i] = fine[j * step * (fx + 1) + i * step];
        return h;
    }

    /// <summary>Lower every vertex that stands higher than a <paramref name="slope"/> rise from any vertex near it allows,
    /// until none does: the high ground's edges become ramps of that slope, and nothing low is raised. Each vertex looks
    /// at its sixteen nearest neighbours in the two sweeps — the eight around it and the eight a knight's move away — so
    /// a ramp's foot and top run within about 2% of true circles around a level instead of eight-sided shapes.</summary>
    public static void LimitSlope(float[] h, int nx, int nz, float dx, float dz, float slope)
    {
        int row = nx + 1;
        int[] oi = { -1, -2, -1, 0, 1, 2, -1, 1 };
        int[] oj = { 0, -1, -1, -1, -1, -1, -2, -2 };
        var rise = new float[oi.Length];
        for (int o = 0; o < oi.Length; o++) rise[o] = slope * Mathf.Sqrt(oi[o] * dx * oi[o] * dx + oj[o] * dz * oj[o] * dz);

        bool changed = true;
        for (int pass = 0; changed && pass < 200; pass++)
        {
            changed = false;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                    for (int o = 0; o < oi.Length; o++) changed |= Lower(i, j, i + oi[o], j + oj[o], rise[o]);
            for (int j = nz; j >= 0; j--)
                for (int i = nx; i >= 0; i--)
                    for (int o = 0; o < oi.Length; o++) changed |= Lower(i, j, i - oi[o], j - oj[o], rise[o]);
        }

        bool Lower(int i, int j, int ni, int nj, float r)
        {
            if (ni < 0 || nj < 0 || ni > nx || nj > nz) return false;
            float limit = h[nj * row + ni] + r;
            if (h[j * row + i] <= limit + 1e-6f) return false;
            h[j * row + i] = limit;
            return true;
        }
    }

    /// <summary>Settle heights (0..<paramref name="height"/>) into <paramref name="levels"/> flat levels, the lowest at 0
    /// and the highest at <paramref name="height"/>: each band of the range stays flat for its first half and rises
    /// smoothly to the next level over its second; the top band stays flat.</summary>
    public static void Terrace(float[] h, int levels, float height)
    {
        if (levels < 2 || height <= 0f) return;
        // Noise piles up in the middle of its range, which would leave the lowest and highest levels as small
        // patches. Rank the heights instead, so each level covers about the same share of the ground; the shape —
        // which spots are higher than which — is unchanged.
        var order = new int[h.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        var keys = (float[])h.Clone();
        System.Array.Sort(keys, order);
        for (int r = 0; r < order.Length; r++) h[order[r]] = r / (float)(order.Length - 1) * height;

        for (int i = 0; i < h.Length; i++)
        {
            float s = Mathf.Clamp01(h[i] / height) * levels;
            int k = Mathf.Min((int)s, levels - 1);
            float rise = k < levels - 1 ? Mathf.Clamp01((s - k - 0.5f) / 0.5f) : 0f;
            h[i] = (k + rise * rise * (3f - 2f * rise)) * height / (levels - 1);
        }
    }

    /// <summary>Mound heights over a plot whose lowest corner is (<paramref name="originX"/>, <paramref name="originZ"/>):
    /// two layers of noise, the second at half the width, stretched so the lowest vertex sits at 0 and the highest
    /// at exactly <paramref name="height"/>. Same <paramref name="seed"/> = same mounds.</summary>
    public static float[] Heights(int nx, int nz, float sizeX, float sizeZ, float originX, float originZ,
                                  int seed, float height, float width)
    {
        var rng = new System.Random(seed ^ 0x2F6B3C1D);   // its own stream, so the mounds don't follow the dirt patches
        float ox1 = (float)rng.NextDouble() * 1000f, oz1 = (float)rng.NextDouble() * 1000f;
        float ox2 = (float)rng.NextDouble() * 1000f, oz2 = (float)rng.NextDouble() * 1000f;
        float f1 = 1f / Mathf.Max(1f, width), f2 = f1 * 2f;

        var h = new float[(nx + 1) * (nz + 1)];
        float lo = float.MaxValue, hi = float.MinValue;
        int vi = 0;
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float wx = originX + sizeX * (i / (float)nx), wz = originZ + sizeZ * (j / (float)nz);
                float n = Mathf.PerlinNoise(wx * f1 + ox1, wz * f1 + oz1) * 0.7f
                        + Mathf.PerlinNoise(wx * f2 + ox2, wz * f2 + oz2) * 0.3f;
                h[vi++] = n;
                if (n < lo) lo = n;
                if (n > hi) hi = n;
            }

        float span = hi - lo;
        for (int k = 0; k < h.Length; k++) h[k] = span > 1e-6f ? (h[k] - lo) / span * height : 0f;
        return h;
    }

    /// <summary>The steepest slope, rise per world unit, across every triangle of the grid (vertex spacing
    /// <paramref name="dx"/> × <paramref name="dz"/>, split as the ground mesh splits each square).</summary>
    public static float SteepestSlope(float[] h, int nx, int nz, float dx, float dz)
    {
        int row = nx + 1;
        float worst = 0f;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float h0 = h[j * row + i], h1 = h[j * row + i + 1], h2 = h[(j + 1) * row + i], h3 = h[(j + 1) * row + i + 1];
                worst = Mathf.Max(worst, Grad((h1 - h0) / dx, (h2 - h0) / dz));   // triangle v0 v2 v1
                worst = Mathf.Max(worst, Grad((h3 - h2) / dx, (h3 - h1) / dz));   // triangle v1 v2 v3
            }
        return worst;
    }

    /// <summary>Lower every mound by the same factor until the steepest slope is at most <paramref name="maxSlope"/>.
    /// Does nothing when it already is.</summary>
    public static void CapSlope(float[] h, int nx, int nz, float dx, float dz, float maxSlope)
    {
        float worst = SteepestSlope(h, nx, nz, dx, dz);
        if (worst <= maxSlope) return;
        float k = maxSlope / worst;
        for (int i = 0; i < h.Length; i++) h[i] *= k;
    }

    /// <summary>The height of the ground mesh's own surface at world (<paramref name="x"/>, <paramref name="z"/>): the
    /// grid's two triangles per square, interpolated exactly as they are drawn, so a thing seated with this sits on
    /// the drawn ground. Clamped to the grid at its edges.</summary>
    public static float SampleGrid(float[] h, int nx, int nz, float dx, float dz, float originX, float originZ,
                                   float x, float z)
    {
        float gx = Mathf.Clamp((x - originX) / dx, 0f, nx), gz = Mathf.Clamp((z - originZ) / dz, 0f, nz);
        int i = Mathf.Min((int)gx, nx - 1), j = Mathf.Min((int)gz, nz - 1);
        float fx = gx - i, fz = gz - j;
        int row = nx + 1;
        float h0 = h[j * row + i], h1 = h[j * row + i + 1], h2 = h[(j + 1) * row + i], h3 = h[(j + 1) * row + i + 1];
        return fx + fz <= 1f
            ? h0 + (h1 - h0) * fx + (h2 - h0) * fz
            : h3 + (h2 - h3) * (1f - fx) + (h1 - h3) * (1f - fz);
    }

    public static float Highest(float[] h)
    {
        float hi = 0f;
        foreach (var v in h) if (v > hi) hi = v;
        return hi;
    }

    static float Grad(float gx, float gz) => Mathf.Sqrt(gx * gx + gz * gz);
}
