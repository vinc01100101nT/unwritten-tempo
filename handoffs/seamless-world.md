# Handoff — the seamless world (`world`): the 7×7 block around Lastline, filled like the nature pack's demo

**Task:** replace zone maps with one seamless world (32-unit squares streamed around each player), laid out by the
world layout tool and built square by square with the Idyllic Fantasy Nature pack. The first block — squares 17..23 ×
17..23 around Lastline — is built. This session: real layout (15 towns, levels, cliffs, rivers, ponds, caves), the
block build, Lastline joined to it, cliff rocks, the fields' dense fill, and the player graphics settings. No Tempo
issue/doc id.

## Status

**IN PROGRESS — the fields' fill was built at the very end and NOT measured in play.** The world was rebuilt with
19,854 props (was ~2,100) and ~10.1 million carpet plants built in; building one square from its prefab rose from
11 ms to 81 ms worst in the editor (a possible hitch each time a square loads). Frame time with every square dense is
UNMEASURED. **Don't trust prior "it works" claims** for frame rate.

**Measuring trap:** with the editor NOT the focused window, Unity throttles it — the profiler, the stats tool and the
performance panel all lag or freeze, and two rounds of numbers this session were wrong because of it. Measure only
while the user has the editor focused, standing still, frame cap lifted (`Application.targetFrameRate = -1`, put back
to 60 after). Average the profiler's main-thread frames (`ProfilerDriver.GetHierarchyFrameDataView`) over 90+ frames;
single-frame `get_performance_stats` readings jump 2–22 ms.

The game server must be rebuilt + restarted for the user to play the current world (`/tp world`, GM).

## What's done (this session)

**Layout** — `tools/world-layout/build_world_layout.py` → `data/world-layout.json` + map `~/Downloads/world-layout.html`
- 15 towns, one weapon each; levels 2.5 units with cliffs; rivers in valleys; ponds (Lastline, Pluvenne, Pennaryl,
  Hazemere oasis); 4 caves; Halcyra high ground; Tide Temple low-tide path; trap pits get ramp cuts.
- **Each town's ground is flat out to 47.6 units from its middle, easing over 24 more** (`TOWN_REACH 39.6` =
  Lastline's half-diagonal, `TOWN_CLEAR 8`, `TOWN_EASE 24`). Moved the Gemmara cave and two bridges; passes 11→15.

**World builder** — `unity/Assets/Scripts/Editor/SeamlessWorldBuilder.cs` (`SeamlessWorldBuilder.BuildCore()`, run
through the editor link; ~25–66 s)
- `Blocks = { (17,23,17,23) }`, map id `world`, output `Assets/2_Generated/World/` (square prefabs + Lastline piece).
- **Lastline joined:** rim heights read from its mesh; its copied ground uses the world's grass/road
  (`SameGrassAsTheWorld`, `world_lastline_ground.mat`); **roads leave through its gates** (`Terrain.LayRoads` /
  `ThroughGate`: gates = its `Portal_*` doors, 6 units straight then a curve onto the layout road 16 units out);
  gate paving fades into road (`GateRoadWhole 3`, `GateRoadFade 8`).
- **Cliff rocks** (pack Cliff_01/02) line every wall, **solid** (user's call; exception written into RULE ONEGROUND
  in `CLAUDE.md`).
- **Shadows:** only trees, cliff rocks and Rock_Big cast; small props don't.
- **Flowers:** meadow patches single-colour, **a quarter of the demo's count** (`FlowerThinOut 0.75`), every flower
  **twice as tall** (`FlowerExtraHeight 1`) with ±15% height roll (`FlowerHeightJitter`); single flowers too.
- **Fields' fill (newest):** layers `field trees/firs/bushes/rocks/plants` at the pack demo island's density
  (`DemoTrees 235, DemoFirs 70, DemoBushes 464, DemoRocks 380` per 300×300 demo metres; `ownKeep`), and **grass over
  every square's open ground** at a quarter of the demo (`FieldGrassThinOut 0.75`). Only on nature-pack themes
  (`NatureTheme`); kept-biome towns get none.

**Runtime**
- `FlowerCarpet.cs`: draws only cells in the camera's view and within the player's distance; density re-lay
  (0.3 s after the slider rests); plant shadows per setting; extra-height/height-jitter/thin-out fields.
