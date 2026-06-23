# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- **Effort**: model ALWAYS Opus 4.8 (never assess/print model). Assess EFFORT only; auto-GO Low/Medium; PAUSE >Medium until user GO. Tag `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE this file (never append); prune landed; keep short.

## ✅ Landed & verified (committed)
- **World Creator pipeline** (`c5d1fee` + `84602a8`) — declarative connected-world authoring, replaces manual `MapLinker`. Spec doc `q176qkd22qmbk4r5t9g7fcc30n895whr`. Memory: [[world-creator-pipeline]].
  - **HTML tool** `tools/world-creator/index.html` (self-contained): Paint cells (town=yellow/field=green/dungeon=purple/special=red) · Warp (adjacent dots ≤3/border) · Link (distant anchors) · Export/Import `world.json`.
  - **Unity** `Editor/WorldCreator.cs` → `Tools ▸ unwritten ▸ World ▸ World Creator…`: load json → validate → **Build World** (clone type→template `MapRecipe`, seed=json|FNV(id), `RecipeGenerator.Generate` per map; paired `MakePortal`+`MakeSpawn`, spawn ids `w{i}_from_{map}` for ≤3 stacked warps). Templates wired in window slots (EditorPrefs by GUID); user made `TownRecipe`/`FieldRecipe`.
  - **Roads to doorways**: `PathRouting.BuildToExits` + `Generate(r, exitPointsXZ)` — roads run from centre hub to each portal (crossroads at Start spawn). Templates need `pathWidth>0`.
  - World = 8 maps (town_0/1, field_0..5) baked + connected. ⚠️ Build World OVERWRITES scene files (confirm dialog warns).
  - **Legacy manual map tools RETIRED** (`d216930`): `MapLinkWindow.cs` (MapLinker + both link windows) + `MapRecipeEditor.cs` (per-recipe Generate buttons + starter) + RecipeGenerator's "Generate Map from Selected Recipe" menu all DELETED. World Creator (Build World) is the ONLY map-build/link path; recipes authored via Assets ▸ Create ▸ unwritten ▸ Map Recipe; `RecipeGenerator.Generate()` engine + Apply Foliage Wind kept.
- **World Map HUD** (`d119097`) — press **`N`** in-game for a full-screen top-down atlas of the whole world + pulsing "you are here" marker. (**`M` = corner minimap**, separate.)
  - `WorldAtlas.cs` (Resources SO) + `Editor/WorldAtlasBaker.cs` (per-scene top-down snapshot → composite by grid cell; atlas marked `HideAndDontSave` so OpenScene cleanup can't destroy it mid-bake) + `WorldMapHUD.cs` (self-bootstraps into Systems scene, builds own canvas). Build World auto-bakes the atlas; standalone "🗺 Bake World Map only" button too.

## ⏭️ NEXT (optional, pick any — none urgent)
- Cluster near-duplicate exits on the same edge (stacked warps currently make near-parallel roads).
- Brighten world-map tiles if they read too dark (snapshot lighting; same as Bake Minimap).
- Bigger roadmap items still open: threat/aggro (deferred), next class (#2), weather calendar.
- Unit-test harness = **SKIPPED** by user (still available if wanted; adding `.asmdef` is its own change).

## Gotchas
- No local Play loop — **user presses Play** to verify.
- To rebuild the world: edit in the HTML tool → Export `world.json` → World Creator → Build World (auto-bakes the map). To boot, open a generated map scene (e.g. `town_0`) as active before Play (`GameRoot.firstMap` default is the literal `"Town"` which no longer exists).
- Custom runtime shaders (`Unwritten/FogScreen`, `Unwritten/GroundBlend`) MUST be in **Always Included Shaders** before a real build.
- Re-baking `Systems.unity` resets hand-tuned overlay/light values → prefer editing script defaults over full re-bake.
- SO weather assets SHADOW code timelines once generated → re-bake after editing `WeatherSequences.cs`.
- Perf check: in-game **PerfHud (F3)** for GC/s; judge true frame time from a Development Build, not the editor.
- `Assets/test/TownTheme.asset` has dangling refs to 2 deleted 2D monster prefabs (test scratch, harmless).
