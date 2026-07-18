---
name: map-painter-ground-density-mismatch
description: Map painter drew one ground-texture repeat per CELL while the game tiles at 2.5u/repeat — preview/game scale disagreed whenever cellSize ≠ 2.5.
metadata: 
  node_type: memory
  type: project
  originSessionId: a757d62d-6795-442b-9ea0-dcd770218a18
---

**Symptom:** House-vs-ground-motif proportions in the map painter didn't match the generated map (user compared a house against the circle motif in `prontera_tile`). Got worse/better as `BrushPalette.cellSize` changed — the tell that ground texture scale was coupled to a knob the game ignores.

**Root cause:** Two different owners of "how big is one ground-texture repeat". Game: `GroundBlend.shader` samples world-XZ × tiling 0.4 → one repeat = **2.5 world units**, constant. Painter: `ctx.drawImage(img, …, cellSize*p, cellSize*p)` stretched one full repeat across each **cell** → repeat = `cellSize` world units. Objects were drawn at true measured footprints in both, so the ground pattern was off relative to props by `cellSize/2.5`.

**Fix (single source of truth):** `GroundBlendBuilder.GroundTexWorldSize = 2.5f` (public const) → used by `MakeGroundMaterial` (tiling = 1/it) AND exported as `groundTexWorldSize` in `prop-manifest.json` (`BrushPaletteWindow.ExportManifest`). Painter (`index.html`) now fills ground cells with a world-anchored repeating `CanvasPattern` scaled to that density (`groundPattern()`, anchored at map centre = game world origin), instead of per-cell `drawImage`.

**Gotchas:**
- Old manifests lack the field → painter defaults to 2.5 (correct for the current game constant).
- Also fixed: the hover ghost for "🎲 Random" sized itself from the brush-level footprint = **group max** — a mixed group (houses 1.2–2u + trees 8–22u in one brush) made the ghost 21.8u. Now ghosts the first/chosen variant.
- The measured native prop sizes are truth: trees really are 8–22u vs houses ~2u — the painter faithfully showing that is correct, not a painter bug. See [[map-painter-pipeline]], [[ground-blend-small-cellsize-washed-paths]].
