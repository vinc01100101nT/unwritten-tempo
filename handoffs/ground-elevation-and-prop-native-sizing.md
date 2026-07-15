# Handoff — Ground elevation (gutters/cliffs) + prop native-sizing

## Task/bug
Two intertwined authoring features for the RO-style town workflow: (1) a **paintable ground-elevation system**
(gutters, raised sidewalks, stairs, cliffs) with matching collision; (2) **removing all auto prop-resizing** so a
prefab's own size is the single source of truth. No Tempo issue/doc linked yet.

## Status
**IN PROGRESS — code complete, NONE of it verified in Unity.** Unity was never recompiled/Play-tested this
session (project rule: don't force-refresh). **Don't trust any "it works" phrasing — nothing is confirmed.**
Compile-correctness is reasoned-only; visuals + collision feel + the elevated mesh are untested.

## What's done
**A. Elevation system**
- NEW `unity/Assets/Scripts/GroundElevation.cs` — single source of truth for step heights: `LevelHeight = 0.2f`,
  `MaxClimbStep = 0.3f`, `LevelToY(level)`. (1 notch = walkable curb/stair; ≥2 notches = cliff.)
- `Editor/GroundBlendBuilder.cs` — added `LayPlotSampledElevated(...)`: per-painter-cell flat tops at
  `level×LevelHeight`, TRUE vertical walls between differing levels + border skirt, keeps the grass/dirt blend,
  **MeshCollider** on the shaped mesh. Refactored material creation into `MakeGroundMaterial` + `PersistMesh`
  (flat `LayPlotSampled` path preserved, uses them too). Walls are double-sided; wall UVs are (along-edge, worldY).
- `Editor/WalkableGround.cs` — extracted `EnsurePathfinderConfig(scene, groundLayer)` (shared by flat box + the
  elevated MeshCollider path); flat floor unchanged. `maxStep` now = `GroundElevation.MaxClimbStep`.
- `Pathfinder3DConfig.cs` — default `maxStep` now reads `GroundElevation.MaxClimbStep` (was 0.6).
- `Player3DController.cs` — `DefaultCapsuleStepOffset` now = `GroundElevation.MaxClimbStep` (value unchanged 0.3,
  just single-sourced so player + mobs agree on curb-vs-cliff).
- `Editor/MapPainterImporter.cs` — parses new `heights[]` from map.json, flips cz like ground cells, builds a
  `levels[cx,cz]` grid, dispatches to `LayPlotSampledElevated` when any cell ≠ 0 (else the flat path).
- `tools/map-painter/index.html` — new **⬆️ Raise / ⬇️ Lower** brushes (L = brush dir, R = opposite), per-stroke
  dedup (`S.strokeCells`), `S.heights` state, elevation overlay (tint + level number), hover outline, undo/redo,
  export/import (`heights`), stat + footer hint.

**B. Prop native-sizing (all auto-resize removed)**
- `Editor/GlbPropImporter.cs` — removed `TargetFootprint` normalize; `NormalizeScaleAndPivot`→`RepivotToBase`
  (pivot only, keeps native GLB size). Dialog/doc updated.
- Placement now respects the prefab's OWN scale (no override) in `MapPainterImporter` (props) and `PropPalette`.
- `BrushPalette.cs` + `BrushPaletteWindow.cs` — removed the `defaultScale` knob entirely; manifest footprints
  measured at native (1). `MapSidecar.cs` doc updated.
- `PlacementTools.cs` — deleted the legacy **Scale ×0.3** menu item (+ TOOLS.md entry).
- Wall autotile kept (structural cell-fit) via named `WallKitScale = 0.3f` in `MapPainterImporter`.

**C. Protocol** — `CLAUDE.md` gained the unified "do a handoff" protocol (this file is the first product).

## Uncommitted working-tree changes (all unstaged, revertible)
NEW: `unity/Assets/Scripts/GroundElevation.cs` (+ .meta)
Modified: `CLAUDE.md`, `tools/map-painter/index.html`, `tools/map-painter/prop-manifest.json`,
`unity/Assets/BrushPalette.asset`, `Scripts/BrushPalette.cs`, `Scripts/Pathfinder3DConfig.cs`,
`Scripts/Player3DController.cs`, `Editor/{BrushPaletteWindow, GlbPropImporter, GlbPropPipeline, GroundBlendBuilder,
MapPainterImporter, MapSidecar, PlacementTools, PropPalette, WalkableGround, TOOLS.md}.cs`.
(Note: `prop-manifest.json` + `BrushPalette.asset` show as modified from earlier work — the asset's now-removed
`defaultScale` field is orphaned/harmless.)

## What's left
1. **Focus Unity → recompile**; fix any compile errors (watch Console).
2. **Re-import GLB props to native**: Tools ▸ unwritten ▸ Props ▸ Optimize + Import GLB Prop… (strips old baked
   0.3-era scale).
3. **Re-export manifest**: Tools ▸ unwritten ▸ World ▸ Brush Palette… → Export → ↻ Reload manifest in the painter.
4. **Play-test elevation**: paint gutter/steps/cliff in the painter → export → Import Painted Map → Play; confirm
   step-up curbs, walk-in gutters, 2+ notch cliffs block; feet follow the shaped ground.
5. Eyeball-size a few props (drag in → scale → right-click Scale row → Apply to Prefab) and confirm the size
   persists through painter placement.

## Dead ends / known risks (all HYPOTHESES — none measured)
- Wall visual quality (double-sided, planar-ish UVs) may look streaky/low-res — untested, first-pass.
- Painter cell = **4u**, so elevation edges are chunky on diagonals (I earlier mis-stated 0.8u; 0.8 is only the
  internal blend density).
- Recipe/field-map scatter (`RecipeGenerator` `g.scale`) still scales props — INTENTIONALLY left (different
  system); user may want it stripped too.
- Border skirt drops 0.5u below min level around the WHOLE map when any elevation exists — harmless but adds geo.

## Next single step
Focus Unity, let it recompile, and read the Console — the first discriminating check is simply "does it compile?"
(new file `GroundElevation.cs` + the `GroundBlendBuilder` local functions are the most likely to have a typo).

## How to resume
> Resume the "ground elevation + prop native-sizing" task. Read `handoffs/ground-elevation-and-prop-native-sizing.md`
> first. Code is complete but UNVERIFIED in Unity — start by recompiling and fixing any errors, then follow "What's left".
