---
name: ground-blend-small-cellsize-washed-paths
description: Elevated ground blend washes out 1-cell paths at small cellSize because sub-cell count rounded to 1 (only cell-corner samples).
metadata: 
  node_type: memory
  type: project
  originSessionId: a757d62d-6795-442b-9ea0-dcd770218a18
---

**Symptom:** Painted 1-cell-wide paths (map-painter) render washed-out / smeared into neighbours in the generated map, not crisp like the painter grid — noticed after author set BrushPalette `cellSize = 1` (was 4). Only affects the ELEVATED ground path (`LayPlotSampledElevated`; a map with height notches).

**Root cause:** `GroundBlendBuilder.LayPlotSampledElevated` chose sub-cell chops per painter cell as `sub = round(cellSize / 0.8)`. At `cellSize=1` → `sub=1`, so each cell's top patch has vertices only at the 4 CORNERS, none at the centre. A corner sits on the border between a road cell (weight 1) and grass (0), so bilinear reads 0.5 → a 1-cell path peaks at **0.5** everywhere and smears. At `cellSize=4` (`sub=5`) interior vertices land near the centre so it reached ~0.9 and looked fine. Verified offline: peak weight 0.50 (sub=1) vs 0.90 (sub=5) vs 1.00 (sub=2).

**Fix:** `GroundBlendBuilder.cs:250` — floor `sub` at 2 and round up to EVEN (`sub += sub & 1`) so a vertex always lands at each cell centre (full weight). cellSize=4 goes 5→6 (harmless refinement, re-import only).

**Gotcha:** ground mesh is baked at author time — the fix only takes effect on RE-IMPORT (Tools ▸ unwritten ▸ World ▸ Import Painted Map). The flat `LayPlotSampled` uses a fixed ~0.8u grid so it never had this bug. See [[map-painter-pipeline]].
