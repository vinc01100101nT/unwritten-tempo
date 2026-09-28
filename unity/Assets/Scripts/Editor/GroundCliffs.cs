using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Rock cliffs lining both sides of a map's roads (<see cref="MapRecipe.cliffHeight"/>). Everything farther than half
/// the pass width from a road is high ground; the edge facing the road is a carved rock face.
///
/// <para>Two pieces, each doing one job:</para>
/// <list type="bullet">
/// <item>The ground mesh itself rises to the high ground (<see cref="IsHigh"/>), so feet, props and creature paths on
/// top sit at the right height. Its grid is too coarse for a vertical step, so the triangles that would ramp up to the
/// high ground are left out, wholly behind the carved face and under the grass strip.</item>
/// <item>The cliff surface, as one smooth piece: a carved face (vertical columns and ledges, pushed back
/// from the wall line into the rock), a rounded shoulder at the top, and a strip of grass running back over the gap
/// the left-out ramp leaves. It uses the map's own ground material with the rock layer switched on, so the grass on top is the
/// ground's exact grass and fades into the rock over the shoulder, and into the pass floor at the foot. It is also the
/// collision, on the Ground layer: feet stand on the grass top where it is drawn, bodies stop against the carved face
/// itself, and anything looking down for the ground finds the cliff top — so the creatures' pathfinder reads the full
/// drop at the face and never routes into the low ground hidden under the rock.</item>
/// </list>
///
/// <para>The wall line is traced from the distance-to-road field, so any road network works — bends, forks and
/// crossings merge on their own. It wanders in and out from the road (bays and points), every corner is rounded, and the
/// rim along its top swells, so no stretch of cliff is a straight, level line.</para>
/// </summary>
public static class GroundCliffs
{
    // Deepest the carving goes back behind the wall line. Always behind it, so no rock pokes out into the pass below
    // the top lip.
    const float Recess = 0.7f;
    // The rounded shoulder where the face turns into the top: a quarter circle this big, varying along the cliff,
    // drawn in this many rows.
    const float ShoulderMin = 0.25f, ShoulderMax = 0.55f;
    const int ShoulderRows = 5;
    // The rounded foot where the pass floor curves up into the face: a quarter circle this big, out in front of the wall
    // line, drawn in this many rows. Its lowest part is gentle enough to walk; the rest is as steep as the face.
    const float FootRadius = 0.5f;
    const int FootRows = 5;
    // How far the wall line wanders in toward the road and back out, and how long one bay or point is along it.
    const float EdgeWander = 0.8f;
    const float EdgeWanderLength = 8f;
    // How far the rim rises above the high ground's height, at most. Never below it: the ground's own ramp reaches
    // the high ground's height under the grass strip, and a dip would let it show through.
    const float RimRise = 0.35f;
    // The wandering makes the distance to the wall line change by up to this much per unit walked (Perlin noise
    // changes by at most about 2.5 per unit of its input): distances below are measured in that field, so they are
    // stretched by this to stay true in the world.
    const float FieldStretch = 1f + EdgeWander * 2.5f / EdgeWanderLength;
    // More than one diagonal of the ground grid (0.8 × √2 = 1.13), stretched: the ground's ramp up to the high ground
    // spans at most that, so starting it this far behind the deepest carving keeps all of it out of sight.
    const float GridSlack = 1.13f * FieldStretch + 0.1f;
    // How far behind the wall line the ground is high.
    const float HighFrom = Recess * FieldStretch + GridSlack;
    // Where the ground's ramp is sure to have reached the top: the grass strip reaches at least this far back.
    const float RampEnd = HighFrom + GridSlack;
    // How many rows the grass strip is drawn in, and over how much of its width the rim rolls down to the high ground.
    // Past that roll the strip lies exactly on the high ground's height with the ground's own shading, so where the
    // strips of two cliffs overlap (on a ridge too narrow for the ground itself to rise) they are the same surface
    // and nothing shows where they cross.
    const int CapRows = 6;
    const float RimRoll = 1f;
    // How finely the wall line is traced, and the height of one row of the carved face, in world units.
    const float TraceStep = 0.25f;
    const float RowHeight = 0.25f;
    // The shallowest the face may lean back between two rows, in degrees: well past the 45° every body can walk, so no
    // part of the face below the shoulder is ever a slope to walk up. Leaning forward (a ledge's underside) is free.
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
        /// <summary>Is the ground at this point raised to the top of the cliffs?</summary>
        public bool IsHigh(float x, float z) => PastWall(x, z) >= HighFrom;
        /// <summary>Is this point inside the band the face, its grass strip and the ramp under them occupy — where no
        /// prop may stand, or it would poke through the rock?</summary>
        public bool InCliffBand(float x, float z)
        {
            float d = PastWall(x, z);
            return d > -FootRadius && d < RampEnd + 0.3f;
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

    /// <summary>The finished cliff: one mesh, drawn and collided with (vertex colours = the ground's own weights, uv1.x
    /// = how much rock shows, uv1.y = how far up the cliff, 0 at the pass floor to 1 at the top).</summary>
    public struct Built { public Mesh mesh; public int segments; }

    /// <summary>Trace the wall line over a plot and build the face, shoulder and grass strip along it.
    /// <paramref name="floorY"/> is the pass floor's height, <paramref name="topY"/> the high ground's (the cliff top,
    /// ground levels included, already brought down at meeting points), and <paramref name="maxRise"/> the most the high
    /// ground ever stands above the pass floor;
    /// <paramref name="sample"/> colours the grass strip exactly like the ground around it.</summary>
    public static Built Build(Spec s, Vector3 center, float sizeX, float sizeZ,
                              System.Func<float, float, float> floorY, System.Func<float, float, float> topY,
                              float maxRise, GroundBlendBuilder.GroundSampler sample)
    {
        float ox = center.x - sizeX * 0.5f, oz = center.z - sizeZ * 0.5f;
        int cx = Mathf.CeilToInt(sizeX / TraceStep), cz = Mathf.CeilToInt(sizeZ / TraceStep);
        float sx = sizeX / cx, sz = sizeZ / cz;
        // Signed distance to the wall line: negative in the pass, positive in the rock.
        var f = new float[cx + 1, cz + 1];
        for (int j = 0; j <= cz; j++)
            for (int i = 0; i <= cx; i++)
                f[i, j] = s.PastWall(ox + i * sx, oz + j * sz);

        var rng = new System.Random(s.seed ^ 0x51C1FF);
        float o1 = (float)rng.NextDouble() * 1000f, o2 = (float)rng.NextDouble() * 1000f;
        float o3 = (float)rng.NextDouble() * 1000f, o4 = (float)rng.NextDouble() * 1000f;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var rockW = new List<Vector2>();
        var cols = new List<Color>();
        var tris = new List<int>();
        // Every point of the surface is worked out from its position alone, so where two traced pieces meet they build
        // the very same points: share them, and the shading runs smoothly along the whole cliff.
        var shared = new Dictionary<Vector3Int, int>();
        int faceRows = Mathf.Max(2, Mathf.CeilToInt((maxRise + RimRise) / RowHeight));
        int segments = 0;

        // Toward the road, level: the direction the face looks. Taken from the distance field itself, so two pieces of
        // the traced line meeting at a point agree on it there and the carving has no cracks.
        Vector3 TowardRoad(float x, float z)
        {
            const float e = 0.05f;
            float gx = s.PastWall(x + e, z) - s.PastWall(x - e, z);
            float gz = s.PastWall(x, z + e) - s.PastWall(x, z - e);
            var g = new Vector3(-gx, 0f, -gz);
            return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.forward;
        }

        // How far behind the wall line the face stands at this point: the face stands well back, cut into stepped
        // columns and blocks each at its own depth, and level bands jut forward from it almost to the wall line like
        // ledges. A function of position only, so it is seamless.
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

        // How far back the grass strip reaches from a point: to where the ground's ramp is sure to be at the top; or,
        // on a ridge too narrow for that, to the ridge's crest, where the other side's strip takes over; and never past
        // the map's edge. Every one of those is found to a fraction of a step, so neighbouring strips reach nearly the
        // same distance — a jump between neighbours folds the strip into a dark crease.
        float minX = ox + 0.01f, maxX = ox + sizeX - 0.01f, minZ = oz + 0.01f, maxZ = oz + sizeZ - 0.01f;
        float CapReach(Vector3 p, Vector3 intoRock)
        {
            const float step = 0.1f;
            float prevL = 0f, prevF = s.PastWall(p.x, p.z), crestL = 0f, crestF = prevF;
            for (float L = step; L <= 8f; L += step)
            {
                Vector3 b = p + intoRock * L;
                if (b.x < minX || b.x > maxX || b.z < minZ || b.z > maxZ) return prevL;
                float here = s.PastWall(b.x, b.z);
                if (here >= RampEnd) return prevL + step * (RampEnd - prevF) / Mathf.Max(1e-5f, here - prevF);
                if (here > crestF) { crestF = here; crestL = L; }
                prevL = L; prevF = here;
            }
            return crestL;
        }

        // The grass strip's far edge lies on the ground, and so does the rounded foot's; their shading must match the
        // ground's there or the edge shows.
        var onGround = new List<int>();
        var onFloor = new List<int>();
        Vector3 GroundNormal(float x, float z)
        {
            const float e = 0.1f;
            return new Vector3(topY(x - e, z) - topY(x + e, z), 2f * e, topY(x, z - e) - topY(x, z + e)).normalized;
        }
        Vector3 FloorNormal(float x, float z)
        {
            const float e = 0.1f;
            return new Vector3(floorY(x - e, z) - floorY(x + e, z), 2f * e, floorY(x, z - e) - floorY(x, z + e)).normalized;
        }

        int Vertex(Vector3 v, float rock, float up)
        {
            var key = new Vector3Int(Mathf.RoundToInt(v.x * 1e4f), Mathf.RoundToInt(v.y * 1e4f), Mathf.RoundToInt(v.z * 1e4f));
            if (shared.TryGetValue(key, out int at)) return at;
            at = verts.Count;
            verts.Add(v);
            uvs.Add(new Vector2(v.x, v.z));
            rockW.Add(new Vector2(Mathf.Clamp01(rock), Mathf.Clamp01(up)));
            sample(v.x, v.z, out Vector4 w);
            cols.Add(new Color(Mathf.Clamp01(w.x), Mathf.Clamp01(w.y), Mathf.Clamp01(w.z), Mathf.Clamp01(w.w)));
            shared.Add(key, at);
            return at;
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // One end's cross-section: up the carved face from the pass floor, round the shoulder, back over the top.
        void Profile(Vector3 p, Vector3 inward, List<int> into)
        {
            into.Clear();
            float fy = floorY(p.x, p.z);
            // Near a meeting point the cliff shrinks to nothing: its carving, its rim and its height all go together.
            float standing = s.Standing(p.x, p.z);
            float rim = s.Rim(p.x, p.z) * standing;
            float top = topY(p.x, p.z) + rim;
            float radius = Mathf.Min(Mathf.Lerp(ShoulderMin, ShoulderMax, Mathf.PerlinNoise(p.x * 0.45f + o2, p.z * 0.45f + o1)),
                                     (top - fy) * 0.5f);
            // The rounded foot, shrinking with the cliff near a meeting point and never more than a third of its height
            // — but always there, however small, so every cross-section has the same rows and neighbours join up.
            float foot = Mathf.Max(0.02f, Mathf.Min(FootRadius * standing, (top - fy) * 0.3f));
            float faceTop = top - radius, bottom = fy + foot;
            // Where the rock gives way to grass on the shoulder, wandering a little along the cliff.
            float edge = (Mathf.PerlinNoise(p.x * 1.7f + o3, p.z * 1.7f + o4) - 0.5f) * 0.6f;

            // Up the rounded foot: from flat on the floor out in front of the wall line, curving up to stand upright at
            // the wall line at the foot's height, where the face takes over. The lowest point tucks just under the floor.
            for (int m = 0; m < FootRows; m++)
            {
                float a = m / (float)FootRows * Mathf.PI * 0.5f;
                float outward = foot * (1f - Mathf.Sin(a));
                float y = m == 0 ? fy - 0.02f : fy + foot * (1f - Mathf.Cos(a));
                float above = y - fy;
                int at = Vertex(new Vector3(p.x, y, p.z) + inward * outward, Smooth01((above + 0.05f) / 0.35f),
                                above / Mathf.Max(0.01f, top - fy));
                into.Add(at);
                if (m == 0) onFloor.Add(at);
            }

            float rTop = 0f, rBelow = 0f;
            float leanPerRow = (faceTop - bottom) / faceRows / Mathf.Tan(FaceSteepestLeanDegrees * Mathf.Deg2Rad);
            for (int k = 0; k <= faceRows; k++)
            {
                float y = Mathf.Lerp(bottom, faceTop, k / (float)faceRows);
                float above = y - fy;
                // The face's own carving eases in from the wall line above the foot, so the two meet without a step.
                float r = Recessed(p.x, p.z, y) * Smooth01((y - bottom) / 0.5f) * standing;
                if (k > 0) r = Mathf.Min(r, rBelow + leanPerRow);
                rBelow = r;
                if (k == faceRows) rTop = r;
                into.Add(Vertex(new Vector3(p.x, y, p.z) - inward * r, Smooth01((above + 0.05f) / 0.35f), above / Mathf.Max(0.01f, top - fy)));
            }
            for (int m = 1; m <= ShoulderRows; m++)
            {
                float t = m / (float)ShoulderRows, a = t * Mathf.PI * 0.5f;
                float r = rTop + radius * (1f - Mathf.Cos(a));
                float y = faceTop + radius * Mathf.Sin(a);
                into.Add(Vertex(new Vector3(p.x, y, p.z) - inward * r, 1f - Smooth01((t + edge - 0.15f) / 0.9f), (y - fy) / Mathf.Max(0.01f, top - fy)));
            }
            Vector3 front = p - inward * (rTop + radius);
            float reach = Mathf.Max(CapReach(p, -inward), rTop + radius + 0.3f);
            Vector3 back = p - inward * reach;
            float span = reach - (rTop + radius);
            for (int k = 1; k <= CapRows; k++)
            {
                float t = k / (float)CapRows;
                Vector3 v = Vector3.Lerp(front, back, t);
                float along = span * t;
                // A hair above the ground it lies over, down to exactly on it at the far edge, so the edge meets it.
                v.y = topY(v.x, v.z) + rim * (1f - Smooth01(along / RimRoll)) + (k == CapRows ? 0f : 0.004f);
                int at = Vertex(v, edge - 0.1f - t, 1f);
                into.Add(at);
                if (along >= RimRoll || k == CapRows) onGround.Add(at);
            }
        }

        var colA = new List<int>(); var colB = new List<int>();
        void AddSegment(Vector3 a, Vector3 b)
        {
            segments++;
            var ends = new[] { a, b };
            var inward = new[] { TowardRoad(a.x, a.z), TowardRoad(b.x, b.z) };
            Profile(a, inward[0], colA);
            Profile(b, inward[1], colB);

            // Each square between the two cross-sections, wound to face out of the rock. One choice for the whole strip,
            // made where it is unambiguous — the face, whose squares run up and must look toward the road — because the
            // strip is one continuous surface: a square judged on its own (a ledge's underside looks down) gets it wrong.
            bool facing = Vector3.Dot(Vector3.Cross(Vector3.up, b - a), inward[0] + inward[1]) > 0f;
            for (int k = 0; k + 1 < colA.Count; k++)
            {
                int a0 = colA[k], a1 = colA[k + 1], b0 = colB[k], b1 = colB[k + 1];
                if (facing) { tris.Add(a0); tris.Add(a1); tris.Add(b0); tris.Add(b0); tris.Add(a1); tris.Add(b1); }
                else        { tris.Add(a0); tris.Add(b0); tris.Add(a1); tris.Add(b0); tris.Add(b1); tris.Add(a1); }
            }

        }

        // Trace the wall line square by square: where the signed distance changes sign along a square's edge, the
        // line crosses there. Two crossings = one piece; four (two lines grazing one square) = two pieces.
        var cross = new List<Vector3>(4);
        for (int j = 0; j < cz; j++)
            for (int i = 0; i < cx; i++)
            {
                cross.Clear();
                float x0 = ox + i * sx, z0 = oz + j * sz, x1 = x0 + sx, z1 = z0 + sz;
                float fa = f[i, j], fb = f[i + 1, j], fc = f[i + 1, j + 1], fd = f[i, j + 1];
                Crossing(fa, fb, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), cross);
                Crossing(fb, fc, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), cross);
                Crossing(fc, fd, new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), cross);
                Crossing(fd, fa, new Vector3(x0, 0, z1), new Vector3(x0, 0, z0), cross);
                if (cross.Count == 2) AddSegment(cross[0], cross[1]);
                else if (cross.Count == 4) { AddSegment(cross[0], cross[3]); AddSegment(cross[1], cross[2]); }
            }

        var visual = new Mesh { name = "cliffs" };
        visual.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        visual.SetVertices(verts);
        visual.SetUVs(0, uvs);
        visual.SetUVs(1, rockW);
        visual.SetColors(cols);
        visual.SetTriangles(tris, 0);
        visual.RecalculateNormals();
        var normals = visual.normals;
        foreach (int i in onGround) normals[i] = GroundNormal(verts[i].x, verts[i].z);
        foreach (int i in onFloor) normals[i] = FloorNormal(verts[i].x, verts[i].z);
        visual.normals = normals;
        visual.RecalculateBounds();


        return new Built { mesh = visual, segments = segments };
    }

    // Where the wall line crosses the edge from p (signed distance fp) to q (fq), if it does.
    static void Crossing(float fp, float fq, Vector3 p, Vector3 q, List<Vector3> into)
    {
        if ((fp < 0f) == (fq < 0f)) return;
        into.Add(Vector3.Lerp(p, q, fp / (fp - fq)));
    }
}
