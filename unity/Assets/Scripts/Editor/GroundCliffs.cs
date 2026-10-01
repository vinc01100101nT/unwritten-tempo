using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Rock cliffs lining both sides of a map's roads (<see cref="MapRecipe.cliffHeight"/>). Everything past the wall line
/// is high ground; the edge facing the road is a carved rock face.
///
/// <para>A cliff is not a piece of its own: it is the map's ground, shaped (RULE ONEGROUND in the project rules).
/// <see cref="BuildGround"/> lays the whole field as ONE mesh with ONE collider — the pass floor curving up into a
/// rounded foot, a carved face standing on the cut line, a rounded shoulder and a swelling rim, then the high ground —
/// and the rock is paint on it, the ground material's own rock layer faded in by uv1.x. Where the cut line crosses a
/// square of the ground, the square is split along it into a floor part and a top part, and the face is stitched
/// between a low and a high copy of each crossing point, so floor, face and top share their edge points and nothing
/// lies over anything.</para>
///
/// <para>The wall line comes from the distance-to-road field, so any road network works — bends, forks and crossings
/// merge on their own. It wanders in and out from the road (bays and points), every corner is rounded, and the rim
/// along its top swells, so no stretch of cliff is a straight, level line.</para>
/// </summary>
public static class GroundCliffs
{
    /// <summary>The ground's squares on a cliff map are at most this wide, so the cut line — and with it the foot, the
    /// face and the shoulder — is drawn as finely as the carving.</summary>
    public const float GridCell = 0.25f;
    // Where the ground splits into floor and top, this far behind the wall line: the foot curves up to it from in front,
    // the face stands on it, the shoulder rolls back from it.
    const float Cut = 0.3f;
    // Deepest the carving goes back behind the cut line. Always behind it, so no rock pokes out past the foot and rim.
    const float Recess = 0.7f;
    // The rounded shoulder where the face turns into the top: a quarter circle this big, varying along the cliff.
    const float ShoulderMin = 0.25f, ShoulderMax = 0.55f;
    // The rounded foot where the pass floor curves up into the face, starting this far in front of the wall line.
    // Its lowest part is gentle enough to walk; its top is as steep as the face.
    const float FootRadius = 0.5f;
    // How far the wall line wanders in toward the road and back out, and how long one bay or point is along it.
    const float EdgeWander = 0.8f;
    const float EdgeWanderLength = 8f;
    // How far the rim rises above the high ground's height, at most, and over how much ground behind the shoulder it
    // rolls back down to it.
    const float RimRise = 0.35f;
    const float RimRoll = 1f;
    // The wandering makes the distance to the wall line change by up to this much per unit walked (Perlin noise
    // changes by at most about 2.5 per unit of its input): distances below are measured in that field, so they are
    // stretched by this to stay true in the world.
    const float FieldStretch = 1f + EdgeWander * 2.5f / EdgeWanderLength;
    // How far behind the wall line props keep clear. The same distance the cliffs before this one needed, kept so a
    // map rebuilt from the same seed places every prop exactly where it was.
    const float PropClearSlack = 1.13f * FieldStretch + 0.1f;
    const float PropClearBehind = Recess * FieldStretch + PropClearSlack + PropClearSlack + 0.3f;
    // The height of one row of the carved face, in world units.
    const float RowHeight = 0.25f;
    // How far up from the foot, and down from the shoulder, the carving takes to reach its full depth.
    const float CarveEase = 0.5f;
    // The shallowest the face may lean back between two rows, in degrees: well past the 45° every body can walk, so no
    // part of the face is ever a slope to walk up. Leaning forward (a ledge's underside) is free.
    const float FaceSteepestLeanDegrees = 50f;
    // Rock stands along the map's own edge too, this far in, so a pass that runs to the edge ends in a cliff instead of
    // opening onto nothing.
    const float EdgeRock = 1.5f;
    // A meeting point: where a cliff shrinks to nothing and its high ground slopes down to meet the pass. Within Open of
    // its centre there is no cliff at all; by Reach the cliff is back to full height. Reach is long enough that even
    // the tallest cliff (1.5 + 1.2 of ground levels) comes down at under 45°, which the player's legs can climb.
    const float MeetingOpen = 1.5f, MeetingReach = 10f;
    // Meeting points are kept this far apart, and this far in from the map's edge.
    const float MeetingSpacing = 15f, MeetingEdgeClear = 6f;
    // How much the wall line is smoothed, as the spread of a blur over it, in world units: every corner — where two
    // passes meet, where a road bends sharply, where a pass meets the map's edge — comes out rounded, not pointed.
    const float CornerRounding = 1.2f;

