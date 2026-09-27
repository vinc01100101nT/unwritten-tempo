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
    /// <summary>The ground's shape for a biome: rolling mounds, or — with <paramref name="levels"/> of 2 or more — that
    /// many flat levels joined by slopes, from 0 up to <paramref name="height"/>, hills about <paramref name="width"/>
    /// across. Never steeper than <paramref name="maxSlope"/>: where it would be, the higher side is cut back into a
    /// slope a creature can walk (<see cref="LimitSlope"/>), so every level keeps its height and only its edges give.</summary>
    public static float[] Make(int nx, int nz, float sizeX, float sizeZ, float originX, float originZ,
                               int seed, float height, float width, int levels, float maxSlope)
    {
        float dx = sizeX / nx, dz = sizeZ / nz;
        var h = Heights(nx, nz, sizeX, sizeZ, originX, originZ, seed, height, width);
        if (levels >= 2) Terrace(h, levels, height);
        float allowed = maxSlope;
        for (int tries = 0; tries < 6; tries++)
        {
            LimitSlope(h, nx, nz, dx, dz, allowed);
            float steepest = SteepestSlope(h, nx, nz, dx, dz);
            if (steepest <= maxSlope) return h;
            // A slope limited along the grid lines can still run a little steeper across a triangle: tighten and redo.
            allowed *= maxSlope / steepest * 0.98f;
        }
        CapSlope(h, nx, nz, dx, dz, maxSlope);
        return h;
    }

    /// <summary>Lower every vertex that stands higher than a <paramref name="slope"/> rise from any neighbour allows,
    /// until none does: the high ground's edges become ramps of exactly that slope, and nothing low is raised.</summary>
    public static void LimitSlope(float[] h, int nx, int nz, float dx, float dz, float slope)
    {
        int row = nx + 1;
        float cx = slope * dx, cz = slope * dz, cd = slope * Mathf.Sqrt(dx * dx + dz * dz);
        bool changed = true;
        for (int pass = 0; changed && pass < 200; pass++)
        {
            changed = false;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                    changed |= Lower(i, j, i - 1, j, cx) | Lower(i, j, i, j - 1, cz)
                             | Lower(i, j, i - 1, j - 1, cd) | Lower(i, j, i + 1, j - 1, cd);
            for (int j = nz; j >= 0; j--)
                for (int i = nx; i >= 0; i--)
                    changed |= Lower(i, j, i + 1, j, cx) | Lower(i, j, i, j + 1, cz)
                             | Lower(i, j, i + 1, j + 1, cd) | Lower(i, j, i - 1, j + 1, cd);
        }

        bool Lower(int i, int j, int ni, int nj, float rise)
        {
            if (ni < 0 || nj < 0 || ni > nx || nj > nz) return false;
            float limit = h[nj * row + ni] + rise;
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
