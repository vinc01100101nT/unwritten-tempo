---
name: world-builder
description: The real seamless world "world" is built square by square from data/world-layout.json by SeamlessWorldBuilder.BuildCore(); how squares are kept independent
metadata:
  type: project
---

Since 2026-10-27 the test builder became `SeamlessWorldBuilder` (Editor), map id "world", output `Assets/2_Generated/World`.
- Squares are the layout's own 32-unit grid, id (I, J) = layout x/32, y/32; `Blocks` lists every built area (first: 17..23 x 17..23, Lastline's 7x7). A square not in Blocks is deleted with its address on the next build.
- Scene coords: x = layout x - 640, z = 640 - layout y (north +z), y = layout height - Lastline's height (13.12).
- Cliffs: a layout step > 1.5 between cells becomes a near-vertical jump in the one ground (side picked by the corner nearest the four-corner blend) — not GroundCliffs (the planner's suggestion, not used). The pack's cliff rocks line every wall and are SOLID (their own mesh colliders kept, the user's call 2026-10-27 after walking through them): they stand up to 2.7 units in front of the ground's wall, so drawn-only rocks let a body walk into them.
- Proven 2026-10-27: shared edges identical (10,357 points), a wider build changes no existing square (ground, props, carpets), rebuild byte-identical. Props stand by neighbour rolls only (no placement-order chain), nothing is held back from the built box's edge, carpet discs touching any built square are laid, bridges within 40 units are fitted built or not.
- Roads graded to at most 0.25 rise per unit; ground eases up to bridge ends; a short straight road runs onto each bridge end. Measured steepest on roads: 26 deg (Parley road at the north gate), 24 deg across the bridge; walk limit 45.
- Kept-biome towns (hazemere desert, hilt coast, cindrel volcanic, kharnel haunted) take trees/flowers from their Biome Table row at the recipe group's scale; others use nature-pack themes.
- Edge walls follow the outline of the built squares (MapEdgeMath.OutlineWalls); a world not cut into equal squares keeps the old box walls.
- SeamlessWorld files chunks by 32-unit cells so streaming never walks every chunk.
- The scene's line was written into EditorBuildSettings.asset on disk by hand once (the editor had it only in memory).
