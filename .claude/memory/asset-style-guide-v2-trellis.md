---
name: asset-style-guide-v2-trellis
description: LOCKED prop-concept spec is Style Guide v2.1 (TRELLIS 2) — supersedes the Hunyuan-strict v1.0 rules
metadata: 
  node_type: memory
  type: project
  originSessionId: 07a573a9-1d14-4d44-8344-f81ce293bcb8
---

2026-06-29: **Asset Concept Style Guide v2.1 (TRELLIS 2)** is the LOCKED production spec for prop/building concept art (Claude+GPT agreed). Tempo doc id **q17axe2t5tymwkh1f6bfd1xmdh89g0g8**. Supersedes [[hunyuan3d-asset-pipeline]] v1.0 and relaxes the paranoid [[hunyuan-concept-input-contract]] rules — those defenses ("single object only / white bg only / chunky-closed-base-or-holes") were Hunyuan fragility, not art direction. See [[trellis2-vs-hunyuan-prop-pipeline]] for why we switched.

**What CHANGED from v1.0 (the relaxations):**
- Composition: one PRIMARY structure + a small cluster of supporting props sharing its footprint (a vignette), NOT one bare prop. Never two buildings. 70–85% of frame.
- Background: white OR flat neutral grey + a soft ground-contact shadow ENCOURAGED (helps base/floor seam → cleaner foundations).
- Foundations/stairs/retaining walls now encouraged (Trellis handles them).

**What stayed (style, engine-independent):** stylized fantasy / chunky / hand-painted / silhouette-first / material separation / warm palette / detail-in-texture / 3/4 hero angle / modular shared vocabulary.

**New rules v1.0 lacked:** backside honesty (Trellis invents occluded faces → keep detail on visible 3/4 sides); avoid deep interior cavities through openings (shallow inset doors/windows unless real interior); style-anchor reference image fed alongside each concept for cross-asset consistency; poly/topology/texture-res are OUT OF SCOPE (handled at Trellis export + Blender retopo, not the concept).

**v2.2 PROPOSED, not active (needs mileage):** Asset Tier Classification S/A/B/C/D (cathedral→debris); when promoted, tie each tier to a retopo poly target + manual-polish allowance.
