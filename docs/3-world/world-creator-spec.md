---
status: Both parts built; the pipeline has produced all 41 maps
owner: vince
last-updated: 2026-08-30
tags:
  - world-content
---

# World Creator — Spec

A two-part pipeline for authoring the connected world of *unwritten*. You paint a grid of maps and their warps in a fun browser tool, export one JSON file, and a single Unity orchestrator digests it — generating every map (via the existing `RecipeGenerator`) and wiring all the warp portals deterministically.

The JSON file is the **single source of truth** for world topology. Rebuilding from it is idempotent, so regenerating a map never strands its links — the killer flaw of the old per-scene `MapLinker` flow.

---

## :warning: v2 update (2026-06-27) — free-form, rectangular maps (CLEAN BREAK)

The original design (below) put maps on a **fixed square grid**. v2 removes the grid entirely:

- **Maps are free-placed** anywhere in world space and can be **any rectangular size** — width and height are independent **integers** (in world units). Square is just `w == h`.
- `**pos:[x,y]**` (float, top-left corner; `x`→east, `y`→south) + `**size:[w,h]**` (integer) replace `cell:[col,row]`. Per-map `size` drives `MapRecipe.sizeX`/`sizeZ` (already separate axes), so rectangular ground is free.
- **Every warp is explicitly anchored** on both ends (`anchorA`/`anchorB` = `{edge, offset}`). The adjacency-derived `offset`-only form and the top-level `worldSize` are **gone** — there's no grid to derive a facing edge from.
- **Clean break, no migration:** v1 grid files (`cell`) do **not** import. The world-map atlas is now composited by each map's world-space rectangle at one px/world-unit scale (was a grid of fixed squares).
- Square-map results are **identical** to v1 (shared `EdgePoint` rim geometry generalized to `halfX`/`halfZ`).

The sections below are kept for lineage; where they say "grid / cell / adjacency", read the v2 model above.

---

## Pipeline at a glance

1. **HTML World Creator** (browser) — paint cells on a grid (Town/Field/Dungeon/Special), drop blue-dot warps between them, Export Data → `world.json`.
2. `**world.json**` — the contract. Abstract topology only (types, cells, warps). No prefab/asset references.
3. **Unity World Creator** (one Editor window) — Import `world.json`, then **Build World**: for each map cell it assigns a seed and calls the Map Generator; for each warp it places paired `Portal3D` + arrival `SpawnPoint`; registers scenes in Build Settings; validates the graph.

Separation of concerns: the HTML tool is blind to Unity assets and only emits *intent*; Unity resolves intent into real prefabs, recipes, and scenes.

---

## Design decisions (locked)

- **Warp portals, not edge seams.** Authentic RO model = step on a warp pad → load linked map. Reuses the existing `Portal3D` trigger + `MapManager.Travel` runtime unchanged.
- **Seeds are assigned by Unity, not the HTML tool.** The orchestrator rolls a deterministic seed per map cell and passes it to the generator. Same `world.json` → same world. A map may carry an optional pinned seed to lock a look.
- **Path tile / path width default to seed-randomized in Unity**, with optional per-map override in the JSON. They are *map-level*, never per-portal.
- **Portals feed the generator as exit points.** A map's warp positions are handed to `RecipeGenerator` so the recipe routes its paths to *terminate at each doorway* — pads land on roads, not mid-field. (This is the most important mechanic.)
- **Adjacent + distant warps, one model.** Adjacent warps derive their edge/direction from grid neighbour coords. Distant warps (cross-map, non-neighbour) carry an explicit anchor (edge + offset) on each end. Same `portal` shape either way.
- **WorldGraph has zero runtime cost.** It is an author-time artifact only — read by the Editor tool at build time, never loaded or ticked at runtime.

---

## JSON schema (the contract)

The single file both tools agree on. Versioned so we can evolve it safely. **Current = v2 (free-form, rectangular).**

- `version` (int) — schema version. **Current `2`.**
- `maps` (array of map objects):
  - `id` (string) — unique, stable. Becomes the Unity scene name (`Assets/Scenes/<id>.unity`).
  - `pos` ([x, y], float) — top-left corner in world units. `x`→east, `y`→south. Free placement, no grid.
  - `size` ([w, h], **integer**) — map dimensions in world units. Independent axes (rectangular). Drives `MapRecipe.sizeX`/`sizeZ`.
  - `type` ("town" | "field" | "dungeon" | "special") — picks the recipe theme + colour.
  - `seed` (int, optional) — pin a look; omit to let Unity roll one.
  - `pathTile` ("road" | "soil" | "stones" | "rock", optional) — override; omit to randomize.
  - `pathWidth` (number, optional) — override; omit to randomize.
