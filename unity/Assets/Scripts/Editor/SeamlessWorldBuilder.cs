using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// BUILDS THE WORLD, square by square, from the world layout (<c>data/world-layout.json</c>, written only by
/// <c>tools/world-layout/build_world_layout.py</c>): its ground, cliffs, roads, rivers, bridges, ponds, themes, props
/// and carpets — every square of ground a prefab of its own, loaded around whoever is near (<see cref="SeamlessWorld"/>).
///
/// <para><b>One ground.</b> Every height, every colour and every normal comes from one set of functions of the world
/// position (<see cref="Terrain"/>), read from the layout, so two neighbouring squares work out the very same points along
/// the edge they share and meet without a seam, and a square built later never changes one built before. A level change
/// of the layout is a sheer rock wall in that one ground (RULE ONEGROUND); a road climbs it on the ramp the layout cut.
/// The bridge's walkable deck is the bridge model's own top surface, sampled into the ground mesh; Lastline's own
/// hand-made ground is kept exactly, and the world ground meets its rim at the same points.</para>
///
/// <para><b>Run through the live editor</b> — <c>SeamlessWorldBuilder.BuildCore()</c>. It opens no scene: Lastline is
/// read from a hidden copy, the squares are assembled in a hidden scene, and the world scene file is written directly,
/// so nothing the user has open changes. It writes only its own output — the world scene, the prefabs, meshes and
/// materials under <see cref="OutDir"/>, the scene's line in the build list and its entries in the Addressables group
/// <see cref="GroupName"/> — and running it again replaces exactly those.</para>
/// </summary>
public static class SeamlessWorldBuilder
{
    public const string MapId = "world";
    const string ScenePath = "Assets/Scenes/" + MapId + ".unity";
    // Lastline as the world builds it: its ground, props, walls, a marker in each gate and its arrival spot. Authored,
    // since the old Lastline map it was taken from is gone (2026-10-27).
    const string LastlineSourcePath = "Assets/1_Authored/Prefabs/Towns/Lastline.prefab";
    const string OutDir = "Assets/2_Generated/World";
    const string GroupName = "World";
    const string FieldRecipePath = "Assets/3_Data/Recipes/FieldRecipe.asset";
    // The world layout, at the repository's top: the one description of where everything big in the world is.
    static string LayoutPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "world-layout.json"));

    // Ground points every quarter metre, as fine as the generated fields with mounds or cliffs; a square is 128 of them.
    const float Step = 0.25f;
    const int ChunkCells = 128;
    const float SquareSize = Step * ChunkCells;

    // THE SQUARES BUILT, by the layout's own grid: square (I, J) holds layout x I*32..(I+1)*32 and layout y J*32..(J+1)*32.
    // Ids come from the layout, never from what is built, so a square keeps its id and its content as the world grows.
    // Lastline's 7 x 7 (decided 2026-10-27): Lastline sits on the corner of squares 19/20, so the block runs 17..23.
    static readonly (int i0, int i1, int j0, int j1)[] Blocks = { (17, 23, 17, 23) };

    // The built squares' box, in this scene's coordinates — set from Blocks when a build starts.
    static Vector2 WorldMin, WorldMax;
    // Lastline's own ground, measured from its saved mesh: 49 x 62 m around the middle.
    static readonly Rect Town = Rect.MinMaxRect(-24.5f, -31f, 24.5f, 31f);

    // ---- the layout ------------------------------------------------------------------------------------------------

    [System.Serializable] sealed class LayoutFile
    {
        public int[] size; public int cell; public int[] cells; public int[] heights;
        public string[] themes; public int[] theme_cells;
        public LTown[] towns; public LRiver[] rivers; public LRoad[] roads; public LBridge[] bridges;
        public LPond[] ponds; public LPlace[] landmarks; public LDoor[] dungeon_doors;
    }
    [System.Serializable] sealed class LTown { public string id, name, weapon; public float x, y, height; }
    [System.Serializable] sealed class LRiver { public float[] spring, xy, bed; }
    [System.Serializable] sealed class LRoad { public string from, to; public float[] xy, heights; }
    [System.Serializable] sealed class LBridge { public string[] road; public float x, y; }
    [System.Serializable] sealed class LPond { public string id, near; public float x, y, radius, water; }
    [System.Serializable] sealed class LPlace { public string id, name; public float x, y; }
    [System.Serializable] sealed class LDoor { public string town; public float x, y; }

    // A line of the layout (a road, a river) with a number along it, filed by 16-unit squares so a point only measures
    // the pieces near it: thousands of points per square, thousands of pieces in the world.
    sealed class Lines
    {
        const float Bin = 16f;
        readonly List<(Vector2 a, Vector2 b, float va, float vb)> segs = new List<(Vector2, Vector2, float, float)>();
        readonly Dictionary<(int, int), List<int>> bins = new Dictionary<(int, int), List<int>>();
        public readonly float reach;
        public Lines(float reach) { this.reach = reach; }

        public void Add(Vector2[] pts, float[] values)
        {
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                int n = segs.Count;
                segs.Add((pts[i], pts[i + 1], values[i], values[i + 1]));
                Vector2 lo = Vector2.Min(pts[i], pts[i + 1]) - Vector2.one * reach, hi = Vector2.Max(pts[i], pts[i + 1]) + Vector2.one * reach;
                for (int bj = Mathf.FloorToInt(lo.y / Bin); bj <= Mathf.FloorToInt(hi.y / Bin); bj++)
                    for (int bi = Mathf.FloorToInt(lo.x / Bin); bi <= Mathf.FloorToInt(hi.x / Bin); bi++)
                    {
                        if (!bins.TryGetValue((bi, bj), out var list)) bins[(bi, bj)] = list = new List<int>();
                        list.Add(n);
                    }
            }
        }

        /// <summary>The distance to the nearest piece within reach (float.MaxValue when none), the number there, and the
        /// way the line runs there.</summary>
        public float Nearest(float x, float z, out float value, out Vector2 dir)
        {
            value = 0f; dir = Vector2.zero;
            if (!bins.TryGetValue((Mathf.FloorToInt(x / Bin), Mathf.FloorToInt(z / Bin)), out var list)) return float.MaxValue;
            float best = float.MaxValue; var p = new Vector2(x, z);
            foreach (var n in list)
            {
                var (a, b, va, vb) = segs[n];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; value = Mathf.Lerp(va, vb, t); dir = ab.normalized; }
            }
            return best == float.MaxValue ? best : Mathf.Sqrt(best);
        }
    }

    // ---- the one world description -------------------------------------------------------------------------------

    /// <summary>Every height and colour of the world, as functions of the position in this scene, read from the layout.
    /// Scene x = layout x - Lastline's x, scene z = Lastline's y - layout y (north is +z), scene y = layout height -
    /// Lastline's own height, so Lastline's copied scene keeps its own numbers.</summary>
    sealed class Terrain
    {
        public const float RoadHalf = 1.5f;
        // The rock skin's height scale, and how far a river's water runs out from its line.
        public const float WallHeight = 12f;
        // Under every water: a shelf you can wade, this deep, then a drop too steep to walk down to the deep middle.
        public const float ShelfDepth = 0.25f, DeepDepth = 1.0f;
        // How far the river's water reaches out from its line, and how far its valley floor reaches.
        const float RiverWaterOut = 4.6f, RiverEdge = 2.6f;
        // A step between neighbouring layout cells bigger than this is a cliff (the layout's own rule).
        public const float CliffStep = 1.5f;
        // The steepest a road climbs: a quarter of a unit up for each unit along (about 14 degrees).
        const float RoadGrade = 0.25f;
        // How far beyond each end of a bridge the road's ground eases up to its deck.
        const float BridgeApproach = 6f;

        public readonly LayoutFile L;
        readonly float ox, oy, baseHeight;
        readonly int cw, ch, cell;
        public readonly Lines roads = new Lines(12f), rivers = new Lines(14f);
        public readonly List<(LPond p, Vector2 at, float level)> ponds = new List<(LPond, Vector2, float)>();
        public readonly List<(string name, Vector2 at)> keepClear = new List<(string, Vector2)>();
        public readonly List<BridgeDeck> bridges = new List<BridgeDeck>();
        public readonly List<(Vector2 from, Vector2 to, float height)> crossings = new List<(Vector2, Vector2, float)>();
        public readonly List<string> townIds;

        // Lastline's rim: the height of the top of its ground along each side, a quarter metre apart.
        float[] rimW, rimE, rimS, rimN;

        public Terrain(LayoutFile layout)
        {
            L = layout;
            var lastline = L.towns.First(t => t.id == "lastline");
            ox = lastline.x; oy = lastline.y; baseHeight = lastline.height;
            cw = L.cells[0]; ch = L.cells[1]; cell = L.cell;
            townIds = L.themes.ToList();
            foreach (var r in L.roads) layoutRoads.Add((Points(r.xy), r.heights.Select(h => h - baseHeight).ToArray()));
            foreach (var r in L.rivers) rivers.Add(Points(r.xy), r.bed.Select(h => h - baseHeight).ToArray());
            foreach (var p in L.ponds) ponds.Add((p, Scene(p.x, p.y), p.water - baseHeight));
            foreach (var m in L.landmarks) keepClear.Add((m.name, Scene(m.x, m.y)));
            foreach (var d in L.dungeon_doors) keepClear.Add((d.town + " dungeon door", Scene(d.x, d.y)));
        }

        public Vector2 Scene(float lx, float ly) => new Vector2(lx - ox, oy - ly);

        // A road's heights along it, never steeper than RoadGrade: the layout's height at each point, eased so no stretch
        // climbs faster — a forward and a backward pass, each holding the next point within the grade of the last. Where
        // the ground is steeper the road runs in a cutting or on a bank, and the ground eases to it.
        static float[] Graded(Vector2[] pts, float[] h)
        {
            var g = (float[])h.Clone();
            for (int pass = 0; pass < 2; pass++)
                for (int k = 1; k < g.Length; k++)
                {
                    int i = pass == 0 ? k : g.Length - 1 - k, prev = pass == 0 ? i - 1 : i + 1;
                    float most = RoadGrade * Vector2.Distance(pts[i], pts[prev]);
                    g[i] = Mathf.Clamp(g[i], g[prev] - most, g[prev] + most);
                }
            return g;
        }
        Vector2[] Points(float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = Scene(xy[2 * i], xy[2 * i + 1]);
            return pts;
        }

        public void SetRim(float[] w, float[] e, float[] s, float[] n) { rimW = w; rimE = e; rimS = s; rimN = n; }

        // The roads as the layout draws them, in this scene; laid by LayRoads once the hand-made town's gates are known.
        readonly List<(Vector2[] pts, float[] h)> layoutRoads = new List<(Vector2[], float[])>();

        // HOW A ROAD ENTERS A HAND-MADE TOWN: through one of its gates, never over its wall. The layout runs every road
        // from the town's middle; here the stretch of it within GateLead of the town's edge is replaced by a road that
        // leaves the gate facing the road's own way, runs straight out of it for GateStraight, then curves onto the
        // layout's road GateLead out. Roads that leave the town the same way share a gate.
        public const float GateStraight = 6f, GateLead = 16f;

        public void LayRoads(Rect town, List<(Vector2 at, Vector2 outward)> gates)
        {
            foreach (var (pts, h) in layoutRoads)
            {
                var p = new List<Vector2>(pts); var hs = new List<float>(h);
                if (gates.Count > 0)
                {
                    ThroughGate(town, gates, p, hs);
                    p.Reverse(); hs.Reverse();
                    ThroughGate(town, gates, p, hs);
                    p.Reverse(); hs.Reverse();
                }
                var arr = p.ToArray();
                roads.Add(arr, Graded(arr, hs.ToArray()));
            }
        }

        void ThroughGate(Rect town, List<(Vector2 at, Vector2 outward)> gates, List<Vector2> p, List<float> h)
        {
            if (DistToRect(town, p[0].x, p[0].y) > 0f) return;
            int rejoin = p.FindIndex(q => DistToRect(town, q.x, q.y) >= GateLead);
            if (rejoin < 0) return;
            Vector2 toward = (p[rejoin] - town.center).normalized;
            var gate = gates.OrderByDescending(g => Vector2.Dot((g.at - town.center).normalized, toward)).First();
            Vector2 edge = new Vector2(Mathf.Clamp(gate.at.x, town.xMin, town.xMax), Mathf.Clamp(gate.at.y, town.yMin, town.yMax));
            if (gate.outward.x != 0f) edge.x = gate.outward.x > 0f ? town.xMax : town.xMin;
            else edge.y = gate.outward.y > 0f ? town.yMax : town.yMin;
            float rimH = RimHeight(edge.x, edge.y), endH = h[rejoin];
            Vector2 straight = edge + gate.outward * GateStraight, end = p[rejoin];
            Vector2 bend = straight + gate.outward * (Vector2.Distance(straight, end) * 0.5f);
            var np = new List<Vector2> { gate.at, edge, straight };
            var nh = new List<float> { rimH, rimH, rimH };
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(straight, end) / 1.5f));
            for (int k = 1; k < steps; k++)
            {
                float t = (float)k / steps, u = 1f - t;
                np.Add(u * u * straight + 2f * u * t * bend + t * t * end);
                nh.Add(Mathf.Lerp(rimH, endH, t));
            }
            p.RemoveRange(0, rejoin); h.RemoveRange(0, rejoin);
            p.InsertRange(0, np); h.InsertRange(0, nh);
        }

        // Every ground point's height worked out once, with one point of margin all round so a normal at the built box's
        // edge has neighbours: i = 0..nx, j = 0..nz are the box's points, stored shifted by one.
        float[] grid;
        int gw, gh;

        public void BuildGrid(int nx, int nz)
        {
            gw = nx + 3; gh = nz + 3;
            grid = new float[gw * gh];
            for (int j = 0; j < gh; j++)
                for (int i = 0; i < gw; i++)
                    grid[j * gw + i] = Height(WorldMin.x + (i - 1) * Step, WorldMin.y + (j - 1) * Step);
        }

        public float HeightAt(int i, int j) => grid[(j + 1) * gw + (i + 1)];

        public Vector3 NormalAt(int i, int j)
        {
            float hx = HeightAt(i + 1, j) - HeightAt(i - 1, j);
            float hz = HeightAt(i, j + 1) - HeightAt(i, j - 1);
            return new Vector3(-hx, 2f * Step, -hz).normalized;
        }

        static float Smooth(float a, float b, float x)
        {
            x = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a));
            return x * x * (3f - 2f * x);
        }

        static float DistToRect(Rect r, float x, float z)
        {
            float dx = Mathf.Max(r.xMin - x, 0f, x - r.xMax), dz = Mathf.Max(r.yMin - z, 0f, z - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ---- the layout's cells -----------------------------------------------------------------------------------

        float CellHeight(int ci, int cj)
        {
            ci = Mathf.Clamp(ci, 0, cw - 1); cj = Mathf.Clamp(cj, 0, ch - 1);
            return L.heights[cj * cw + ci] * 0.1f - baseHeight;
        }

        /// <summary>The index into the layout's themes of the town whose land this is, -1 for the sea.</summary>
        public int ThemeAt(float x, float z)
        {
            float lx = x + ox, ly = oy - z;
            int ci = Mathf.Clamp(Mathf.FloorToInt(lx / cell), 0, cw - 1), cj = Mathf.Clamp(Mathf.FloorToInt(ly / cell), 0, ch - 1);
            return L.theme_cells[cj * cw + ci];
        }

        // THE LAYOUT'S GROUND, BETWEEN ITS CELLS. Inside a cell the four nearest cell heights are blended — but only the
        // ones on the point's own side of any cliff: the corner whose height is nearest the four's blend at the point
        // decides the side, and a corner more than a cliff step away from it is replaced by it. So the ground is smooth on each level and jumps the whole step between two neighbouring points where the
        // levels meet: a sheer wall in the one ground.
        float LayoutGround(float x, float z)
        {
            float lx = x + ox, ly = oy - z;
            float u = lx / cell - 0.5f, v = ly / cell - 0.5f;
            int i0 = Mathf.FloorToInt(u), j0 = Mathf.FloorToInt(v);
            float fu = u - i0, fv = v - j0;
            float h00 = CellHeight(i0, j0), h10 = CellHeight(i0 + 1, j0), h01 = CellHeight(i0, j0 + 1), h11 = CellHeight(i0 + 1, j0 + 1);
            float lo = Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11)), hi = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
            if (hi - lo > CliffStep)
            {
                // The side is read off the blend of all four, so a wall follows the level's own outline in smooth curves
                // and diagonals instead of the cells' square steps; a little noise lets it wander.
                float blend = Mathf.Lerp(Mathf.Lerp(h00, h10, fu), Mathf.Lerp(h01, h11, fu), fv)
                            + (Mathf.PerlinNoise(lx * 0.23f + 13.7f, ly * 0.23f + 2.9f) - 0.5f) * 0.35f * (hi - lo);
                float near = h00;
                foreach (var c in new[] { h10, h01, h11 }) if (Mathf.Abs(c - blend) < Mathf.Abs(near - blend)) near = c;
                if (Mathf.Abs(h00 - near) > CliffStep) h00 = near;
                if (Mathf.Abs(h10 - near) > CliffStep) h10 = near;
                if (Mathf.Abs(h01 - near) > CliffStep) h01 = near;
                if (Mathf.Abs(h11 - near) > CliffStep) h11 = near;
            }
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fu), Mathf.Lerp(h01, h11, fu), fv);
        }

        // ---- roads, water, Lastline's rim ---------------------------------------------------------------------------

        public float DistRoads(float x, float z) => roads.Nearest(x, z, out _, out _);
        public float DistRiver(float x, float z) => rivers.Nearest(x, z, out _, out _);

        /// <summary>The distance to the nearest pond's middle, and that pond (-1 when there is none near).</summary>
        public float DistPond(float x, float z, out int which)
        {
            which = -1; float best = float.MaxValue; var p = new Vector2(x, z);
            for (int i = 0; i < ponds.Count; i++)
            {
                float d = Vector2.Distance(p, ponds[i].at) - ponds[i].p.radius;
                if (d < best) { best = d; which = i; }
            }
            return best;
        }

        // A water's bed by distance from its edge inward: a gentle bank down to a shelf a body can wade, then a drop
        // too steep to walk down, to the deep middle. `inward` is metres inside the water's edge (negative = bank).
        static float Bed(float level, float inward, float bankTop, float bankWidth)
        {
            float shelf = level - ShelfDepth, deep = level - DeepDepth;
            if (inward < -bankWidth) return bankTop;
            if (inward < 0.6f) return Mathf.Lerp(bankTop, shelf, Smooth(-bankWidth, 0.6f, inward));
            if (inward < 1.4f) return shelf;
            if (inward < 1.75f) return Mathf.Lerp(shelf, deep, (inward - 1.4f) / 0.35f);
            return deep;
        }

        static float Rim(float[] side, float along)
        {
            if (side == null || side.Length == 0) return 0f;
            int i = Mathf.Clamp(Mathf.RoundToInt(along / Step), 0, side.Length - 1);
            return side[i];
        }

        // The height of Lastline's rim at the point of its edge nearest to (x, z).
        float RimHeight(float x, float z)
        {
            float cx = Mathf.Clamp(x, Town.xMin, Town.xMax), cz = Mathf.Clamp(z, Town.yMin, Town.yMax);
            if (Mathf.Approximately(cx, Town.xMin)) return Rim(rimW, cz - Town.yMin);
            if (Mathf.Approximately(cx, Town.xMax)) return Rim(rimE, cz - Town.yMin);
            if (Mathf.Approximately(cz, Town.yMin)) return Rim(rimS, cx - Town.xMin);
            return Rim(rimN, cx - Town.xMin);
        }

        static float Mounds(float x, float z) =>
            0.9f * (Mathf.PerlinNoise(x * 0.06f + 31.7f, z * 0.06f + 11.3f) - 0.5f) +
            0.3f * (Mathf.PerlinNoise(x * 0.15f + 5.1f, z * 0.15f + 7.9f) - 0.5f);

        public float Height(float x, float z)
        {
            float h = LayoutGround(x, z) + Mounds(x, z);
            float dv = rivers.Nearest(x, z, out float riverBed, out _);
            // Roads run level across, at the layout's height along them, and the ground eases to them over four metres —
            // except over a river, where the road crosses on its bridge and the water runs underneath.
            float dr = roads.Nearest(x, z, out float roadHeight, out _);
            if (dr < RoadHalf + 4f && dv > RiverWaterOut + 1f) h = Mathf.Lerp(roadHeight, h, Smooth(RoadHalf, RoadHalf + 4f, dr));
            // Lastline's rim, exactly, eased back into the world over six metres.
            float dt = DistToRect(Town, x, z);
            if (dt < 6f) h = Mathf.Lerp(RimHeight(x, z), h, Smooth(0f, 6f, dt));
            // The river: the water in a channel down the valley the layout cut, its edge wandering a little.
            if (dv < RiverEdge + 1.6f + 1f)
            {
                float level = riverBed - 0.15f;
                float edge = RiverEdge + (Mathf.PerlinNoise(x * 0.2f + 91f, z * 0.2f + 13f) - 0.5f) * 0.8f;
                h = Mathf.Min(h, Bed(level, edge - dv, Mathf.Max(level + 0.3f, Mathf.Min(h, level + 1.2f)), 1.6f));
            }
            // A bridge's ends: the road's ground eases up to the deck's height over BridgeApproach metres beyond each end,
            // so the deck never stands above a bank that dips down to the water.
            foreach (var (from, to, deckHeight) in crossings)
            {
                Vector2 along = (to - from).normalized, p = new Vector2(x, z);
                float s = Vector2.Dot(p - from, along), len = Vector2.Distance(from, to);
                float beyond = s < 0f ? -s : s > len ? s - len : 0f;
                float side = Mathf.Abs(Vector2.Dot(p - from, new Vector2(-along.y, along.x)));
                if (beyond > 0f && beyond < BridgeApproach && side < RoadHalf + 2f)
                    h = Mathf.Max(h, Mathf.Lerp(deckHeight, h, Mathf.Max(Smooth(0f, BridgeApproach, beyond), Smooth(RoadHalf, RoadHalf + 2f, side))));
            }
            // A pond: a hollow with a gentle bank all round, so nobody is stuck in it.
            float dp = DistPond(x, z, out int pi);
            if (pi >= 0 && dp < 6f)
            {
                float level = ponds[pi].level;
                float edge = (Mathf.PerlinNoise(x * 0.25f + 7f, z * 0.25f + 29f) - 0.5f) * 1.2f;
                float bank = Mathf.Lerp(level + 0.25f, h, Smooth(edge + 1.5f, 6f, dp));
                h = Mathf.Min(h, Bed(level, edge - dp, bank, 1.5f));
            }
            return h;
        }

        public Vector3 Normal(float x, float z)
        {
            float hx = Height(x + Step, z) - Height(x - Step, z);
            float hz = Height(x, z + Step) - Height(x, z - Step);
            return new Vector3(-hx, 2f * Step, -hz).normalized;
        }

        // (road, stone, rock, dirt) — the ground shader's four layers over the grass.
        public Color Weights(float x, float z, Vector3 n)
        {
            float dv = DistRiver(x, z), dp = DistPond(x, z, out _);
            float road = dv < RiverWaterOut + 1f ? 0f : 1f - Smooth(RoadHalf - 0.35f, RoadHalf + 0.6f, DistRoads(x, z));
            // Pebbles on the river's valley floor and round the ponds, feathered out rather than cut.
            float stone = Mathf.Max(1f - Smooth(4.6f, 6f, dv), 1f - Smooth(1f, 2.5f, dp));
            float patch = Mathf.PerlinNoise(x * 0.06f + 200f, z * 0.06f + 41f) * 0.65f
                        + Mathf.PerlinNoise(x * 0.13f + 77f, z * 0.13f + 12f) * 0.35f;
            float dirt = Smooth(0.62f, 0.75f, patch);
            // Next to Lastline the ground is plain grass and road, the two things its rim blends into.
            dirt *= Smooth(2f, 6f, DistToRect(Town, x, z));
            return new Color(road, stone, 0f, dirt);
        }

        // The cliff layer (uv1): x = how much of the rock skin, laid on from the side, shows here — the steeper the
        // more; y = how far up its rock the point stands.
        public Vector2 Rock(float x, float z, float height, Vector3 n)
        {
            float steep = 1f - Smooth(0.62f, 0.86f, n.y);
            return new Vector2(steep, Mathf.Clamp01(Mathf.Repeat(height, WallHeight) / WallHeight));
        }

        /// <summary>The surface height of the water covering a point, or NaN. The river's sheet reaches well in under its
        /// banks, so its own edge is hidden in the ground; it falls with the river.</summary>
        public float WaterAt(float x, float z, out int body)
        {
            body = -1;
            float dv = rivers.Nearest(x, z, out float bed, out _);
            if (dv < RiverWaterOut) { body = 0; return bed - 0.15f; }
            float dp = DistPond(x, z, out int pi);
            if (pi >= 0 && dp < 2.2f) { body = 1 + pi; return ponds[pi].level; }
            return float.NaN;
        }

        // Which way the river runs at a point, in metres per second — downstream along it; a pond lies still.
        public Vector2 FlowAt(float x, float z)
        {
            float dv = rivers.Nearest(x, z, out _, out Vector2 dir);
            return dv < RiverWaterOut ? dir * 0.6f : Vector2.zero;
        }

        /// <summary>TRUE when the ground here is a cliff's foot: ground at least a cliff step higher right beside it.</summary>
        public bool CliffFoot(float x, float z)
        {
            float h = Height(x, z);
            return Height(x + 1.2f, z) > h + CliffStep || Height(x - 1.2f, z) > h + CliffStep
                || Height(x, z + 1.2f) > h + CliffStep || Height(x, z - 1.2f) > h + CliffStep;
        }

        /// <summary>The foot of the nearest wall within <paramref name="reach"/> of this point, walking along the four
        /// directions a quarter unit at a time until the ground jumps more than a cliff step: where its foot is, which way
        /// it rises, and how tall it is (0 when none is in reach).</summary>
        public float WallNear(float x, float z, float reach, out Vector2 foot, out Vector2 toward)
        {
            float best = float.MaxValue, tall = 0f; foot = new Vector2(x, z); toward = Vector2.zero;
            foreach (var d in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                float last = Height(x, z);
                for (float s = Step; s <= reach; s += Step)
                {
                    float px = x + d.x * s, pz = z + d.y * s, here = Height(px, pz);
                    if (here - last > CliffStep * 0.5f)
                    {
                        // The jump: the wall's height is how far the ground climbs over the next metre.
                        float top = Mathf.Max(here, Height(px + d.x, pz + d.y));
                        if (s < best) { best = s; foot = new Vector2(px - d.x * Step, pz - d.y * Step); toward = d; tall = top - last; }
                        break;
                    }
                    last = here;
                }
            }
            return tall > CliffStep ? tall : 0f;
        }

        public bool PropFree(float x, float z)
        {
            if (DistRoads(x, z) < RoadHalf + 1.5f) return false;
            if (DistRiver(x, z) < 6.5f || DistPond(x, z, out _) < 4f) return false;
            var p = new Vector2(x, z);
            var town = new Rect(Town.x - 4f, Town.y - 4f, Town.width + 8f, Town.height + 8f);
            if (town.Contains(p)) return false;
            foreach (var (_, at) in keepClear) if ((at - p).sqrMagnitude < 36f) return false;
            // Never held back from the built squares' edge: that edge moves as the world grows, and a square must not.
            return Normal(x, z).y > 0.93f;
        }
    }

    // ---- building ------------------------------------------------------------------------------------------------

    sealed class ChunkOut
    {
        public int cx, cz;       // the square's layout id (I, J)
        public Rect rect;
        public GameObject root;
        public Transform props;
        public string prefabPath;
    }

    // The square (I, J) in this scene's coordinates.
    static Rect SquareRect(Terrain t, int i, int j)
    {
        Vector2 a = t.Scene(i * SquareSize, (j + 1) * SquareSize), b = t.Scene((i + 1) * SquareSize, j * SquareSize);
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    /// <summary>Build the squares in <see cref="Blocks"/>. Returns a report of what was written and measured.</summary>
    public static string BuildCore()
    {
        var report = new StringBuilder();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EnsureFolder(OutDir + "/Chunks");
        EnsureFolder(OutDir + "/Meshes");
        madeMeshes.Clear();
        foreach (var town in KeptBiomes.Keys) Themes.Remove(town);   // read from the Biome Table afresh on every build

        if (!File.Exists(LayoutPath)) throw new System.Exception("no world layout at " + LayoutPath + " — run tools/world-layout/build_world_layout.py");
        var layout = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(LayoutPath));
        var terrain = new Terrain(layout);
        var squares = new List<(int i, int j)>();
        foreach (var (i0, i1, j0, j1) in Blocks)
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (!squares.Contains((i, j))) squares.Add((i, j));
        var box = SquareRect(terrain, squares[0].i, squares[0].j);
        foreach (var (i, j) in squares) { var r = SquareRect(terrain, i, j); box = Rect.MinMaxRect(Mathf.Min(box.xMin, r.xMin), Mathf.Min(box.yMin, r.yMin), Mathf.Max(box.xMax, r.xMax), Mathf.Max(box.yMax, r.yMax)); }
        WorldMin = box.min; WorldMax = box.max;
        report.AppendLine($"{squares.Count} squares, scene box {WorldMin} .. {WorldMax}.");

        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var material = WorldMaterial(NatureScale(report));
            // Lastline first: its rim decides the heights the world ground must meet.
            Vector3 arrival = LastlinePiece(terrain, material, report, out string lastlinePrefab);
            var river = WaterMaterial("river", new Vector4(0f, -0.6f, 0f, 0f));
            var pondMat = WaterMaterial("pond", Vector4.zero);

            int nx = Mathf.RoundToInt((WorldMax.x - WorldMin.x) / Step);
            int nz = Mathf.RoundToInt((WorldMax.y - WorldMin.y) / Step);
            // The bridges first: the ground eases up to each bridge's ends, so the grid is worked out after them.
            FitBridges(terrain, preview, report);
            terrain.BuildGrid(nx, nz);
            report.AppendLine($"Ground heights worked out in {clock.ElapsedMilliseconds} ms.");

            var outs = new List<ChunkOut>();
            foreach (var (si, sj) in squares)
            {
                var rect = SquareRect(terrain, si, sj);
                int i0 = Mathf.RoundToInt((rect.xMin - WorldMin.x) / Step), i1 = i0 + ChunkCells;
                int j0 = Mathf.RoundToInt((rect.yMin - WorldMin.y) / Step), j1 = j0 + ChunkCells;
                var c = new ChunkOut { cx = si, cz = sj, rect = rect };
                var ground = GroundMesh(terrain, i0, i1, j0, j1, c.rect, $"world_{si}_{sj}");
                if (ground == null) continue;
                c.root = new GameObject($"Square {si} {sj}");
                SceneManager.MoveGameObjectToScene(c.root, preview);
                var g = new GameObject("Ground");
                g.transform.SetParent(c.root.transform, false);
                g.layer = LayerMask.NameToLayer("Ground");
                g.AddComponent<MeshFilter>().sharedMesh = ground;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                g.AddComponent<MeshCollider>().sharedMesh = ground;

                AddWater(terrain, c, river, pondMat);
                var props = new GameObject("Props");
                props.transform.SetParent(c.root.transform, false);
                c.props = props.transform;
                outs.Add(c);
            }

            float reach = 0f;
            foreach (var deck in terrain.bridges) reach = Mathf.Max(reach, PlaceBridge(deck, outs, preview));
            reach = Mathf.Max(reach, ScatterProps(terrain, outs, preview, report));
            LayCarpets(terrain, outs, report);
            ApplyNatureWind(NatureScale(report), report);

            var addresses = new List<(string address, Rect rect)>();
            foreach (var c in outs)
            {
                c.prefabPath = $"{OutDir}/Chunks/square_{c.cx}_{c.cz}.prefab";
                PrefabUtility.SaveAsPrefabAsset(c.root, c.prefabPath);
                Object.DestroyImmediate(c.root);
                addresses.Add(($"{MapId}/square_{c.cx}_{c.cz}", c.rect));
            }
            addresses.Add(($"{MapId}/lastline", Town));
            RegisterAddresses(outs, lastlinePrefab);
            // A square an earlier build made that is no longer in Blocks: its prefab and its address go, so nothing can
            // load ground the world no longer has.
            var keepPaths = new HashSet<string>(outs.Select(c => c.prefabPath));
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { OutDir + "/Chunks" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith("square_") || keepPaths.Contains(path)) continue;
                settings.RemoveAssetEntry(guid);
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.SaveAssetIfDirty(settings.FindGroup(GroupName));
            AssetDatabase.SaveAssetIfDirty(settings);

            float loadSeconds = MeasureChunkBuild(outs, preview, report);
            WriteScene(addresses, arrival, reach, loadSeconds);
            WorldBuildUtil.RegisterInBuildSettings(ScenePath);
            // Meshes an earlier build made under names this one no longer uses are left over otherwise. Deleted only
            // after everything new is saved: deleting a mesh and making a new one at the same path in one go leaves the
            // editor holding prefabs whose meshes read as missing until they are imported again.
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { OutDir + "/Meshes" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!madeMeshes.Contains(path)) AssetDatabase.DeleteAsset(path);
            }

            // ONLY ITS OWN ASSETS are saved — never every dirty asset in the project, which would write whatever else the
            // editor happens to have changed (a font that grew its glyph sheet, the user's unsaved work).
            AssetDatabase.SaveAssetIfDirty(material);
            AssetDatabase.SaveAssetIfDirty(river);
            AssetDatabase.SaveAssetIfDirty(pondMat);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            report.AppendLine($"{outs.Count} squares + Lastline, prop reach {reach:0.0} m, built in {clock.ElapsedMilliseconds} ms.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
        // The scene is written as text, so the bake a saved scene triggers never sees it: the minimap is drawn here.
        report.AppendLine(MinimapBaker.Bake(ScenePath) ? "Minimap drawn: the overview and every square." : "Minimap: nothing to draw.");
        return report.ToString();
    }
    // Lastline's ground, buildings and walls, copied out of a hidden copy of its authored prefab into one prefab, exactly.
    // Its gate markers stay behind; its arrival spot becomes the world's. Its rim heights are read off its ground mesh so
    // the fields meet it.
    static Vector3 LastlinePiece(Terrain terrain, Material world, StringBuilder report, out string prefabPath)
    {
        prefabPath = $"{OutDir}/Chunks/lastline.prefab";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(LastlineSourcePath)
                     ?? throw new System.Exception(LastlineSourcePath + " is missing");
        var town = EditorSceneManager.NewPreviewScene();
        try
        {
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(source, town);
            // Only the outer prefab: the props inside stay instances of their palette prefabs, as they were in the old map.
            PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            GameObject ground = null, props = null, wall = null;
            Vector3 arrival = Vector3.zero;
            var gates = new List<(Vector2 at, Vector2 outward)>();
            foreach (Transform part in copy.transform)
            {
                // Each gate marker is a gate, facing out of the side it is nearest.
                if (part.name.StartsWith("Gate_")) gates.Add(Gate(new Vector2(part.position.x, part.position.z)));
                if (part.name == "Ground_lastline") ground = part.gameObject;
                else if (part.name == "Props") props = part.gameObject;
                else if (part.name == "Wall") wall = part.gameObject;
                else if (part.name == "Arrival") arrival = part.position;
            }
            if (ground == null) throw new System.Exception(LastlineSourcePath + " has no Ground_lastline");

            ReadRim(terrain, ground, report);
            terrain.LayRoads(Town, gates);
            report.AppendLine($"Lastline: {gates.Count} gates; every road leaves it through one.");
            BlendRim(terrain, ground, report);
            SameGrassAsTheWorld(ground, world, report);

            var holder = new GameObject("Lastline");
            SceneManager.MoveGameObjectToScene(holder, town);
            ground.transform.SetParent(holder.transform, true);
            if (props != null) props.transform.SetParent(holder.transform, true);
            // KEPT, at the user's word: Lastline's walls are part of what Lastline is.
            if (wall != null) wall.transform.SetParent(holder.transform, true);
            PrefabUtility.SaveAsPrefabAsset(holder, prefabPath);
            report.AppendLine($"Lastline: ground, {(props != null ? props.transform.childCount : 0)} prop groups and its walls kept; arrival point at {arrival}.");
            return arrival;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(town);
        }
    }

    static (Vector2 at, Vector2 outward) Gate(Vector2 at)
    {
        float w = at.x - Town.xMin, e = Town.xMax - at.x, s = at.y - Town.yMin, n = Town.yMax - at.y, m = Mathf.Min(w, e, s, n);
        return (at, m == w ? Vector2.left : m == e ? Vector2.right : m == s ? Vector2.down : Vector2.up);
    }

    // The top of Lastline's ground along each of its four sides, from the vertices that lie on that side.
    static void ReadRim(Terrain terrain, GameObject ground, StringBuilder report)
    {
        var mf = ground.GetComponentInChildren<MeshFilter>();
        var mesh = mf.sharedMesh;
        var m = mf.transform.localToWorldMatrix;
        int alongZ = Mathf.RoundToInt(Town.height / Step) + 1, alongX = Mathf.RoundToInt(Town.width / Step) + 1;
        var w = Filled(alongZ); var e = Filled(alongZ); var s = Filled(alongX); var n = Filled(alongX);
        foreach (var v in mesh.vertices)
        {
            Vector3 p = m.MultiplyPoint3x4(v);
            if (Mathf.Abs(p.x - Town.xMin) < 0.02f) Top(w, (p.z - Town.yMin) / Step, p.y);
            if (Mathf.Abs(p.x - Town.xMax) < 0.02f) Top(e, (p.z - Town.yMin) / Step, p.y);
            if (Mathf.Abs(p.z - Town.yMin) < 0.02f) Top(s, (p.x - Town.xMin) / Step, p.y);
            if (Mathf.Abs(p.z - Town.yMax) < 0.02f) Top(n, (p.x - Town.xMin) / Step, p.y);
        }
        FillGaps(w); FillGaps(e); FillGaps(s); FillGaps(n);
        terrain.SetRim(w, e, s, n);
        report.AppendLine($"Lastline rim heights: west {Range(w)}, east {Range(e)}, south {Range(s)}, north {Range(n)}.");
    }

    // HOW A HAND-MADE PIECE MEETS THE WORLD GROUND WITHOUT A LINE: its outer band fades into the very textures the world
    // ground wears at that edge — the same tile at the same world scale, so the last ring of its points draws exactly what
    // the first ring of the world's draws. Lastline's ground material carries the world's grass in its stone layer and
    // the world's road in its rock layer, so its rim fades to grass, and to road where a world road meets it. Done on a
    // COPY of its ground mesh for this world; Lastline's own scene and mesh are untouched.
    const float RimBlend = 3f;
    // Through a gate the world road reaches further in: whole road from the rim to GateRoadWhole metres inside it (past
    // where the town's paving stops at its wall line, so the paving and the strip beside it end on the same colour and
    // meet without a line), fading back to the town's own paving by GateRoadFade.
    const float GateRoadWhole = 3f, GateRoadFade = 8f;

    static float Smooth01(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    static void BlendRim(Terrain t, GameObject ground, StringBuilder report)
    {
        var mf = ground.GetComponentInChildren<MeshFilter>();
        var src = mf.sharedMesh;
        var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
        string Tile(string prop) => mat.HasProperty(prop) && mat.GetTexture(prop) != null ? mat.GetTexture(prop).name : "";
        if (Tile("_StoneMap") != "BASE_OR_PATCH_meadow_grass" || Tile("_RockMap") != "PATH_dirt_road")
            throw new System.Exception($"Lastline's ground no longer carries the world's grass and road (stone '{Tile("_StoneMap")}', rock '{Tile("_RockMap")}') — its rim cannot be blended.");
        var mesh = Object.Instantiate(src);
        mesh.name = "world_lastline_ground";
        var m = mf.transform.localToWorldMatrix;
        var verts = mesh.vertices;
        var cols = mesh.colors;
        int blended = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = m.MultiplyPoint3x4(verts[i]);
            float edge = Mathf.Min(p.x - Town.xMin, Town.xMax - p.x, p.z - Town.yMin, Town.yMax - p.z);
            if (edge >= GateRoadFade) continue;
            // At the rim: road where a world road arrives, grass everywhere else.
            Vector2 rimPoint = new Vector2(Mathf.Clamp(p.x, Town.xMin, Town.xMax), Mathf.Clamp(p.z, Town.yMin, Town.yMax));
            float road = 1f - Mathf.Clamp01((t.DistRoads(rimPoint.x, rimPoint.y) - Terrain.RoadHalf + 0.35f) / 0.95f);
            float k = edge < RimBlend ? Smooth01(1f - edge / RimBlend) : 0f;
            k = Mathf.Max(k, road * (1f - Smooth01((edge - GateRoadWhole) / (GateRoadFade - GateRoadWhole))));
            if (k <= 0f) continue;
            // Grass fully under the road too: the layers are laid one over another, so anything less would let the
            // town's paving show through where the road is not whole.
            var target = new Color(0f, 1f, road, 0f);
            cols[i] = Color.Lerp(cols[i], target, k);
            blended++;
        }
        mesh.colors = cols;
        mesh = SaveMesh(mesh);
        mf.sharedMesh = mesh;
        report.AppendLine($"Lastline's rim: {blended} points fade into the world's grass within {RimBlend} m of its edge, and into its roads within {GateRoadFade} m of a gate.");
    }

    // LASTLINE'S GRASS AND ROAD ARE THE WORLD'S (the user, 2026-10-27): on a COPY of its ground material for this world,
    // its lawn layer wears the world's grass and its road layer the world's road, each at the world's own scale, so its
    // lawns, its rim and the fields around it are one grass and its gates run on into one road. Its paving and soil
    // are its own. Lastline's own scene and material are untouched.
    static void SameGrassAsTheWorld(GameObject ground, Material world, StringBuilder report)
    {
        var mr = ground.GetComponentInChildren<MeshRenderer>();
        string path = OutDir + "/world_lastline_ground.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(mr.sharedMaterial); AssetDatabase.CreateAsset(mat, path); }
        else { mat.shader = mr.sharedMaterial.shader; mat.CopyPropertiesFromMaterial(mr.sharedMaterial); }
        mat.SetTexture("_StoneMap", world.GetTexture("_GrassMap")); mat.SetFloat("_StoneTiling", world.GetFloat("_GrassTiling"));
        mat.SetTexture("_RockMap", world.GetTexture("_PathMap")); mat.SetFloat("_RockTiling", world.GetFloat("_PathTiling"));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
        mr.sharedMaterial = mat;
        report.AppendLine($"Lastline's lawns and rim wear the world's grass ({world.GetTexture("_GrassMap").name}) and its gates the world's road.");
    }

    static float[] Filled(int count)
    {
        var a = new float[count];
        for (int i = 0; i < count; i++) a[i] = float.NaN;
        return a;
    }

    static void Top(float[] side, float at, float y)
    {
        int i = Mathf.RoundToInt(at);
        if (i < 0 || i >= side.Length || Mathf.Abs(at - i) > 0.1f) return;
        if (float.IsNaN(side[i]) || y > side[i]) side[i] = y;
    }

    // Sides of the mesh with vertices further apart than a quarter metre: the heights between are drawn straight across.
    static void FillGaps(float[] side)
    {
        int last = -1;
        for (int i = 0; i < side.Length; i++)
        {
            if (float.IsNaN(side[i])) continue;
            if (last < 0) for (int k = 0; k < i; k++) side[k] = side[i];
            else for (int k = last + 1; k < i; k++) side[k] = Mathf.Lerp(side[last], side[i], (k - last) / (float)(i - last));
            last = i;
        }
        if (last < 0) { for (int k = 0; k < side.Length; k++) side[k] = 0f; return; }
        for (int k = last + 1; k < side.Length; k++) side[k] = side[last];
    }

    static string Range(float[] a)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in a) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
        return $"{lo:0.00}..{hi:0.00}";
    }

    // One chunk of the world ground, its points every quarter metre, Lastline's own ground left out. The bridge deck,
    // where it falls in this chunk, is added to the same mesh. Null when the chunk lies wholly inside Lastline.
    static Mesh GroundMesh(Terrain t, int i0, int i1, int j0, int j1, Rect rect, string name)
    {
        int w = i1 - i0 + 1, h = j1 - j0 + 1;
        var index = new int[w * h];
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var cols = new List<Color>();
        var rockUv = new List<Vector2>();
        var tris = new List<int>();
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = WorldMin.x + (i0 + i) * Step, z = WorldMin.y + (j0 + j) * Step;
                bool insideTown = x > Town.xMin + 0.01f && x < Town.xMax - 0.01f && z > Town.yMin + 0.01f && z < Town.yMax - 0.01f;
                if (insideTown) { index[j * w + i] = -1; continue; }
                index[j * w + i] = verts.Count;
                var normal = t.NormalAt(i0 + i, j0 + j);
                float y = t.HeightAt(i0 + i, j0 + j);
                verts.Add(new Vector3(x, y, z));
                norms.Add(normal);
                uvs.Add(new Vector2(x, z));
                cols.Add(t.Weights(x, z, normal));
                rockUv.Add(t.Rock(x, z, y, normal));
            }
        for (int j = 0; j + 1 < h; j++)
            for (int i = 0; i + 1 < w; i++)
            {
                float mx = WorldMin.x + (i0 + i + 0.5f) * Step, mz = WorldMin.y + (j0 + j + 0.5f) * Step;
                if (Town.Contains(new Vector2(mx, mz))) continue;
                int v0 = index[j * w + i], v1 = index[j * w + i + 1], v2 = index[(j + 1) * w + i], v3 = index[(j + 1) * w + i + 1];
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) continue;
                tris.Add(v0); tris.Add(v2); tris.Add(v1);
                tris.Add(v1); tris.Add(v2); tris.Add(v3);
            }
        if (tris.Count == 0) return null;

        // The whole deck goes into the one chunk holding the bridge's middle, with the bridge model, never into two.
        foreach (var deck in t.bridges) if (rect.Contains(deck.footprint.center)) AddDeck(deck, verts, norms, uvs, cols, rockUv, tris);

        var mesh = new Mesh { name = name + "_ground" };
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols);
        mesh.SetUVs(1, rockUv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh = SaveMesh(mesh);
        return mesh;
    }

    // ---- the bridge ----------------------------------------------------------------------------------------------

    const string BridgePrefabPath = "Assets/1_Authored/Prefabs/Props/Bridges/BridgeRock.prefab";
    // The deck is sampled every half metre along the crossing and every quarter metre across it, so the parapets are
    // caught as well as the arch; it sits this far under the model's surface, so the model is what is seen.
    const float DeckAlong = 0.5f, DeckAcross = 0.25f, DeckSink = 0.03f;
    // Stone this much higher than the walkway's middle is a parapet; the search for it steps this far at a time.
    // The deck's parapet stands ParapetInto inside the stone's inner face and ParapetBelow under its top; a gap in the
    // stone wider than StoneGap ends the walkway.
    const float ParapetRise = 0.2f, ParapetProbe = 0.05f, ParapetInto = 0.45f, ParapetBelow = 0.3f, StoneGap = 0.3f;

    /// <summary>The bridge model fitted to the crossing, and its top surface: the heights the walkable deck takes.</summary>
    sealed class BridgeDeck
    {
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
        public Rect footprint;
        // Rows along the crossing; across each row, from one side to the other: the top of the left parapet, its foot,
        // the walkway's points, the right parapet's foot and its top. A parapet's foot and top stand on one spot just
        // inside its inner face, so the deck rises straight up there, hidden in the stone, and stops a body.
        public Vector3 start, along, across;
        public int rows, cols;
        public float[] offset, height;
        public bool Has(int a, int c) => !float.IsNaN(height[a * cols + c]);
        public Vector3 Point(int a, int c) => start + along * a + across * offset[a * cols + c] + Vector3.up * height[a * cols + c];
    }

    // EVERY BRIDGE OF THE LAYOUT IN THE BUILT BOX: its road's crossing of the river, bank to bank along the road, at the
    // road's height on the higher bank. The bridge model is fitted to it, and its walkable deck is the model's own top
    // surface sampled into the ground (FitBridge).
    static void FitBridges(Terrain t, Scene preview, StringBuilder report)
    {
        // Every bridge near the built squares is fitted, built or not, so the props it keeps clear of come out the same
        // whichever squares are built; its model and deck go only into the square holding its middle, when that is built.
        var box = Rect.MinMaxRect(WorldMin.x - 40f, WorldMin.y - 40f, WorldMax.x + 40f, WorldMax.y + 40f);
        foreach (var b in t.L.bridges)
        {
            var at = t.Scene(b.x, b.y);
            if (!box.Contains(at)) continue;
            var road = t.L.roads.First(r => r.from == b.road[0] && r.to == b.road[1]);
            var line = new Lines(40f);
            var pts = new Vector2[road.xy.Length / 2];
            for (int k = 0; k < pts.Length; k++) pts[k] = t.Scene(road.xy[2 * k], road.xy[2 * k + 1]);
            line.Add(pts, new float[pts.Length]);
            line.Nearest(at.x, at.y, out _, out Vector2 dir);
            // From the crossing out along the road each way, until the river's water and its channel are behind.
            Vector2 End(float sign)
            {
                var p = at;
                for (int s = 0; s < 120 && t.DistRiver(p.x, p.y) < 5.6f; s++) p += dir * (sign * 0.5f);
                return p;
            }
            Vector2 from = End(-1f), to = End(1f);
            float height = Mathf.Max(t.Height(from.x, from.y), t.Height(to.x, to.y));
            t.crossings.Add((from, to, height));
            t.bridges.Add(FitBridge(from, to, height, preview, report));
            // The road runs straight onto each end of the bridge: a short stretch of road along the bridge's own line,
            // from the deck's height at its end to the road's own height a little way out, so a road that curves near the
            // river still meets the bridge head on instead of running past its side.
            Vector2 axis = (to - from).normalized;
            foreach (var (end, outward) in new[] { (from, -axis), (to, axis) })
            {
                var outer = end + outward * 8f;
                t.roads.Nearest(outer.x, outer.y, out float outerHeight, out _);
                t.roads.Add(new[] { end, outer }, new[] { height, outerHeight });
            }
        }
        report.AppendLine($"{t.bridges.Count} bridge(s) in the built box.");
    }
    // Fits the bridge model to the crossing — as long as the crossing, at its own proportions, centred on its middle at
    // the road's height — and samples the model's top surface from above into the deck heights. Where a sample falls
    // through a gap between stones it takes the heights either side of it along the crossing.
    static BridgeDeck FitBridge(Vector2 crossingFrom, Vector2 crossingTo, float crossingHeight, Scene preview, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BridgePrefabPath);
        if (prefab == null) throw new System.Exception("no bridge model at " + BridgePrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var drawn = DrawnUpClose(go);
            var b = BoundsOf(drawn, go.transform);

            Vector2 line = crossingTo - crossingFrom;
            float length = line.magnitude;
            var u = new Vector3(line.x, 0f, line.y) / length;
            var v = new Vector3(-u.z, 0f, u.x);
            var mid = (crossingFrom + crossingTo) * 0.5f;
            var deck = new BridgeDeck { scale = length / b.size.z, rotation = Quaternion.LookRotation(u) };
            deck.position = new Vector3(mid.x, crossingHeight, mid.y) - deck.rotation * new Vector3(b.center.x, 0f, b.center.z) * deck.scale;
            go.transform.SetPositionAndRotation(deck.position, deck.rotation);
            go.transform.localScale = Vector3.one * deck.scale;
            foreach (var r in drawn)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.GetComponent<MeshCollider>();
                if (mc == null) mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            Physics.SyncTransforms();

            var world = BoundsOf(drawn, null);
            deck.footprint = Rect.MinMaxRect(world.min.x, world.min.z, world.max.x, world.max.z);
            float halfWidth = b.size.x * 0.5f * deck.scale;
            int walk = Mathf.CeilToInt(halfWidth * 2f / DeckAcross) + 1;
            deck.rows = Mathf.CeilToInt(length / DeckAlong) + 1;
            deck.cols = walk + 4;
            deck.along = u * (length / (deck.rows - 1));
            deck.across = v;
            deck.start = new Vector3(crossingFrom.x, 0f, crossingFrom.y);
            deck.offset = new float[deck.rows * deck.cols];
            deck.height = new float[deck.rows * deck.cols];

            var physics = preview.GetPhysicsScene();
            float from = world.max.y + 1f, reachDown = world.size.y + 2f;
            float Probe(Vector3 p) => physics.Raycast(new Vector3(p.x, from, p.z), Vector3.down, out var h, reachDown) ? h.point.y - DeckSink : float.NaN;
            int found = 0;
            for (int a = 0; a < deck.rows; a++)
            {
                var at = deck.start + deck.along * a;
                float centre = Probe(at);
                if (float.IsNaN(centre)) { for (int c = 0; c < deck.cols; c++) deck.height[a * deck.cols + c] = float.NaN; continue; }
                found++;
                // Out from the middle to each side, a few centimetres at a time: the walkway runs on while the stone
                // stays within ParapetRise of the middle's height; the first point higher than that is the parapet.
                var edge = new float[2]; var edgeY = new float[2]; var wall = new float[2]; var wallY = new float[2];
                for (int side = 0; side < 2; side++)
                {
                    float dir = side == 0 ? -1f : 1f;
                    edge[side] = 0f; edgeY[side] = centre; wall[side] = float.NaN;
                    for (float d = ParapetProbe; d <= halfWidth; d += ParapetProbe)
                    {
                        float y = Probe(at + v * (dir * d));
                        // Past the stone's own edge (a gap wider than StoneGap) the walkway has ended.
                        if (float.IsNaN(y)) { if (d - edge[side] > StoneGap) break; continue; }
                        if (y > centre + ParapetRise)
                        {
                            // Well inside the parapet and below its top, so its rounded inner edge still hides the deck.
                            float into = Mathf.Min(d + ParapetInto, halfWidth);
                            float top = Probe(at + v * (dir * into));
                            wall[side] = dir * into;
                            wallY[side] = Mathf.Max(edgeY[side], (float.IsNaN(top) ? y : top) - ParapetBelow);
                            break;
                        }
                        edge[side] = d; edgeY[side] = y;
                    }
                    if (float.IsNaN(wall[side])) { wall[side] = dir * edge[side]; wallY[side] = edgeY[side]; }
                }
                int row = a * deck.cols;
                deck.offset[row] = wall[0]; deck.height[row] = wallY[0];
                deck.offset[row + 1] = wall[0]; deck.height[row + 1] = edgeY[0];
                float lastY = edgeY[0];
                for (int k = 0; k < walk; k++)
                {
                    float off = Mathf.Lerp(-edge[0], edge[1], k / (float)(walk - 1));
                    float y = Probe(at + v * off);
                    if (float.IsNaN(y)) y = lastY;
                    deck.offset[row + 2 + k] = off; deck.height[row + 2 + k] = y;
                    lastY = y;
                }
                deck.offset[row + walk + 2] = wall[1]; deck.height[row + walk + 2] = edgeY[1];
                deck.offset[row + walk + 3] = wall[1]; deck.height[row + walk + 3] = wallY[1];
            }
            // A row whose middle falls through a gap between stones takes the rows either side of it.
            for (int a = 0; a < deck.rows; a++)
            {
                if (deck.Has(a, 0)) continue;
                int lo = a - 1, hi = a + 1;
                while (lo >= 0 && !deck.Has(lo, 0)) lo--;
                while (hi < deck.rows && !deck.Has(hi, 0)) hi++;
                if (lo < 0 && hi >= deck.rows) continue;
                int from0 = lo >= 0 ? lo : hi, to0 = hi < deck.rows ? hi : lo;
                float w = from0 == to0 ? 0f : (a - from0) / (float)(to0 - from0);
                for (int c = 0; c < deck.cols; c++)
                {
                    deck.offset[a * deck.cols + c] = Mathf.Lerp(deck.offset[from0 * deck.cols + c], deck.offset[to0 * deck.cols + c], w);
                    deck.height[a * deck.cols + c] = Mathf.Lerp(deck.height[from0 * deck.cols + c], deck.height[to0 * deck.cols + c], w);
                }
            }
            report.AppendLine($"Bridge: {prefab.name} at {deck.scale:0.00}x, {world.size.x:0.0} x {world.size.z:0.0} m, its deck found under {found} of {deck.rows} rows.");
            return deck;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // The renderers drawn up close: level 0 of every switch, or all of them when the model does not switch.
    static List<Renderer> DrawnUpClose(GameObject go)
    {
        var drawn = new List<Renderer>();
        var groups = go.GetComponentsInChildren<LODGroup>(true);
        foreach (var g in groups)
        {
            var lods = g.GetLODs();
            if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r != null) drawn.Add(r);
        }
        if (groups.Length == 0) drawn.AddRange(go.GetComponentsInChildren<Renderer>(true));
        return drawn;
    }

    // Their bounds in world space, or in the space of `local` (its rotation and scale undone) when given.
    static Bounds BoundsOf(List<Renderer> renderers, Transform local)
    {
        var b = new Bounds();
        bool any = false;
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            foreach (var corner in Corners(mf.sharedMesh.bounds))
            {
                var p = mf.transform.TransformPoint(corner);
                if (local != null) p = local.InverseTransformPoint(p);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    // The bridge model, drawn only (its colliders taken off — the deck in the ground is what is walked on), in the
    // chunk holding its middle. Returns how far it reaches past that chunk's edges.
    static float PlaceBridge(BridgeDeck deck, List<ChunkOut> chunks, Scene scene)
    {
        var chunk = chunks.Find(c => c.rect.Contains(deck.footprint.center));
        if (chunk == null) return 0f;   // its middle's square is not built yet
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BridgePrefabPath), scene);
        go.transform.SetParent(chunk.props, false);
        go.transform.SetPositionAndRotation(deck.position, deck.rotation);
        go.transform.localScale = Vector3.one * deck.scale;
        WorldBuildUtil.SetLayerRecursive(go, 0);
        PropPlacement.StripColliders(go);
        return PastChunk(deck.footprint, chunk.rect);
    }

    static float PastChunk(Rect r, Rect chunk) => Mathf.Max(0f, chunk.xMin - r.xMin, r.xMax - chunk.xMax, chunk.yMin - r.yMin, r.yMax - chunk.yMax);

    // The deck: the bridge model's top surface, sampled, as part of the ground mesh it joins at both ends. Painted road,
    // so where it shows between the model's stones it reads as the road running on; its own normals from its faces.
    static void AddDeck(BridgeDeck d, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> c, List<Vector2> rk, List<int> tri)
    {
        var roadCol = new Color(1f, 0f, 0f, 0f);
        int first = v.Count;
        var index = new int[d.rows * d.cols];
        for (int a = 0; a < d.rows; a++)
            for (int k = 0; k < d.cols; k++)
            {
                if (!d.Has(a, k)) { index[a * d.cols + k] = -1; continue; }
                index[a * d.cols + k] = v.Count;
                var p = d.Point(a, k);
                v.Add(p);
                n.Add(Vector3.up);
                uv.Add(new Vector2(p.x, p.z));
                c.Add(roadCol);
                rk.Add(new Vector2(0f, 0.5f));
            }
        // Wound so the faces look up whichever way the crossing runs.
        bool flip = Vector3.Cross(d.along, d.across).y < 0f;
        for (int a = 0; a + 1 < d.rows; a++)
            for (int k = 0; k + 1 < d.cols; k++)
            {
                int v0 = index[a * d.cols + k], v1 = index[a * d.cols + k + 1];
                int v2 = index[(a + 1) * d.cols + k], v3 = index[(a + 1) * d.cols + k + 1];
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) continue;
                if (flip) { tri.Add(v0); tri.Add(v1); tri.Add(v2); tri.Add(v1); tri.Add(v3); tri.Add(v2); }
                else { tri.Add(v0); tri.Add(v2); tri.Add(v1); tri.Add(v1); tri.Add(v2); tri.Add(v3); }
            }
        var acc = new Vector3[v.Count - first];
        for (int q = 0; q < tri.Count; q += 3)
        {
            int a = tri[q], bb = tri[q + 1], cc = tri[q + 2];
            if (a < first) continue;
            var fn = Vector3.Cross(v[bb] - v[a], v[cc] - v[a]);
            acc[a - first] += fn; acc[bb - first] += fn; acc[cc - first] += fn;
        }
        for (int q = 0; q < acc.Length; q++) n[first + q] = acc[q].sqrMagnitude > 0f ? acc[q].normalized : Vector3.up;
    }

    // The water surfaces in this square: one continuous sheet per body of water (the river, each pond), each point at its
    // water's own height — the river's falling with it — reaching in under the banks so its own edge is always hidden in
    // the ground; the water's visible edge is wherever the bank rises out of it, faded by the water shader. Each carries
    // its flow (the river downstream, a pond still) and tells the game where it lies (WaterSheet, at its highest point).
    // Drawn only — not ground, no collider.
    static void AddWater(Terrain t, ChunkOut c, Material river, Material pond)
    {
        AddSheet(t, c, river, 0, "River water");
        for (int p = 0; p < t.ponds.Count; p++) AddSheet(t, c, pond, 1 + p, "Pond water " + t.ponds[p].p.id);
    }

    static void AddSheet(Terrain t, ChunkOut c, Material mat, int body, string name)
    {
        const float cell = 1f;
        int nx = Mathf.RoundToInt(c.rect.width / cell), nz = Mathf.RoundToInt(c.rect.height / cell);
        var index = new int[(nx + 1) * (nz + 1)];
        var verts = new List<Vector3>(); var flows = new List<Vector2>(); var tris = new List<int>();
        bool Covers(float x, float z) { t.WaterAt(x, z, out int b); return b == body; }
        for (int k = 0; k < index.Length; k++) index[k] = -1;
        Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
        float top = float.MinValue;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x0 = c.rect.xMin + i * cell, z0 = c.rect.yMin + j * cell;
                if (!Covers(x0 + cell * 0.5f, z0 + cell * 0.5f)) continue;
                int Corner(int ci, int cj)
                {
                    int key = cj * (nx + 1) + ci;
                    if (index[key] >= 0) return index[key];
                    float x = c.rect.xMin + ci * cell, z = c.rect.yMin + cj * cell;
                    // A corner just outside the water takes the height of the water beside it.
                    float level = t.WaterAt(x, z, out int b);
                    if (b != body) level = t.WaterAt(x0 + cell * 0.5f, z0 + cell * 0.5f, out _);
                    index[key] = verts.Count;
                    verts.Add(new Vector3(x, level, z));
                    top = Mathf.Max(top, level);
                    flows.Add(t.FlowAt(x, z));
                    lo = Vector2.Min(lo, new Vector2(x, z)); hi = Vector2.Max(hi, new Vector2(x, z));
                    return index[key];
                }
                int a = Corner(i, j), bb = Corner(i, j + 1), cc = Corner(i + 1, j + 1), d = Corner(i + 1, j);
                tris.Add(a); tris.Add(bb); tris.Add(cc);
                tris.Add(a); tris.Add(cc); tris.Add(d);
            }
        if (tris.Count == 0) return;
        var mesh = new Mesh { name = $"world_{c.cx}_{c.cz}_{name.Replace(' ', '_').ToLowerInvariant()}" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, flows);
        var normals = new Vector3[verts.Count];
        for (int k = 0; k < normals.Length; k++) normals[k] = Vector3.up;
        mesh.normals = normals;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh = SaveMesh(mesh);
        var go = new GameObject(name);
        go.transform.SetParent(c.root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        var sheet = go.AddComponent<WaterSheet>();
        sheet.level = top;
        sheet.min = lo;
        sheet.max = hi;
    }
    // Trees, rocks, flowers and grass, each standing on the ground by its pivot, kept off the roads, the water, Lastline
    // and the landmarks' spots, and stored in the square that holds its base. Trees grow in groves, flowers and grass in
    // clumps, a few lone trees between; broken rock gathers at the foot of the cliffs. Each is rolled by its own square of
    // land (PropLayers), and whether it stands is decided by its neighbours' rolls only, never by what was placed first —
    // so a square comes out the same whichever squares are built and in whatever order. Returns how far the widest of
    // them reaches past its own square, which the world's load ring adds.
    const string NatureDir = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Prefabs/";
    static readonly Dictionary<PropCategory, string[]> NatureProps = new Dictionary<PropCategory, string[]>
    {
        { PropCategory.Rocks, new[] { "Rock_Small_01", "Rock_Small_02", "Rock_Small_03", "Rock_Medium_01", "Rock_Medium_02", "Rock_Medium_03", "Rock_Big_01", "Rock_Big_02", "Rock_Big_03" } },
        { PropCategory.Grass, new[] { "Grass_01", "Grass_02", "Grass_03" } },
    };

    static string[] Trees(string kind, string colour) => new[] { 1, 2, 3, 4, 5 }.Select(i => $"{kind}_0{i}{colour}").ToArray();

    /// <summary>A TOWN'S THEME (decided with the user 2026-10-27, by each weapon's style where it fits): its trees, its
    /// single flowers, and the plants of its meadow carpets — nature-pack names. Only single-colour meadows: the pack's
    /// mixed-colour meadow plants read as dots of every colour and are not used (the user, 2026-10-27). A town whose look the pack cannot give
    /// keeps its biome's own props (<see cref="KeptBiomes"/>): its trees and flowers are then asset paths from that biome's
    /// row of the Biome Table, and it has no meadow carpet.</summary>
    sealed class Theme
    {
        public string[] trees, flowers, carpet;
    }

    // The towns that keep their biome's old props (decided with the user 2026-10-27): the desert, the beach, and the
    // dark isle's volcano and graves.
    static readonly Dictionary<string, Biome> KeptBiomes = new Dictionary<string, Biome>
    {
        { "hazemere", Biome.Desert }, { "hilt", Biome.Coast }, { "cindrel", Biome.Volcanic }, { "kharnel", Biome.Haunted },
    };

    static readonly Dictionary<string, Theme> Themes = new Dictionary<string, Theme>
    {
        { "lastline", new Theme { trees = Trees("BroadleafTree", "_Green").Concat(Trees("WillowTree", "_Green")).ToArray(),
                                  flowers = new[] { "Flower_Blue_01", "Flower_Orange", "Flower_Pink", "Flower_Purple", "Flower_Red", "Flower_White", "Flower_Yellow" },
                                  carpet = new[] { "FlowerMeadow_Red", "FlowerMeadow_Blue", "FlowerMeadow_Orange", "FlowerMeadow_Pink", "FlowerMeadow_Purple" } } },
        { "old_oak", new Theme { trees = Trees("BroadleafTree", "_Green"), flowers = new[] { "Flower_White", "Flower_Yellow" },
                                 carpet = new[] { "FlowerMeadow_White" } } },
        { "parley", new Theme { trees = Trees("BlossomTree", "").Concat(Trees("WillowTree", "_Pink")).ToArray(), flowers = new[] { "Flower_Pink" },
                                carpet = new[] { "FlowerMeadow_Pink" } } },
        { "harrowen", new Theme { trees = Trees("BroadleafTree", "_Green"), flowers = new[] { "Flower_Orange" },
                                  carpet = new[] { "FlowerMeadow_Orange" } } },
        { "pennaryl", new Theme { trees = Trees("BroadleafTree", "_Green").Concat(Trees("Fir", "")).ToArray(), flowers = new[] { "Flower_Blue_01", "Flower_Blue_02" },
                                  carpet = new[] { "Grass_01", "Grass_02", "Grass_03", "Plants" } } },
        { "gemmara", new Theme { trees = Trees("Fir", ""), flowers = new[] { "Flower_Blue_01", "Flower_Blue_02" }, carpet = new[] { "FlowerMeadow_Blue" } } },
        { "tornel", new Theme { trees = Trees("BroadleafTree", "_Red").Concat(Trees("WillowTree", "_Red")).ToArray(), flowers = new[] { "Flower_YellowRed" },
                                carpet = new[] { "FlowerMeadow_Red" } } },
        { "velmora", new Theme { trees = Trees("BroadleafTree", "_Purple").Concat(Trees("WillowTree", "_Purple")).ToArray(), flowers = new[] { "Flower_Purple" },
                                 carpet = new[] { "FlowerMeadow_Purple" } } },
        { "pluvenne", new Theme { trees = Trees("WillowTree", "_Blue"), flowers = new[] { "Flower_White" }, carpet = new[] { "Grass_01", "Grass_02", "Grass_03" } } },
        { "luminhal", new Theme { trees = Trees("BroadleafTree", "_Blue"), flowers = new[] { "Flower_Blue_01" },
                                  carpet = new[] { "FlowerMeadow_Blue" } } },
        { "halcyra", new Theme { trees = Trees("WillowTree", "_Blue"), flowers = new[] { "Flower_White" }, carpet = new[] { "FlowerMeadow_White" } } },
    };

    static Theme ThemeOf(Terrain t, int index)
    {
        if (index < 0) return null;
        string town = t.townIds[index];
        if (Themes.TryGetValue(town, out var th)) return th;
        if (!KeptBiomes.TryGetValue(town, out var biome)) throw new System.Exception($"{town} has no theme and no kept biome");
        var row = BiomeTable.Load()?.For(biome);
        string[] Paths(PropCategory cat) => row != null && row.TryGetProps(cat, out var list) && list != null
            ? list.Where(p => p != null).Select(AssetDatabase.GetAssetPath).ToArray() : new string[0];
        return Themes[town] = new Theme { trees = Paths(PropCategory.Trees), flowers = Paths(PropCategory.Flowers), carpet = null };
    }

    static readonly string[] NatureBushes = { "Bush_01_01", "Bush_01_02", "Bush_02_01", "Bush_02_02", "Bush_03_01", "Bush_03_02" };
    static readonly string[] NaturePlants = { "Plant_01", "Plant_02", "Plant_03", "Plant_04", "Plant_05", "Plant_06", "Plant_07", "Plant_08", "Plants" };
    static readonly string[] NatureBranches = { "Branch_01", "Branch_02", "Branch_03", "Branch_04", "Branch_05", "Branch_06" };
    static readonly string[] NatureStones = { "Stone_Big_01", "Stone_Big_02", "Stone_Big_03", "Stone_Medium_01", "Stone_Medium_02", "Stone_Medium_03", "Stones_01", "Stones_02", "Stones_03" };
    // Every flower's height varies this much either way on top of its size (the user, 2026-10-27: "random height, for a
    // small height range, so they dont look uniform") — the carpets' flowers and the single flowers alike.
    const float FlowerHeightJitter = 0.15f;
    // The share of the carpets' flowers left out, so a meadow is less packed than the demo ground grows it (the user,
    // 2026-10-27: "reduce the density of the flowers"; halved again the same day for the frame rate — MEASURED standing
    // in a meadow at half the demo's count: about 1.7 million carpet triangles in view, 11.6-17 ms a frame on the
    // graphics card against 2.9 ms with the carpets off). Grass and plants keep the demo's own count.
    const float FlowerThinOut = 0.75f;
    // Every flower stands this much taller again than the pack's size for it (1 = twice as tall; the user, 2026-10-27:
    // "increase the flower heights by x2"), the carpets' flowers and the single flowers alike. Widths are unchanged.
    const float FlowerExtraHeight = 1f;
    // A meadow's cards are drawn with this copy of the pack's meadow material, whose shader turns each card about its
    // upright axis so one of its four crossed pictures always faces the camera (the user, 2026-10-28): seen edge-on from
    // the top-down camera, a card's colour averaged to peach.
    const string MeadowFacingMaterialPath = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Materials/Flowers/FlowerMeadowFacing.mat";

    // THE FIELDS' FILL (the user, 2026-10-27, after a one-square trial): every field filled the way the pack's own store
    // pictures are — grass over all its open ground, and trees, firs, bushes, plants and rocks all through it, not only
    // in groves — as thick as the pack's demo island (counts below). Only where the land wears a nature-pack theme: a
    // town that keeps its biome's own props (KeptBiomes) keeps its own look.
    static readonly string[] NatureFirs = { "Fir_01", "Fir_02", "Fir_03", "Fir_04", "Fir_05" };
    static bool NatureTheme(Theme th) => th != null && th.trees != null && th.trees.Length > 0 && !th.trees[0].StartsWith("Assets/");
    // The pack's demo island, counted 2026-10-27 from its scene: a 300 x 300 ground holding 235 broadleaf, willow and
    // blossom trees, 70 firs, 464 bushes and 380 rocks (small, medium and big).
    const float DemoIslandSide = 300f;
    const int DemoTrees = 235, DemoFirs = 70, DemoBushes = 464, DemoRocks = 380;
    // The fields' grass: this share of the demo's left out (0.75 = a quarter of the demo's grass).
    const float FieldGrassThinOut = 0.75f;
    static readonly string[] NatureCliffs = { "Cliff_01", "Cliff_02", "Cliff_03", "Cliff_04", "Cliff_05" };
    // The pack's two tall cliff rocks (1.9 and 3.1 units at world scale), which line the walls; the rest lie at their feet.
    static readonly string[] CliffFaceRocks = { "Cliff_01", "Cliff_02" };
    // Each face rock's height and depth at the world's scale, read from its meshes (yawed any way, the depth is its
    // smaller footprint side so it never stands proud of the wall by more than half its width).
    static readonly Dictionary<string, (float tall, float depth, float lift)> faceRockSizes = new Dictionary<string, (float, float, float)>();
    static (float tall, float depth, float lift) FaceRockSize(string model)
    {
        if (faceRockSizes.TryGetValue(model, out var size)) return size;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + model + ".prefab");
        var drawn = new List<Renderer>(prefab.GetComponentsInChildren<Renderer>());
        var b = BoundsOf(drawn, prefab.transform);
        float scale = NatureScale(new StringBuilder());
        return faceRockSizes[model] = (b.size.y * scale, Mathf.Min(b.size.x, b.size.z) * scale, -b.min.y * scale);   // lift: pivot to the rock's underside
    }
    static float FaceRockHeight(string model) => FaceRockSize(model).tall;
    static float FaceRockDepth(string model) => FaceRockSize(model).depth;
    static readonly string[] NatureBankPlants = { "Reeds_01", "Reeds_02", "Reeds_03", "Cattail_01", "Cattail_02", "Cattail_03" };
    static readonly string[] NatureFloating = { "LilyPads_01", "LilyPads_02", "LilyPads_03", "Waterlily_01", "Waterlily_02" };
    static readonly string[] NatureEffects = { "GodRays", "FloatingLeafs_Green", "FloatingLeafs_Yellow", "Particles" };
    static readonly string[] NatureButterflies = { "ButterflySpawnArea" };
    // EVERY PIECE AT THE WORLD'S SCALE. The pack is built around its own person — its demo's walking body,
    // NaturePersonPath's height — and this world around ours (CharacterSpec.Active.bodyHeight); everything from the pack
    // is scaled by the one ratio between them, so a tree, a blade of grass and a floating leaf keep the sizes they have
    // against a person in the pack. Both heights are read, never typed.
    const string NaturePersonPath = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Prefabs/Code Related/Controller.prefab";

    static float NatureScale(StringBuilder report)
    {
        var person = AssetDatabase.LoadAssetAtPath<GameObject>(NaturePersonPath);
        var body = person != null ? person.GetComponentInChildren<CharacterController>(true) : null;
        if (body == null) throw new System.Exception("no walking body in " + NaturePersonPath + " to read the pack's person from");
        float theirs = body.height * body.transform.lossyScale.y, ours = CharacterSpec.Active.bodyHeight;
        float scale = ours / theirs;
        report.AppendLine($"Nature at the world's scale: their person {theirs:0.###} m, ours {ours:0.###} m, so x{scale:0.###}.");
        return scale;
    }

    // THE CARPETS: meadow patches, each a disc of its town's carpet plants, spread the way the pack's own demo ground
    // spreads them. The demo keeps its plants in square spots; inside its painted patches some spots are empty and the rest
    // hold one, two, up to eight — read here, for each theme, from the demo's ground of the same plants. Where the patches
    // go is rolled by squares of PatchGrid units from their place alone (StableRandom), so a patch is the same whichever
    // squares are built. A disc that crosses a square's edge gives each square it crosses a FlowerCarpet patch of its own
    // part, with the ground's heights; roads, water and steep ground get none. Wall-to-wall carpet cannot be drawn (a
    // square would hold about 740,000 plants), so about one patch is in view at a time (decided with the user 2026-10-27).
    const string NatureDemoGroundPath = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Demo/Settings/Land_Terrain.asset";
    const float PatchGrid = 40f, PatchChance = 0.55f, PatchRadius = 11f;
    // The number of spots in the biggest connected patch of `mask` (spots touching side or corner).
    static int LargestPatch(bool[] mask, int w, int h)
    {
        var seen = new bool[mask.Length];
        var queue = new Queue<int>();
        int best = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || seen[i]) continue;
            int n = 0; seen[i] = true; queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue(); n++;
                int x = c % w, y = c / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int k = ny * w + nx;
                        if (mask[k] && !seen[k]) { seen[k] = true; queue.Enqueue(k); }
                    }
            }
            best = Mathf.Max(best, n);
        }
        return best;
    }

    static void LayCarpets(Terrain t, List<ChunkOut> chunks, StringBuilder report)
    {
        var demo = AssetDatabase.LoadAssetAtPath<TerrainData>(NatureDemoGroundPath);
        if (demo == null) throw new System.Exception("no demo ground at " + NatureDemoGroundPath + " to read the carpet's thickness from");
        // The demo's plants per spot, all its plants together, and which spots lie INSIDE each plant's painted patches
        // (at least 6 of the 9 spots around it, itself included, carry that plant), so the bare ground around a patch is
        // never counted as its gaps.
        int w = demo.detailWidth, h = demo.detailHeight, kindsInDemo = demo.detailPrototypes.Length;
        var total = new int[w * h];
        var inside = new bool[kindsInDemo][];
        var own = new long[kindsInDemo];        // this plant's count over the spots inside its patches
        var widest = new float[kindsInDemo];    // the side of its biggest patch, in demo metres
        for (int l = 0; l < kindsInDemo; l++)
        {
            var layer = demo.GetDetailLayer(0, 0, w, h, l);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) total[y * w + x] += layer[y, x];
            var mask = new bool[w * h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int around = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (layer[y + dy, x + dx] > 0) around++;
                    mask[y * w + x] = around >= 6;
                    if (mask[y * w + x]) own[l] += layer[y, x];
                }
            inside[l] = mask;
            widest[l] = Mathf.Sqrt(LargestPatch(mask, w, h)) * (demo.size.x / w);
        }
        // The demo plants whose ground a carpet plant stands for: the plant itself if the demo grows it, else the demo's
        // plants of its family (the name before the first '_': FlowerMeadow, Grass, Flower, Plant).
        List<int> DemoKindsFor(string name)
        {
            var same = new List<int>();
            string family = name.Split('_')[0];
            for (int pass = 0; pass < 2 && same.Count == 0; pass++)
                for (int l = 0; l < kindsInDemo; l++)
                {
                    var proto = demo.detailPrototypes[l].prototype;
                    if (proto == null || System.Array.IndexOf(inside[l], true) < 0) continue;
                    if (pass == 0 ? proto.name == name : proto.name.Split('_')[0] == family) same.Add(l);
                }
            return same;
        }
        float scale = NatureScale(report);
        float spot = demo.size.x / w * scale;
        long laid = 0; int discs = 0;
        var spread = new StringBuilder();
        var sizes = new Dictionary<string, (float mid, float jitter)>();
        foreach (var d in demo.detailPrototypes)
        {
            if (d.prototype == null) continue;
            // The demo ground draws each plant between its min and max width and height: the middle, and how far
            // either way it varies.
            float lo = (d.minWidth + d.minHeight) * 0.5f, hi = (d.maxWidth + d.maxHeight) * 0.5f;
            sizes[d.prototype.name] = ((lo + hi) * 0.5f, (hi - lo) / Mathf.Max(0.001f, lo + hi));
        }
        // Each theme's spread, worked out once.
        var spreads = new Dictionary<Theme, (float[] chances, float patchWidth, float perSpot)>();
        (float[] chances, float patchWidth, float perSpot) SpreadOf(string id, Theme th)
        {
            if (spreads.TryGetValue(th, out var s)) return s;
            // The chance a spot of this theme's ground holds 0, 1, 2 … plants: the demo's own spots inside the patches of
            // the plants this carpet stands for.
            var demoKinds = th.carpet.SelectMany(DemoKindsFor).Distinct().ToList();
            if (demoKinds.Count == 0) throw new System.Exception("the demo ground grows nothing like " + string.Join(", ", th.carpet) + " to read the " + id + " carpet's spread from");
            var held = new List<long>();
            for (int i = 0; i < w * h; i++)
            {
                bool any = false;
                foreach (var l in demoKinds) if (inside[l][i]) { any = true; break; }
                if (!any) continue;
                while (held.Count <= total[i]) held.Add(0);
                held[total[i]]++;
            }
            long spots = 0, plants = 0;
            for (int i = 0; i < held.Count; i++) { spots += held[i]; plants += held[i] * i; }
            var chances = new float[held.Count];
            long upTo = 0;
            for (int i = 0; i < held.Count; i++) { upTo += held[i]; chances[i] = (float)((double)upTo / spots); }
            chances[chances.Length - 1] = 1f;
            float perSpot = (float)plants / spots;
            // The demo grows these plants either one by one mixed (its grasses share every spot) or each in a patch of
            // its own (a meadow spot holds only its own colour): mixed if the plants' own counts fall short of the
            // spots' totals. Patches are as wide as the demo's.
            long ownOfThese = demoKinds.Sum(l => own[l]);
            bool patches = th.carpet.Length > 1 && ownOfThese >= plants * 0.95;
            float patchWidth = patches ? demoKinds.Average(l => widest[l]) * scale : 0f;
            spread.Append($" {id} {perSpot:0.00} a spot ({100.0 * held[0] / spots:0}% empty);");
            return spreads[th] = (chances, patchWidth, perSpot);
        }

        // Where the patches stand: one roll per PatchGrid square. Every disc touching a built square is laid in the squares it
        // touches, whether or not its other squares are built, so a square's carpet never depends on its neighbours.
        var built = Rect.MinMaxRect(WorldMin.x, WorldMin.y, WorldMax.x, WorldMax.y);
        uint where = StableRandom.Seed("carpet patches");
        // The middles of the meadow patches (flowers only): the fields' grass leaves them alone, so a meadow is flowers and
        // nothing under them (the user, 2026-10-28).
        var meadowCentres = new List<Vector2>();
        for (int gj = Mathf.FloorToInt((built.yMin - PatchRadius) / PatchGrid); gj <= Mathf.FloorToInt((built.yMax + PatchRadius) / PatchGrid); gj++)
            for (int gi = Mathf.FloorToInt((built.xMin - PatchRadius) / PatchGrid); gi <= Mathf.FloorToInt((built.xMax + PatchRadius) / PatchGrid); gi++)
            {
                uint sq = StableRandom.Square(where, gi, gj);
                if (StableRandom.Value(sq, 0) >= PatchChance) continue;
                var centre = new Vector2((gi + 0.15f + StableRandom.Value(sq, 1) * 0.7f) * PatchGrid, (gj + 0.15f + StableRandom.Value(sq, 2) * 0.7f) * PatchGrid);
                var disc = Rect.MinMaxRect(centre.x - PatchRadius, centre.y - PatchRadius, centre.x + PatchRadius, centre.y + PatchRadius);
                if (!disc.Overlaps(built)) continue;
                int themeIndex = t.ThemeAt(centre.x, centre.y);
                if (themeIndex < 0) continue;
                var th = ThemeOf(t, themeIndex);
                if (th.carpet == null || th.carpet.Length == 0) continue;
                var (chances, patchWidth, perSpot) = SpreadOf(t.townIds[themeIndex], th);
                // One seed for the whole of this patch, from its square: every square it crosses rolls the same spots the
                // same way, so it runs on across their edges.
                int seed = unchecked((int)StableRandom.Square(where, gi, gj + 100000));
                discs++;
                // A meadow patch is ONE colour (the user, 2026-10-27), picked by the patch's own roll; grass and plants
                // still mix plant by plant, as the demo grows them.
                bool meadow = th.carpet.All(n => n.StartsWith("FlowerMeadow_"));
                if (meadow) meadowCentres.Add(centre);
                int only = meadow ? Mathf.Min(th.carpet.Length - 1, (int)(StableRandom.Value(sq, 3) * th.carpet.Length)) : -1;
                for (int kind = 0; kind < th.carpet.Length; kind++)
                {
                    if (meadow && kind != only) continue;
                    var name = th.carpet[kind];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + name + ".prefab");
                    var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
                    var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                    if (mr == null) throw new System.Exception("no plant mesh in " + name);
                    var mat = meadow ? AssetDatabase.LoadAssetAtPath<Material>(MeadowFacingMaterialPath) : mr.sharedMaterial;
                    if (mat == null) throw new System.Exception(MeadowFacingMaterialPath + " is missing");
                    if (!mat.enableInstancing) { mat.enableInstancing = true; EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
                    LayPiece(disc, centre, PatchRadius, name, mf, mat, chances, meadow ? 1 : th.carpet.Length, meadow ? 0 : kind,
                             meadow ? 0f : patchWidth, seed, perSpot / (meadow ? 1 : th.carpet.Length));
                }
            }

        // THE FIELDS' GRASS (the user, 2026-10-27): the pack's three grasses, mixed plant by plant as the demo grows them,
        // over every bit of open ground in every built square that wears a nature-pack theme — one piece per square, all
        // from one seed, so it runs on across the squares' edges.
        {
            var grass = new Theme { carpet = NatureProps[PropCategory.Grass] };
            var (gChances, gWidth, gPerSpot) = SpreadOf("field grass", grass);
            int gSeed = unchecked((int)StableRandom.Seed("field grass"));
            for (int kind = 0; kind < grass.carpet.Length; kind++)
            {
                var name = grass.carpet[kind];
                var mf = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + name + ".prefab").GetComponentInChildren<MeshFilter>(true);
                var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
                if (!mat.enableInstancing) { mat.enableInstancing = true; EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
                foreach (var c in chunks)
                    LayPiece(c.rect, c.rect.center, c.rect.size.magnitude * 0.5f + 0.1f, name, mf, mat, gChances, grass.carpet.Length, kind, gWidth,
                             gSeed, gPerSpot / grass.carpet.Length, FieldGrassThinOut, natureOnly: true, skipMeadows: true);
            }
            report.AppendLine($"Fields' grass: over every square's open ground, {gPerSpot:0.00} plants a spot at the demo's count, a quarter of it drawn.");
        }

        // One patch's piece in every built square it crosses: the plants on its open ground (no road, water, steep
        // ground or town — and, for natureOnly, no land whose theme keeps its biome's own props), at the demo's sizes.
        void LayPiece(Rect disc, Vector2 centre, float radius, string name, MeshFilter mf, Material mat, float[] chances, int kinds, int kind,
                      float patchWidth, int seed, float perSpotOfThis, float thin = -1f, bool natureOnly = false, bool skipMeadows = false)
        {
            foreach (var c in chunks)
            {
                if (!c.rect.Overlaps(disc)) continue;
                var clip = Rect.MinMaxRect(Mathf.Max(c.rect.xMin, disc.xMin), Mathf.Max(c.rect.yMin, disc.yMin), Mathf.Min(c.rect.xMax, disc.xMax), Mathf.Min(c.rect.yMax, disc.yMax));
                bool flower = name.StartsWith("Flower");
                float leftOut = thin >= 0f ? thin : flower ? FlowerThinOut : 0f;
                var patch = new FlowerCarpet.Patch
                {
                    mesh = mf.sharedMesh, material = mat, centre = centre, radius = radius, clip = clip,
                    spot = spot, chances = chances, kinds = kinds, kind = kind, patchWidth = patchWidth,
                    scale = scale * (sizes.TryGetValue(name, out var sz) ? sz.mid : 1f), scaleJitter = sizes.TryGetValue(name, out var sj) ? sj.jitter : 0.2f,
                    heightJitter = flower ? FlowerHeightJitter : 0f,
                    extraHeight = flower ? FlowerExtraHeight : 0f,
                    thinOut = leftOut,
                    seed = seed, gridMin = clip.min, gridStep = Step,
                    gridW = Mathf.CeilToInt(clip.width / Step) + 2, gridH = Mathf.CeilToInt(clip.height / Step) + 2,
                };
                patch.heights = new float[patch.gridW * patch.gridH];
                for (int j = 0; j < patch.gridH; j++)
                    for (int i = 0; i < patch.gridW; i++)
                    {
                        float x = clip.xMin + i * Step, z = clip.yMin + j * Step, y = t.Height(x, z);
                        float water = t.WaterAt(x, z, out _);
                        bool bare = t.DistRoads(x, z) < Terrain.RoadHalf + 0.2f || (!float.IsNaN(water) && y < water + 0.05f)
                                    || t.Normal(x, z).y < 0.8f || Town.Contains(new Vector2(x, z));
                        if (!bare && natureOnly)
                        {
                            int theme = t.ThemeAt(x, z);
                            bare = theme < 0 || !NatureTheme(ThemeOf(t, theme));
                        }
                        if (!bare && skipMeadows)
                            foreach (var m in meadowCentres)
                                if ((new Vector2(x, z) - m).sqrMagnitude < PatchRadius * PatchRadius) { bare = true; break; }
                        patch.heights[j * patch.gridW + i] = bare ? float.NaN : y;
                    }
                var carpet = c.root.GetComponent<FlowerCarpet>();
                if (carpet == null) carpet = c.root.AddComponent<FlowerCarpet>();
                carpet.patches.Add(patch);
                float area = radius >= clip.size.magnitude * 0.5f ? clip.width * clip.height : clip.width * clip.height * Mathf.PI / 4f;
                laid += Mathf.RoundToInt(area / (spot * spot) * perSpotOfThis * (1f - leftOut));
            }
        }
        report.AppendLine($"Carpets: {discs} meadow patches {PatchRadius * 2f:0} units across, in spots {spot:0.0000} units wide, spread as the demo ground's:{spread} — about {laid} plants.");
    }

    // THE WIND AS THE PACK AUTHORED IT, THE BEND AT THE WORLD'S SCALE. The pack's demo sets the wind and the bend from
    // two controllers in its scene, each with a list of the materials it drives: those values are read from that scene
    // and written to the same materials. The WIND is written as the demo has it, unscaled: it is weather, not a thing
    // standing in the world, and shrinking it with the plants (tried 2026-10-26) made each gust a third of a person wide
    // and moved a plant about 4 pixels at the game camera, so the rolling waves could no longer be seen. The BEND is
    // the player pushing plants aside, measured from the player's body, so its range and strength follow the world's scale.
    const string NatureDemoScenePath = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Demo/Demo.unity";

    static void ApplyNatureWind(float scale, StringBuilder report)
    {
        var scene = File.ReadAllText(NatureDemoScenePath);
        float Value(string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(scene, @"(propertyPath: " + key + @"\s*\n\s*value: |\n  " + key + @": )([-0-9.eE]+)");
            if (!m.Success) throw new System.Exception("the demo scene sets no " + key);
            return float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        // The guids in the block of the scene that holds `key`: the wind controller's overrides, or the bend controller.
        List<string> Materials(string key)
        {
            int at = scene.IndexOf(key, System.StringComparison.Ordinal);
            int start = scene.LastIndexOf("\n--- ", at, System.StringComparison.Ordinal), end = scene.IndexOf("\n--- ", at, System.StringComparison.Ordinal);
            var block = scene.Substring(start, (end < 0 ? scene.Length : end) - start);
            var list = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(block, @"fileID: 2100000, guid: ([0-9a-f]{32})"))
                if (!list.Contains(m.Groups[1].Value)) list.Add(m.Groups[1].Value);
            return list;
        }
        float strength = Value("_windStrength"), speed = Value("_windSpeed"), variation = Value("_windVariation"), wave = Value("_waveScale");
        float range = Value("_startBendRange"), bend = Value("_bendStrength");
        int wound = 0, bent = 0;
        foreach (var guid in Materials("_windStrength"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null || !m.HasProperty("_Wind_Strength")) continue;
            m.SetFloat("_Wind_Strength", strength); m.SetFloat("_Wind_Speed", speed);
            m.SetFloat("_Wind_Variation", variation); m.SetFloat("_Wave_Scale", wave);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); wound++;
        }
        foreach (var guid in Materials("_bendStrength"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null || !m.HasProperty("_Bend_Strength")) continue;
            m.SetFloat("_Start_Bend_Range", range * scale); m.SetFloat("_Bend_Strength", bend * scale);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); bent++;
        }
        report.AppendLine($"Wind as the pack authored it on {wound} plant materials (strength {strength}, gusts {wave}), bend at the world's scale on {bent}.");
    }

    static float ScatterProps(Terrain t, List<ChunkOut> chunks, Scene scene, StringBuilder report)
    {
        var table = BiomeTable.Load();
        var row = table != null ? table.For(Biome.Grassland) : null;
        if (row == null) { report.AppendLine("No Grassland row in the Biome Table — no props placed."); return 0f; }
        // THE FIELD RECIPE'S OWN PROP GROUPS decide each kind's size, how much it varies, whether it is solid or
        // walk-through, and whether it sways — the same values every generated field uses, read, never typed again here.
        var recipe = AssetDatabase.LoadAssetAtPath<MapRecipe>(FieldRecipePath);
        if (recipe == null) throw new System.Exception("no field recipe at " + FieldRecipePath);
        RecipePropGroup Group(PropCategory cat) => recipe.propGroups.Find(g => g != null && g.category == cat);
        float reach = 0f;
        int placed = 0;
        float natureScale = NatureScale(report);
        var layers = PropLayers(t);
        var perLayer = new StringBuilder();
        var built = Rect.MinMaxRect(WorldMin.x, WorldMin.y, WorldMax.x, WorldMax.y);

        // A CANDIDATE: what one square of one layer rolls — whether it holds a prop, where, which, how big, which way, and
        // its say against same-layer neighbours. Worked out from the square's place and the layer's name only, and the
        // ground's fixed functions, so it is the same whichever squares are being built.
        var known = new Dictionary<(int, int, int), (bool ok, Vector2 at, string model, float size, float yaw, float say)>();
        (bool ok, Vector2 at, string model, float size, float yaw, float say) Candidate(int l, int i, int j)
        {
            if (known.TryGetValue((l, i, j), out var c)) return c;
            var layer = layers[l];
            uint sq = StableRandom.Square(StableRandom.Seed(layer.name), i, j);
            float x = (i + StableRandom.Value(sq, 0)) * layer.spacing, z = (j + StableRandom.Value(sq, 1)) * layer.spacing;
            var at = new Vector2(x, z);
            bool ok = layer.bounds.Contains(at) && StableRandom.Value(sq, 2) < layer.chance(x, z);
            string model = null;
            if (ok)
            {
                var models = layer.models(AreaAt(t, x, z, StableRandom.Value(sq, 3), StableRandom.Value(sq, 4)));
                ok = models != null && models.Length > 0;
                if (ok) model = models[Mathf.Min(models.Length - 1, (int)(StableRandom.Value(sq, 5) * models.Length))];
            }
            if (ok && !layer.anywhere && !t.PropFree(x, z)) ok = false;
            // A cliff face's lining stands on the wall's own foot, so its spacing is kept along the wall itself.
            if (ok && layer.facing)
            {
                ok = t.WallNear(x, z, 1.4f, out Vector2 foot, out _) > 0f;
                at = foot;
            }
            if (ok) foreach (var deck in t.bridges)
                {
                    var f = deck.footprint;
                    if (Rect.MinMaxRect(f.xMin - 1f, f.yMin - 1f, f.xMax + 1f, f.yMax + 1f).Contains(at)) { ok = false; break; }
                }
            c = (ok, at, model, StableRandom.Value(sq, 6), StableRandom.Value(sq, 7), StableRandom.Value(sq, 8));
            known[(l, i, j)] = c;
            return c;
        }

        // A candidate STANDS unless a candidate of an earlier layer (a bigger kind), or one of its own layer with a bigger
        // say, is within the larger of the two keeping distances — whether or not that one stands itself. No chain runs
        // across the world, so nothing outside a square's own neighbourhood can change it.
        bool Stands(int l, int i, int j)
        {
            var me = Candidate(l, i, j);
            if (!me.ok) return false;
            for (int e = 0; e <= l; e++)
            {
                var other = layers[e];
                float keep = layers[l].ownKeep ? layers[l].keep : Mathf.Max(layers[l].keep, other.keep);
                if (!other.bounds.Overlaps(Rect.MinMaxRect(me.at.x - keep, me.at.y - keep, me.at.x + keep, me.at.y + keep))) continue;
                int a0 = Mathf.FloorToInt((me.at.x - keep) / other.spacing), a1 = Mathf.FloorToInt((me.at.x + keep) / other.spacing);
                int b0 = Mathf.FloorToInt((me.at.y - keep) / other.spacing), b1 = Mathf.FloorToInt((me.at.y + keep) / other.spacing);
                for (int b = b0; b <= b1; b++)
                    for (int a = a0; a <= a1; a++)
                    {
                        if (e == l && a == i && b == j) continue;
                        var o = Candidate(e, a, b);
                        if (!o.ok || (o.at - me.at).sqrMagnitude >= keep * keep) continue;
                        if (e < l || o.say > me.say) return false;
                    }
            }
            return true;
        }

        void Spawn(Layer layer, Vector2 at, string model, float sizeRoll, float yawRoll)
        {
            if (layer.facing)
            {
                // Set on the wall's foot, half buried in it, a rock as tall as the wall where one is (else the tallest,
                // stacked in rows up the wall), its top just over the wall's top.
                float wall = t.WallNear(at.x, at.y, 1.4f, out Vector2 foot, out Vector2 toward);
                if (wall <= 0f) return;
                float y0 = t.Height(foot.x, foot.y);
                string pick = CliffFaceRocks.OrderBy(m => FaceRockHeight(m)).FirstOrDefault(m => FaceRockHeight(m) >= wall) ?? CliffFaceRocks.OrderBy(FaceRockHeight).Last();
                float tall = FaceRockHeight(pick), depth = FaceRockDepth(pick);
                var into = foot + toward * (depth * 0.5f);
                int rows = Mathf.Max(1, Mathf.CeilToInt((wall + 0.15f) / tall));
                for (int r = 0; r < rows; r++)
                {
                    // The top row's top stands 0.15 over the wall's top; the rows under it reach down to the foot.
                    float bottom = y0 + wall + 0.15f - tall * (rows - r);
                    SpawnOne(layer, into, bottom + FaceRockSize(pick).lift, pick, 0.5f, Mathf.Repeat(yawRoll + r * 0.37f, 1f), true);
                }
                return;
            }
            SpawnOne(layer, at, float.NaN, model, sizeRoll, yawRoll, false);
        }

        void SpawnOne(Layer layer, Vector2 at, float atHeight, string model, float sizeRoll, float yawRoll, bool fixedSize)
        {
            var chunk = chunks.Find(c => c.rect.Contains(at));
            if (chunk == null) return;
            var group = Group(layer.cat);
            if (group == null || !row.TryGetProps(layer.cat, out var prefabs) || prefabs == null || prefabs.Count == 0) return;
            // The nature pack's models, at THE WORLD'S SCALE: one factor for the whole pack, its person against ours. A kept
            // biome's own prop (an asset path) stands at the size every generated field gives it (the recipe group's).
            bool kept = model.StartsWith("Assets/");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kept ? model : NatureDir + model + ".prefab");
            if (prefab == null) throw new System.Exception("no prop " + model);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetParent(chunk.props, false);
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, (kept ? group.scale : natureScale) * (fixedSize ? 1f : 1f + (sizeRoll * 2f - 1f) * group.scaleJitter));
            if (layer.cat == PropCategory.Flowers)
            {
                // A flower's height rolled apart from its size, keyed by where it stands so it is the same whatever is built.
                uint spot = StableRandom.Square(StableRandom.Seed(layer.name + " height"), Mathf.RoundToInt(at.x * 100f), Mathf.RoundToInt(at.y * 100f));
                var sc = go.transform.localScale;
                go.transform.localScale = new Vector3(sc.x, sc.y * (1f + FlowerExtraHeight) * (1f + (StableRandom.Value(spot, 0) * 2f - 1f) * FlowerHeightJitter), sc.z);
            }
            float y = !float.IsNaN(atHeight) ? atHeight : float.IsNaN(layer.height(at.x, at.y)) ? t.Height(at.x, at.y) : layer.height(at.x, at.y);
            go.transform.position = new Vector3(at.x, y, at.y);
            go.transform.rotation = Quaternion.Euler(0f, group.randomYaw ? yawRoll * 360f : 0f, 0f);
            WorldBuildUtil.SetLayerRecursive(go, 0);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            // A bush's leaf trigger is kept: walking out of it shakes leaves loose. Nothing stands on a trigger.
            // The nature's own shader moves its leaves in the wind, so our sway is never added to it.
            // A CLIFF FACE'S ROCKS ARE SOLID (the user, 2026-10-27: "make it solid"): they stand out in front of the wall
            // in the ground, so a body is stopped by the rock it sees, with the rock's own collider.
            if (!group.solid && !layer.facing)
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) if (!col.isTrigger) Object.DestroyImmediate(col);
            // SHADOWS ONLY FROM THE BIG THINGS (the user, 2026-10-27): trees, cliff rocks and big rocks cast one; bushes,
            // smaller rocks, stones, plants, grass, flowers, reeds and lilies do not. MEASURED in the trial square before
            // this: shadows added about 2.6 million triangles and 2-8 ms a frame on the graphics card, every caster
            // within the shadow distance being drawn a second time for its shadow.
            bool casts = layer.cat == PropCategory.Trees || model.StartsWith("Cliff_") || model.StartsWith("Rock_Big");
            if (!casts)
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true)) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // From each mesh's own bounds through its transform: a renderer's bounds can still say where it was made (the
            // world origin) until it is drawn. Only meshes count: an effect or a butterfly area has no mesh to measure.
            var meshes = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) meshes.Add(r);
            }
            if (meshes.Count > 0)
            {
                var bd = BoundsOf(meshes, null);
                reach = Mathf.Max(reach, PastChunk(Rect.MinMaxRect(bd.min.x, bd.min.z, bd.max.x, bd.max.z), chunk.rect));
            }
            placed++;
        }

        // Layer by layer, only the squares inside the built box.
        for (int l = 0; l < layers.Count; l++)
        {
            var layer = layers[l];
            // Two units past the built box: a cliff face's rock moves onto its wall's foot, which can lie in a built square
            // while the square it was rolled in does not.
            var b = Rect.MinMaxRect(Mathf.Max(layer.bounds.xMin, built.xMin - 2f), Mathf.Max(layer.bounds.yMin, built.yMin - 2f),
                                    Mathf.Min(layer.bounds.xMax, built.xMax + 2f), Mathf.Min(layer.bounds.yMax, built.yMax + 2f));
            if (b.width <= 0f || b.height <= 0f) continue;
            int i0 = Mathf.FloorToInt(b.xMin / layer.spacing), i1 = Mathf.FloorToInt(b.xMax / layer.spacing);
            int j0 = Mathf.FloorToInt(b.yMin / layer.spacing), j1 = Mathf.FloorToInt(b.yMax / layer.spacing);
            int laidHere = 0, rolled = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    var c = Candidate(l, i, j);
                    if (!c.ok || !built.Contains(c.at)) continue;
                    rolled++;
                    if (!Stands(l, i, j)) continue;
                    Spawn(layer, c.at, c.model, c.size, c.yaw);
                    laidHere++;
                }
            if (rolled > 0) perLayer.Append($"{layer.name} {laidHere}/{rolled}, ");
        }
        report.AppendLine($"{placed} props placed, square by square: {perLayer}");
        return reach;
    }

    // ---- props, square by square ------------------------------------------------------------------------------------
    // EVERY PROP IS ROLLED BY ITS OWN SQUARE OF LAND. Each layer (one kind of prop in one kind of place) cuts the world into
    // squares as wide as that kind keeps apart; a square rolls whether it holds one, where in it, which model, how big and
    // which way it faces from its own place on the map and the layer's name only (StableRandom). The town whose theme a
    // spot shows is read from the layout at a place moved by a slow wave and a little of the square's own roll, so a
    // border wanders and the two themes mix along it and fade into each other instead of changing on a straight line.
    // How far the border between two towns' themes wanders either way, and how wide either way the two mix, in world
    // units (our person is 0.327 tall).
    const float BorderWave = 6f, BorderMix = 4f;
    // How wide a grove is, and where on the grove pattern (0-1) a grove starts, and where its middle starts.
    const float GroveWidth = 14f, GroveEdge = 0.62f, GroveHeart = 0.72f, GroveSkirt = 0.5f;

    sealed class Layer
    {
        public string name;
        public PropCategory cat;
        // How wide its squares are (at most one in each), and how far it keeps from every other prop.
        public float spacing, keep;
        public bool anywhere;
        public System.Func<float, float, float> height = (x, z) => float.NaN;
        public Rect bounds;
        public System.Func<float, float, float> chance;
        public System.Func<Theme, string[]> models;
        // A cliff face's lining: set against the wall beside it and stacked in rows up its whole height, solid.
        public bool facing;
        // Keeps only its own distance from every earlier prop, not the wider of the two: the fields' fill layers,
        // packed as close as the pack's demo packs them.
        public bool ownKeep;
    }

    // Which town's theme a spot shows: the layout's, at the spot moved by the border's wave and the roll's mix.
    static Theme AreaAt(Terrain t, float x, float z, float mixX, float mixZ)
    {
        float wx = (Mathf.PerlinNoise(x * 0.035f + 3.7f, z * 0.035f + 8.1f) - 0.5f) * 2f * BorderWave + (mixX * 2f - 1f) * BorderMix;
        float wz = (Mathf.PerlinNoise(x * 0.035f + 21.3f, z * 0.035f + 5.9f) - 0.5f) * 2f * BorderWave + (mixZ * 2f - 1f) * BorderMix;
        int moved = t.ThemeAt(x + wx, z + wz);
        return ThemeOf(t, moved >= 0 ? moved : t.ThemeAt(x, z));
    }

    static float Grove(float x, float z) => Mathf.PerlinNoise(x / GroveWidth + 37.1f, z / GroveWidth + 91.7f);
    static float Clump(float x, float z, float offset) => Mathf.PerlinNoise(x / 3f + offset, z / 3f + offset * 1.7f);

    // Every layer, biggest first.
    static List<Layer> PropLayers(Terrain t)
    {
        var world = Rect.MinMaxRect(-100000f, -100000f, 100000f, 100000f);
        var all = new List<Layer>();
        void Add(string name, PropCategory cat, float spacing, System.Func<float, float, float> chance, System.Func<Theme, string[]> models,
                 Rect? bounds = null, bool anywhere = false, System.Func<float, float, float> height = null, float keep = -1f, bool facing = false)
        {
            var layer = new Layer { name = name, cat = cat, spacing = spacing, keep = keep < 0f ? spacing : keep, chance = chance, models = models,
                                    bounds = bounds ?? world, anywhere = anywhere, facing = facing };
            if (height != null) layer.height = height;
            all.Add(layer);
        }
        Rect Around(Vector2 c, float r) => Rect.MinMaxRect(c.x - r, c.y - r, c.x + r, c.y + r);

        // EVERY CLIFF WALL LINED WITH THE PACK'S OWN CLIFF ROCKS (decided with the user 2026-10-27), at true size, close
        // enough to overlap, stacked up the wall's whole height, each one solid so a body stops at the rock it sees.
        Add("cliff faces", PropCategory.Rocks, 1f, (x, z) =>
        {
            if (t.DistRoads(x, z) < Terrain.RoadHalf + 0.6f || t.DistRiver(x, z) < 4f || t.DistPond(x, z, out _) < 1f) return 0f;
            if (Rect.MinMaxRect(Town.xMin - 2f, Town.yMin - 2f, Town.xMax + 2f, Town.yMax + 2f).Contains(new Vector2(x, z))) return 0f;
            return t.WallNear(x, z, 1.4f, out _, out _) > 0f ? 0.95f : 0f;
        }, th => CliffFaceRocks, anywhere: true, keep: 0.8f, facing: true);
        // Broken rock at the foot of every cliff wall; boulders and stones in the open.
        Add("cliff foot rocks", PropCategory.Rocks, 3f, (x, z) => t.CliffFoot(x, z) ? 0.35f : 0f, th => NatureCliffs, keep: 2.5f);
        Add("cliff foot stones", PropCategory.Rocks, 1.4f, (x, z) => t.CliffFoot(x, z) ? 0.2f : 0f, th => NatureProps[PropCategory.Rocks]);
        // Trees: groves of the town's trees, and a few lone ones between the groves.
        Add("grove trees", PropCategory.Trees, 2.6f, (x, z) => Grove(x, z) >= GroveEdge ? 0.6f : 0f, th => th?.trees);
        Add("lone trees", PropCategory.Trees, 5f, (x, z) => Grove(x, z) < GroveSkirt ? 0.04f : 0f, th => th?.trees);
        Add("rocks", PropCategory.Rocks, 3f, (x, z) => 0.012f, th => NatureProps[PropCategory.Rocks]);
        Add("stones", PropCategory.Rocks, 1.5f, (x, z) => 0.007f, th => NatureStones);
        {
            // THE FIELDS' FILL, AS THICK AS THE PACK'S OWN DEMO ISLAND: each kind's count there over the island's area,
            // at the world's scale, is the chance of each of the layer's squares holding one; kept apart only by about
            // their own width. Only on land wearing a nature-pack theme.
            float island = Mathf.Pow(DemoIslandSide * NatureScale(new StringBuilder()), 2f);
            float Chance(int demoCount, float spacing) => Mathf.Min(1f, demoCount / island * spacing * spacing);
            Add("field trees", PropCategory.Trees, 2f, (x, z) => Chance(DemoTrees, 2f), th => NatureTheme(th) ? th.trees : null, keep: 0.8f);
            Add("field firs", PropCategory.Trees, 3f, (x, z) => Chance(DemoFirs, 3f), th => NatureTheme(th) ? NatureFirs : null, keep: 0.8f);
            Add("field bushes", PropCategory.Grass, 1.2f, (x, z) => Chance(DemoBushes, 1.2f), th => NatureTheme(th) ? NatureBushes : null, keep: 0.3f);
            Add("field rocks", PropCategory.Rocks, 1.4f, (x, z) => Chance(DemoRocks, 1.4f), th => NatureTheme(th) ? NatureProps[PropCategory.Rocks] : null, keep: 0.3f);
            Add("field plants", PropCategory.Grass, 0.8f, (x, z) => 0.08f, th => NatureTheme(th) ? NaturePlants : null, keep: 0.4f);
            foreach (var layer in all) if (layer.name.StartsWith("field ")) layer.ownKeep = true;
        }
        // Round each pond: a rim of rocks, bushes behind it, reeds at the water's edge.
        foreach (var (p, at, level) in t.ponds)
        {
            float r = p.radius;
            Add($"{p.id} rim rocks", PropCategory.Rocks, 0.9f, (x, z) => { float d = Vector2.Distance(at, new Vector2(x, z)); return d >= r + 0.6f && d <= r + 2.4f ? 0.2f : 0f; },
                th => NatureProps[PropCategory.Rocks], Around(at, r + 2.4f), anywhere: true);
            Add($"{p.id} bushes", PropCategory.Grass, 0.6f, (x, z) => { float d = Vector2.Distance(at, new Vector2(x, z)); return d >= r + 2.5f && d <= r + 6f ? 0.1f : 0f; },
                th => NatureBushes, Around(at, r + 6f));
            Add($"{p.id} reeds", PropCategory.Grass, 0.35f, (x, z) => { float d = Vector2.Distance(at, new Vector2(x, z)); return d >= r - 0.8f && d <= r + 0.6f ? 0.4f : 0f; },
                th => NatureBankPlants, Around(at, r + 0.6f), anywhere: true);
        }
        // Light rays or floating leaves, and butterflies, at the heart of each grove.
        Add("grove lights", PropCategory.Grass, 10f, (x, z) => Grove(x, z) >= GroveHeart ? 1f : 0f, th => NatureEffects, keep: 0.1f);
        Add("grove butterflies", PropCategory.Grass, 10f, (x, z) => Grove(x, z) >= GroveHeart ? 1f : 0f, th => NatureButterflies, keep: 0.1f);
        // Bushes round each grove's skirt; reeds and cattails on the river's banks.
        Add("grove bushes", PropCategory.Grass, 1.2f, (x, z) => { float g = Grove(x, z); return g >= GroveSkirt && g < GroveEdge ? 0.015f : 0f; }, th => NatureBushes);
        Add("bank reeds", PropCategory.Grass, 0.9f, (x, z) => { float dv = t.DistRiver(x, z); return dv > 2.3f && dv < 3.4f ? 0.2f : 0f; },
            th => NatureBankPlants, anywhere: true);
        // Flowers, grass and fallen branches under the groves' skirts, and clumps of grass, plants and the town's flowers
        // in the open.
        Add("grove flowers", PropCategory.Flowers, 0.8f, (x, z) => { float g = Grove(x, z); return g >= GroveSkirt && g < GroveEdge ? 0.01f : 0f; }, th => th?.flowers);
        Add("grove branches", PropCategory.Grass, 0.8f, (x, z) => { float g = Grove(x, z); return g >= GroveSkirt && g < GroveEdge ? 0.005f : 0f; }, th => NatureBranches);
        Add("grove grass", PropCategory.Grass, 0.8f, (x, z) => { float g = Grove(x, z); return g >= GroveSkirt && g < GroveEdge ? 0.012f : 0f; }, th => NatureProps[PropCategory.Grass]);
        Add("grass clumps", PropCategory.Grass, 0.6f, (x, z) => Clump(x, z, 5.3f) >= 0.8f ? 0.1f : 0f, th => NatureProps[PropCategory.Grass]);
        Add("plant clumps", PropCategory.Grass, 0.6f, (x, z) => Clump(x, z, 17.9f) >= 0.8f ? 0.08f : 0f, th => NaturePlants);
        Add("flower clumps", PropCategory.Flowers, 0.6f, (x, z) => Clump(x, z, 29.3f) >= 0.8f ? 0.06f : 0f, th => th?.flowers);
        // Lily pads and water lilies floating on each pond.
        foreach (var (p, at, level) in t.ponds)
        {
            float r = p.radius - 1.5f, lv = level;
            Add($"{p.id} lilies", PropCategory.Grass, 1.1f, (x, z) => Vector2.Distance(at, new Vector2(x, z)) <= r ? 0.5f : 0f,
                th => NatureFloating, Around(at, r), anywhere: true, height: (x, z) => lv);
        }
        return all;
    }
    // How long building one chunk out of its prefab takes on this machine — the main-thread half of a load — so the load
    // ring starts from a measured number. The worst of three chunks, in seconds.
    static float MeasureChunkBuild(List<ChunkOut> chunks, Scene scene, StringBuilder report)
    {
        float worst = 0f;
        for (int i = 0; i < chunks.Count && i < 3; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(chunks[i * Mathf.Max(1, chunks.Count / 3)].prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            float took = (float)sw.Elapsed.TotalSeconds;
            Object.DestroyImmediate(go);
            worst = Mathf.Max(worst, took);
        }
        report.AppendLine($"Building one chunk from its prefab took at worst {worst * 1000f:0} ms in the editor.");
        return Mathf.Max(0.05f, worst);
    }

    // ---- outputs -------------------------------------------------------------------------------------------------

    static Material WorldMaterial(float worldScale)
    {
        const string path = OutDir + "/world_ground.mat";
        var shader = Shader.Find("Unwritten/GroundBlend");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        else mat.shader = shader;
        // A TRIAL of the Idyllic Fantasy Nature pack's painted ground (2026-10-26), each tile repeating every as many metres
        // as the pack's own ground layer for it says, at the world's scale (RULE WORLDSCALE).
        void Tex(string prop, string tile, string tilingProp, float metres)
        {
            var tex = GroundTiles.Load(tile);
            if (tex == null) throw new System.Exception("no ground tile " + tile);
            mat.SetTexture(prop, tex);
            mat.SetFloat(tilingProp, 1f / (metres * worldScale));
        }
        Tex("_GrassMap", "BASE_OR_PATCH_painted_grass", "_GrassTiling", 4f);
        Tex("_DirtMap", "BASE_OR_PATCH_OR_PATH_painted_dirt", "_DirtTiling", 4f);
        Tex("_PathMap", "BASE_OR_PATCH_OR_PATH_painted_dirt", "_PathTiling", 4f);
        Tex("_StoneMap", "BASE_OR_PATCH_OR_PATH_painted_dirt_stone", "_StoneTiling", 10f);
        Tex("_RockMap", "BASE_OR_PATCH_OR_PATH_painted_rock", "_RockTiling", 7f);
        float tiling = 1f / (7f * worldScale);
        mat.SetColor("_Color", Color.white);
        // THE CLIFF FIELDS' OWN ROCK SKIN, on every steep face and on the cave's rock: laid on from all three sides so a
        // face shows rock rather than streaks, faded in by the ground's cliff layer (uv1.x) — the same material switch a
        // generated cliff field uses (GroundBlendBuilder), and only on this world's material.
        mat.EnableKeyword("_CLIFF_ROCK");
        mat.DisableKeyword("_CLIFF_FACE_COLOR");
        var rockTex = GroundTiles.Load("BASE_OR_PATCH_OR_PATH_painted_rock");
        if (rockTex != null) mat.SetTexture("_CliffRockMap", rockTex);
        mat.SetFloat("_CliffRockTiling", tiling);
        mat.SetColor("_CliffFaceColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // The water's look: a template material, copied whole on every build so the template owns every value. Two Uber
    // Stylized Water features stay off when it is the template: refraction needs URP's opaque texture, a copy of the
    // whole screen every frame, and planar reflection a second camera drawing the scene again.
    // Every river and pond uses the Genshin water (decided with the user 2026-10-27: every nature-pack prop is used,
    // except its water).
    const string WaterTemplatePath = "Assets/1_Authored/Water/UberStylizedWater/Template Materials/UWa-Template-Genshin.mat";

    // A sheet's material: the template, with its surface foam drifting along `flow` (world X and Z) at the template's own
    // speed, or as the template has it where the water stands still.
    static Material WaterMaterial(string name, Vector4 flow)
    {
        string path = $"{OutDir}/world_{name}_water.mat";
        var template = AssetDatabase.LoadAssetAtPath<Material>(WaterTemplatePath);
        if (template == null) throw new System.Exception("no water template at " + WaterTemplatePath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(template); AssetDatabase.CreateAsset(mat, path); }
        else { mat.shader = template.shader; mat.CopyPropertiesFromMaterial(template); }
        // Each feature is a toggle that sets its keyword again from its own value, so both are switched off together.
        foreach (var feature in new[] { "_ENABLEREFRACTION", "_ENABLEPLANERREFLECTION" })
        {
            mat.SetFloat(feature, 0f);
            mat.DisableKeyword(feature);
        }
        var along = new Vector2(flow.x, flow.y);
        if (along.sqrMagnitude > 0f && mat.HasProperty("_SurfFoam_Pan"))
        {
            Vector4 pan = template.GetVector("_SurfFoam_Pan");
            Vector2 drift = along.normalized * new Vector2(pan.x, pan.y).magnitude;
            mat.SetVector("_SurfFoam_Pan", new Vector4(drift.x, drift.y, pan.z, pan.w));
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static readonly HashSet<string> madeMeshes = new HashSet<string>();

    static Mesh SaveMesh(Mesh mesh)
    {
        string path = $"{OutDir}/Meshes/{mesh.name}.asset";
        madeMeshes.Add(path);
        return WorldBuildUtil.SaveMeshInPlace(mesh, path);
    }

    static void RegisterAddresses(List<ChunkOut> chunks, string lastlinePrefab)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        if (schema != null) schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        void Add(string path, string address)
        {
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false);
            entry.address = address;
        }
        foreach (var c in chunks) Add(c.prefabPath, $"{MapId}/square_{c.cx}_{c.cz}");
        Add(lastlinePrefab, $"{MapId}/lastline");
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
        AssetDatabase.SaveAssetIfDirty(group);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    // The world scene, written as text: its own four settings blocks (light, fog, sky) kept from the scene as it is,
    // then the world, the pathfinder settings, the grassland marker and the arrival point. Written directly so no
    // scene is ever opened.
    static void WriteScene(List<(string address, Rect rect)> chunks, Vector3 arrival, float reach, float loadSeconds)
    {
        if (!File.Exists(ScenePath)) throw new System.Exception(ScenePath + " is missing — its settings blocks are what a rebuild keeps");
        var template = File.ReadAllLines(ScenePath);
        var sb = new StringBuilder();
        foreach (var line in template)
        {
            if (line.StartsWith("--- !u!1001") || line.StartsWith("--- !u!1 ")) break;
            sb.Append(line).Append('\n');
        }
        string worldGuid = ScriptGuid("SeamlessWorld"), pathGuid = ScriptGuid("Pathfinder3DConfig"),
               biomeGuid = ScriptGuid("MapBiome"), spawnGuid = ScriptGuid("SpawnPoint");
        int ground = 1 << LayerMask.NameToLayer("Ground");
        string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string V2(Vector2 v) => "{x: " + F(v.x) + ", y: " + F(v.y) + "}";

        void Object(long go, long tr, long mb, string name, Vector3 pos, string script, string body)
        {
            sb.Append($"--- !u!1 &{go}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n  - component: {{fileID: {tr}}}\n  - component: {{fileID: {mb}}}\n  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n");
            sb.Append($"--- !u!114 &{mb}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {script}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n{body}");
            sb.Append($"--- !u!4 &{tr}\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\n  m_LocalPosition: {{x: {F(pos.x)}, y: {F(pos.y)}, z: {F(pos.z)}}}\n  m_LocalScale: {{x: 1, y: 1, z: 1}}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: 0}}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n");
        }

        var world = new StringBuilder();
        world.Append($"  worldMin: {V2(WorldMin)}\n  worldMax: {V2(WorldMax)}\n  chunks:\n");
        foreach (var (address, rect) in chunks)
            world.Append($"  - address: {address}\n    min: {V2(rect.min)}\n    max: {V2(rect.max)}\n");
        world.Append("  areas:\n");
        world.Append($"  - name: Lastline\n    kind: {(int)SeamlessAreaKind.Town}\n    min: {V2(Town.min)}\n    max: {V2(Town.max)}\n");
        world.Append($"  propReach: {F(reach)}\n  measuredLoadSeconds: {F(loadSeconds)}\n");

        Object(100001, 100002, 100003, "World", Vector3.zero, worldGuid, world.ToString());
        Object(100011, 100012, 100013, "Pathfinder3DConfig", Vector3.zero, pathGuid,
               $"  heightAware: 1\n  groundMask:\n    serializedVersion: 2\n    m_Bits: {ground}\n");
        Object(100021, 100022, 100023, "MapBiome", Vector3.zero, biomeGuid, $"  biome: {(int)Biome.Grassland}\n");
        Object(100031, 100032, 100033, "SpawnPoint_Center", arrival, spawnGuid, $"  id: {SavePoint.DefaultSpawnId}\n");
        string lookGuid = AssetDatabase.AssetPathToGUID(LookProfilePath);
        if (string.IsNullOrEmpty(lookGuid)) throw new System.Exception("no colour grading profile at " + LookProfilePath);
        Object(100041, 100042, 100043, "Look", Vector3.zero, PackageScriptGuid(typeof(UnityEngine.Rendering.Volume)),
               $"  m_IsGlobal: 1\n  priority: 0\n  blendDistance: 0\n  weight: 1\n  sharedProfile: {{fileID: 11400000, guid: {lookGuid}, type: 2}}\n");
        // The pack's plants lean away from whoever plays here (PlantBend).
        Object(100051, 100052, 100053, "PlantBend", Vector3.zero, ScriptGuid("PlantBend"), "");
        var roots = new StringBuilder("  - {fileID: 100002}\n  - {fileID: 100012}\n  - {fileID: 100022}\n  - {fileID: 100032}\n  - {fileID: 100042}\n  - {fileID: 100052}\n");
        sb.Append("--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n" + roots);
        File.WriteAllText(ScenePath, sb.ToString());
    }

    // A TRIAL of the Idyllic Fantasy Nature pack's colour grading (2026-10-26): its demo's profile, less motion blur and
    // depth of field, as a global volume in this world's scene. The game camera draws post-processing for it.
    const string LookProfilePath = "Assets/1_Authored/Nature/IdyllicLook_PostProcessing.asset";

    // The asset id of a component script that lives in a package, found by its class rather than typed in.
    static string PackageScriptGuid(System.Type type)
    {
        foreach (var guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript", new[] { "Packages" }))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type) return guid;
        }
        throw new System.Exception("no script for " + type.FullName);
    }

    static string ScriptGuid(string className)
    {
        foreach (var guid in AssetDatabase.FindAssets(className + " t:MonoScript"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == className) return guid;
        }
        throw new System.Exception("no script named " + className);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