    /// <summary>A map's cliffs: which roads they line, how tall they stand, and how wide the pass between them is.</summary>
    public sealed class Spec
    {
        public readonly IReadOnlyList<PathRouting.Route> roads;
        public readonly float height, passWidth;
        public readonly int seed;
        /// <summary>The face's colour, shaded lighter up the cliff; alpha 0 = none chosen, so the face is drawn with the rock tile.</summary>
        public readonly Color faceColor;

        readonly float wx, wz, rx, rz, jx, jz;
        // The smoothed wall-line field, worked out once over the whole map plus a margin, and read back by
        // interpolation — so everything that asks where the cliff is (the ground, the face, the props, the creatures'
        // floor) gets the one same answer.
        readonly float[,] field;
        readonly float fx0, fz0;
        readonly int fnx, fnz;
        const float FieldStep = 0.25f;

        readonly List<Vector2> meetings = new List<Vector2>();

        public Spec(IReadOnlyList<PathRouting.Route> roads, float height, float passWidth, int seed,
                    Vector3 center, float sizeX, float sizeZ, int meetingPoints = 0, Color faceColor = default)
        {
            this.roads = roads; this.height = height; this.passWidth = passWidth; this.seed = seed;
            this.faceColor = faceColor;
            var rng = new System.Random(seed ^ 0x3C1D5E);
            wx = (float)rng.NextDouble() * 1000f; wz = (float)rng.NextDouble() * 1000f;
            rx = (float)rng.NextDouble() * 1000f; rz = (float)rng.NextDouble() * 1000f;
            jx = (float)rng.NextDouble() * 1000f; jz = (float)rng.NextDouble() * 1000f;

            float minX = center.x - sizeX * 0.5f, maxX = center.x + sizeX * 0.5f;
            float minZ = center.z - sizeZ * 0.5f, maxZ = center.z + sizeZ * 0.5f;
            const float margin = 3f;
            fx0 = minX - margin; fz0 = minZ - margin;
            fnx = Mathf.CeilToInt((sizeX + margin * 2f) / FieldStep) + 1;
            fnz = Mathf.CeilToInt((sizeZ + margin * 2f) / FieldStep) + 1;
            var raw = new float[fnx, fnz];
            var roadSide = new bool[fnx, fnz];
            for (int j = 0; j < fnz; j++)
                for (int i = 0; i < fnx; i++)
                {
                    float x = fx0 + i * FieldStep, z = fz0 + j * FieldStep;
                    float wander = (Mathf.PerlinNoise(x / EdgeWanderLength + wx, z / EdgeWanderLength + wz) - 0.5f) * 2f * EdgeWander;
                    float fromRoad = PathRouting.MinDistanceXZ(new Vector2(x, z), roads) - WallDistance - wander;
                    float fromEdge = EdgeRock - Mathf.Min(Mathf.Min(x - minX, maxX - x), Mathf.Min(z - minZ, maxZ - z));
                    raw[i, j] = Mathf.Max(fromRoad, fromEdge);
                    roadSide[i, j] = fromRoad > fromEdge + 1f;
                }
            field = Blur(raw, CornerRounding / FieldStep);
            PickMeetings(meetingPoints, roadSide, minX + MeetingEdgeClear, maxX - MeetingEdgeClear,
                         minZ + MeetingEdgeClear, maxZ - MeetingEdgeClear, rng);
        }

        // Choose where the meeting points go: random spots on the wall line along a road (never along the map's edge),
        // kept apart from each other and clear of the map's edge. Same seed, same spots.
        void PickMeetings(int count, bool[,] roadSide, float minX, float maxX, float minZ, float maxZ, System.Random rng)
        {
            if (count <= 0) return;
            var candidates = new List<Vector2>();
            for (int j = 0; j < fnz; j++)
                for (int i = 0; i + 1 < fnx; i++)
                {
                    if ((field[i, j] < 0f) == (field[i + 1, j] < 0f) || !roadSide[i, j]) continue;
                    float t = field[i, j] / (field[i, j] - field[i + 1, j]);
                    var p = new Vector2(fx0 + (i + t) * FieldStep, fz0 + j * FieldStep);
                    if (p.x >= minX && p.x <= maxX && p.y >= minZ && p.y <= maxZ) candidates.Add(p);
                }
            for (int tries = 0; tries < 200 && meetings.Count < count && candidates.Count > 0; tries++)
            {
                var p = candidates[rng.Next(candidates.Count)];
                bool clear = true;
                foreach (var m in meetings) if ((m - p).sqrMagnitude < MeetingSpacing * MeetingSpacing) clear = false;
                if (clear) meetings.Add(p);
            }
        }

