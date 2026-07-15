---
name: trellis-imperfection-is-the-linework
description: ART DIRECTION — keep Trellis2 mesh imperfection; the hand-drawn crease ink pass turns topology noise into interior line art.
metadata: 
  node_type: memory
  type: project
  originSessionId: 29f0b9ab-99c4-46f2-8f04-9f7f7d014e16
---

**Locked art direction (2026-07-14):** we no longer chase perfect meshes from Trellis2 — we EMBRACE its imperfection. The [[hand-drawn]] filter's **crease** pass (`HandDrawnFX.crease`, default 60; `NormalEdge`/`crease` in `InkScreen.shader`) inks *normal discontinuities* — every facet, seam, and lumpy transition on a mesh. A perfectly smooth/retopo'd mesh gives the crease detector almost nothing; Trellis2's faceted, slightly-noisy topology is full of discontinuities, so the "flaws" render as expressive interior linework you'd otherwise hand-author. Default is `silhouette` (Outlines) = 0, so the look leans ENTIRELY on crease lines — the imperfection IS the art.

**Why:** the user saw the town-bench/plant screenshot and loved how the crease lines read on the raw Trellis GLB.

**How to apply:**
- Do NOT over-smooth, decimate to perfectly-flat, or aggressively retopo Trellis GLB props purely for "cleanliness" — that erases the crease linework. Retopo only when needed for perf/collision (see [[glb-optimize-offline-gltftransform]]), and prefer keeping surface facets.
- When a prop looks flat/under-inked, the fix is usually MORE crease-friendly topology (or raise `crease`), not a cleaner mesh.
- Ties to [[direction-2p5d-pivot]] (props are live-3D) and the Trellis pipeline ([[trellis2-tier1-town-pipeline-proven]], [[asset-style-guide-v2-trellis]]).
