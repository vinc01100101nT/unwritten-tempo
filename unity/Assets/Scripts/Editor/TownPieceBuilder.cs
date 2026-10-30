using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BUILDS A TOWN from its plan (data/towns/&lt;town&gt;.json, drawn in the town editor, tools/town-editor): its ground,
/// its wall with towers and gates, and every house, tree and market good where the plan puts them — saved as
/// <c>Assets/1_Authored/Prefabs/Towns/&lt;Name&gt;.prefab</c>, the same kind of town piece as the hand-made Lastline, which
/// the world builder then sets into the world.
///
/// <para><b>The plan is the town; the piece is only what is built from it.</b> Every build replaces the piece and the
/// town's folder of built meshes and materials, so the piece is never edited by hand. A town with no plan file (the
/// hand-made Lastline) is never touched.</para>
///
/// <para>The piece is laid out like Lastline's, so the world builder reads both the same way: a <c>Ground_&lt;id&gt;</c> with
/// its ground mesh in <c>Ground_Visual</c> (wearing the tiles of the town's own tile list, its levels stacked a curb at a
/// time, its curbs following each street's true outline), a
/// <c>Props</c>, a <c>Wall</c>, a <c>Gate_&lt;side&gt;</c> marker in each gate and an <c>Arrival</c>. Town-local: x east,
/// z north, the middle at 0, 0, its ground at height 0.</para>
/// </summary>
public static class TownPieceBuilder
{
    const string TownsDir = "Assets/1_Authored/Prefabs/Towns";
    // Lastline's curb stone, worn by every town's curbs.
    const string LastlineCurbPath = TownsDir + "/Lastline/ground_curb.mat";
    const string WallPartPath = "Assets/1_Authored/Prefabs/Props/Outdoor/Wall_part1.prefab";
    const string GateTowerPath = "Assets/1_Authored/Prefabs/Props/Outdoor/Wall_tower2.prefab";
    const string WallTowerPath = "Assets/1_Authored/Prefabs/Props/Outdoor/Wall_tower1.prefab";
    const string MeadowPath = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Prefabs/FlowerMeadow_Pink.prefab";
    static string PlanPath(string id) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "towns", id + ".json"));

    // Lastline's ground points are half a metre apart; so are these.
    const float Step = 0.5f;
    // Lastline's wall: one kit wall piece every 1.17 m, a tower each side of a gate.
    const float WallPartLength = 1.17f;
    // A gate this close to a wall line opens it.
    const float GateOnLine = 0.25f;

    [System.Serializable] sealed class Plan
    {
        public string id, name, weapon, theme, tint;
        public PGate[] gates; public PArea[] areas; public PStreet[] streets; public PPiece[] pieces; public PKit[] kit;
        public PWall wall; public PWallLine[] walls; public PStreet[] canals; public PBridge[] bridges; public PMound[] mounds; public PLift lift;
        // The town's ground tiles (any number), the tiles of its lawn and of its soil (trees' beds, the stream's bed), the
        // plants its plant brush offers, and where they were painted.
        public PTile[] tiles; public string lawnTile, soilTile; public PPlant[] plants; public PPlantGrid plantGrid;
        // Where players arrive, town-local x, z; left out, 3 units inside the south gate.
        public float[] arrival;
    }
    // A ground tile: a texture in GroundTiles, by its name, and how many times it repeats per unit of ground.
    [System.Serializable] sealed class PTile { public string name; public float tiling; }
    [System.Serializable] sealed class PPlant { public string kind, prefab; }
    // The painted plants: a grid of cells from the piece's south-west corner, row by row from the south, each holding 0 (none)
    // or 1 + its plant's place in the plant list, kept as runs: a value, then how many cells in a row hold it.
    [System.Serializable] sealed class PPlantGrid { public float x0, z0, step; public int nx, nz; public int[] runs; }
    // The ground raised or lowered by hand: heights on a square grid over the whole piece, from its south-west corner.
    [System.Serializable] sealed class PLift { public float x0, z0, step; public int nx, nz; public float[] h; }
    [System.Serializable] sealed class PBridge { public string kind; public float x, z, bearing, length, width, scale; public int canal; }
    [System.Serializable] sealed class PMound { public float x, z, length, width; public float[] dir; }
    // A gate: where it stands, town-local; on a wall line it opens the wall there.
    [System.Serializable] sealed class PGate { public string road; public float x, z; }
    [System.Serializable] sealed class PArea
    {
        public string look, label, level, tile; public float[] rect, circle, poly;
        // Per outline point, 1 where the outline curves smoothly through it (the town editor's Shift-drag), 0 a sharp corner.
        public int[] smooth;
        // A tree's bed only, never in a plan: the level of the paving at the tree, which the bed stands a curb below or above.
        [System.NonSerialized] public int stand;
        [System.NonSerialized] float[] ring;
        // The outline as it is built: its points, with the curves through its smooth ones laid out as short straight runs.
        public float[] Ring => ring ??= poly == null ? null : Outline(poly, smooth);
    }

    // AN AREA'S OUTLINE: straight from point to point, but curving through every point marked smooth — each side touching
    // one bends, leaving a smooth point along the line through its two neighbours and a sharp one straight at the next
    // point, in CurveSteps runs. The town editor lays the same curve (outlineOf), so the curb is built where it is drawn.
    const int CurveSteps = 16;
    static float[] Outline(float[] poly, int[] smooth)
    {
        int n = poly.Length / 2;
        if (smooth == null || System.Array.IndexOf(smooth, 1) < 0) return poly;
        Vector2 P(int i) { i = (i % n + n) % n; return new Vector2(poly[2 * i], poly[2 * i + 1]); }
        bool S(int i) { i = (i % n + n) % n; return i < smooth.Length && smooth[i] == 1; }
        var o = new List<float>();
        for (int i = 0; i < n; i++)
        {
            Vector2 a = P(i), b = P(i + 1);
            o.Add(a.x); o.Add(a.y);
            if (!S(i) && !S(i + 1)) continue;
            Vector2 ma = S(i) ? (P(i + 1) - P(i - 1)) * 0.5f : b - a, mb = S(i + 1) ? (P(i + 2) - P(i)) * 0.5f : b - a;
            for (int k = 1; k < CurveSteps; k++)
            {
                float t = k / (float)CurveSteps, t2 = t * t, t3 = t2 * t;
                var p = (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * ma + (-2f * t3 + 3f * t2) * b + (t3 - t2) * mb;
                o.Add(p.x); o.Add(p.y);
            }
        }
        return o.ToArray();
    }
    [System.Serializable] sealed class PStreet { public string name, level, tile; public float width; public float[] xz; }
    [System.Serializable] sealed class PPiece { public string kind; public int variant; public float x, z, rot; }
    [System.Serializable] sealed class PKit { public string kind, prefab, role; public float[] footprint; public float scale; }
    [System.Serializable] sealed class PWall { public float line, gate; }
    // A wall line, drawn in the town editor: its points (x, z, x, z…), whether it closes back on its first point, and the
    // tower standing at each point ("Wall_tower1", "Wall_tower2", or "" for none).
    [System.Serializable] sealed class PWallLine { public float[] xz; public bool closed; public string[] towers; }

    /// <summary>Build the town piece for <paramref name="townId"/> from its plan, replacing the one built before. Returns
    /// a report.</summary>
    public static string Build(string townId)
    {
        var report = new StringBuilder();
        if (!File.Exists(PlanPath(townId))) throw new System.Exception("no plan at " + PlanPath(townId) + " — run tools/town-plan/build_town_plan.py");
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath(townId)));
        if (plan.walls == null) throw new System.Exception(PlanPath(townId) + " has no walls list (a town with no wall has an empty one) - open it in the town editor and save it");
        string prefabPath = $"{TownsDir}/{plan.name}.prefab";
        string assetDir = $"{TownsDir}/{plan.name}";
        AssetDatabase.DeleteAsset(prefabPath);
        AssetDatabase.DeleteAsset(assetDir);
        RequireLevels(plan);
        RequireTiles(plan);
        Beds(plan);
        if (!AssetDatabase.IsValidFolder(assetDir)) AssetDatabase.CreateFolder(TownsDir, plan.name);

        float halfX = 24.5f, halfZ = 31f;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject(plan.name);
            SceneManager.MoveGameObjectToScene(root, scene);
            var shape = new Shape(plan, halfX, halfZ);
            Bridges(plan, shape, scene, report);
            Ground(plan, shape, scene, root.transform, assetDir, halfX, halfZ, report);
            Wall(plan, shape, root.transform, halfX, halfZ, report);
            Props(plan, shape, root.transform, assetDir, halfX, halfZ, report);
            foreach (var g in plan.gates)
            {
                // Only a gate out of the town is one the world's roads leave by: a gate in a wall inside the town is a gap.
                if (!OutOfTown(plan, g, halfX, halfZ)) { report.AppendLine($"{plan.name}: the gate at {g.x:0.##}, {g.z:0.##} is in a wall inside the town; no road leaves by it."); continue; }
                // In the middle of the gate: the world builder reads the side from where it stands.
                var marker = new GameObject("Gate_" + Side(g, halfX, halfZ));
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = new Vector3(g.x, HandLift(plan, g.x, g.z), g.z);
            }
            var arrival = new GameObject("Arrival");
            arrival.transform.SetParent(root.transform, false);
            var south = System.Array.Find(plan.gates, g => Side(g, halfX, halfZ) == "south");
            float ax = south != null ? south.x : 0f, az = -halfZ + plan.wall.line + 3f;
            if (plan.arrival != null && plan.arrival.Length == 2) { ax = plan.arrival[0]; az = plan.arrival[1]; }
            arrival.transform.localPosition = new Vector3(ax, HandLift(plan, ax, az), az);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            report.AppendLine($"{plan.name}: saved as {prefabPath}.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return report.ToString();
    }

    // The edge of the piece a gate is nearest, the side it leads out of (the world builder's Gate reads it the same way).
    static string Side(PGate g, float halfX, float halfZ)
    {
        float w = g.x + halfX, e = halfX - g.x, s = g.z + halfZ, n = halfZ - g.z, m = Mathf.Min(w, e, s, n);
        return m == w ? "west" : m == e ? "east" : m == s ? "south" : "north";
    }

    // Does the way straight out of a gate to the piece's edge meet no other wall?
    static bool OutOfTown(Plan plan, PGate g, float halfX, float halfZ)
    {
        Vector2 a = new Vector2(g.x, g.z), b = a;
        switch (Side(g, halfX, halfZ)) { case "west": b.x = -halfX; break; case "east": b.x = halfX; break; case "south": b.y = -halfZ; break; default: b.y = halfZ; break; }
        foreach (var line in plan.walls)
        {
            var path = WallPath(line);
            for (int k = 0; k + 1 < path.Count; k++)
                if (Crossing(a, b, path[k], path[k + 1], out var at) && (at - a).magnitude > plan.wall.gate / 2f) return false;
        }
        return true;
    }

    static bool Crossing(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 at)
    {
        at = default;
        Vector2 r = b - a, s = d - c;
        float den = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(den) < 1e-6f) return false;
        float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den, u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
        at = a + r * t;
        return true;
    }

    // ---- the ground ----------------------------------------------------------------------------------------------

    // THE GROUND: one mesh, each street and area standing a whole number of curbs (GroundElevation.LevelHeight) above the
    // lawn outside the wall, with a curb face along every edge between two levels. The curb follows each shape's true
    // outline, round where it is round (GroundBlendBuilder.LayPlotCurbed). Every point wears a tile from the town's tile
    // list (GroundTileArray), and its curbs Lastline's curb stone; the mesh and the tiles are kept in the town's folder.
    const float GroundCell = 0.25f;
    // How far the ground ramps up to a bridge's end (the town editor's 3D view reads it from TownPictures' index).
    internal const float RampLength = 2.0f;

    static void Ground(Plan plan, Shape shape, Scene scene, Transform root, string assetDir, float halfX, float halfZ, StringBuilder report)
    {
        var tileOf = new Dictionary<string, int>();
        for (int i = 0; i < plan.tiles.Length; i++) tileOf[plan.tiles[i].name] = i;
        // Each point's tile, by its place in the list (GroundTileArray.Spread turns it into what the shader reads).
        void Sample(float wx, float wz, out Vector4 w) => w = new Vector4(tileOf[TileName(plan, wx, wz, halfX, halfZ)], 0f, 0f, 0f);
        var names = new string[plan.tiles.Length]; var tilings = new float[plan.tiles.Length];
        for (int i = 0; i < names.Length; i++) { names[i] = plan.tiles[i].name; tilings[i] = plan.tiles[i].tiling; }
        var tiles = GroundTileArray.Build(names, $"{assetDir}/Ground_{plan.id}_tiles.asset");
        var mat = GroundTileArray.NewMaterial(tiles, $"Ground_{plan.id}_tiles");
        AssetDatabase.CreateAsset(mat, $"{assetDir}/Ground_{plan.id}_tiles.mat");
        string name = $"Ground_{plan.id}";
        var stack = Stack.Of(plan, halfX, halfZ);
        var extras = new GroundBlendBuilder.CurbedExtras
        {
            sink = shape.HasWater ? shape.Sink : null,
            lift = shape.Lift,
            bedLook = new Color(tileOf[plan.soilTile], 0f, 0f, 0f),
            // Each bridge's own model joined into the ground: walked on, solid, and drawn as itself.
            joinTops = (v, n, uv, c, tan, byMaterial) =>
            {
                foreach (var d in shape.decks) BridgeDecks.AddModel(d, v, n, uv, c, null, tan, byMaterial);
            },
        };
        var ground = GroundBlendBuilder.LayPlotCurbed(scene, name, 2f * halfX, 2f * halfZ, GroundCell, LayerMask.NameToLayer("Ground"),
                                                      (x, z) => Levels(plan, stack, x, z, halfX, halfZ), stack.lowest, GroundElevation.LevelHeight, Sample,
                                                      mat, Load<Material>(LastlineCurbPath), out var mesh, out var water, extras);
        int split = GroundTileArray.Spread(mesh, tilings);
        foreach (var col in ground.GetComponentsInChildren<MeshCollider>(true)) if (col.sharedMesh == mesh) { col.sharedMesh = null; col.sharedMesh = mesh; }
        AssetDatabase.CreateAsset(mesh, $"{assetDir}/{name}_ground.asset");
        ground.transform.SetParent(root, false);
        report.AppendLine($"{plan.name}: ground of {mesh.vertexCount} points, {mesh.GetIndexCount(0) / 3} top and "
                          + $"{(mesh.subMeshCount > 1 ? mesh.GetIndexCount(1) / 3 : 0)} curb triangles, {shape.decks.Count} bridge decks in it; "
                          + $"levels {stack.lowest} to {stack.lowest + stack.steps}, {plan.tiles.Length} tiles, {split} triangles where tiles meet.");
        if (water == null) return;
        // The water: a surface over the stream's bed, drawn only, made from the world's own water as the rivers are.
        AssetDatabase.CreateAsset(water, $"{assetDir}/{name}_water.asset");
        var go = new GameObject("Water");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = water;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SeamlessWorldBuilder.WaterMaterialAt($"{assetDir}/Water_{plan.id}.mat", new Vector4(-0.6f, 0f, 0f, 0f));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var sheet = go.AddComponent<WaterSheet>();
        sheet.level = water.bounds.max.y;
        sheet.min = new Vector2(water.bounds.min.x, water.bounds.min.z);
        sheet.max = new Vector2(water.bounds.max.x, water.bounds.max.z);
        AssetDatabase.SaveAssetIfDirty(mr.sharedMaterial);
        report.AppendLine($"{plan.name}: water over the stream, {water.vertexCount} points.");
    }

    // THE TOWN'S SHAPE beyond its curbs: its stream (how far a point is inside the water), and how high its ground is lifted
    // (the mounds over the stream's ends, and a ramp up to each bridge's ends) — read by the ground and by every piece
    // standing on it, so a tree on a mound stands on the mound.
    sealed class Shape
    {
        readonly Plan plan;
        public readonly List<BridgeDecks.Deck> decks = new List<BridgeDecks.Deck>();
        // Each mound: where its bridge stands, the way it runs away from the bridge, the bridge's width and length, its own
        // length and width, and how high it starts (the bridge arch's underside).
        public readonly List<(Vector2 at, Vector2 dir, float bridgeWidth, float bridgeLength, float length, float width, float height)> mounds
            = new List<(Vector2, Vector2, float, float, float, float, float)>();
        // Each bridge end: where it is, the way out of the bridge, the bridge's width, and how far its deck stands above
        // the ground there.
        public readonly List<(Vector2 at, Vector2 outward, float width, float above)> ramps = new List<(Vector2, Vector2, float, float)>();
        // Each bridge's footprint on dry ground, and its deck's height along it: the banks under a bridge are built up to
        // its deck, so its side walls stand on ground instead of hanging over a bank a body can walk under and stick.
        public readonly List<(Vector2 mid, Vector2 dir, float halfLength, float halfWidth, float[] along, float[] height)> footprints
            = new List<(Vector2, Vector2, float, float, float[], float[])>();

        public Shape(Plan plan, float halfX, float halfZ) { this.plan = plan; }
        public bool HasWater => plan.canals != null && plan.canals.Length > 0;

        // How far a point is inside the stream (above 0 in it). Past the middle of each mound's bridge the stream is gone,
        // under the mound, its end a steep bank under the arch.
        public float Sink(float x, float z)
        {
            float g = -1f;
            foreach (var c in plan.canals) g = Mathf.Max(g, c.width * 0.5f - DistToStreet(c, x, z));
            if (g <= -1f) return g;
            var p = new Vector2(x, z);
            foreach (var m in mounds)
            {
                float v = Vector2.Dot(p - m.at, m.dir);
                g = Mathf.Min(g, -v * 8f);
            }
            return g;
        }

        public float MoundLift(float x, float z)
        {
            float h = 0f; var p = new Vector2(x, z);
            foreach (var m in mounds)
            {
                Vector2 d = m.dir, n = new Vector2(d.y, -d.x);
                // Rising under the back half of the bridge, where the bridge hides it, to the deck's height at its back
                // edge; then easing down over the mound's length. Across, full under the bridge, easing off past its ends.
                float v = Vector2.Dot(p - m.at, d), u = Mathf.Abs(Vector2.Dot(p - m.at, n)), back = m.bridgeWidth * 0.5f;
                if (v < 0f || v > back + m.length) continue;
                float along = v < back ? Smooth01(v / back) : 1f - Smooth01((v - back) / m.length);
                float across = 1f - Smooth01((u - (m.bridgeLength * 0.5f - 0.3f)) / 2.5f);
                h = Mathf.Max(h, m.height * along * Mathf.Clamp01(across));
            }
            return h;
        }

        // The ground raised by hand, eased to nothing at the water's edge: the water lies flat, so its bed and banks are
        // never lifted.
        public float HandLift(float x, float z)
        {
            float h = TownPieceBuilder.HandLift(plan, x, z);
            return HasWater && h != 0f ? h * Mathf.Clamp01(-Sink(x, z)) : h;
        }

        public float Lift(float x, float z)
        {
            float hand = HandLift(x, z);
            float h = hand + MoundLift(x, z); var p = new Vector2(x, z);
            foreach (var f in footprints)
            {
                float s = Vector2.Dot(p - f.mid, f.dir), u = Mathf.Abs(Vector2.Dot(p - f.mid, new Vector2(f.dir.y, -f.dir.x)));
                if (Mathf.Abs(s) > f.halfLength || u > f.halfWidth + 0.25f || (HasWater && Sink(x, z) > -0.3f)) continue;
                int k = 0;
                while (k + 2 < f.along.Length && f.along[k + 1] < s) k++;
                float t = Mathf.InverseLerp(f.along[k], f.along[k + 1], s);
                h = Mathf.Max(h, Mathf.Lerp(f.height[k], f.height[k + 1], t) - GroundElevation.LevelHeight - 0.02f);
            }
            foreach (var r in ramps)
            {
                float beyond = Vector2.Dot(p - r.at, r.outward), side = Mathf.Abs(Vector2.Dot(p - r.at, new Vector2(r.outward.y, -r.outward.x)));
                if (beyond < -0.3f || beyond > RampLength || side > r.width * 0.5f + 0.4f) continue;
                float k = 1f - Smooth01(Mathf.Max(0f, beyond) / RampLength);
                float s = 1f - Smooth01((side - r.width * 0.5f) / 0.4f);
                h = Mathf.Max(h, hand + r.above * k * Mathf.Clamp01(s));
            }
            return h;
        }
    }

    static float Smooth01(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    // THE BRIDGES: each stone bridge fitted to its crossing by the world's own bridge fitter — model scaled to the crossing,
    // its walkway's heights sampled for the ramps and mounds around it, the model itself laid into the ground. A bridge at a mound
    // gives the mound its height: the underside of the bridge's arch over the stream's middle.
    static void Bridges(Plan plan, Shape shape, Scene scene, StringBuilder report)
    {
        if (plan.bridges == null) return;
        float rise = GroundElevation.LevelHeight;
        foreach (var b in plan.bridges)
        {
            var dir = new Vector2(Mathf.Sin(b.bearing * Mathf.Deg2Rad), Mathf.Cos(b.bearing * Mathf.Deg2Rad));
            var mid = new Vector2(b.x, b.z);
            Vector2 from = mid - dir * b.length * 0.5f, to = mid + dir * b.length * 0.5f;
            float height = rise + Mathf.Max(shape.MoundLift(from.x, from.y) + shape.HandLift(from.x, from.y),
                                             shape.MoundLift(to.x, to.y) + shape.HandLift(to.x, to.y));
            var deck = BridgeDecks.Fit(from, to, height, scene, report);
            shape.decks.Add(deck);
            // How far the deck's ends stand above the bank: the ground ramps up to meet them.
            var alongS = new List<float>(); var deckY = new List<float>();
            for (int a = 0; a < deck.rows; a++)
            {
                if (!deck.Has(a)) continue;
                alongS.Add((a / (float)(deck.rows - 1) - 0.5f) * b.length);
                deckY.Add(deck.top[a]);
            }
            if (alongS.Count >= 2) shape.footprints.Add((mid, dir, b.length * 0.5f, b.width * 0.5f, alongS.ToArray(), deckY.ToArray()));
            foreach (var (row, end, outward) in new[] { (0, from, -dir), (deck.rows - 1, to, dir) })
            {
                if (!deck.Has(row)) continue;
                float above = deck.top[row] - height;
                if (above > 0.01f) shape.ramps.Add((end, outward, b.width, above));
            }
            if (plan.mounds != null)
                foreach (var m in plan.mounds)
                {
                    if (Mathf.Abs(m.x - b.x) > 0.01f || Mathf.Abs(m.z - b.z) > 0.01f) continue;
                    // As high as the bridge's own deck at its middle, so the bank behind it meets the bridge's top.
                    float top = deck.Has(deck.rows / 2) ? deck.top[deck.rows / 2] : height + 0.5f;
                    var md = new Vector2(m.dir[0], m.dir[1]).normalized;
                    shape.mounds.Add((mid, md, b.width, b.length, m.length, m.width, Mathf.Max(0.2f, top - rise)));
                    report.AppendLine($"{plan.name}: a mound behind the bridge at {mid}, {top - rise:0.00} high at the bridge.");
                }
        }
        report.AppendLine($"{plan.name}: {plan.bridges.Length} bridges fitted, {shape.ramps.Count} ends ramped up to their decks.");
    }

    // THE GROUND'S LEVELS (the user, 2026-10-29): every street and area is "high", "normal" or "low" — a curb above, level
    // with, or a curb below what it is laid on — so a high area on a high area stands two curbs up. Inside the wall the
    // ground starts one curb above the lawn outside it. The areas stack on each other in their order; the streets and
    // courts are paths, each measured from the areas' ground under it — past the wall from the town ground's height, so a
    // low street runs out of a gate level with the lawn — so crossing streets meet level and a court covers the streets
    // running into it; the trees' beds stack on everything. (The town editor works it out the same way.)
    static int Rel(string level) => level == "high" ? 1 : level == "low" ? -1 : 0;
    static bool IsBed(PArea a) => a.look == "pit" || a.look == "planter";
    static bool IsPath(PArea a) => a.look == "court";

    // The level at a point, in curbs above the lawn.
    static int LevelAt(Plan plan, float x, float z, float halfX, float halfZ)
    {
        float line = plan.wall.line;
        int stacked = 0;
        foreach (var a in plan.areas) if (!IsBed(a) && !IsPath(a) && InArea(a, x, z)) stacked += Rel(a.level);
        int level = (Mathf.Abs(x) < halfX - line && Mathf.Abs(z) < halfZ - line ? 1 : 0) + stacked, under = 1 + stacked;
        foreach (var s in plan.streets) if (DistToStreet(s, x, z) <= s.width * 0.5f) level = under + Rel(s.level);
        foreach (var a in plan.areas) if (IsPath(a) && InArea(a, x, z)) level = under + Rel(a.level);
        foreach (var a in plan.areas) if (IsBed(a) && InArea(a, x, z)) level = a.stand + Rel(a.level);
        return level;
    }

    // The levels a plan's ground runs over (always 0, the lawn, and 1, the ground inside the wall).
    sealed class Stack
    {
        public int lowest, steps;
        public static Stack Of(Plan plan, float halfX, float halfZ)
        {
            int lo = 0, hi = 1;
            for (float x = -halfX; x <= halfX; x += GroundCell)
                for (float z = -halfZ; z <= halfZ; z += GroundCell)
                {
                    int l = LevelAt(plan, x, z, halfX, halfZ);
                    lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l);
                }
            return new Stack { lowest = lo, steps = hi - lo };
        }
    }

    // FOR EACH STEP OF HEIGHT, HOW FAR A POINT IS INSIDE THE GROUND STANDING ABOVE IT: value t is the distance into the
    // ground at level lowest + t + 1 or higher (negative outside it), so the line where it is 0 is that step's curb — each
    // shape's true outline, straight, round or curved as the plan draws it.
    static float[] Levels(Plan plan, Stack stack, float x, float z, float halfX, float halfZ)
    {
        const float Far = 1000f;
        int steps = stack.steps, lowest = stack.lowest;
        float line = plan.wall.line, inside = Mathf.Min(halfX - line - Mathf.Abs(x), halfZ - line - Mathf.Abs(z));
        var g = new float[steps];
        // One shape laid `rel` curbs from the ground `from`: inside it, the ground stands that far from `from`.
        void Lay(float[] from, int rel, float outside)
        {
            // Over a unit outside the shape it moves no curb: the builder only needs to know the point is clear of every
            // curb by more than the band of stones, which it still is.
            if (outside > 1f) return;
            var src = (float[])from.Clone(); var was = (float[])g.Clone();
            for (int t = 0; t < steps; t++)
            {
                int u = t - rel;
                g[t] = Combine(was[t], u < 0 ? Far : u >= steps ? -Far : src[u], outside);
            }
        }
        // A bed's own ground: its paving's level all over it.
        float[] Flat(int level)
        {
            var f = new float[steps];
            for (int t = 0; t < steps; t++) f[t] = lowest + t + 1 <= level ? Far : -Far;
            return f;
        }
        // The areas' ground as the paths read it: the town ground's height everywhere, past the wall too.
        for (int t = 0; t < steps; t++) g[t] = lowest + t + 1 <= 1 ? Far : -Far;
        foreach (var a in plan.areas) if (!IsBed(a) && !IsPath(a) && Rel(a.level) != 0) Lay(g, Rel(a.level), OutsideArea(a, x, z));
        var areas = (float[])g.Clone();
        // The areas' ground as it is built: the town ground inside the wall, the lawn past it.
        for (int t = 0; t < steps; t++) { int m = lowest + t + 1; g[t] = m <= 0 ? Far : m == 1 ? inside : -Far; }
        foreach (var a in plan.areas) if (!IsBed(a) && !IsPath(a) && Rel(a.level) != 0) Lay(g, Rel(a.level), OutsideArea(a, x, z));
        foreach (var s in plan.streets) Lay(areas, Rel(s.level), DistToStreet(s, x, z) - s.width * 0.5f);
        foreach (var a in plan.areas) if (IsPath(a)) Lay(areas, Rel(a.level), OutsideArea(a, x, z));
        foreach (var a in plan.areas) if (IsBed(a)) Lay(Flat(a.stand), Rel(a.level), OutsideArea(a, x, z));
        return g;
    }

    // One step's value where a shape is laid: `outsideOf` the distance outside the shape, `outside` the value without it,
    // `inside` the value it lays. Above 0 where the ground reaches the step; its size how far the nearest place is where
    // that changes — the shape's edge counting only where the two sides really differ, so two grounds of one height meet
    // with no curb between them.
    static float Combine(float outside, float inside, float outsideOf)
    {
        float A = outside, B = inside, o = outsideOf;
        if (o < 0f)
            return B > 0f ? (A > 0f ? Mathf.Min(B, Mathf.Max(-o, A)) : Mathf.Min(B, -o))
                          : (A > 0f ? Mathf.Max(B, o) : Mathf.Max(Mathf.Min(A, o), B));
        return A > 0f ? (B > 0f ? Mathf.Min(A, Mathf.Max(o, B)) : Mathf.Min(A, o))
                      : (B > 0f ? Mathf.Max(A, -o) : Mathf.Max(A, Mathf.Min(-o, B)));
    }

    // Every street and area must say its level: a missing one would silently pick one.
    static void RequireLevels(Plan plan)
    {
        bool Ok(string l) => l == "high" || l == "normal" || l == "low";
        var missing = new List<string>();
        foreach (var s in plan.streets) if (!Ok(s.level)) missing.Add("street '" + s.name + "'");
        for (int i = 0; i < plan.areas.Length; i++)
            if (!Ok(plan.areas[i].level)) missing.Add($"area {i} ({plan.areas[i].look} '{plan.areas[i].label}')");
        if (missing.Count > 0) throw new System.Exception(plan.name + ": no level (high, normal or low) on " + string.Join(", ", missing));
    }

    // Every street and area, the lawn and the soil must wear a tile from the town's list, and every tile must exist.
    static void RequireTiles(Plan plan)
    {
        var listed = new HashSet<string>();
        var wrong = new List<string>();
        foreach (var t in plan.tiles ?? new PTile[0])
        {
            listed.Add(t.name);
            if (GroundTiles.Load(t.name) == null) wrong.Add($"tile '{t.name}' is not in {GroundTiles.Dir}");
            if (!(t.tiling > 0f)) wrong.Add($"tile '{t.name}' has no tiling");
        }
        void Need(string what, string tile) { if (string.IsNullOrEmpty(tile) || !listed.Contains(tile)) wrong.Add($"{what} wears '{tile}', not in the tile list"); }
        Need("the lawn", plan.lawnTile); Need("the soil", plan.soilTile);
        foreach (var s in plan.streets) Need("street '" + s.name + "'", s.tile);
        for (int i = 0; i < plan.areas.Length; i++) Need($"area {i} ({plan.areas[i].look} '{plan.areas[i].label}')", plan.areas[i].tile);
        if (wrong.Count > 0) throw new System.Exception(plan.name + ": " + string.Join("; ", wrong));
    }

    // How far a point is outside an area, negative inside it.
    static float OutsideArea(PArea a, float x, float z)
    {
        if (a.circle != null && a.circle.Length == 3) return new Vector2(x - a.circle[0], z - a.circle[1]).magnitude - a.circle[2];
        if (a.Ring != null && a.Ring.Length >= 6)
        {
            float best = float.MaxValue; var p = new Vector2(x, z);
            for (int k = 0, n = a.Ring.Length / 2; k < n; k++)
            {
                int k1 = (k + 1) % n;
                Vector2 q = new Vector2(a.Ring[2 * k], a.Ring[2 * k + 1]), qr = new Vector2(a.Ring[2 * k1], a.Ring[2 * k1 + 1]) - q;
                float t = Mathf.Clamp01(Vector2.Dot(p - q, qr) / Mathf.Max(1e-6f, qr.sqrMagnitude));
                best = Mathf.Min(best, (q + qr * t - p).magnitude);
            }
            return InArea(a, x, z) ? -best : best;
        }
        if (a.rect == null || a.rect.Length != 4) return float.MaxValue;
        float dx = Mathf.Max(a.rect[0] - x, x - a.rect[2]), dz = Mathf.Max(a.rect[1] - z, z - a.rect[3]);
        return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dz, 0f)).magnitude + Mathf.Min(Mathf.Max(dx, dz), 0f);
    }

    // The height of the ground under a point: a curb up on the raised side, and whatever the town's shape lifts it by.
    static float Height(Plan plan, Shape shape, float x, float z, float halfX, float halfZ) =>
        LevelAt(plan, x, z, halfX, halfZ) * GroundElevation.LevelHeight + shape.Lift(x, z);

    // The tile a point wears, laid in the order the looks are: the lawn, the areas, a mound's grass, the streets, the court,
    // a tree's bed of soil.
    static string TileName(Plan plan, float x, float z, float halfX, float halfZ)
    {
        float line = plan.wall.line;
        string t = plan.lawnTile;
        if (!(Mathf.Abs(x) > halfX - line || Mathf.Abs(z) > halfZ - line))
            foreach (var a in plan.areas) if (!IsBed(a) && InArea(a, x, z)) t = a.tile;
        if (plan.mounds != null)
            foreach (var m in plan.mounds)
            {
                Vector2 d = new Vector2(m.dir[0], m.dir[1]).normalized, q = new Vector2(x - m.x, z - m.z);
                float v = Vector2.Dot(q, d), u = Mathf.Abs(Vector2.Dot(q, new Vector2(d.y, -d.x)));
                if (v > 0.6f && v < m.length + 1.2f && u < m.width * 0.5f) t = plan.lawnTile;
            }
        foreach (var s in plan.streets) if (DistToStreet(s, x, z) <= s.width * 0.5f) t = s.tile;
        foreach (var a in plan.areas) if (a.look == "court" && InArea(a, x, z)) t = a.tile;
        foreach (var a in plan.areas) if (IsBed(a) && InArea(a, x, z)) t = plan.soilTile;
        return t;
    }

    // The look at a point: lawn outside the wall; inside, each of the plan's areas in its order, then its streets.
    // Built: as the ground is painted, a mound grassed and a tree's bed over everything; otherwise as the plan lays its areas.
    static string LookName(Plan plan, float x, float z, float halfX, float halfZ, bool built)
    {
        float line = plan.wall.line;
        bool outside = Mathf.Abs(x) > halfX - line || Mathf.Abs(z) > halfZ - line;
        var c = "grass";
        if (!outside)
            foreach (var a in plan.areas)
                if (InArea(a, x, z)) c = a.look;
        // A mound over the stream is grassed, from just behind its bridge to where it has eased back down.
        if (plan.mounds != null)
            foreach (var m in plan.mounds)
            {
                Vector2 d = new Vector2(m.dir[0], m.dir[1]).normalized, q = new Vector2(x - m.x, z - m.z);
                float v = Vector2.Dot(q, d), u = Mathf.Abs(Vector2.Dot(q, new Vector2(d.y, -d.x)));
                if (built && v > 0.6f && v < m.length + 1.2f && u < m.width * 0.5f) c = "grass";
            }
        foreach (var s in plan.streets)
            if (DistToStreet(s, x, z) <= s.width * 0.5f) c = "street";
        // The court sits over the streets that run into it.
        foreach (var a in plan.areas)
            if (a.look == "court" && InArea(a, x, z)) c = "court";
        // A tree's bed of soil sits over whatever it is cut into.
        if (built)
            foreach (var a in plan.areas)
                if ((a.look == "pit" || a.look == "planter") && InArea(a, x, z)) c = a.look;
        return c;
    }

    // TREES DON'T GROW IN PAVEMENT (the user, 2026-10-28): every tree or bush standing on paving gets a bed of soil round
    // its foot with a curb at its edge — sunk a curb below the raised paving, or a planter raised a curb above the
    // street-level square or a street. Worked out at every build from where each one stands, so a moved tree takes its
    // bed with it; a plan never holds them.
    static void Beds(Plan plan)
    {
        var kit = new Dictionary<string, PKit>();
        foreach (var k in plan.kit) kit[k.kind] = k;
        var areas = new List<PArea>(plan.areas);
        foreach (var p in plan.pieces)
        {
            string role = kit[p.kind].role;
            if (role != "tree" && role != "bush") continue;
            float size = role == "tree" ? 0.5f : 0.3f;
            // Read with the beds laid so far, so a tree standing in its neighbour's bed shares it.
            string look = LookName(plan, p.x, p.z, 24.5f, 31f, false);
            // Only paving needs a bed; grass, meadow and dirt are soil already. A bed sinks into raised paving and stands up
            // out of low paving.
            if (look != "stone" && look != "court" && look != "street") continue;
            int stand = LevelAt(plan, p.x, p.z, 24.5f, 31f);
            bool raised = stand >= 1;
            areas.Add(new PArea { look = raised ? "pit" : "planter", level = raised ? "low" : "high", stand = stand, label = "", tile = plan.soilTile, circle = new[] { p.x, p.z, size } });
            plan.areas = areas.ToArray();
        }
    }

    // The ground raised (or lowered, below 0) by hand at a point: the plan's grid of heights, read between its points.
    static float HandLift(Plan plan, float x, float z)
    {
        var l = plan.lift;
        if (l == null || l.h == null || l.nx < 2 || l.nz < 2 || l.h.Length != l.nx * l.nz) return 0f;
        float fx = Mathf.Clamp((x - l.x0) / l.step, 0f, l.nx - 1.001f), fz = Mathf.Clamp((z - l.z0) / l.step, 0f, l.nz - 1.001f);
        int i = (int)fx, j = (int)fz; float u = fx - i, v = fz - j;
        float a = l.h[j * l.nx + i], b = l.h[j * l.nx + i + 1], c = l.h[(j + 1) * l.nx + i], d = l.h[(j + 1) * l.nx + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    // The box round an area, south-west corner to north-east.
    static bool Box(PArea a, out float x0, out float z0, out float x1, out float z1)
    {
        x0 = z0 = float.MaxValue; x1 = z1 = float.MinValue;
        if (a.circle != null && a.circle.Length == 3) { x0 = a.circle[0] - a.circle[2]; x1 = a.circle[0] + a.circle[2]; z0 = a.circle[1] - a.circle[2]; z1 = a.circle[1] + a.circle[2]; return true; }
        if (a.rect != null && a.rect.Length == 4) { x0 = a.rect[0]; z0 = a.rect[1]; x1 = a.rect[2]; z1 = a.rect[3]; return true; }
        if (a.Ring == null || a.Ring.Length < 6) return false;
        for (int k = 0; k + 1 < a.Ring.Length; k += 2)
        {
            x0 = Mathf.Min(x0, a.Ring[k]); x1 = Mathf.Max(x1, a.Ring[k]); z0 = Mathf.Min(z0, a.Ring[k + 1]); z1 = Mathf.Max(z1, a.Ring[k + 1]);
        }
        return true;
    }

    static bool InArea(PArea a, float x, float z)
    {
        if (a.rect != null && a.rect.Length == 4) return x >= a.rect[0] && z >= a.rect[1] && x <= a.rect[2] && z <= a.rect[3];
        if (a.circle != null && a.circle.Length == 3) return (new Vector2(x - a.circle[0], z - a.circle[1])).sqrMagnitude <= a.circle[2] * a.circle[2];
        if (a.Ring != null && a.Ring.Length >= 6)
        {
            bool inside = false;
            for (int k = 0, n = a.Ring.Length / 2, m = n - 1; k < n; m = k++)
            {
                float xk = a.Ring[2 * k], zk = a.Ring[2 * k + 1], xm = a.Ring[2 * m], zm = a.Ring[2 * m + 1];
                if ((zk > z) != (zm > z) && x < (xm - xk) * (z - zk) / (zm - zk) + xk) inside = !inside;
            }
            return inside;
        }
        return false;
    }

    static float DistToStreet(PStreet s, float x, float z)
    {
        float best = float.MaxValue; var p = new Vector2(x, z);
        for (int k = 0; k + 3 < s.xz.Length; k += 2)
        {
            Vector2 a = new Vector2(s.xz[k], s.xz[k + 1]), b = new Vector2(s.xz[k + 2], s.xz[k + 3]), ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            best = Mathf.Min(best, (a + ab * t - p).magnitude);
        }
        return best;
    }

    // ---- the wall --------------------------------------------------------------------------------------------------

    // THE WALL: the lines drawn in the plan, any number, each open or closed into a loop. Its pieces run along each
    // straight stretch, one per WallPartLength, spread evenly; a tower stands at every point the plan gives one; a gate on
    // a line opens a gap plan.wall.gate wide there with a gate tower each side. Every piece stands on the higher ground
    // either side of it, so a wall along a curb stands on its top.
    static void Wall(Plan plan, Shape shape, Transform root, float halfX, float halfZ, StringBuilder report)
    {
        var wall = new GameObject("Wall").transform;
        wall.SetParent(root, false);
        var part = Load(WallPartPath);
        var kinds = new Dictionary<string, GameObject> { ["Wall_tower1"] = Load(WallTowerPath), ["Wall_tower2"] = Load(GateTowerPath) };
        float gap = plan.wall.gate;
        int parts = 0, towers = 0, opened = 0;
        float Ground(Vector2 at, Vector2 along)
        {
            var side = new Vector2(along.y, -along.x).normalized * 0.3f;
            return Mathf.Max(Height(plan, shape, at.x + side.x, at.y + side.y, halfX, halfZ), Height(plan, shape, at.x - side.x, at.y - side.y, halfX, halfZ));
        }
        void Tower(string kind, Vector2 at, Vector2 along, float rot)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (!kinds.TryGetValue(kind, out var prefab)) throw new System.Exception($"{plan.name}: a wall point names the tower \"{kind}\"; there are only {string.Join(", ", kinds.Keys)}");
            Put(wall, prefab, new Vector3(at.x, Ground(at, along), at.y), rot);
            towers++;
        }
        foreach (var line in plan.walls)
        {
            var path = WallPath(line);
            if (path.Count < 2) continue;
            int n = line.xz.Length / 2;
            string TowerAt(int k) => line.towers != null && k < line.towers.Length ? line.towers[k] : "";
            var s = new float[path.Count];
            for (int k = 1; k < path.Count; k++) s[k] = s[k - 1] + (path[k] - path[k - 1]).magnitude;
            float length = s[path.Count - 1];
            bool loop = path.Count > n;
            // Where along the line each gate on it opens.
            var gates = new List<float>();
            foreach (var g in plan.gates)
            {
                float best = float.MaxValue, at = 0f;
                for (int k = 0; k + 1 < path.Count; k++)
                {
                    Vector2 a = path[k], ab = path[k + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(new Vector2(g.x, g.z) - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    float d = (a + ab * t - new Vector2(g.x, g.z)).magnitude;
                    if (d < best) { best = d; at = s[k] + t * ab.magnitude; }
                }
                if (best <= GateOnLine) gates.Add(at);
            }
            gates.Sort();
            opened += gates.Count;
            // The stretches of wall between the gates, from a to b along the line (a loop's last one runs on past its
            // end); an end at a gate stands a gate tower.
            var runs = new List<(float a, float b, bool gateA, bool gateB)>();
            if (gates.Count == 0) runs.Add((0f, length, false, false));
            else if (!loop)
            {
                float a = 0f; bool ga = false;
                foreach (var c in gates) { runs.Add((a, c - gap / 2f, ga, true)); a = c + gap / 2f; ga = true; }
                runs.Add((a, length, ga, false));
            }
            else
                for (int k = 0; k < gates.Count; k++)
                    runs.Add((gates[k] + gap / 2f, (k + 1 < gates.Count ? gates[k + 1] : gates[0] + length) - gap / 2f, true, true));
            foreach (var (a, b, gateA, gateB) in runs)
            {
                if (b - a < 0.3f) continue;
                // The run's points: its two ends and every point of the line between them, each with its tower.
                var pts = new List<Vector2>(); var kind = new List<string>();
                Vector2 At(float d)
                {
                    d = loop ? Mathf.Repeat(d, length) : d;
                    int k = 0;
                    while (k + 2 < path.Count && s[k + 1] < d) k++;
                    float seg = s[k + 1] - s[k];
                    return Vector2.Lerp(path[k], path[k + 1], seg > 0f ? (d - s[k]) / seg : 0f);
                }
                bool whole = loop && gates.Count == 0;
                if (!whole) { pts.Add(At(a)); kind.Add(gateA ? "Wall_tower2" : TowerAt(0)); }
                for (int lap = 0; lap < (loop ? 2 : 1); lap++)
                    for (int k = 0; k < n; k++)
                    {
                        float d = s[k] + lap * length;
                        if (whole ? lap == 0 : d > a + 1e-4f && d < b - 1e-4f) { pts.Add(path[k]); kind.Add(TowerAt(k)); }
                    }
                if (!whole) { pts.Add(At(b)); kind.Add(gateB ? "Wall_tower2" : TowerAt(n - 1)); }
                int m = pts.Count;
                Vector2 Next(int k) => whole ? pts[(k + 1) % m] : pts[k + 1];
                Vector2 Prev(int k) => whole ? pts[(k + m - 1) % m] : pts[k - 1];
                // The pieces, along each straight stretch: a point the line runs straight through does not break it.
                int last = whole ? m : m - 1, from = 0;
                for (int k = 1; k <= last; k++)
                {
                    Vector2 here = pts[k % m];
                    bool straightOn = k < last && Straight(Prev(k % m) - here, Next(k % m) - here);
                    if (straightOn) continue;
                    Vector2 start = pts[from], dir = here - start;
                    float len = dir.magnitude;
                    if (len > 1e-4f)
                    {
                        dir /= len;
                        int count = len < 0.3f ? 0 : Mathf.CeilToInt(len / WallPartLength);
                        for (int q = 0; q < count; q++)
                        {
                            Vector2 at = start + dir * (len * (q + 0.5f) / count);
                            Put(wall, part, new Vector3(at.x, Ground(at, dir), at.y), PieceTurn(dir));
                        }
                        parts += count;
                    }
                    from = k % m;
                }
                // The towers: at a point between two stretches, facing out of the corner (Lastline's); at an end, facing
                // away from its wall; at a point the wall runs straight through, square to it.
                for (int k = 0; k < m; k++)
                {
                    bool end = !whole && (k == 0 || k == m - 1);
                    Vector2 here = pts[k];
                    if (end) { Vector2 other = k == 0 ? pts[1] : pts[m - 2]; Tower(kind[k], here, here - other, Turn(here - other)); continue; }
                    Vector2 u = (Prev(k) - here).normalized, v = (Next(k) - here).normalized;
                    Tower(kind[k], here, v, (u + v).magnitude < 0.05f ? Mathf.Repeat(Turn(v) - 90f, 180f) + 90f : Turn(-(u + v)));
                }
            }
        }
        report.AppendLine($"{plan.name}: wall of {plan.walls.Length} lines, {parts} pieces and {towers} towers, {opened} gates in it.");
    }

    // A wall line's points in order, a closed line's first point again at its end.
    static List<Vector2> WallPath(PWallLine line)
    {
        var path = new List<Vector2>();
        for (int k = 0; k + 1 < line.xz.Length; k += 2) path.Add(new Vector2(line.xz[k], line.xz[k + 1]));
        if (line.closed && path.Count >= 3) path.Add(path[0]);
        return path;
    }

    static bool Straight(Vector2 back, Vector2 on) =>
        back.sqrMagnitude > 1e-8f && on.sqrMagnitude > 1e-8f && Vector2.Dot(back.normalized, on.normalized) < -0.9999f;

    // The turn about y that faces along d: 0 north (+z), 90 east (+x).
    static float Turn(Vector2 d) => Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 360f);

    // A wall piece's turn along a stretch heading d, as Lastline's are laid: 0 along x, 270 along z, 315 and 45 on the
    // two diagonals.
    static float PieceTurn(Vector2 d) => Mathf.Repeat(Mathf.Repeat(Turn(d), 180f) - 90f, 360f);

    static GameObject Put(Transform parent, GameObject prefab, Vector3 at, float rot)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = at;
        go.transform.localRotation = Quaternion.Euler(0f, rot, 0f);
        return go;
    }

    static GameObject Load(string path) => Load<GameObject>(path);
    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new System.Exception("no asset " + path);

    // ---- the props -------------------------------------------------------------------------------------------------

    static void Props(Plan plan, Shape shape, Transform root, string assetDir, float halfX, float halfZ, StringBuilder report)
    {
        var props = new GameObject("Props").transform;
        props.SetParent(root, false);
        float natureScale = SeamlessWorldBuilder.NatureScale(new StringBuilder());
        var tint = new Tint(plan, assetDir);
        var kit = new Dictionary<string, PKit>();
        foreach (var k in plan.kit) kit[k.kind] = k;
        int placed = 0;
        foreach (var p in plan.pieces)
        {
            var k = kit[p.kind];
            bool nature = k.role == "tree" || k.role == "bush" || k.role == "effect";
            // On the ground under it: the paving a curb up, a street the ground's own height.
            var go = Put(props, Load(k.prefab.Replace("{n}", p.variant.ToString())), new Vector3(p.x, Height(plan, shape, p.x, p.z, halfX, halfZ), p.z), p.rot);
            if (k.scale > 0f) go.transform.localScale = Vector3.one * k.scale;
            // The kit's buildings and goods keep the kit's own size, as Lastline's do; the nature pack's plants stand at the
            // world's scale against a person, as everywhere in the world.
            if (nature) Natural(go, natureScale, k.role == "tree");
            // The town's tint on its bushes and drifting leaves, never on its trees, whose colour it is.
            if (k.role == "bush" || k.role == "effect") tint.Apply(go);
            // Blossom falling everywhere (the user, 2026-10-28): the pack's drifting leaves, three times as thick.
            if (k.role == "effect") Thicker(go, 3f);
            placed++;
        }
        int meadow = Meadows(plan, shape, props, natureScale, halfX, halfZ);
        int painted = Plants(plan, shape, props, natureScale, halfX, halfZ, out int bare);
        report.AppendLine($"{plan.name}: {placed} pieces from the plan, {meadow} pink meadow patches and {painted} painted plants"
                          + $"{(bare > 0 ? $" ({bare} painted where the ground is no longer grass or meadow, left out)" : "")}, plants at x{natureScale:0.###}{tint.Said}.");
    }

    // A TOWN'S TINT: the colour of one tree's blossom (the plan names the tree), worn by the town's bushes and its drifting
    // leaves — each on its own copy of the pack's material, kept in the town's folder, so the pack and every other place
    // that uses it are untouched. A plant's material takes the tree's top and bottom colours; a particle's its top colour.
    sealed class Tint
    {
        readonly Color top, bottom;
        readonly bool on;
        readonly string dir, id;
        readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        public string Said => on ? $", bushes and leaves tinted #{ColorUtility.ToHtmlStringRGB(top)}" : "";

        public Tint(Plan plan, string assetDir)
        {
            dir = assetDir; id = plan.id;
            if (string.IsNullOrEmpty(plan.tint)) return;
            foreach (var r in Load(plan.tint).GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_Top_Color") && m.HasProperty("_Bottom_Color") && m.GetTexture("_Texture") != null
                        && m.GetTexture("_Texture").name.Contains("Blossom"))
                    {
                        top = m.GetColor("_Top_Color"); bottom = m.GetColor("_Bottom_Color"); on = true;
                    }
            if (!on) throw new System.Exception(plan.tint + " has no blossom material to take the town's tint from");
        }

        public void Apply(GameObject go)
        {
            if (!on) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = Copy(mats[i]);
                r.sharedMaterials = mats;
            }
        }

        Material Copy(Material m)
        {
            if (copies.TryGetValue(m, out var c)) return c;
            bool plant = m.HasProperty("_Top_Color") && m.HasProperty("_Bottom_Color");
            bool particle = !plant && (m.HasProperty("_BaseColor") || m.HasProperty("_Color"));
            if (!plant && !particle) return copies[m] = m;
            c = new Material(m) { name = $"{m.name}_{id}" };
            Color Keep(Color want, Color had) => new Color(want.r, want.g, want.b, had.a);
            if (plant)
            {
                c.SetColor("_Top_Color", Keep(top, m.GetColor("_Top_Color")));
                c.SetColor("_Bottom_Color", Keep(bottom, m.GetColor("_Bottom_Color")));
            }
            else
            {
                if (m.HasProperty("_BaseColor")) c.SetColor("_BaseColor", Keep(top, m.GetColor("_BaseColor")));
                if (m.HasProperty("_Color")) c.SetColor("_Color", Keep(top, m.GetColor("_Color")));
            }
            AssetDatabase.CreateAsset(c, $"{dir}/{c.name}.mat");
            return copies[m] = c;
        }
    }

    static void Thicker(GameObject go, float times)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var em = ps.emission;
            em.rateOverTimeMultiplier *= times;
            var main = ps.main;
            main.maxParticles = Mathf.CeilToInt(main.maxParticles * times);
        }
    }

    // A nature piece the way the world builder sets its own: at the world's scale, walked through (its leaf trigger kept),
    // its effects scaled with it, and a shadow only from a tree.
    static void Natural(GameObject go, float scale, bool casts)
    {
        go.transform.localScale = Vector3.one * scale;
        WorldBuildUtil.SetLayerRecursive(go, 0);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) if (!col.isTrigger) Object.DestroyImmediate(col);
        if (!casts)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // THE PAINTED PLANTS (the town editor's plant brush): one plant in each painted cell, nudged off the cell's middle and
    // turned by the cell's own number, so it stands exactly where the editor drew it — on grass and meadow only.
    static int Plants(Plan plan, Shape shape, Transform props, float scale, float halfX, float halfZ, out int bare)
    {
        bare = 0;
        var grid = plan.plantGrid;
        if (grid == null || grid.runs == null || grid.runs.Length == 0 || plan.plants == null) return 0;
        var prefabs = new Dictionary<int, GameObject>();
        int n = 0, cell = 0;
        for (int r = 0; r + 1 < grid.runs.Length; r += 2)
        {
            int value = grid.runs[r], count = grid.runs[r + 1];
            for (int c = 0; c < count; c++, cell++)
            {
                if (value <= 0) continue;
                int i = cell % grid.nx, j = cell / grid.nx;
                if (value > plan.plants.Length) throw new System.Exception($"{plan.name}: a painted cell holds plant {value}, the list has {plan.plants.Length}");
                uint h = PlantHash(i, j);
                float x = grid.x0 + (i + 0.5f) * grid.step, z = grid.z0 + (j + 0.5f) * grid.step;
                var look = LookName(plan, x, z, halfX, halfZ, true);
                if (look != "grass" && look != "meadow") { bare++; continue; }
                x += ((h & 1023) / 1023f - 0.5f) * grid.step * 0.8f;
                z += (((h >> 10) & 1023) / 1023f - 0.5f) * grid.step * 0.8f;
                if (!prefabs.TryGetValue(value, out var prefab)) prefabs[value] = prefab = Load(plan.plants[value - 1].prefab);
                var go = Put(props, prefab, new Vector3(x, Height(plan, shape, x, z, halfX, halfZ), z), ((h >> 20) & 511) / 511f * 360f);
                Natural(go, scale, false);
                n++;
            }
        }
        return n;
    }

    // A painted cell's own number: the town editor works out the same one (plantHash), to draw its plant where it stands.
    static uint PlantHash(int i, int j)
    {
        unchecked
        {
            uint h = (uint)(i * 73856093) ^ (uint)(j * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }
    }

    // The plan's pink meadow beds, whatever their shape, each filled with the pack's pink meadow patches, edge to edge.
    static int Meadows(Plan plan, Shape shape, Transform props, float scale, float halfX, float halfZ)
    {
        var prefab = Load(MeadowPath);
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        probe.transform.localScale = Vector3.one * scale;
        var b = new Bounds(probe.transform.position, Vector3.zero);
        foreach (var r in probe.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        Object.DestroyImmediate(probe);
        float each = Mathf.Max(0.3f, Mathf.Min(b.size.x, b.size.z) * 0.8f);
        int n = 0;
        foreach (var a in plan.areas)
        {
            if (a.look != "meadow" || !Box(a, out var x0, out var z0, out var x1, out var z1)) continue;
            for (float z = z0; z <= z1; z += each)
                for (float x = x0; x <= x1; x += each)
                {
                    if (OutsideArea(a, x, z) > -each * 0.4f) continue;
                    var go = Put(props, prefab, new Vector3(x, Height(plan, shape, x, z, halfX, halfZ), z), (n * 61) % 360);
                    Natural(go, scale, false);
                    n++;
                }
        }
        return n;
    }
}
