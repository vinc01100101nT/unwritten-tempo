# GPT-4o concept → Trellis 2 town-prop pipeline PROVEN (Tier-1 landmarks)

**Date:** 2026-06-30

**What happened:** First full end-to-end run of the new RO-fantasy capital art pipeline. Generated GPT-4o image concepts → Trellis 2 → 3D, three for three, all production-ready and visually consistent as a set.

## The proven loop
1. GPT-4o image gen from a **distilled style preamble** (image-only subset of Asset Concept Style Guide v2.1, doc `q17axe2t5tymwkh1f6bfd1xmdh89g0g8`) + one per-prop line. Working copy doc: **"GPT Prop Prompt — Paste-Me"** `q17013b7vgpxyqp01rsw53xyc589n6yd`.
2. Chosen concept → **Trellis 2** at STOCK defaults → clean GLB.
3. (next) Blender retopo → Unity import.

## Trellis 2 settings that worked (Tier-1 landmarks) — STOCK, no tuning
- Resolution **1024** (NOT 1536 — earlier confusion; 1024 produced everything below)
- Decimation Target **250000** (high on purpose; retopo to low-poly downstream)
- Texture Size **2048**
- Stage 1 (Sparse): Guidance 7.5 / Rescale 0.7 / Steps 14 / Rescale T 5
- Stage 2 (Shape): Guidance 7.5 / Rescale 0.5 / Steps 14 / Rescale T 3
- Stage 3 (Material): Guidance 1 / Rescale 0 / Steps 14 / Rescale T 3
- Debugging & Profiling panel = IGNORE (all profiler checkboxes off; zero effect on mesh)

## Assets produced (Tier-1 town set, style-consistent)
- **Central fountain** — tiered stone basin, copper accents, water (water sheets may be lossy in Trellis → add as FX/shader in Unity if needed)
- **Stone well** — blue shingle roof + fieldstone + iron banding; **rope SURVIVED** (the thin element I predicted Trellis might drop)
- **Hero statue / knight monument** — seed `162051756`. Standout: **verdigris/green patina baked into the 2048 texture** (not geometry) → reads as real centuries-old bronze. **Backside (fully Trellis-invented) reconstructed believably** — validates the "backside honesty" rule (keep important language on visible 3/4 front, let Trellis fill rear).

## Key lessons
- **A seed is bound to its INPUT IMAGE, not transferable between props.** Reusing a "good" well seed on the statue is effectively just one fixed random roll for the statue — it does NOT carry quality over. Workflow: Randomize Seed ON while exploring a prop; turn OFF + pin the seed only AFTER you have a keeper (to reproduce/iterate it). (Statue happened to roll well on the fixed seed — luck, not causation.)
- **Detail-in-texture works as designed:** patina, moss, stone cracks all live in the 2048 texture, confirming the style guide's "favor painted surface info over geometry."
- Texture quality visibly strong — consistent with prior finding Trellis 2 > Hunyuan3D.
- Res split idea: 1024 is enough even for hero landmarks; can drop Tier-4 clutter lower to save time (untested but logical).

## Context / direction
- Decision: town = NEW GPT-generatable RO-fantasy-capital style; do NOT match the existing Quaternius village kit (kept for rustic/field maps). Full town set = ~18 props (Tiers 1–4) in the Paste-Me doc; Tier-1 (this) done.
- Next: Blender retopo + Unity import of the 3 landmarks → eyeball as a set in-scene → then the 5 house variants (4a–4e).
