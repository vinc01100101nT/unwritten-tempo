---
name: trellis2-vs-hunyuan-prop-pipeline
description: Trellis 2 beats Hunyuan3D for prop gen — solved the same holey input with zero param tuning; known-good default settings
metadata: 
  node_type: memory
  type: project
  originSessionId: 07a573a9-1d14-4d44-8344-f81ce293bcb8
---

2026-06-29: Tested **Trellis 2** against the SAME concept image that gave Hunyuan3D a holey/shredded result (see [[hunyuan-concept-input-contract]] "dark/thin/busy → holes"). Trellis 2 produced a clean, solid, shippable stylized fantasy house (chunky silhouette, crisp hand-painted-style shingles/stone, closed base, multi-object scene held together — house + barrels + crate + fence + lantern) matching the Style Guide v1.0 contract from [[hunyuan3d-asset-pipeline]].

**Key win:** NO parameter tuning — dropped the image, pressed Generate, ~10 min to finish. Removes Hunyuan's input-babysitting tax, which matters for scaling to hundreds of props.

**Known-good Trellis 2 defaults (all stock):**
- Resolution 1024 (options 512/1024/1536)
- Decimation Target 250000
- Texture Size 2048 (up to 4096)
- Stage 1 Sparse Structure: guidance 7.5, rescale 0.7, steps 14, rescale T 5
- Stage 2 Shape: guidance 7.5, rescale 0.5, steps 14, rescale T 3
- Stage 3 Material: guidance 1, rescale 0, steps 14, rescale T 3

**Still verify before adopting:** topology/face density in Blender (250k decimation is HIGH — needs retopo/decimate to low-poly target), UV/atlas sanity at 2048, and scale vs Quaternius charcoal char (~×0.3). Texture quality noticeably better than Hunyuan (no muddy baked-AO).
