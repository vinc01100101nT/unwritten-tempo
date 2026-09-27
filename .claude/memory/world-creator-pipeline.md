---
name: world-creator-pipeline
description: World Creator = HTML free-form map editor → world.json (v2) → Unity orchestrator pipeline for authoring the connected world (replaces manual MapLinker). v2 (2026-06-27) dropped the grid: maps are free-placed, any rectangular INT size, all warps explicitly anchored.
metadata: 
  node_type: memory
  type: project
  originSessionId: 0ed1697a-c3d2-4a71-857d-aaf77fae8078
  modified: 2026-09-25T17:08:50.191Z
---

Started 2026-06-23 (user's design). The proper, autonomous replacement for the manual per-scene `MapLinker`/`LinkMapsWindow` flow. The world's topology becomes ONE declarative source of truth so regenerating a map never strands its warps.

**Pipeline:** HTML free-form editor → `world.json` (the contract) → Unity **World Creator** EditorWindow (the orchestrator) → existing `RecipeGenerator` per map.

**⚠️ SCHEMA v2 (2026-06-27) — CLEAN BREAK from v1 (no migration; v1 grid files don't import).** Maps are now free-placed at arbitrary world positions and any RECTANGULAR integer size (no grid). `{ version:2, maps:[{id, pos:[x,y]/*float, top-left, x→east y→south*/, size:[w,h]/*INTEGER units*/, type:"town|field|dungeon|special", seed?, pathTile?, pathWidth?}], portals:[{a, b, anchorA:{edge,offset}, anchorB:{edge,offset}}] }`. **Every warp now carries explicit edge anchors on BOTH ends** — the old adjacency-derived `offset`-only form and top-level `worldSize` are GONE. Key rules unchanged: **seeds assigned by Unity, not HTML**; a warp's `offset` (0..1 along an edge) places both the portal and the paired arrival spawn; ≤3 warps per map-pair (soft). Colours: town=yellow, field=green, dungeon=purple, special=red (matches [[portal-color-convention]]). Per-map `size` drives `MapRecipe.sizeX`/`sizeZ` (separate axes already supported by RecipeGenerator → rectangular ground is free). `EdgePoint(edge,offset,halfX,halfZ)` is the shared rim-geometry (square-map results identical to v1).

**v2 touch set (5 files, compiled clean):** `tools/world-creator/index.html` (rewrite: Edit-mode drag-move + 8 int-snap resize handles + create/retype/delete menus; Warp-mode anchored links w/ draggable end-markers), `WorldCreator.cs` (pos/size parse, rectangular EdgePoint, anchored-only portals, `CollectEntries`→world rects), `WorldAtlasBaker.cs` (composite free-form rects at one px/world-unit scale, ≤4096px, replaces grid-of-squares), `WorldAtlas.cs` (per-map world-rect `Entry{x,y,w,h}` + `minX/minY/worldW/worldH/ppu`, dropped cols/rows/cellPixels/worldSize), `WorldMapHUD.cs` (marker from world rect + in-map player pos). Note: the corner **minimap** (`Minimap`/`MinimapCatalog`/`MinimapBaker`, its own `worldSize` per map) is a SEPARATE system — see [[minimap-is-a-baked-picture]].

**Status:** Parts 1+2+3 DONE (see the v2 note above for the current free-form model). **Part 1** = `tools/world-creator/index.html` (self-contained). *(Historical v1: Paint/Warp(adjacent dots ≤3/border)/Link(distant anchors). v2 unified these into Edit + anchored Warp — see above.)* Export/Import. **Part 2** = `unity/Assets/Scripts/Editor/WorldCreator.cs` — `Tools ▸ unwritten ▸ World ▸ World Creator…`: load json → validate → **Build World** (PASS1 clone type→template MapRecipe, seed=json-or-FNV(id), size=worldSize, `RecipeGenerator.Generate`; PASS2 paired `MakePortal`+`MakeSpawn`, unique spawn ids `w{i}_from_{map}` so ≤3 stacked warps work — `EnsurePortal` dedupes by target so it was NOT reused). **Decided: per-type content = template recipe assets** (window slots, EditorPrefs by GUID), not code-gen. Build World re-bakes scenes from scratch (overwrites hand-edits; confirm dialog warns). **Part 3 DONE** = `PathRouting.BuildToExits` (roads link the doorways to each other, meandering with random turns — NO centre hub since 2026-09-26, at the user's instruction) + `RecipeGenerator.Generate(r, exitPointsXZ)` overload + `WorldCreator.ComputeExits`/`ResolvePortalEdges`; exit XZ uses same `EdgePoint` as portal placement so pads sit on roads; falls back to random pathCount for portal-less maps (templates need pathWidth>0). First milestone committed `c5d1fee` (predates Part 3 — re-run Build World for roads). Reuses [[recipe-generator]] / Portal3D / MapManager.Travel unchanged. Scene name = the map id (see MAP IDS below); a door's colour comes from the destination map's `type` (`RegionBuilder.KindOfType`), never its name.

**Legacy tools RETIRED `d216930` (2026-06-23):** `MapLinkWindow.cs` (MapLinker + both link windows), `MapRecipeEditor.cs` (per-recipe Generate buttons + starter), and RecipeGenerator's "Generate Map from Selected Recipe" menu are all DELETED. **World Creator (Build World) is now the ONLY map-build/link path.** Recipes authored via `Assets ▸ Create ▸ unwritten ▸ Map Recipe`; the `RecipeGenerator.Generate()` engine + `Apply Foliage Wind` are kept.

Spec doc: Tempo doc `q176qkd22qmbk4r5t9g7fcc30n895whr`. The `MapLinker`/`Link Two Maps…` linker facts in [[recipe-generator]] are now historical — Build World supersedes them.

**PER-MAP BIOME (HTML side completed 2026-06-27):** the Unity side already had it — `WorldCreator.MapJson.biome` + `TryParseBiome` (grassland/snow/desert/volcanic/corrupted, aliases ice/sand/lava/void) + Build World applies `clone.biome` per map (→ `RecipeGenerator.BiomeOf` ground base/patch/tint). The **only missing piece was authoring**: now added to `tools/world-creator/index.html` — the map context menu has a **Biome** section (swatch + ✓), `world.json` export adds `biome` (omitted when grassland=default), import/copy/paste carry it, and each map renders a biome wash + name label. So a field's theme is set per map in the browser tool and flows through to the bake. (No Unity-side change was needed.)

**NEVER CLICK BUILD WORLD WITH HAND-MADE MAPS OR AN UNSAVED SCENE OPEN (2026-09-26):** the window's Build World
regenerates EVERY map in the file (it would wipe `lastline`, the user's precious hand-made Lastline) and opens each map
as the only scene (drops unsaved changes in the open Systems scene); the snapshot world-map bake opens scenes the same
way. Driven through the live editor, use `WorldCreator.BuildCore(json, town/field/dungeon/special recipe paths,
keep ids)` — keep list never opened or edited, maps opened additively and closed — and
`WorldCreator.BakeWorldMapCore(json, picture path)` for the painted world map (opens nothing). The current world's
layout is `tools/world-creator/worlds/world-regions.json` (maps carry a `name`), its picture
`Resources/WorldMap/world-map.png`; recipes `3_Data/Recipes/{Town,Field,Dungeon}Recipe.asset`. `lastline`'s doors were
retargeted/added BY HAND to the new fields — rebuilding keeps it only if `lastline` is in the keep list.
**BuildCore also places doors INTO the special maps it does not generate** (the shadow places), leading nowhere —
after any rebuild, delete every door and arrival point naming a special map's id (28 objects in 14 scenes on
2026-09-26); `tools/check-map-ids.py` fails on them.

**GENERATED MAPS ARE EDITABLE (2026-06-27):** because Build World calls `RecipeGenerator.Generate`, every world map now also emits a Map Painter **sidecar** `tools/map-painter/exports/<scene>.map.json` — see [[map-painter-pipeline]]. Workflow realised: generic maps (fields) fully auto via World Creator; to hand-tweak one, Import its sidecar into the Map Painter, edit, re-bake. Fully-custom towns are still painted from scratch.

**MAP IDS — ONE SOURCE (2026-09-26, the user's demand):** a town's or special place's `id` is its `name` in snake_case
(`Old Oak` → `old_oak`, `lastline`, `aurora_steps`), set by the browser tool's `snakeCase`/`uniqueId` on every Rename;
a field is `<town code>_fildNN` and a dungeon floor `<town code>_dunNN` (`lst_fild01`, `olk_dun01`), which the tool
asks for when one is renamed and Import never re-derives (`idFollowsName`). The id IS the scene file name, what `/tp` takes, and a save's
`sceneName`. The layout json is the only owner; scenes, doors (`targetScene`, `spawnId`, `SpawnPoint.id`,
`Portal_wN_to_<id>` names), Build Settings, `WorldAtlas`, Map Painter sidecars, `GameRoot.firstMap` and
`MapBgmTable` are copies, and `tools/check-map-ids.py` (+ its prover) fails on any drift, at commit and end of turn.
Renaming existing scenes = text-replace the door/arrival lines in every `.unity`, then `AssetDatabase.MoveAsset` (keeps
the GUID, updates Build Settings). Seeds roll from the id, so a renamed unseeded map rebuilds with a new layout — every generated map in
`world-regions.json` carries a pinned `seed` for that reason.

