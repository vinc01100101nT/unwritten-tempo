---
name: hunyuan-concept-input-contract
description: "Hunyuan3D geometry quality is set by the INPUT IMAGE, not the speed knobs — the concept-art contract that lets turbo settings produce clean meshes"
metadata: 
  node_type: memory
  type: reference
  originSessionId: dc477f18-ff7c-4bfa-bb7d-6bee9f1f1fe2
---

2026-06-29: Hunyuan3D-2mini-Turbo (image→shape) at FAST settings (Inference Steps 7, Octree Resolution 128) produces clean geometry ONLY if the input concept image cooperates. Cranking steps/octree (e.g. 256–384, 30+ steps) fixes quality but costs hours — NOT viable for hundreds of props. So fix the INPUT, keep turbo.

**Failure that triggered this:** a cottage came out with a shredded/holey FOUNDATION in 3D. Root cause = the concept image's base was dark, low-contrast, and cluttered (barrels, steps, railings, lantern, thin trim fused into shadow). Hunyuan can't infer solid geometry from busy/dark/thin detail → tears into holes. No Blender voxel-remesh size fixes missing source geometry (remesh only closes cavities; an open shredded shell has none). Verified: regenerating with a clean concept gave a SOLID base at the SAME turbo 7/128.

**Concept-art contract (what GPT must produce as the image prompt):**
- One solid object, strong clean silhouette; NO thin/floating/lacy detail fused to the body (no separate railings, ladders, scattered barrels, spindly props). Matches Style Guide v1.0 (chunky, no greebles) — see [[hunyuan3d-asset-pipeline]].
- SIMPLE SOLID BASE — sit it on a chunky closed stone plinth/block; bottom reads as one closed mass. (This was THE fix.)
- Bright, even, soft light; NO deep shadows (shadow = missing geometry to the model).
- 3/4 orthographic-ish hero angle, centered, fills the frame, plain white/neutral bg (Remove Background is on).
- No ground-contact clutter (grass tufts, dirt, stairs, fences touching the object) — worst tearing.
- Detail goes in FORM + TEXTURE, not thin protruding geometry — bake cobble/woodgrain into the texture, keep the shape blocky.

**Rule of thumb:** if a region is dark, thin, or busy → it WILL have holes in 3D. Bright, chunky, simple → solid mesh.

**Remaining minor offenders even on a good input:** flower boxes / thin metal door rings → blob or web over at turbo; drop them for the cleanest pass.

Cleanup that IS still scriptable for leftovers (don't hand-tune per mesh): headless Blender batch = import → Voxel Remesh (~0.03) → Decimate → export GLB (`blender --background --python clean_prop.py`). And props seat on the Y0 ground floor ([[world-ground-standard]]), so any surviving base fringe can be sunk below ground and never seen.