- `WaterPooling.cs`: the downhill step never lands on a no-ground square (fixed an index error on the world map).
- **Settings ▸ Graphics** (`SettingsWindow.cs`, `GameSettings.cs`): Shadow quality (1024–8192, default Very high),
  Grass & flower distance (10–40, default 20), Grass & flower density (25–100%), Plant shadows, Show performance.
- `GraphicsQuality.cs` (new): the game draws with its own copy of the render settings (shadow resolution), so the
  saved `UniversalRP.asset` stays at 1024 (an asset rule caps it).
- `PerfHud.cs`: now in every build, shown by the setting / F10; FPS, CPU, GPU, RAM, VRAM coloured green→red.
  `PlayerInteractor.cs` no longer wraps its hide call in a dev-only guard.
- Project setting `enableFrameTimingStats` turned on (for the GPU line).

**Removed again this session:** the URP Outline package trial (code, renderer feature, layers, marks, setting — all
gone; it drew outlines through occluders and could not look hand-drawn). The blue see-through silhouette is switched
off on `Spike3DRenderer.asset` "temporarily" (user's word) — switch back when asked.

## Measured
- Trial square before the fill went world-wide (one dense square, quarter flowers): main thread 7.5 ms, GPU 8–10.5 ms,
  ~100 fps uncapped; carpets were the cost (15 ms GPU at half-density flowers), shadows ~1 ms after the small-prop
  change; 4096 vs 1024 shadows no measurable difference.
- Carpet main-thread cost 5.3–6.1 → 2.0–2.6 ms after the camera-view check.

## Uncommitted working-tree changes (measured 2026-10-27 ~11:40)
~350 entries in `git status`; most are other sessions' work. This session's: untracked (never committed)
`SeamlessWorldBuilder.cs`, `FlowerCarpet.cs`, `GraphicsQuality.cs`, `SeamlessWorld.cs`; modified `WaterPooling.cs`,
`GameSettings.cs`, `SettingsWindow.cs`, `PerfHud.cs`, `PlayerInteractor.cs`, `MapEdgeWalls.cs`, `tools/check-map-ids.py`,
`tools/prove-map-ids-check.py`, `tools/world-layout/build_world_layout.py` + `data/world-layout.json` (untracked),
`Assets/2_Generated/World/` (untracked), `Spike3DRenderer.asset` (silhouette off), `UniversalRP.asset`,
`ProjectSettings.asset` (frame timing on; also "bake collision meshes" on — NOT mine, saved by my SaveAssets),
`GraphicsSettings.asset` (two always-included shaders removed — NOT mine, same save), `EditorBuildSettings.asset`
(world scene), `CLAUDE.md` (ONEGROUND exception), `.claude/memory/world-builder.md`. The effects pack's
`FE_ForwardRenderer.asset` shows a Unity file-format upgrade from a save. Nothing committed.

## What's left
1. Measure the world-wide fill in play (editor focused): frame time in a few dense spots, and the square-load hitch
   (81 ms build per square in the editor). If too slow: a draw distance for props, like the carpets'.
2. Ask the user about the two project-setting changes that were not mine (bake collision meshes on; two shaders off
   the always-included list).
3. Lastline's pond sits outside the block (layout moved it); the south gate has no road.
4. Next blocks of the world, minimap per square, the continent.

## Dead ends (proven)
- Unity Toon Shader: leaf-card trees became outlined triangles. URP Outline (free, MIT): outlines draw through
  occluders, colour reads lighter than set, a halo not a stroke; its UPM manifest is broken (URP dependency with no
  version). ToonShading (free) is GPL-3.0 — unusable for a closed game.
- Shorter carpet distance (40→20) did not help the camera's close top-down view: everything on screen is within 20.
- Pushing cliff rocks into the wall: dropped in favour of solid rocks (user).
- Single-frame stats and unfocused-editor readings: unreliable (see Status).

## Next single step
With the user in Play and the editor focused, stand in a dense field square and measure 90-frame averages with the
frame cap lifted; then walk across a square edge and look for the load hitch in the profiler.

## How to resume
```text
Resume the seamless world work: read handoffs/seamless-world.md first, check editor_status (stop Play before ANY script edit), then continue from "Next single step".
```
