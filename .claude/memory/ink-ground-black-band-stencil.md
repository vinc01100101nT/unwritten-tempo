---
name: ink-ground-black-band-stencil
description: Hand-drawn ink turned far ground into a solid black band; fixed by excluding ground via stencil bit 32 (ground drawn LAST).
metadata: 
  node_type: memory
  type: project
  originSessionId: 5f536c1b-c746-4c3f-ba3a-110241c18d15
---

**Symptom:** With the Hand-Drawn ink filter on (`HandDrawnFX` / `Unwritten/InkScreen`), the far ground near the
horizon smeared into a solid **pure-black band**. Then, after the first real fix, the **player/character lost its
ink at some camera angles** but kept it at others.

**Root causes (measured live via `execute_code`, per the second-fix rule):**
1. The band is driven **entirely by the CREASE term**: `Outlines(_DepthEdge)=0`, `Crease(_NormalEdge)=60`,
   `Edge threshold=0`. On flat ground viewed edge-on, perspective leaves a tiny apparent surface curvature the
   `invC=1/dC` normalize never fully cancels; ×60 with a 0 threshold slams `e→1`. Measured: `e` saturates to a
   solid 1.0 at eye-depth ≳4 (near ground dC≈1 stays faint). It is NOT a silhouette/horizon line.
2. **Dead-end:** a "grazing-slope reject" heuristic (`abs(dU-dD)*invC`) — right idea, but I guessed the
   threshold ~20× too high. Real vertical fractional slope on the band is **~0.05–0.10**, not the `>1` assumed,
   so it never triggered. Removed it entirely.
3. **Real fix = exclude ground by stencil.** `Unwritten/GroundBlend` (the ground-only shader; props use URP/Lit)
   stamps **stencil bit 32** in its ForwardLit pass; `InkScreen` adds `Stencil { Ref 32 ReadMask 32 Comp NotEqual }`
   so the Overlay ink quad discards ground pixels. (Bit **64** is owned by `OccludedSilhouette` — don't reuse.)
4. **Why the character then lost ink (the angle-dependent bug):** stencil marks are **screen-space, not
   depth-aware** — an object drawn IN FRONT of the ground does NOT clear the ground's bit, so the character
   inherited bit 32 wherever ground sat behind it. Angle-dependence = URP opaque **draw-order** (front-to-back by
   bounds center) flipping whether ground drew before/after the character.
5. **Fix for that (one line):** set GroundBlend `Tags { "Queue"="Geometry+500" }` (=2500, still opaque) so the
   ground draws **LAST** among opaque. Then at any pixel a nearer object covers, ground fails ZTest and never
   stamps it → only genuinely-visible ground gets bit 32. Order-independent. Material had `m_CustomRenderQueue: -1`
   so it inherits the shader queue automatically.

**Files:** `unity/Assets/Shaders/GroundBlend.shader` (Queue + `Stencil ... Pass Replace` write),
`unity/Assets/Shaders/InkScreen.shader` (`Stencil ... Comp NotEqual` test).

**Gotcha to remember:** a stencil "exclude this surface" mask only works if the writer is drawn LAST (or via a
ZTest-Equal after-opaque pass) — otherwise everything in front of it inherits the bit. See
[[occluded-silhouette-stencil]] for the after-opaque re-draw pattern (the heavier alternative).