        /// <summary>The meeting points this map's cliffs have.</summary>
        public IReadOnlyList<Vector2> MeetingPoints => meetings;

        /// <summary>How much of the cliff stands here, 0..1: 0 at a meeting point, where the high ground has come
        /// down to the pass, rising smoothly to 1 (full height) by <see cref="MeetingReach"/> away.</summary>
        public float Standing(float x, float z)
        {
            float s = 1f;
            foreach (var m in meetings)
            {
                float d = Vector2.Distance(m, new Vector2(x, z));
                float t = Mathf.Clamp01((d - MeetingOpen) / (MeetingReach - MeetingOpen));
                s = Mathf.Min(s, t * t * (3f - 2f * t));
            }
            return s;
        }

        /// <summary>Distance from the road's centre line to the wall line, on average; it wanders by
        /// <see cref="EdgeWander"/> either way, and every corner is rounded by <see cref="CornerRounding"/>.</summary>
        public float WallDistance => passWidth * 0.5f;
        /// <summary>How far past the wall line this point is: negative in the pass, positive in the rock.</summary>
        public float PastWall(float x, float z)
        {
            float gx = Mathf.Clamp((x - fx0) / FieldStep, 0f, fnx - 1.001f);
            float gz = Mathf.Clamp((z - fz0) / FieldStep, 0f, fnz - 1.001f);
            int i = (int)gx, j = (int)gz;
            float tx = gx - i, tz = gz - j;
            return Mathf.Lerp(Mathf.Lerp(field[i, j], field[i + 1, j], tx),
                              Mathf.Lerp(field[i, j + 1], field[i + 1, j + 1], tx), tz);
        }
        /// <summary>How far the rim stands above the high ground here: broad swells with smaller breaks, level with
        /// the high ground in between.</summary>
        public float Rim(float x, float z)
        {
            float broad = Mathf.PerlinNoise(x * 0.3f + rx, z * 0.3f + rz) - 0.5f;
            float broken = Mathf.PerlinNoise(x * 1.1f + jx, z * 1.1f + jz) - 0.5f;
            return Mathf.Clamp01(broad * 1.6f + broken * 0.6f + 0.3f) * RimRise;
        }
        /// <summary>Is this point in the band the foot, the face and the shoulder occupy, or too close behind it — where
        /// no prop may stand, or it would poke through the rock?</summary>
        public bool InCliffBand(float x, float z)
        {
            float d = PastWall(x, z);
            return d > -FootRadius && d < PropClearBehind;
        }
    }

