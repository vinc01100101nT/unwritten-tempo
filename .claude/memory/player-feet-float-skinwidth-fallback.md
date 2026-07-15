---
name: player-feet-float-skinwidth-fallback
description: "Player capsule floated above ground = CharacterController skinWidth hover. FIX = lower capsuleSkinWidth (one number). GLOBAL RULE (2026-07-03, vince): skinWidth = 0.02 for EVERY unit; owner = Player3DController.DefaultCapsuleSkinWidth, PathAgent3D reads it. Claude over-engineered it; lesson = isolate the smallest variable before restructuring the model."
metadata: 
  node_type: memory
  type: project
  originSessionId: 0c3fe0d7-a3c9-4712-9faf-6095daf76c7e
---

Resolved 2026-07-03 (long saga — Claude massively over-engineered this; the real fix was one number).

**GLOBAL RULE (2026-07-03, vince): skin width = `0.02` for EVERY unit.** Single owner = `Player3DController.DefaultCapsuleSkinWidth = 0.02f`; `PathAgent3D` now READS that constant (was a duplicate `0.02f` literal) so player + mobs + NPCs share one value. The original fix below used `0.01`; the standard is now `0.02`.

**THE ACTUAL FIX (user found it): lower `capsuleSkinWidth` `0.08 → 0.01`.** The green CharacterController capsule floated above the ground because a CharacterController ALWAYS hovers its capsule bottom by `skinWidth`. That hover WAS the visible gap. Turn skinWidth down → capsule bottom touches ground. Done. The user kept `capsuleCenter=(0,0,0)`.

**PROCESS LESSON (the important part): isolate the smallest variable, don't restructure the model.** The resting-height formula `restY = groundY + skinWidth − (center − h/2)·lossy` was written out EARLY with `skinWidth` right in it — yet Claude changed `capsuleCenter` (big) and re-pivoted all the sprites (bigger) instead of turning the smallest term. When a placement is off by a small constant, ask "which ONE value, nudged, closes the gap?" before touching structure. Also: dump the misbehaving object's inspector values as candidate knobs FIRST (skinWidth was never even measured until the end).

--- prior (over-complicated) framing kept for reference ---

**Root cause — capsule center.** `Player3DController`'s CharacterController rests its capsule on the floor; where the ROOT (pivot) ends up = `groundY + skinWidth − (center.y − height/2)·lossy`. With `capsuleCenter=(0,0,0)` (centered capsule, `height=0.5`) the root rests at ~`0.18·lossy` = the CHEST. That is the floating gizmo. Mobs sit at the feet because their CC `center=(0, height/2, 0)` stacks the capsule UP from the pivot.

**Why my first fix "didn't take":** I changed the code DEFAULT (`DefaultCapsuleCenter`) and the field initializer, but the EXISTING player in `Systems.unity` had its own **serialized `capsuleCenter`**, and `ApplyCapsule()` (Awake/OnValidate) stamps the serialized field onto the CC every time. ⚠️ **META-LESSON: a code default ≠ the serialized value on an existing instance.** For capsule issues, fix the SERIALIZED field on the instance (or rebuild), and edit the OWNER field (`capsule*` on `Player3DController`) — the CharacterController's own Center/SkinWidth fields are just mirrors it overwrites, so editing them "does nothing."

**Final config the user chose:** KEEP `capsuleCenter=(0,0,0)` (centered capsule — the visual is feet-seated by `BakedPlayerVisual` regardless) and lower `capsuleSkinWidth` `0.08 → 0.01` so the capsule BOTTOM rests ~on the ground (a CharacterController always hovers by `skinWidth`; mobs use `0.02`). Baked as defaults: `DefaultCapsuleCenter=(0,0,0)`, `DefaultCapsuleSkinWidth=0.01`; `capsuleCenter` field now mirrors the const (single source). Persisted on the player in `Systems.unity`.

**Also done this saga:** the player's baked sprite is now FOOT-pivoted (see [[billboard-feet-pivot-unitanchor]]) so the SPRITE pivot is at the feet too. And a latent bug fixed: `BakedPlayerVisual.GroundedPos` FALLBACK (used only when no Ground-layer collider under the player) ignored `skinWidth` & `lossy` — fixed to `(cc.center.y - cc.height*0.5f)*lossy - cc.skinWidth`. Ties to [[mob-float-skinwidth]].

**Debugging technique that finally cracked it:** side-by-side `execute_code` dump of player vs mob transform+CC (`center.y`, capsule world-span) exposed the capsule-center difference immediately — do that FIRST for "one unit is placed wrong" bugs. Also: `ScreenCapture.CaptureScreenshotAsTexture` → write PNG → Read it = how to SEE the game view via MCP without entering Play.
