# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `feature/map-sidecar-and-biome-html` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- Effort dial: auto-GO Low/Medium; PAUSE >Medium until user sets dial + GO. Tag every reply `**EFFORT: X**` first line.
- No Play mode without being asked. Prefer `Tools ▸ unwritten` editor tools; update `Editor/TOOLS.md` when adding menu items.
- Unity edits at `unity/Assets/Scripts/`. Read `PERFORMANCE.md` before runtime C#.

## Current task — Generated maps editable (sidecar) + biome in World Creator HTML (DONE; compiled clean, 0 errors)
Workflow goal: World Creator owns generation (fields auto), Map Painter owns hand-edits. Generic maps fully auto;
edit one by importing its sidecar into the painter. Towns still painted from scratch.

### Feature 1 — Map Painter sidecar on every bake (round-trip)
Every `RecipeGenerator.Generate` (and so World Creator **Build World**) writes `tools/map-painter/exports/<name>.map.json`
(outside Assets/). Open via painter **Import map…** → edit → re-bake with `MapPainterImporter`.
- NEW `Editor/MapSidecar.cs` — schema DTOs; `ObjectFromWorld` inverts importer transforms (`painterX=worldX+sizeX/2`,
  `painterZ=sizeZ/2−worldZ`, `yaw=worldYaw−forwardOffset`); `RasterizeGround` samples a `GroundSampler` per painter
  cell (inverts cz flip), dominant channel→brush, grass omitted.
- `Editor/PxltgrGroundBlend.cs` — extracted `MakeProceduralSampler(seed,dirtAmount,paths,pathWidth,hardEdge)`; `LayPlot`
  now just calls it → sidecar ground == baked ground exactly.
- `Editor/RecipeGenerator.cs` — captures every placed prop (incl. wall pieces; reserved "wall"→"wall_pieces" so they
  re-import as exact objects not autotiled) into `_sidecarObjects`; `WriteSidecar` after save. `BrushAndOffset` maps a
  group→painter brush id + palette `forwardOffset`.
- Lossy-by-design (painter limits, intended): no per-instance scale (re-import seats at palette `defaultScale` → flower/
  plant scale-jitter stays randomized by the generator, NOT in the data layer — user confirmed), no mob/biome layer.

### Feature 2 — biome per map in World Creator HTML (`tools/world-creator/index.html`)
Unity side was ALREADY done (`WorldCreator.MapJson.biome` + `TryParseBiome` + applies `clone.biome`). Added the only
missing half — authoring: map context-menu **Biome** section (grassland/snow/desert/volcanic/corrupted), exported in
`world.json` (omitted when grassland=default), carried through import/copy/paste, rendered as a fill wash + name label.

### NOT done / next
- Visual judgment (does the round-tripped map look right) — USER drives via Generate/Build World → painter Import.
- Memory updated: `map-painter-pipeline.md`, `world-creator-pipeline.md`.

## Other open threads (details in memory files — pull only if resuming)
- **World Creator v2** (free-form rectangular maps): DONE & landed. Memory `world-creator-pipeline.md`.
- **Mobs/items/maps → data tables** (`mob-item-map-data-tables.md`): NEXT = backfill `id` on existing Item+MapRecipe assets.
- **Billboard additional lights** (`billboard-additional-lights.md`): DONE, TUNING pending.

## Gotchas
- Claude's external file writes don't trip Unity's watcher until focus; a NEW file needs `refresh_unity(mode=force,
  scope=all, compile=request)` (scripts-only/if_dirty won't import it) then `read_console`. MCP connected.
