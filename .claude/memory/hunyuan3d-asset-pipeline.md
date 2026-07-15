---
name: hunyuan3d-asset-pipeline
description: Own-prop generation now via local Hunyuan3D-2 + the Unwritten Asset Generation Style Guide v1.0 (the style contract for all generated props)
metadata: 
  node_type: memory
  type: project
  originSessionId: 381b897e-a6a8-471c-8d06-d86d976276cf
---

As of 2026-06-28 the team got **Hunyuan3D-2 working locally** (user + GPT). From now on props are GENERATED in-house (concept image → Hunyuan3D-2 → Blender cleanup → low-poly game asset) instead of relying solely on bought packs (Pxltgr/Quaternius/etc.).

The consistency contract is **"Unwritten Asset Generation Style Guide v1.0"** (PDF at `C:\Users\Admin\Downloads\Unwritten Asset Generation Style Guide v1.0.pdf`). Key rules when generating/judging an asset:

- **Style:** bright, handcrafted fantasy MMO. Chunky proportions, strong/clear silhouette, large simple shapes, minimal tiny detail; readable from iso camera. Favor thick forms, rounded edges, slight exaggeration, broad surfaces. AVOID thin parts, sharp/tiny detail, greebles, dense noise, photorealism.
- **Materials:** wood = thick planks + visible grain + large beams; stone = large blocks + stylized cracks + rounded weathering; metal = thick trims + simple rivets, NO intricate engravings; cloth = large folds, heavy fabric.
- **Color:** vibrant fantasy palette — warm woods, rich greens, deep blues, clean stone, bright accents. NO washed-out / gray realism / military palettes.
- **Concept-image (Hunyuan3D input) rules:** SINGLE centered object, slight 3/4 ortho-like angle, plain white/light-gray/transparent neutral studio bg (NEVER in an environment), soft studio lighting (no dramatic shadows/sunset/colored light/fog), no cropped edges.
- **Quality goal:** reads at distance, distinct proportions, cleans up well in Blender → low-poly, matches prior Unwritten assets. Prioritize consistency over novelty.

The PDF doubles as a ChatGPT "Art Director" system prompt. Relates to [[character-art-quaternius-charcoal]] and the pack-based world ([[pxltgr-field-migration]], [[recipe-generator]]).
