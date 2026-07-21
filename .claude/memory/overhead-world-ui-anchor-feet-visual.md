---
name: overhead-world-ui-anchor-feet-visual
description: "Overhead world UI (speech/chat bubbles) must anchor on the feet-seated VISUAL, not the player controller root (root sits at WAIST)."
metadata: 
  node_type: memory
  type: project
  originSessionId: be915b80-6c99-467c-922b-7538149371c9
---

Overhead world-space UI on the PLAYER (speech/chat bubble, and any future nameplate/marker) must ride the
feet-seated **`Character3DVisual`** object (its `Character3DPose` transform), NOT the `Player3DController` root.

**Why:** the player's CharacterController capsule is CENTERED (`center.y = 0`, `Player3DController.cs:~93`), so
the controller root transform sits at ~WAIST height. The visible 3D model is a SEPARATE object feet-seated on the
ground each frame by `Character3DVisual.GroundedPos` (its `transform.position` = the feet). NPCs don't have this
split — `AmbientLife.cs:106` sets the townsperson's visual child to `localPosition = zero` and raises the CC
center, so their root == feet. Anchoring a bubble at `root + VisualWorldHeight` was correct for NPCs but floated
the player's bubble ~half a body too high (measured from disk, not live — player only exists in Play).

**How to apply:** one shared anchor `SpeechBubble.ShowOverHead(IStancePose pose, label, life)` rides
`pose`'s own (feet-seated) transform + `VisualWorldHeight + HeadClearance(0.05)`. Player reaches its pose via
`Character3DVisual.Pose` (that component is player-only, so it's the reliable way to get the PLAYER's pose — a bare
`FindFirstObjectByType<Character3DPose>()` can hit a townsperson once NPCs spawn). NPC bark passes its `visual`.

One-bubble-per-character is enforced in `SpeechBubble` via a `Dictionary<Transform, SpeechBubble> active` keyed by
the follow transform: a new line for an owner that already has a live bubble REUSES it (re-`Begin`) instead of
stacking; the entry is dropped on `Recycle` and cleared in `EnsureHost` on scene reload.

Related: [[pivot-is-anchor-source-of-truth]], [[billboard-feet-pivot-unitanchor]].
