---
name: kenney-3d-terrain-overlap
description: How to make merged Kenney 3D terrain — overlap the rounded blocks; walk on flat tier colliders
metadata: 
  node_type: memory
  type: project
  originSessionId: 9ef09c7d-76f5-4718-98f0-8af9f39c4738
---

Kenney **Platformer Kit** grass blocks (`block-grass`) are rounded, self-contained *platform* tiles
(domed top + beveled shoulders), NOT tileable terrain. Tiling them flush leaves dirt "grout" grooves
between every cell (the failed "rows of green buns" render).

**The fix that gives the Kenney-sample merged look:** scale each block's footprint ~**1.3×** the cell
size so neighbours OVERLAP — the rounded shoulders close the grooves → one continuous bumpy green
lawn, with dirt showing only on the outer/cliff faces. Const `OverlapScale` in `TerrainSandbox.cs`
(Tools ▸ unwritten ▸ Terrain Sandbox). Place one block per cell, footprint scaled to cell, Y scaled to
span 0..tierTop (one scaled block = a full-height cliff column, cheap). Tiers from a quantised Perlin
heightmap. This made the overlap approach beat the alternative (flat-plane top + block cliff cladding).

**Texturing the terrain with 2D pixel tiles:** world-projection shader `Unwritten/TerrainProjected`
(grass on up-faces, dirt on cliffs, tiled by world XZ at PPU scale) + `Terrain3DArt.cs`. ⚠️ **Custom 3D
URP shaders render in the GAME (3D renderer / Play) but NOT in the Scene view** — the project's Scene
view / default renderer is the **2D Renderer2D** (index 0), which silently won't draw a custom
UniversalForward-only mesh shader (stock URP/Lit/Unlit DO draw there, which is misleading). **Verify
3D-shader work by pressing Play, never by the Scene view.** This wasted ~6 round-trips of "it's invisible."

**Walking:** don't walk on the bumpy domed mesh colliders — strip them and lay a FLAT per-tier box
collider on the `Ground` layer at each cell's tier top (smooth walk). Single-tier steps are climbable
via the player's `stepOffset`; bigger drops are cliffs. Height-aware routing via `Pathfinder3DConfig`
(see `Build25DElevationSpike`). Part of the [[direction-2p5d-pivot]] 3D auto-generation work.
