---
name: map-generator-tool
description: Procedural Town Map Generator (VIN-13) — editor-tool plan + Phase 0 done
metadata: 
  node_type: memory
  type: project
  originSessionId: 3d53d641-bde3-4961-90d3-7fe673914c12
---

> **⚠️ HISTORICAL (2D). Superseded 2026-06-22 by [[recipe-generator]].** This describes the OLD 2D tilemap generator that was retired at the 2.5D pivot. The current 3D world generator is `MapRecipe` + `RecipeGenerator`. This note is kept only for lineage (the "recipe asset + seed → reproducible map" idea carried forward).

Building an **editor-time** map generator for *unwritten* that bakes **permanent** town scenes from a `MapRecipe` (params + seed). NOT runtime/roguelike. Tracked by issue **VIN-13**; full spec is doc id `q178ppzhynajt660nadeh6ras988n88a` ("Procedural Town Map Generator — Spec").

**Key decisions:** buildings are **tile-stamp templates** captured from hand-painted houses (the House set is 648 individual tiles — can't auto-assemble a coherent house); first layout = **radial / plaza village**; output to a **fresh additive scene** under `Systems`; per-town **recipe asset** for re-roll + hand-finish.

**Depth/collision is PRE-BAKED at generate time** (2026-06-16, perf — [[performance-first-prebake]]): the generator calls `DepthSortRuntime.BakeObstacles()` so the saved scene already holds static foot-sorted prop objects (fixed sort order, no per-frame sorter), and materialises collision into ONE merged `Collision` tilemap (`CollisionLayerTools`, CompositeCollider2D). House tiles → **"Buildings"-named** tilemap, trees → "Obstacles/Trees"; keep a **≥1-cell gap** between objects (4-connected flood fill). Per-prop collision is authorable on `PropTemplate` (`collisionMode` Auto/Custom/None + painted `collisionCells`; Auto = building full / tree bottom-row). See [[ysort-depth]].

**Phases P0–P3 DONE — the generator is built and working (2026-06-16).** `Tools ▸ unwritten ▸ Generate Town` (`Editor/MapGeneratorWindow.cs`) bakes a clean organic town from a `MapRecipe`: grass base, auto-tiled dirt roads + patches, houses, trees, edge treeline, gate Portals + spawns. **Generate Preview** builds an unsaved preview (and now CLOSES any other open scene first, so a leftover saved town can't render over it — that overlap was faking "sharp edges"); **Save Town to Assets/Scenes** keeps it. Same Seed = same town.

**Generic objects:** `HouseTemplate` → **`PropTemplate`** (same GUID, migrated) — every discrete object (house/tree/well/…) is a captured multi-cell prop with a `placement` (`Lot`/`OpenGround`/`PlazaCenter`/`Roadside`) + weight; menu `Capture/Stamp Prop Template`. `TownGenerator.PlaceProps` is the one generic placer; roads BFS-route AROUND `TownLayout.protectedCells` so they never cut through props.

**⭐ Terrain auto-tiling was the hard problem — SOLVED.** Hard "middle-tile-at-edge" seams were (1) scripted `SetTile` not refreshing RuleTiles → fixed with `RefreshAllTiles()` after commit, and (2) roads + patches on SEPARATE tilemaps → fixed by committing all dirt onto the ONE Ground tilemap. Full detail in [[floor-autotile-ruletiles]].

**⚠️ MapRecipe fields were renamed to plain English (2026-06-16) — use the NEW names** (old values auto-migrate via `[FormerlySerializedAs]`): `townDensity`→**`buildingDensity`**, NEW **`treeDensity`** (trees were inversely coupled to density — a bug, now decoupled & direct), `spokeCount`→**`roadCount`**, `centralPlaza`→**`openCenter`**, `plazaRadius`→**`openCenterSize`**, `gateCount`→**`exitCount`**, `streetDecor`/`yardDecor`→**`roadDecor`**/**`fieldDecor`**. Unchanged: `roadWidth`, `roadWander`, `terrainPatch{Count,MinSize,MaxSize,Noise}`. Trade-off: buildings can't sit on roads, so many roads / high `roadWander` = fewer houses; lower `roadCount` for a packed town.

**Open design decision (dev hasn't picked):** patches + paths use the SAME Dirt RuleTile so they merge into one dirt mass. To make terrain distinct on grass: **A)** DarkDirt patches (thin grassy seam where they touch), **B)** use decor/tall-grass for variety (seam-free), or **C)** leave merged. P4 (field/cave themes) reuses the framework.