- `portals` (array of portal objects) — **all warps are explicitly anchored on both ends:**
  - `a` (string) — map id of endpoint A.
  - `b` (string) — map id of endpoint B.
  - `anchorA` / `anchorB` ({ edge: "N"|"E"|"S"|"W", offset: 0..1 }) — the doorway position on each map's rim. `offset` is the fraction along that edge.

A warp's `offset` places **both** the portal pad and the paired arrival spawn on each map's chosen edge, so you step on a pad → arrive at the matching spot on the destination's edge. Up to **~3 warps per map-pair** is a sensible cap (the HTML tool / validator nudge, not enforced by schema). There is no longer any `cell`, top-level `worldSize`, or adjacency-derived `offset`-only portal form.

### Example

```json
{
  "version": 2,
  "maps": [
    { "id": "town_0",  "pos": [0, 0],     "size": [60, 60], "type": "town" },
    { "id": "field_1", "pos": [0, -90],   "size": [90, 60], "type": "field", "seed": 67890 },
    { "id": "field_2", "pos": [-80, 10],  "size": [60, 40], "type": "field" },
    { "id": "cave_0",  "pos": [70, 20],   "size": [50, 80], "type": "dungeon" }
  ],
  "portals": [
    { "a": "town_0",  "b": "field_1",
      "anchorA": { "edge": "N", "offset": 0.35 }, "anchorB": { "edge": "S", "offset": 0.35 } },
    { "a": "town_0",  "b": "field_2",
      "anchorA": { "edge": "W", "offset": 0.5 },  "anchorB": { "edge": "E", "offset": 0.5 } },
    { "a": "town_0",  "b": "cave_0",
      "anchorA": { "edge": "E", "offset": 0.6 },  "anchorB": { "edge": "W", "offset": 0.5 } }
  ]
}
```

---

## Part 1 — HTML World Creator (browser tool)

A single self-contained HTML/JS/CSS file (no build step) that runs locally.

**Canvas (v2 — free-form)**

- A pannable/zoomable world-space canvas (scale = px per world unit); a faint 10-unit guide grid for alignment only — maps are NOT grid-locked.
- **Edit mode:** click empty space → a small menu (Town / Field / Dungeon / Special) → drops a default 60×60 map there. **Drag the body** to move it (position snaps to whole units). **Drag the 8 white handles** to resize — width/height **snap to integers** (min 16). Click a map → menu to retype or delete; `Del` removes the selected map.
- Maps are colour-coded rounded rectangles: **town = yellow, field = green, dungeon = purple, special = red** (matches the in-game portal colour convention), labelled `id` + `w×h`.

**Warps (v2 — one anchored model)**

- **Warp mode:** click map A then map B → a two-way warp with auto facing-edge anchors at offset 0.5 (dashed line + square end-caps). **Drag an end-cap** to slide that doorway along its map's edge (snaps to nearest edge + offset). **Click an end-cap** to delete the warp. `Esc` cancels a half-made link. (The old separate adjacent-dot "Warp" vs distant "Link" modes were unified — every warp is explicitly anchored now.)

**Data I/O**

- **Export** → downloads `world.json` (v2: `pos`/`size` + anchored `portals`).
- **Import** → upload a v2 `world.json` → rebuilds the whole world + warps, fully round-trips. v1 grid files (`cell`) are rejected with a friendly message.

**Status:** Part 1 complete (v2) — free-form rectangular maps + int-snap resize + anchored warps + Export/Import, all in `tools/world-creator/index.html`.

---

## Part 2 — Unity World Creator (Editor orchestrator)

One centralized `EditorWindow` (e.g. `Tools ▸ unwritten ▸ World ▸ World Creator`). It is the main orchestrator — it owns seeds and drives the Map Generator.

**Import + Build World**

- Load `world.json` (file picker), show a summary (N maps, M warps), validate before building.
- **Build World** (idempotent):
  1. For each `map`: assign a seed (use pinned `seed` or roll deterministically from `id`), resolve `type` → a `MapRecipe` template (theme: town = houses/trees/stone paths/grass, no mobs; field = scatter + mobs; dungeon = dungeon props/mobs), compute the map's exit points from its portals, and call the Map Generator with those params → bakes `Assets/Scenes/<id>.unity`.
  2. For each `portal`: place paired `Portal3D` + arrival `SpawnPoint` on both maps at the resolved edge/offset, reusing `RegionBuilder.EnsurePortal/EnsureSpawn` (idempotent) and the destination-colour convention.
  3. `RegisterInBuildSettings` for every scene.
  4. **Validate:** every warp two-way, every map reachable, no dangling spawn id, no door in a wall/off-ground.

