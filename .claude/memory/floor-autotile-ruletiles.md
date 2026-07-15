---
name: floor-autotile-ruletiles
description: "Floor terrain auto-tiling via Unity RuleTiles generated from TilesetFloor.png (8 terrains, decoded layout)"
metadata: 
  node_type: memory
  type: project
  originSessionId: d0ccddb7-4bd1-48ef-a773-909e5e0a4608
---

Floor terrains auto-edge (correct outer/inner corners + edges) via Unity **RuleTile** (pkg `com.unity.2d.tilemap.extras`, already installed). Built by `Assets/Scripts/Editor/FloorRuleTileBuilder.cs` → menu **Tools ▸ unwritten ▸ Build Floor RuleTiles**; verify with **Floor RuleTile Test Patch** (paints all RuleTiles found in `Assets/Art/Tiles/Floor/` with a 1-cell hole + strip + dot, no manual painting). Output: `Assets/Art/Tiles/Floor/<Name>.asset`. Rebuild keeps GUIDs (`EditorUtility.CopySerialized` into existing).

**Source sheet:** `Assets/Art/NinjaAdventure/Backgrounds/Tilesets/TilesetFloor.png` — 352×417, 16px tiles, PPU 16, already sliced (Sprite Mode Multiple, `TilesetFloor_N`), 22 cols. **Custom layout, NOT a standard 47-blob** — decoded by pixel-sampling tile edges/corners (verified 8/8). It's a ~15-tile corner-correct autotile per terrain (edges + outer corners + 4 inner corners + center + strips), NOT a true 47-blob.

**8 terrains = 2 per band, bands every 7 rows** (origin = top-left cell, col,row): `Sand 0,0` · `Salmon 11,0` · `Dirt 0,7` · `DarkDirt 11,7` · `Snow 0,14` · `Mud 11,14` · `Ice 0,21` · `Lava 11,21`. All share the SAME relative cell map.