    // A Gaussian blur of a grid, spread given in grid cells, edges clamped. Blurring the wall-line field rounds its
    // corners and leaves its straight stretches where they were.
    static float[,] Blur(float[,] src, float sigma)
    {
        int nx = src.GetLength(0), nz = src.GetLength(1);
        int r = Mathf.CeilToInt(sigma * 3f);
        var k = new float[r * 2 + 1];
        float sum = 0f;
        for (int i = -r; i <= r; i++) { k[i + r] = Mathf.Exp(-(i * i) / (2f * sigma * sigma)); sum += k[i + r]; }
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        var mid = new float[nx, nz];
        var dst = new float[nx, nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float a = 0f;
                for (int t = -r; t <= r; t++) a += src[Mathf.Clamp(i + t, 0, nx - 1), j] * k[t + r];
                mid[i, j] = a;
            }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float a = 0f;
                for (int t = -r; t <= r; t++) a += mid[i, Mathf.Clamp(j + t, 0, nz - 1)] * k[t + r];
                dst[i, j] = a;
            }
        return dst;
    }

    /// <summary>The whole ground of a cliff map as one mesh — the collider as well as what is drawn. Its first
    /// (<paramref name="nx"/>+1)×(<paramref name="nz"/>+1) vertices are the ground's grid points in the usual order.
    /// <paramref name="floorH"/> is the pass floor's height at each grid point, <paramref name="topH"/> the high ground's
    /// (ground levels included, already brought down at meeting points), <paramref name="maxRise"/> the most the high
    /// ground ever stands above the floor, and <paramref name="sample"/> colours every point like the ground around it.
    /// Vertices are relative to <paramref name="center"/>; uv1.x = how much rock shows, uv1.y = how far up the cliff.</summary>
    public static Mesh BuildGround(Spec s, Vector3 center, float sizeX, float sizeZ, int nx, int nz,
                                   float[] floorH, float[] topH, float maxRise, GroundBlendBuilder.GroundSampler sample,
                                   string meshName)
    {
        var rng = new System.Random(s.seed ^ 0x51C1FF);
        float o1 = (float)rng.NextDouble() * 1000f, o2 = (float)rng.NextDouble() * 1000f;
        float o3 = (float)rng.NextDouble() * 1000f, o4 = (float)rng.NextDouble() * 1000f;

        int row = nx + 1, vcount = row * (nz + 1);
        int faceRows = Mathf.Max(2, Mathf.CeilToInt((maxRise + RimRise) / RowHeight));
        float halfX = sizeX * 0.5f, halfZ = sizeZ * 0.5f;

        var verts = new List<Vector3>(vcount * 2);
        var uvs = new List<Vector2>(vcount * 2);
        var rockW = new List<Vector2>(vcount * 2);
        var cols = new List<Color>(vcount * 2);
        var tris = new List<int>(nx * nz * 7);

        void AddVertex(float wx, float y, float wz, float rock, float up)
        {
            verts.Add(new Vector3(wx - center.x, y, wz - center.z));
            uvs.Add(new Vector2(wx, wz));
            rockW.Add(new Vector2(Mathf.Clamp01(rock), Mathf.Clamp01(up)));
            sample(wx, wz, out Vector4 w);
            cols.Add(new Color(Mathf.Clamp01(w.x), Mathf.Clamp01(w.y), Mathf.Clamp01(w.z), Mathf.Clamp01(w.w)));
        }

        // Toward the road, level: the direction the face looks. From the distance field itself, so the two squares on
        // either side of a crossing agree on it and the carving has no cracks.
        Vector3 TowardRoad(float x, float z)
        {
            const float e = 0.05f;
            float gx = s.PastWall(x + e, z) - s.PastWall(x - e, z);
            float gz = s.PastWall(x, z + e) - s.PastWall(x, z - e);
            var g = new Vector3(-gx, 0f, -gz);
            return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.forward;
        }

        // How far behind the cut line the face stands at this point: stepped columns and blocks each at its own depth,
        // and level bands jutting forward almost to the cut line like ledges. A function of position only, so it is
        // seamless.
        float Recessed(float x, float z, float y)
        {
            // Columns lean a little as they rise, and come in a few sharp depths, like split rock.
            float column = Mathf.Round(Mathf.PerlinNoise(x * 0.9f + y * 0.12f + o1, z * 0.9f - y * 0.08f + o2) * 4f) / 4f;
            // Blocks about 1–2 m across and half a metre tall, each at one of four depths, like fractured slabs.
            float block = Mathf.Round(Mathf.PerlinNoise(x * 0.55f + o3, z * 0.55f + y * 2f + o4) * 3f) / 3f;
            float slab = Mathf.PerlinNoise(x * 2.3f + o3, z * 2.3f + o4);
            float back = Mathf.Clamp01(0.4f + column * 0.25f + block * 0.25f + slab * 0.1f);
            // Ledges: level bands about 0.45 m tall, one every metre, their heights wandering along the cliff, that jut
            // forward out of the face — broken off here and there so no ledge runs the whole cliff.
            float band = Mathf.Repeat(y + Mathf.PerlinNoise((x + z) * 0.15f + o4, o3) * 2f, 1f);
            bool ledge = band > 0.55f && Mathf.PerlinNoise(x * 0.35f + o2, z * 0.35f + o1) > 0.35f;
            return Recess * (ledge ? back * 0.15f : back);
        }

        // One point's cliff, worked out from its position and the two heights alone, so every square that shares a
        // point builds it identically.
        float Foot(float fy, float top, float standing) => Mathf.Max(0.02f, Mathf.Min(FootRadius * standing, (top - fy) * 0.3f));
        float Shoulder(float x, float z, float fy, float top) =>
            Mathf.Max(1e-3f, Mathf.Min(Mathf.Lerp(ShoulderMin, ShoulderMax, Mathf.PerlinNoise(x * 0.45f + o2, z * 0.45f + o1)),
                                       (top - fy) * 0.5f));
        // Where the rock gives way to grass on the shoulder, wandering a little along the cliff.
        float RockEdge(float x, float z) => (Mathf.PerlinNoise(x * 1.7f + o3, z * 1.7f + o4) - 0.5f) * 0.6f;
        float FaceTop(float x, float z, float fy, float top, float rim) =>
            Mathf.Max(fy + Foot(fy, top, s.Standing(x, z)), top + rim - Shoulder(x, z, fy, top));
        float FloorRock(float above) => above > 0.001f ? Smooth01((above + 0.05f) / 0.35f) : 0f;
        float Up(float y, float fy, float top) => (y - fy) / Mathf.Max(0.01f, top - fy);

        // ── the grid points: the pass floor with its rounded foot in front of the cut, the top with its shoulder and
        //    rim behind it ─────────────────────────────────────────────────────────────────────────────────────────
        var past = new float[vcount];
        var wxs = new float[vcount];
        var wzs = new float[vcount];
        for (int j = 0, k = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++, k++)
            {
                float wx = center.x - halfX + sizeX * (i / (float)nx);
                float wz = center.z - halfZ + sizeZ * (j / (float)nz);
                wxs[k] = wx; wzs[k] = wz;
                float f = s.PastWall(wx, wz);
                past[k] = f;
                float fy = floorH[k], top = topH[k];
                float standing = s.Standing(wx, wz);
                if (f < Cut)
                {
                    // Flat on the floor out in front, curving up to stand upright at the cut, at the foot's height.
                    float foot = Foot(fy, top, standing);
                    float u = Mathf.Clamp01((f + foot) / (foot + Cut));
                    float y = fy + foot * (1f - Mathf.Sqrt(1f - u * u));
                    AddVertex(wx, y, wz, FloorRock(y - fy), Up(y, fy, top));
                }
                else
                {
                    // Rounding back from the face's top at the cut, then the rim rolling down to the high ground.
                    float rim = s.Rim(wx, wz) * standing;
                    float radius = Shoulder(wx, wz, fy, top);
                    float faceTop = FaceTop(wx, wz, fy, top, rim);
                    float back = f - Cut, y, rock;
                    if (back < radius)
                    {
                        float c = 1f - back / radius;
                        y = faceTop + (top + rim - faceTop) * Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
                        rock = 1f - Smooth01((back / radius + RockEdge(wx, wz) - 0.15f) / 0.9f);
                    }
                    else
                    {
                        y = top + rim * (1f - Smooth01((back - radius) / RimRoll));
                        rock = 0f;
                    }
                    AddVertex(wx, y, wz, rock, Up(y, fy, top));
                }
            }

        // ── the crossing points: where the cut line crosses a square's edge. Each has a low copy (the foot's top), a
        //    high copy (the face's top) and the face's rows between them, all shared by the two squares on that edge ─
        var faceColumns = new Dictionary<long, int[]>();
        int[] Column(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            long key = (long)a * vcount + b;
            if (faceColumns.TryGetValue(key, out var column)) return column;

            float t01 = (Cut - past[a]) / (past[b] - past[a]);
            float wx = Mathf.Lerp(wxs[a], wxs[b], t01), wz = Mathf.Lerp(wzs[a], wzs[b], t01);
            float fy = Mathf.Lerp(floorH[a], floorH[b], t01), top = Mathf.Lerp(topH[a], topH[b], t01);
            float standing = s.Standing(wx, wz);
            float rim = s.Rim(wx, wz) * standing;
            float bottom = fy + Foot(fy, top, standing);
            float faceTop = FaceTop(wx, wz, fy, top, rim);
            var intoRock = -TowardRoad(wx, wz);
            float leanPerRow = (faceTop - bottom) / faceRows / Mathf.Tan(FaceSteepestLeanDegrees * Mathf.Deg2Rad);

            column = new int[faceRows + 1];
            float rBelow = 0f;
            for (int k = 0; k <= faceRows; k++)
            {
                float y = Mathf.Lerp(bottom, faceTop, k / (float)faceRows);
                // Flush with the cut at the foot and at the shoulder, carved back in between.
                float r = Recessed(wx, wz, y) * Smooth01((y - bottom) / CarveEase) * Smooth01((faceTop - y) / CarveEase) * standing;
                if (k > 0) r = Mathf.Min(r, rBelow + leanPerRow);
                rBelow = r;
                float rock = k == faceRows ? 1f - Smooth01((RockEdge(wx, wz) - 0.15f) / 0.9f) : FloorRock(y - fy);
                column[k] = verts.Count;
                AddVertex(wx + intoRock.x * r, y, wz + intoRock.z * r, rock, Up(y, fy, top));
            }
            faceColumns.Add(key, column);
            return column;
        }
        int Low(int a, int b) => Column(a, b)[0];
        int High(int a, int b) => Column(a, b)[faceRows];

        void Fan(List<int> poly)
        {
            for (int k = 1; k + 1 < poly.Count; k++) { tris.Add(poly[0]); tris.Add(poly[k + 1]); tris.Add(poly[k]); }
        }
        // The face between two crossing points, turned to look toward the floor side.
        void Face(int a0, int b0, int a1, int b1, Vector3 floorSide)
        {
            var x = Column(a0, b0);
            var y = Column(a1, b1);
            var px = verts[x[0]];
            var toward = Vector3.Cross(verts[y[0]] - px, Vector3.up);
            bool forward = Vector3.Dot(toward, floorSide - new Vector3(px.x, 0f, px.z)) > 0f;
            for (int k = 0; k < faceRows; k++)
            {
                int A = x[k], B = y[k], C = x[k + 1], D = y[k + 1];
                if (forward) { tris.Add(A); tris.Add(B); tris.Add(C); tris.Add(B); tris.Add(D); tris.Add(C); }
                else         { tris.Add(A); tris.Add(C); tris.Add(B); tris.Add(B); tris.Add(C); tris.Add(D); }
            }
        }

        // ── the squares: whole ones as two triangles, cut ones split along the cut line with the face between ────────
        var corner = new int[4];
        var high = new bool[4];
        var polyA = new List<int>(6);
        var polyB = new List<int>(6);
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int v0 = j * row + i, v1 = v0 + 1, v2 = v0 + row, v3 = v2 + 1;
                // Walked counter-clockwise seen from above.
                corner[0] = v0; corner[1] = v1; corner[2] = v3; corner[3] = v2;
                int highs = 0;
                for (int q = 0; q < 4; q++) { high[q] = past[corner[q]] >= Cut; if (high[q]) highs++; }
                if (highs == 0 || highs == 4)
                {
                    tris.Add(v0); tris.Add(v2); tris.Add(v1);
                    tris.Add(v1); tris.Add(v2); tris.Add(v3);
                    continue;
                }

                Vector3 Flat(int v) { var p = verts[v]; return new Vector3(p.x, 0f, p.z); }
                bool saddle = highs == 2 && high[0] == high[2];
                if (!saddle)
                {
                    polyA.Clear(); polyB.Clear();
                    int ca = -1, cb = -1, da = -1, db = -1;
                    var floorSide = Vector3.zero; int lows = 0;
                    for (int q = 0; q < 4; q++)
                    {
                        int n = (q + 1) & 3;
                        (high[q] ? polyB : polyA).Add(corner[q]);
                        if (!high[q]) { floorSide += Flat(corner[q]); lows++; }
                        if (high[q] == high[n]) continue;
                        polyA.Add(Low(corner[q], corner[n]));
                        polyB.Add(High(corner[q], corner[n]));
                        if (ca < 0) { ca = corner[q]; cb = corner[n]; } else { da = corner[q]; db = corner[n]; }
                    }
                    Fan(polyA); Fan(polyB);
                    Face(ca, cb, da, db, floorSide / lows);
                }
                else
                {
                    // Two opposite corners alone on their side: the middle of the square says which side joins up.
                    bool middleHigh = (past[v0] + past[v1] + past[v2] + past[v3]) * 0.25f >= Cut;
                    var middle = (Flat(v0) + Flat(v3)) * 0.5f;
                    polyA.Clear();
                    for (int q = 0; q < 4; q++)
                    {
                        int n = (q + 1) & 3;
                        if (high[q] == middleHigh) polyA.Add(corner[q]);
                        if (high[q] != high[n])
                            polyA.Add(middleHigh ? High(corner[q], corner[n]) : Low(corner[q], corner[n]));
                    }
                    Fan(polyA);
                    for (int q = 0; q < 4; q++)
                    {
                        if (high[q] == middleHigh) continue;
                        int p = (q + 3) & 3, n = (q + 1) & 3;
                        polyB.Clear();
                        polyB.Add(middleHigh ? Low(corner[p], corner[q]) : High(corner[p], corner[q]));
                        polyB.Add(corner[q]);
                        polyB.Add(middleHigh ? Low(corner[q], corner[n]) : High(corner[q], corner[n]));
                        Fan(polyB);
                        Face(corner[p], corner[q], corner[q], corner[n], middleHigh ? Flat(corner[q]) : middle);
                    }
                }
            }

        var mesh = new Mesh { name = meshName };
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, rockW);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
}
