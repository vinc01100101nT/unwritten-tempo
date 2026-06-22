# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- **Effort**: model ALWAYS Opus 4.8 (never assess/print model). Assess EFFORT only; auto-GO Low/Medium; PAUSE >Medium until user GO. Tag `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE this file (never append); prune landed; keep short.

## ✅ Landed earlier
- **Worldgen**: recipe generator + winding routed paths (`782c07e`).
- **Dead 2D layer FULLY RETIRED** (`c2fd369`/`54d0412`/`9f3402e`). 3D stack (Player3DController / PersistentPlayer3D / Portal3D / MonsterAI3D / GameCursor3D / PathAgent3D / Pathfinder3D) is the SOLE player layer. Play-tested OK.
- Unit-test harness = **SKIPPED** (user deferred it).

## 🚧 ACTIVE — World Creator pipeline (NEW direction, 2026-06-23)
Replacing the manual per-scene `MapLinker` with a declarative pipeline. **Spec doc:** `q176qkd22qmbk4r5t9g7fcc30n895whr` (🗺️ World Creator — Spec) — read it first.
Pipeline = **HTML grid editor → `world.json` → Unity World Creator (orchestrator) → RecipeGenerator per cell**.
- **JSON contract** (v1): `{version, worldSize:60, maps:[{id,cell:[c,r],type,seed?,pathTile?,pathWidth?}], portals:[{a,b,offset} | +anchorA/anchorB for distant]}`. Seeds assigned by Unity, not HTML. Adjacency → edge/direction derived from cell coords; A's offset maps to same offset on B's facing edge.
- ✅ **Part 1 DONE**: `tools/world-creator/index.html` — self-contained, no build. 3 modes: **Paint** (cells town=yellow/field=green/dungeon=purple/special=red), **Warp** (adjacent, blue dots ≤3/border), **Link** (distant cross-map warps — click A→B, dashed line + square caps, drag cap to re-anchor edge/offset, click cap to delete). Export/Import round-trips both warp kinds. Open via `start "" "...\tools\world-creator\index.html"`. NOT yet user-tested in browser.
- ✅ **Part 2 DONE**: `unity/Assets/Scripts/Editor/WorldCreator.cs` — `Tools ▸ unwritten ▸ World ▸ World Creator…` EditorWindow. Load world.json → validate (dup ids/cells, adjacency, stranded maps, missing templates) → **Build World**: PASS1 clone type→template `MapRecipe` (seed = json seed or FNV(id); size=worldSize) → `RecipeGenerator.Generate` per map; PASS2 group warps per scene → paired `MakePortal`+`MakeSpawn`, spawn ids `w{i}_from_{map}` (handles ≤3 stacked warps — `EnsurePortal` couldn't, dedupes by target). Resolves adjacent (edge from cell delta) + distant (anchors). Templates wired via window slots, persisted in EditorPrefs by GUID. **Decided: template recipe assets** (not code-gen). NOT yet user-tested in Unity.
- ✅ **Part 3 DONE** (`PathRouting.BuildToExits` + `RecipeGenerator.Generate(r, exitPointsXZ)` overload + `WorldCreator.ComputeExits`/`ResolvePortalEdges`): roads now run from a shared centre hub OUT to each portal doorway (crossroads at centre = Start spawn). Exit XZ computed via the SAME `EdgePoint` used to place portals, so pads sit on road ends. Falls back to random `pathCount` routes for portal-less maps. ⚠️ **Must RE-RUN Build World** to regenerate scenes with the new roads (committed scenes `c5d1fee` predate it). ⚠️ templates need `pathWidth>0` or roads won't render.
- ✅ **Parts 1-3 COMMITTED**: `c5d1fee` (pipeline) + `84602a8` (exit-point roads).

## 🚧 ACTIVE — World Map HUD (built 2026-06-23, NOT yet user-tested)
A full-screen world map from top-down snapshots of every map composited by grid cell. **Reuses `MinimapBaker`'s proven URP top-down render technique.** ⚠️ **M is the minimap toggle — world map is on `N`.**
- **`unity/Assets/Scripts/WorldAtlas.cs`** — runtime ScriptableObject (Resources/WorldAtlas.asset): atlas Texture2D + grid dims (cols/rows/minCol/minRow/cellPixels) + worldSize + per-map entries {scene,col,row,type}.
- **`unity/Assets/Scripts/Editor/WorldAtlasBaker.cs`** — `Bake(cells, worldSize, interactive)`: per scene, hide "Mobs" root, ortho top-down render (extent=worldSize/2, fixed-north +Z-up/+X-right) → Texture2D → composite into atlas by cell (minRow at TOP) → PNG `Resources/WorldMap/atlas.png` + WorldAtlas asset.
- **`unity/Assets/Scripts/WorldMapHUD.cs`** — runtime overlay, **self-bootstraps** via `RuntimeInitializeOnLoadMethod` into the Systems scene (no DDOL, no UI-shell rebuild) IF `Resources/WorldAtlas` exists. Builds own overlay Canvas; `N` toggles; pulsing "you are here" marker from active scene's cell + player sub-cell pos (sx=player.x/worldSize etc).
- **WorldCreator**: Build World now auto-bakes the atlas (`interactive:false`); added standalone "🗺 Bake World Map only" button + `CollectCells()`.
- **TEST**: recompile → World Creator → "Bake World Map only" (or re-Build World) → Play from town_0 → press **N**. Marker should sit on the current map; walk → marker moves; travel → marker jumps to the new cell. NOT committed yet.
- ⚠️ Gotchas: gap cells (e.g. col 5, field_5 at col 6) render dark — accurate. If maps look dark, it's snapshot lighting (same as Bake Minimap). Legacy `UnityEngine.UI.Text` used (no TMP dep).
- ⏭️ Then: retire `MapLinkWindow`/`LinkMapsWindow`. Optional: cluster near-duplicate exits on same edge (stacked warps → near-parallel roads).
- ⚠️ Build World OVERWRITES scene files (re-bake from scratch) — confirm dialog warns; hand-edits lost. worldSize 60 → plots ±30; portals at rim − EdgeMargin(4), arrival spawn inset 3.

## Loose ends (low-pri, optional)
- `Assets/test/TownTheme.asset` has dangling refs to the 2 deleted 2D monster prefabs (test scratch, harmless).
- `9f3402e` also swept in StarterTown.unity + generated mesh + recipe asset (Unity re-serialization); benign.

## Gotchas
- No local Play loop — **user presses Play** to verify; no automated tests yet (that's the NEXT task).
- Custom runtime shaders (`Unwritten/FogScreen`, `Unwritten/GroundBlend`) MUST be in **Always Included Shaders** before a real build.
- Re-baking `Systems.unity` resets hand-tuned overlay/light values → prefer editing script defaults over full re-bake.
- SO weather assets SHADOW code timelines once generated → re-bake after editing `WeatherSequences.cs`.
- Perf check: in-game **PerfHud (F3)** for GC/s; judge true frame time from a Development Build, not the editor.