**Relative cell → rule** (offsets from each terrain's origin):
- core 3×3 `(0..2,0..2)` = outer NW/N/NE · W/center/E · SW/S/SE
- vertical strip col 3: `(3,0)` top `(3,1)` mid `(3,2)` bottom; isolated single `(3,3)`
- horizontal strip row 3: `(0,3)` Lcap `(1,3)` mid `(2,3)` Rcap
- **inner (concave) corners**: `(6,2)`=NW `(5,2)`=NE `(6,1)`=SW `(5,1)`=SE (rule = all 4 cardinals This + that diagonal NotThis)
- RuleTile order = most-specific first (isolated/strips → outer corners → edges → inner corners → default center). Floor = `ColliderType.None`.

**Gotchas:** Ice & Lava lack the isolated-single `(3,3)` and h-strip end-caps → those rare cases fall back to center fill (a null sprite just skips that one rule).

**⚠️ EDGE-RIM COMPATIBILITY (key art fact):** each terrain's edge tile is drawn against ONE fixed background, so a terrain only blends seamlessly when placed on THAT background — otherwise you get a hard rectangular seam. Verified by sampling the N-edge rim color: **only Dirt (rim 171,178,52) & DarkDirt (rim 105,153,60) have a GREEN GRASS rim** → these are THE grass-compatible patch/path terrains. Sand(249,163,88)/Salmon/Snow/Mud/Ice/Lava all fade into their OWN material → NOT valid as patches on grass; they're for maps whose BASE ground is that material. So on a grass map: `pathTile` + `patchTerrains` must be Dirt/DarkDirt only. (`DefaultPathTerrain="Dirt"`, `DefaultPatchTerrains={Dirt,DarkDirt}` reflect this.) There is NO standalone grass-fill tile in this sheet — base grass comes from another sheet (e.g. `TilesetNature`).

**Integration status (2026-06-16):** RuleTiles built + verified (8/8) AND wired into the generator. `TownTheme` has `pathTile` (TileBase) + `patchTerrains` (List<TileBase>) — typed TileBase, NOT RuleTile, so the runtime asm needs no 2D-Extras ref (RuleTile : TileBase, Inspector still accepts them). `MapRecipe` has `terrainPatchCount/MinSize/MaxSize`. `TownGenerator`: `PaintRoad` uses `pathTile` if set (1-wide spokes → auto-edged path); new `PlacePatches`/`GrowPatch` stage scatters organic blobs into `ground[]` (under roads/props, walkable, no solidity). Patches commit via existing `CommitArray` SetTile (RuleTiles auto-resolve). Backward-compatible: empty fields → old flat `rng.Pick`. **Auto-wiring:** Build Floor RuleTiles also assigns the theme's pathTile + patchTerrains — targets the TownTheme selected in the Project window, else the lone TownTheme if exactly one (else logs a skip). Defaults in `FloorRuleTileBuilder`: `DefaultPathTerrain="Dirt"`, `DefaultPatchTerrains={Dirt,DarkDirt}` (the rim-compatible grass set — edit these consts to change for other biomes). Water/lava patches are cosmetic/walkable (no hazard/solid yet).

**Edge-quality polish (2026-06-16, generator):** removed the jagged path look in 4 deterministic (seed-stable) steps in `TownGenerator`/`MapRecipe` — (1) `roadWidth` (recipe, default 2; `PaintRoadBrush` paints a disc per spine cell) gives diagonals body so they stop autotiling into a staircase; (2) `roadWander` (recipe, default 2.5; perpendicular sine added to the spoke march in `CarveSpokes`) meanders the centerline so road edges aren't ruler-straight diagonal blades — the monotonic `along` term still reaches the gate so connectivity holds; (3) `SmoothRoads` (add-only: fills any non-protected cell with ≥3 road neighbours) rounds wedge tips / closes 1-cell pinches without ever disconnecting; (4) patches now grow into a MASK then `SmoothPatchMask` (orthogonal majority rule, 2 passes — drop ≤1-neighbour spikes, fill ≥3-neighbour notches) before committing to `ground[]`. Verified visually: organic wandering roads + rounded clearings. ⚠️ Default changes only affect NEW recipes; on an existing `.asset` bump `roadWidth`/`roadWander`/`terrainPatchMinSize` by hand.

**⚠️ THE root-cause fix for hard "middle-tile-at-edge" seams (2026-06-16) — two parts:**
1. **Missing refresh.** Scripted `Tilemap.SetTile` in a loop does NOT re-resolve a RuleTile when LATER neighbours are added — early cells freeze against an incomplete neighbourhood, so the plain middle tile lands where an edge belongs. (Manual Tile-Palette painting refreshes every stroke, which is why hand-painting looked perfect — that isolation test, by the dev, is what cracked it.) **FIX:** call `tm.RefreshAllTiles()` on every tilemap AFTER the commit loop (`MapGeneratorWindow.Generate`). This makes the generator's auto-tiling identical to hand-painting.
2. **Split tilemaps.** A RuleTile only auto-tiles against neighbours on its OWN tilemap. Roads were committed to a separate "Roads" tilemap and patches to "Ground" — both Dirt → each saw only half the dirt shape → seams. **FIX:** commit `layout.road` onto the **Ground** tilemap too, so all same-material dirt shares one surface.

NOTE: the earlier shape work (meander/noise/smoothing) was NOT what fixed the hard edges — these two were. (It diagnosed cleanly: manual palette paint = perfect, so the tile/rules/SetTile were fine; only the refresh was missing.) **Hard-curve faceting** ceiling still exists (~15-tile sheet, not 47-blob) — that one is art, not the generator.

**Same-tile vs distinct terrain:** patches + paths sharing ONE RuleTile (Dirt) = seamless but visually indistinguishable (they merge into one dirt mass). To make terrain read as distinct from paths on a GRASS base, the only rim-compatible second material is **DarkDirt** (Dirt path + DarkDirt patch) — but Dirt and DarkDirt only blend with grass, NOT each other, so a thin green (grassy) verge appears where they touch. Seam-free alternative for ground variety = the **decor layer** (tall grass / flowers), not a second ground material.
