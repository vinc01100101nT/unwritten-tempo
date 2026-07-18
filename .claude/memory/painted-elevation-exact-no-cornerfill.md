---
name: painted-elevation-exact-no-cornerfill
description: Painted-map import must reproduce elevation EXACTLY — FillCornerNotches was silently fattening zigzag shapes; now disabled.
metadata: 
  node_type: memory
  type: project
  originSessionId: a757d62d-6795-442b-9ea0-dcd770218a18
---

**Symptom:** A hand-painted 2-wide zigzag of raised (+1) cells imported into Unity came out 3-wide/blobby — the first row gained a cell, the first column gained a cell. Ground textures stayed put (painted separately); only the ELEVATION silhouette grew.

**Root cause (measured, not guessed):** `GroundBlendBuilder.LayPlotSampledElevated` ran `FillCornerNotches` on the painted `levels` grid — an auto-pass that raises any level-0 cell with two perpendicular higher neighbours (to hide a background grass gap at concave corners). Replaying it on the real map.json added exactly the reported cells. Fiducial trick that nailed it: the user painted one soil (+2) + one road (+3) tile on the top two grass cells as position markers → their fixed cells proved the grass had expanded around them.

**Fix:** `GroundBlendBuilder.cs` — stop calling `FillCornerNotches`; `paintLevels` and `levels` are now the same grid, so geometry renders at exactly the painted notch (the `raised = L > Lp` colour path becomes a harmless no-op). `FillCornerNotches` is kept defined-but-unused for a possible future procedural caller. `LayPlotSampledElevated` is only called by the painted-map importer, so this affects painted maps only — which is where EXACT reproduction is the rule.

**Policy:** a hand-painted map is authoritative — never auto-modify the author's cells. Concave-corner gaps are the author's to fill (e.g. the 📏 diagonal tool). This aligns with the old handoff [[map-painter-diagonal-cuts]] (FillCornerNotches was flagged there as a dead-end to remove). See also [[ground-blend-corner-smear]].