**Map Generator changes (the one real engine change)**

- `RecipeGenerator` gains an **exit-points** input: a list of `{ edge, offset }` (+ resolved world position) where paths must terminate. The path router grows a route from the centre hub to each exit so portals sit on roads.
- The orchestrator passes `type`-derived recipe content, the assigned `seed`, and `pathTile`/`pathWidth` (override or randomized) into the generator.

**Open question (deferred):** exact recipe template per `type` — reuse/extend the existing `MapRecipe` assets vs. generate recipes on the fly. Decide when wiring Part 2.

---

## Why this is better than the old `MapLinker`

- Topology is authored in *one* place and is visualizable; today it's implicit across N scenes.
- Regeneration is safe — rebuild from JSON re-creates links correctly placed.
- Edges are meaningful (directional), not assigned by arbitrary `CountPortals` order.
- Built-in validation catches one-way links, orphans, and doors-in-walls.
- Paths actually lead to the doorways.

---

## Build plan

1. **Spec** (this doc). DONE
2. **HTML World Creator** — paint + colours + Export/Import + adjacent **and distant** warps. DONE
3. **Unity World Creator window** — import + validate + Build World loop (both adjacent + distant). DONE — `WorldCreator.cs`, `Tools ▸ unwritten ▸ World ▸ World Creator…`. Per-type content from **template recipe assets** wired into the window; each warp gets a unique paired spawn id `w{i}_from_{map}` so up to 3 stacked warps per border work (why `RegionBuilder.EnsurePortal`, which dedupes by destination, was *not* reused; Build World re-bakes scenes fresh so there's nothing stale to dedupe).
4. `**RecipeGenerator` exit-points** — paths terminate at portal positions (pads on roads, not mid-field). DONE — `PathRouting.BuildToExits` routes a road from a shared centre hub to each portal doorway (crossroads at the map's centre, where the plaza sits — no arrival point is placed there any more, see 2026-09-18 below); `Generate(r, exitPointsXZ)` overload; `WorldCreator.ComputeExits` feeds them, using the same `EdgePoint` as portal placement so pads land on road ends. Portal-less maps fall back to random `pathCount` routes. (Templates need `pathWidth > 0` for roads to render.)
5. Retire the old `MapLinkWindow` / `LinkMapsWindow` once Build World covers their job. **DONE** — neither exists in the project any more (checked 2026-08-30). Build World is the only linker.

**The pipeline has been run in anger:** 41 maps sit on disk today — 10 towns, 17 fields, 14 dungeons — all generated through it and all carrying real content.

---

## Related

- [Biome Mob Families — Tiered Spawning](biome-mob-families.md) — how the generator decides which mobs to scatter into a map it builds.
- [Story & Setting (Canon)](story-setting-canon.md) — the fiction the towns and dungeons are meant to carry.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the 41 maps this produced, and the M2 gap they still have (no quests, no boss placed).
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — the level bands a generated map spawns against.
- [Performance Checkup — My Routine](../1-planning/performance-checkup-routine.md) — the field maps this builds are where the stress test runs.

## Arrival points — centre ones removed (2026-09-18)

A map's only arrival points are the ones the World Creator places **beside each door**, one per portal. The map
generators used to plant one more at every map's exact centre, called `Start`, with nothing checking what stood
there — and the same generators put the plaza, its props and the treeline on that centre. Arriving on it dropped
a player through the floor and out of the world, or left them standing on a building.

At the user's instruction all 41 centre points were deleted from the maps and both generators stopped making one,
so a re-import or a regeneration cannot bring one back. Nothing else changed: `RegionBuilder.MakeSpawn` /
`EnsureSpawn` and the paired portal placement are exactly as they were.

**What now answers "where do I arrive?"** A door names its own point, as always. Anything arriving without naming
one — a teleport, a respawn, a brand-new character — takes the map's FIRST arrival point, which is a door's.
A map with no doors has no arrival point at all, and the arriving body is left where it already stood; 23 maps are
in that state (every dungeon but two, four fields, seven small towns) pending the respawn rule.
