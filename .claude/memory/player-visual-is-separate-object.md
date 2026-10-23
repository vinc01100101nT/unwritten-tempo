---
name: player-visual-is-separate-object
description: "The visible live-3D player model is a SEPARATE object (Character3DVisual), NOT a child of the player root — target it, not the root, for any per-visible-player render flag/component."
metadata: 
  node_type: memory
  type: project
  originSessionId: 6bc6e3bb-cdfe-4fb4-b57e-d43acc183fec
  modified: 2026-10-23T12:44:28.586Z
---

The player ROOT `Player3D` (the `Player3DController` object) holds only a **disabled** `SpriteRenderer` — the
visible body is a **separate scene object** named `Player3DVisual (<Model>)` carrying `Character3DVisual` +
the `SkinnedMeshRenderer` (`Body`) and equipped-weapon `MeshRenderer` (e.g. `bow_celestia`). It **rides** the
controller (snaps to its feet each frame); it is NOT parented under the player, so
`player.GetComponentsInChildren<Renderer>()` sees ONLY the hidden sprite.

**Consequence:** any component/flag that must touch the *visible* player renderers (renderingLayerMask, a
material swap, an outline marker, dye) MUST be placed on the `Character3DVisual` object, not the player root.

**Facing too:** the root `Player3D` NEVER turns (measured 2026-10-23: yaw 0 while the visual stood at yaw 69).
Which way the player faces = `Player3DController.WorldFacing` (creatures mirror it: `MonsterAI3D.WorldFacing`);
never `transform.forward` of the root, and `GetComponentInChildren<Character3DPose>()` on it finds nothing. Cost
one failed fix: an effect's "face the aim" fallback read the root and stayed world-aligned.

**Where it's built (single place):** `Player3DVisualBuilder.Attach` (Editor) instantiates the model `go` and
adds its rig — add such markers there so a model swap (which rebuilds `go`) re-tags automatically.

**Bug this caused (2026-07-15):** the x-ray occlusion silhouette ([[occlusion-silhouette-feature]]) drew
nothing because `OccludedSilhouetteTarget` was added to the player root → it flagged only the disabled sprite
(`renderingLayerMask` 257) while the real `Body`/weapon renderers stayed unflagged (`rlm` 1). Measured live via
`execute_code` (per the second-fix rule) — `parentedUnderPlayer=False` made it obvious. Fix: add the marker in
`Player3DVisualBuilder.Attach`; retro-tag tool + runtime probe now target `Character3DVisual`.
