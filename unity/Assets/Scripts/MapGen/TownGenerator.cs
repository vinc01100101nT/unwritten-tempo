using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The radial-village layout algorithm. Pure logic: given a <see cref="MapRecipe"/> (+ its
/// <see cref="TownTheme"/>) it returns a <see cref="TownLayout"/> — per-cell tiles for each layer
/// plus door/gate/spawn positions. No Unity scene APIs; <c>MapGeneratorWindow</c> commits it.
/// Deterministic per <c>recipe.seed</c>.
///
/// Pipeline: ground → spokes (roads) → plaza → boundary+gates → props (one generic placer for
/// houses/trees/wells/… by <see cref="PropPlacement"/>) → decor/scatter → spawns → connectivity.
///
/// Two invariants keep the town coherent — and they're GENERIC, so they hold for every kind of
/// object, not just houses:
///   • A stamped prop's solid cells (and the boundary) are <b>protected</b> — see
///     <see cref="TownLayout.protectedCells"/>.
///   • All road / path carving <b>routes around protected cells</b> (BFS), so a path is never
///     painted or punched through a placed object.
/// </summary>
public static class TownGenerator
{
    static readonly Vector2Int[] N4 =
        { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };

    public static TownLayout Generate(MapRecipe recipe)
    {
        var L = new TownLayout(recipe.width, recipe.height);
        if (recipe == null || recipe.theme == null)
        {
            Debug.LogError("[unwritten] MapRecipe / TownTheme missing — assign a theme on the recipe.");
            return L;
        }
        var t = recipe.theme;
        var rng = new MapRng(recipe.seed);
        L.center = new Vector2Int(recipe.width / 2, recipe.height / 2);

        FillGround(L, t, rng);
        PlacePatches(L, t, recipe, rng);           // organic terrain patches (auto-tiling RuleTiles) on the ground
        CarveSpokes(L, t, recipe, rng);            // also fills L.gates
        if (recipe.openCenter) CarvePlaza(L, t, recipe, rng);
        BuildBoundary(L, t, recipe, rng);          // boundary cells become protected
        PlaceProps(L, t, recipe, rng);             // generic: houses / trees / wells / … by placement
        SmoothRoads(L, t, rng);                    // add-only: round wedge tips / close pinches
        Scatter(L, t, recipe, rng);
        PlaceSpawns(L, recipe, rng);
        if (recipe.guaranteeConnectivity) EnsureConnectivity(L);
        return L;
    }

    // ---- terrain stages ------------------------------------------------------

    static void FillGround(TownLayout L, TownTheme t, MapRng rng)
    {
        for (int y = 0; y < L.height; y++)
            for (int x = 0; x < L.width; x++)
                L.ground[L.Idx(x, y)] = rng.Pick(t.groundTiles);
    }

    // Roads never overwrite a protected cell (placed prop / boundary) — that's the generic guard.
    static void PaintRoad(TownLayout L, TownTheme t, MapRng rng, Vector2Int c)
    {
        if (!L.In(c) || L.protectedCells.Contains(c)) return;
        // Prefer the auto-tiling path RuleTile (edges itself across the road network); else flat roadTiles.
        TileBase tile = t.pathTile != null ? t.pathTile : rng.Pick(t.roadTiles);
        if (tile != null) L.road[L.Idx(c)] = tile;
        L.ClearSolid(c);   // roads are walkable
    }

    // Paint a road "brush" — a filled disc of cells around `center`, sized by roadWidth. A 1-wide
    // diagonal spoke autotiles into a sharp staircase; widening to ≥2 gives the path enough body to
    // edge smoothly. width 1 = single cell (back-compat); 2 = a plus; 3 = a 3-wide block. Every cell
    // still goes through PaintRoad, so the widened road STILL routes around protected props/boundary.
    static void PaintRoadBrush(TownLayout L, TownTheme t, MapRng rng, Vector2Int center, int roadWidth)
    {
        if (roadWidth <= 1) { PaintRoad(L, t, rng, center); return; }
        float radius = roadWidth * 0.5f;
        int r = Mathf.CeilToInt(radius);
        float r2 = radius * radius + 0.01f;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (dx * dx + dy * dy <= r2)
                    PaintRoad(L, t, rng, new Vector2Int(center.x + dx, center.y + dy));
    }

    // Add-only road smoothing: fill any non-road, non-protected cell that has ≥3 road neighbours.
    // This rounds the inner corners / wedge tips where spokes converge and closes 1-cell pinch gaps.
    // Because it ONLY adds road (via the protected-safe PaintRoad), it can never disconnect the
    // network. Single pass — enough to soften corners without bloating the roads.
    static void SmoothRoads(TownLayout L, TownTheme t, MapRng rng)
    {
        var toAdd = new List<Vector2Int>();
        for (int y = 1; y < L.height - 1; y++)
            for (int x = 1; x < L.width - 1; x++)
            {
                var c = new Vector2Int(x, y);
                if (L.IsRoad(c) || L.protectedCells.Contains(c)) continue;
                int n = 0;
                foreach (var d in N4) if (L.IsRoad(c + d)) n++;
                if (n >= 3) toAdd.Add(c);
            }
        foreach (var c in toAdd) PaintRoad(L, t, rng, c);
    }

    // ---- terrain patches -----------------------------------------------------

    // Scatter organic blobs of an auto-tiling terrain RuleTile onto the GROUND layer. Each blob is a
    // single RuleTile, so it auto-edges against the surrounding base ground when committed. Patches are
    // purely cosmetic ground (walkable) and sit under roads/props, so no solidity/protection changes.
    static void PlacePatches(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng)
    {
        if (t.patchTerrains == null || t.patchTerrains.Count == 0 || recipe.terrainPatchCount <= 0) return;

        int lo = Mathf.Max(1, recipe.terrainPatchMinSize);
        int hi = Mathf.Max(lo, recipe.terrainPatchMaxSize);

        // Grow blobs into a mask FIRST, smooth the shape, THEN commit. Frontier growth throws out
        // 1-cell spikes/necks that the autotiler would render as sharp points; smoothing rounds them
        // off before they ever touch the ground layer.
        var patch = new Dictionary<Vector2Int, TileBase>();
        for (int i = 0; i < recipe.terrainPatchCount; i++)
        {
            var tile = t.patchTerrains[rng.Range(0, t.patchTerrains.Count)];
            if (tile == null) continue;
            var center = new Vector2Int(rng.Range(2, Mathf.Max(3, L.width - 2)),
                                        rng.Range(2, Mathf.Max(3, L.height - 2)));
            GrowPatch(L, rng, center, rng.Range(lo, hi + 1), recipe.terrainPatchNoise, tile, patch);
        }

        SmoothPatchMask(L, patch);
        foreach (var kv in patch) L.ground[L.Idx(kv.Key)] = kv.Value;   // ground layer = under roads/props
    }

    // Noise-warped radial blob → an organic, LOBED patch (~`size` cells) recorded into the shared
    // mask. The edge radius wobbles smoothly with angle via Perlin noise, so the boundary is a wavy
    // curve instead of a blocky grid-aligned rectangle (the old frontier-growth + CA smoothing made
    // straight axis-aligned runs / 90° corners). Star-convex → always connected, no holes or specks.
    // Deterministic: the only randomness is the per-patch Perlin offset drawn from the seeded rng.
    static void GrowPatch(TownLayout L, MapRng rng, Vector2Int center, int size, float noiseAmp,
                          TileBase tile, Dictionary<Vector2Int, TileBase> patch)
    {
        if (!Interior(L, center)) return;
        float baseR = Mathf.Max(1f, Mathf.Sqrt(size));   // ~radius for the target cell count
        float amp = Mathf.Clamp01(noiseAmp);
        const float lobes = 3f;                          // base bumps around the rim
        float ox = rng.Range(0f, 128f), oy = rng.Range(0f, 128f);   // per-patch noise window
        int rad = Mathf.CeilToInt(baseR * (1f + amp) + 1f);

        for (int dx = -rad; dx <= rad; dx++)
            for (int dy = -rad; dy <= rad; dy++)
            {
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
                // Two octaves of angular Perlin: big lobes + a higher-frequency wiggle. The wiggle
                // keeps the rim from ever having a long near-straight run — and near-straight runs
                // are exactly what rasterize into hard single-cell steps at high zoom.
                float n1 = Mathf.PerlinNoise(ox + cx * lobes, oy + cy * lobes);
                float n2 = Mathf.PerlinNoise(ox * 1.7f + cx * lobes * 3f, oy * 1.7f + cy * lobes * 3f);
                float nz = n1 * 0.6f + n2 * 0.4f;
                float edge = baseR * (1f - amp * 0.5f + amp * nz);   // radius warps with angle
                if (dist > edge) continue;
                var c = new Vector2Int(center.x + dx, center.y + dy);
                if (Interior(L, c)) patch[c] = tile;   // record into the mask
            }
    }

    // Morphological smoothing of the patch SHAPE (orthogonal majority rule, a couple of passes):
    // drop cells with ≤1 patch-neighbour (kills isolated spikes & 1-wide neck tips) and fill interior
    // cells with ≥3 patch-neighbours (rounds concave notches). A filled cell inherits a neighbour's
    // tile. Deterministic (no rng → same seed = same town) and purely cosmetic — patches carry no
    // solidity, so this can never affect walkability or roads.
    static void SmoothPatchMask(TownLayout L, Dictionary<Vector2Int, TileBase> patch)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var candidates = new HashSet<Vector2Int>(patch.Keys);
            foreach (var c in patch.Keys)
                foreach (var d in N4) candidates.Add(c + d);

            var remove = new List<Vector2Int>();
            var add = new Dictionary<Vector2Int, TileBase>();
            foreach (var c in candidates)
            {
                if (!Interior(L, c)) continue;
                int n = 0; TileBase nbTile = null;
                foreach (var d in N4)
                    if (patch.TryGetValue(c + d, out var nt)) { n++; if (nbTile == null) nbTile = nt; }

                if (patch.ContainsKey(c)) { if (n <= 1) remove.Add(c); }
                else if (n >= 3 && nbTile != null) add[c] = nbTile;
            }

            foreach (var c in remove) patch.Remove(c);
            foreach (var kv in add) patch[kv.Key] = kv.Value;
        }
    }

    static bool Interior(TownLayout L, Vector2Int c)
        => c.x >= 1 && c.y >= 1 && c.x < L.width - 1 && c.y < L.height - 1;

    // Radial roads from the centre to the map edges. The first `exitCount` roads' edge cells
    // become exits. Each spine cell is painted with a roadWidth brush, so diagonal roads get body
    // and autotile smoothly instead of a single-cell staircase.
    static void CarveSpokes(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng)
    {
        int spokes = Mathf.Max(1, recipe.roadCount);
        float jitter = rng.Range(0f, Mathf.PI * 2f);
        var edgeCells = new List<Vector2Int>();

        for (int i = 0; i < spokes; i++)
        {
            float ang = jitter + i * (Mathf.PI * 2f / spokes) + rng.Range(-0.15f, 0.15f);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var perp = new Vector2(-dir.y, dir.x);
            var origin = new Vector2(L.center.x + 0.5f, L.center.y + 0.5f);
            // A gentle perpendicular sine wave breaks the ruler-straight diagonal (the main "sharp
            // edge" left after widening) into a wandering path. The monotonic `along` term (dir * d)
            // still marches to the edge, so connectivity to the gate is preserved.
            float freq = rng.Range(0.18f, 0.34f);
            float phase = rng.Range(0f, Mathf.PI * 2f);
            Vector2Int last = L.center;

            int steps = L.width + L.height;
            for (int step = 0; step < steps; step++)
            {
                float d = step * 0.9f;
                Vector2 p = origin + dir * d + perp * (recipe.roadWander * Mathf.Sin(d * freq + phase));
                var cell = new Vector2Int(Mathf.RoundToInt(p.x - 0.5f), Mathf.RoundToInt(p.y - 0.5f));
                if (!L.In(cell)) continue;   // the sine may briefly swing off-map; keep marching
                PaintRoadBrush(L, t, rng, cell, recipe.roadWidth);
                last = cell;
            }
            edgeCells.Add(last);
        }

        int gateN = Mathf.Clamp(recipe.exitCount, 1, edgeCells.Count);
        for (int i = 0; i < gateN; i++)
        {
            var cell = edgeCells[i];
            var outward = CardinalToEdge(L, cell);
            L.gates.Add(new Gate { cell = cell, outward = outward, id = "Gate_" + GateName(outward) + i });
        }
    }

    static void CarvePlaza(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng)
    {
        int r = Mathf.Max(0, recipe.openCenterSize);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                PaintRoad(L, t, rng, new Vector2Int(L.center.x + dx, L.center.y + dy));
            }
        // The centrepiece (well / statue) is now a PlazaCenter PropTemplate, placed in PlaceProps.
    }

    // Edge ring, with gaps left at each gate. Treeline → tree layer; others → solid building layer.
    static void BuildBoundary(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng)
    {
        if (recipe.boundary == BoundaryStyle.None) return;
        bool treeline = recipe.boundary == BoundaryStyle.Treeline;

        for (int x = 0; x < L.width; x++)
        {
            TryBoundary(L, t, recipe, rng, new Vector2Int(x, 0), treeline);
            TryBoundary(L, t, recipe, rng, new Vector2Int(x, L.height - 1), treeline);
        }
        for (int y = 0; y < L.height; y++)
        {
            TryBoundary(L, t, recipe, rng, new Vector2Int(0, y), treeline);
            TryBoundary(L, t, recipe, rng, new Vector2Int(L.width - 1, y), treeline);
        }
    }

    static void TryBoundary(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng, Vector2Int c, bool treeline)
    {
        foreach (var g in L.gates)
            if (Mathf.Abs(c.x - g.cell.x) <= 1 && Mathf.Abs(c.y - g.cell.y) <= 1) return;   // leave the gate gap

        var tile = rng.Pick(t.boundaryTiles);
        if (tile == null) return;
        if (treeline) L.tree[L.Idx(c)] = tile;   // treeline = walk-under Obstacles layer
        else L.building[c] = tile;
        L.AddProtectedSolid(c);   // the wall/treeline is immovable — carving routes around it
        foreach (var h in PropTemplate.HalfCellsOf(c)) L.AddCollision(h);   // edge blocks (half-cells)
    }

    // ---- generic prop placement ----------------------------------------------

    // One placer for every object. What a prop IS comes from its captured layers; WHERE it goes
    // comes from its PropPlacement. Adding a new kind of object = capture + tag, never new code.
    static void PlaceProps(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng)
    {
        if (t.props == null || t.props.Count == 0) return;

        var by = new Dictionary<PropPlacement, List<PropTemplate>>();
        foreach (var p in t.props)
        {
            if (p == null || p.cells == null || p.cells.Count == 0) continue;
            if (!by.TryGetValue(p.placement, out var list)) by[p.placement] = list = new List<PropTemplate>();
            list.Add(p);
        }

        PlacePlazaCenter(L, rng, by);
        PlaceOnLots(L, t, recipe, rng, by);
        PlaceOnOpenGround(L, recipe, rng, by);
        PlaceRoadside(L, rng, by);
    }

    static void PlacePlazaCenter(TownLayout L, MapRng rng, Dictionary<PropPlacement, List<PropTemplate>> by)
    {
        if (!by.TryGetValue(PropPlacement.PlazaCenter, out var list) || list.Count == 0) return;
        var prop = WeightedPick(list, rng);
        if (prop == null) return;

        var origin = new Vector2Int(L.center.x - prop.size.x / 2, L.center.y - prop.size.y / 2);
        for (int dx = 0; dx < prop.size.x; dx++)
            for (int dy = 0; dy < prop.size.y; dy++)
            {
                var c = new Vector2Int(origin.x + dx, origin.y + dy);
                if (L.In(c)) L.road[L.Idx(c)] = null;   // clear the plaza road beneath the centrepiece
            }
        Stamp(L, prop, origin);
    }

    static void PlaceOnLots(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng,
                            Dictionary<PropPlacement, List<PropTemplate>> by)
    {
        if (!by.TryGetValue(PropPlacement.Lot, out var list) || list.Count == 0) return;

        int maxW = 1, maxH = 1;
        foreach (var p in list) { maxW = Mathf.Max(maxW, p.size.x); maxH = Mathf.Max(maxH, p.size.y); }
        int stepX = maxW + 2, stepY = maxH + 2;   // +2 = the ≥1-cell gap on each side
        float placeChance = 0.20f + 0.80f * Mathf.Clamp01(recipe.buildingDensity);

        for (int sy = 2; sy < L.height - maxH - 2; sy += stepY)
            for (int sx = 2; sx < L.width - maxW - 2; sx += stepX)
            {
                if (!rng.Chance(placeChance)) continue;
                var prop = WeightedPick(list, rng);
                if (prop == null) continue;
                var origin = new Vector2Int(sx + rng.Range(0, 2), sy + rng.Range(0, 2));
                if (!CanPlace(L, prop, origin)) continue;
                Stamp(L, prop, origin);

                // Entrance: keep the cell just outside the anchor walkable, then carve a path to a road.
                var approach = new Vector2Int(origin.x + prop.anchorCell.x, origin.y + prop.anchorCell.y - 1);
                if (L.In(approach)) { L.ClearSolid(approach); L.doors.Add(approach); }
                ConnectAnchorToRoad(L, t, recipe, rng, approach);
            }
    }

    static void PlaceOnOpenGround(TownLayout L, MapRecipe recipe, MapRng rng,
                                  Dictionary<PropPlacement, List<PropTemplate>> by)
    {
        if (!by.TryGetValue(PropPlacement.OpenGround, out var list) || list.Count == 0) return;

        float density = 0.10f * Mathf.Clamp01(recipe.treeDensity);   // direct: higher slider = more trees
        int attempts = Mathf.RoundToInt(L.width * L.height * density);
        for (int i = 0; i < attempts; i++)
        {
            var prop = WeightedPick(list, rng);
            if (prop == null) continue;
            int ox = rng.Range(2, Mathf.Max(3, L.width - prop.size.x - 2));
            int oy = rng.Range(2, Mathf.Max(3, L.height - prop.size.y - 2));
            var origin = new Vector2Int(ox, oy);
            if (CanPlace(L, prop, origin)) Stamp(L, prop, origin);   // no road link for open-ground props
        }
    }

    static void PlaceRoadside(TownLayout L, MapRng rng, Dictionary<PropPlacement, List<PropTemplate>> by)
    {
        if (!by.TryGetValue(PropPlacement.Roadside, out var list) || list.Count == 0) return;

        var roads = new List<Vector2Int>();
        for (int y = 0; y < L.height; y++)
            for (int x = 0; x < L.width; x++)
            {
                var c = new Vector2Int(x, y);
                if (L.IsRoad(c)) roads.Add(c);
            }
        foreach (var rc in roads)
        {
            if (!rng.Chance(0.08f)) continue;
            var prop = WeightedPick(list, rng);
            if (prop == null) continue;
            foreach (var d in N4)
            {
                var origin = rc + d;
                if (CanPlace(L, prop, origin)) { Stamp(L, prop, origin); break; }
            }
        }
    }

    static bool CanPlace(TownLayout L, PropTemplate p, Vector2Int origin)
    {
        // Footprint + a 1-cell margin must be free of roads, solids (incl. protected) and out-of-bounds.
        for (int dx = -1; dx <= p.size.x; dx++)
            for (int dy = -1; dy <= p.size.y; dy++)
            {
                var c = new Vector2Int(origin.x + dx, origin.y + dy);
                if (!L.In(c)) return false;
                if (L.solid.Contains(c)) return false;
                if (L.road[L.Idx(c)] != null) return false;
            }
        return true;
    }

    static void Stamp(TownLayout L, PropTemplate p, Vector2Int origin)
    {
        foreach (var cell in p.cells)
        {
            var c = new Vector2Int(origin.x + cell.pos.x, origin.y + cell.pos.y);
            if (!L.In(c) || cell.tile == null) continue;
            string layer = (cell.layer ?? "").ToLowerInvariant();
            if (layer.Contains("decor")) L.decor[L.Idx(c)] = cell.tile;
            else if (layer.Contains("overhead") || layer.Contains("roof")) L.overhead[L.Idx(c)] = cell.tile;
            else if (layer.Contains("ground") || layer.Contains("floor")) L.ground[L.Idx(c)] = cell.tile;
            else if (layer.Contains("tree") || layer.Contains("obstacle") || layer.Contains("foliage") || layer.Contains("bush"))
            { L.tree[L.Idx(c)] = cell.tile; L.AddProtectedSolid(c); }
            else { L.building[c] = cell.tile; L.AddProtectedSolid(c); }   // default: solid building
        }

        // Real in-game collision = the prop's resolved footprint (Auto/Custom/None) in HALF-cells. Kept
        // SEPARATE from the layout's `solid`/protected sets above (those still treat the whole footprint as
        // occupied so roads route around it); only THIS becomes a collider, pre-baked into the Collision
        // object. The prop's full-cell origin maps to half-cell origin*2; offsets are already half-cells.
        foreach (var cc in p.CollisionFootprint())
            L.AddCollision(new Vector2Int(origin.x * 2 + cc.x, origin.y * 2 + cc.y));
    }

    static PropTemplate WeightedPick(List<PropTemplate> list, MapRng rng)
    {
        if (list == null || list.Count == 0) return null;
        float total = 0f;
        foreach (var p in list) total += Mathf.Max(0f, p.weight);
        if (total <= 0f) return list[rng.Range(0, list.Count)];
        float r = rng.Range(0f, total);
        foreach (var p in list) { r -= Mathf.Max(0f, p.weight); if (r <= 0f) return p; }
        return list[list.Count - 1];
    }

    // ---- decoration & spawns -------------------------------------------------

    static void Scatter(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng)
    {
        for (int y = 1; y < L.height - 1; y++)
            for (int x = 1; x < L.width - 1; x++)
            {
                var c = new Vector2Int(x, y);
                if (L.solid.Contains(c) || L.IsRoad(c)) continue;

                bool nearRoad = NearRoad(L, c);
                float decorP = nearRoad ? recipe.roadDecor : recipe.fieldDecor;
                if (rng.Chance(decorP * 0.4f))
                {
                    var d = rng.Pick(t.decorTiles);
                    if (d != null) L.decor[L.Idx(c)] = d;
                }
            }
    }

    static void PlaceSpawns(TownLayout L, MapRecipe recipe, MapRng rng)
    {
        for (int i = 0; i < recipe.npcSpawnCount; i++)
        {
            var c = RandomWalkableNear(L, L.center, Mathf.Max(3, recipe.openCenterSize + 4), rng);
            if (c.HasValue) L.npcSpawns.Add(c.Value);
        }
        for (int i = 0; i < recipe.monsterSpawnerCount; i++)
        {
            var edge = new Vector2Int(rng.Range(2, L.width - 2), rng.Range(2, L.height - 2));
            var c = RandomWalkableNear(L, edge, 5, rng);
            if (c.HasValue) L.monsterSpawns.Add(c.Value);
        }
    }

    // ---- connectivity (routes around protected cells) ------------------------

    // Flood-fill from centre over walkable cells; for any door/gate/spawn that's cut off, carve a
    // corridor to the reachable set that goes AROUND placed props/boundary (never through them).
    static void EnsureConnectivity(TownLayout L)
    {
        var reachable = Flood(L, L.center);
        var targets = new List<Vector2Int>();
        targets.AddRange(L.doors);
        targets.AddRange(L.npcSpawns);
        targets.AddRange(L.monsterSpawns);
        foreach (var g in L.gates) targets.Add(g.cell);

        foreach (var target in targets)
        {
            if (reachable.Contains(target)) continue;
            if (CarveAround(L, target, reachable))
                reachable = Flood(L, L.center);   // re-expand after carving
        }
    }

    static HashSet<Vector2Int> Flood(TownLayout L, Vector2Int start)
    {
        var seen = new HashSet<Vector2Int>();
        if (!L.Walkable(start)) return seen;
        var q = new Queue<Vector2Int>();
        q.Enqueue(start); seen.Add(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in N4)
            {
                var nb = c + d;
                if (L.Walkable(nb) && seen.Add(nb)) q.Enqueue(nb);
            }
        }
        return seen;
    }

    // BFS from `target` to the reachable set through non-protected cells, then clear that corridor
    // (clears expendable scatter solids; protected props/boundary are excluded from the search).
    static bool CarveAround(TownLayout L, Vector2Int target, HashSet<Vector2Int> reachable)
    {
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var seen = new HashSet<Vector2Int> { target };
        var q = new Queue<Vector2Int>(); q.Enqueue(target);
        bool found = false; Vector2Int hit = target;

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (reachable.Contains(c)) { hit = c; found = true; break; }
            foreach (var d in N4)
            {
                var nb = c + d;
                if (!L.In(nb) || seen.Contains(nb)) continue;
                if (L.protectedCells.Contains(nb)) continue;   // never carve through props / boundary
                seen.Add(nb); prev[nb] = c; q.Enqueue(nb);
            }
        }
        if (!found) return false;   // boxed in by protected cells only — leave it (rare)

        var cur = hit;
        while (cur != target)
        {
            L.ClearSolid(cur);
            if (!prev.TryGetValue(cur, out var p)) break;
            cur = p;
        }
        L.ClearSolid(target);
        return true;
    }

    // Carve a path from a Lot prop's entrance to the nearest road, routing around solids/protected.
    static void ConnectAnchorToRoad(TownLayout L, TownTheme t, MapRecipe recipe, MapRng rng, Vector2Int approach)
    {
        if (!L.In(approach) || L.IsRoad(approach)) return;
        var path = BfsToRoad(L, approach);
        if (path == null) return;   // EnsureConnectivity is the safety net
        foreach (var c in path) PaintRoadBrush(L, t, rng, c, recipe.roadWidth);
    }

    // Shortest path of walkable/ground cells from `start` to the nearest road, going around any
    // solid (protected or not). Returns the non-road cells to paint as road, or null if unreachable.
    static List<Vector2Int> BfsToRoad(TownLayout L, Vector2Int start)
    {
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var seen = new HashSet<Vector2Int> { start };
        var q = new Queue<Vector2Int>(); q.Enqueue(start);
        bool found = false; Vector2Int hit = start;

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (L.IsRoad(c)) { hit = c; found = true; break; }
            foreach (var d in N4)
            {
                var nb = c + d;
                if (!L.In(nb) || seen.Contains(nb)) continue;
                if (L.protectedCells.Contains(nb)) continue;            // around props / boundary
                if (!L.IsRoad(nb) && L.solid.Contains(nb)) continue;    // and around other solids
                seen.Add(nb); prev[nb] = c; q.Enqueue(nb);
            }
        }
        if (!found) return null;

        var path = new List<Vector2Int>();
        var cur = hit;
        while (cur != start)
        {
            if (!L.IsRoad(cur)) path.Add(cur);   // the road `hit` itself is skipped
            if (!prev.TryGetValue(cur, out var p)) break;
            cur = p;
        }
        path.Add(start);
        return path;
    }

    // ---- small helpers -------------------------------------------------------

    static bool NearRoad(TownLayout L, Vector2Int c)
    {
        foreach (var d in N4) if (L.IsRoad(c + d)) return true;
        return false;
    }

    static Vector2Int? RandomWalkableNear(TownLayout L, Vector2Int origin, int radius, MapRng rng)
    {
        for (int tries = 0; tries < 30; tries++)
        {
            var c = new Vector2Int(origin.x + rng.Range(-radius, radius + 1),
                                   origin.y + rng.Range(-radius, radius + 1));
            if (L.Walkable(c) && !L.IsRoad(c)) return c;
        }
        return null;
    }

    static Vector2Int CardinalToEdge(TownLayout L, Vector2Int c)
    {
        int distL = c.x, distR = L.width - 1 - c.x, distB = c.y, distT = L.height - 1 - c.y;
        int min = Mathf.Min(Mathf.Min(distL, distR), Mathf.Min(distB, distT));
        if (min == distL) return new Vector2Int(-1, 0);
        if (min == distR) return new Vector2Int(1, 0);
        if (min == distB) return new Vector2Int(0, -1);
        return new Vector2Int(0, 1);
    }

    static string GateName(Vector2Int outward)
    {
        if (outward.x < 0) return "W";
        if (outward.x > 0) return "E";
        if (outward.y < 0) return "S";
        return "N";
    }
}
