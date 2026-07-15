---
name: occluded-silhouette-stencil
description: "X-ray \"see hero through walls\" silhouette — stencil AREA mask (not per-fragment depth) + two-bit equipment coverage"
metadata: 
  node_type: memory
  type: project
  originSessionId: 13bf7cc9-2d96-4300-906a-9e7b65a4d4a2
---

The x-ray occlusion silhouette (paint the player blue where hidden behind buildings) is `OccludedSilhouette.shader`
+ `OccludedSilhouetteFeature.cs` (URP 17.4 RenderGraph, event AfterRenderingTransparents), targets flagged by
`OccludedSilhouetteTarget.cs`. Marker lives on `Player3DVisual (BaseCharacter)` (the visual is a SEPARATE object
from the player root — see [[player-visual-is-separate-object]]).

**Bug 1 — self-occlusion (player showed through its OWN body: eyes through back of head).** ROOT CAUSE: any
per-fragment depth test (hardware `ZTest Greater`, or in-shader compare vs `_CameraDepthTexture`) compares the
player against a depth buffer that INCLUDES the player, so the player's nearer parts (head-cap, weapon) occlude its
own farther parts. Unfixable by tuning/bias/cull — 3 dead-ends proved it. FIX: decide occlusion by SCREEN AREA via a
scratch stencil bit (64), 3 passes redrawing the player mesh: (0) ClearArea `ZTest Always ColorMask 0` zero the bit
over the whole silhouette; (1) MarkVisible `ZTest LEqual ColorMask 0, Pass Replace ZFail Keep` SET the bit where the
player is front-most (set-only ⇒ order-independent, so a pixel behind your own head-cap is still "visible"); (2)
PaintOccluded `ZTest Always, Comp NotEqual Pass Replace` paint only where the bit is UNSET = player present but
nothing of it visible = hidden by scene. Feature binds `resourceData.activeDepthTexture` as `AccessFlags.ReadWrite`
depth attachment (ZWrite Off) so Pass 1 tests against live scene depth and all 3 share the stencil. GOTCHA: use
LITERAL stencil values in ShaderLab (`WriteMask 64`), not a `#define` — fixed-function Stencil blocks don't reliably
expand macros. Unity `ZTest LEqual` is reversed-Z-agnostic (means "closer or equal") so it's robust.

**Bug 2 — held weapon made the body x-ray through it (blue blob at hand/robe near the sword; looked like "sword +
ground trigger it").** The sword is NOT a silhouette target (mounted at runtime by `WeaponSocket`, execution order 50,
AFTER the marker's Start order 0 — measured live: body mask=257 hasBit=True, sword mask=1 hasBit=False). It's the BODY
glowing because the sword sits in scene depth in front of it and Pass 1 only drew the body. FIX: second bit
`CoverBit` (512). Body = `Bit|CoverBit`, equipment = `CoverBit` only (equipment recognised by its `WeaponGrip`
component). Passes: Clear+MarkVisible filter `CoverBit` (body+gear cover the visible area), Paint filters `Bit`
(body only) ⇒ gear covers the body behind it but is never painted. `WeaponSocket.Remount` calls
`OccludedSilhouetteTarget.Refresh()` after mounting so runtime weapon swaps re-flag. Both bugs CONFIRMED FIXED live
by the user 2026-07-15.
