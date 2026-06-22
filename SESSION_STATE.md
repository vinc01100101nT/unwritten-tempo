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
- ⏭️ **Part 3 (next)**: `RecipeGenerator`/`PathRouting` gains **exit-points** input so paths terminate at portal positions (pads on roads, not mid-field — the key mechanic). Currently portals sit on the rim but paths are routed independently.
- Then retire `MapLinkWindow`/`LinkMapsWindow`.
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
