---
name: character-3d-visual-system
description: "Live-3D player visual (Animator) sharing the billboard's brain + StanceController choke point via IStancePose"
metadata: 
  node_type: memory
  type: project
  originSessionId: 103b083d-880d-4e90-b082-336a01c28a5a
---

The player can render as a LIVE 3D skinned Quaternius model (Animator-driven) instead of a baked billboard,
picked per-visual — done as the Option-1 "shared brain, one choke point, two backends" refactor (2026-07-04,
`experiment/3d-characters`). Confirmed working in the real Town scene.

**The seam:** `StanceController` (the single pose choke point) no longer hard-drives `BakedBillboard8` — it drives
an **`IStancePose`** sink (`Play`/`PlayFall`/`SetActionSpeed`/`HasAction`/`NaturalDuration`/`ResumeSelfDrive`).
Two implementers: `BakedBillboard8` (billboard) and `Character3DPose` (Animator: locomotion → 1-D `Speed` blend
tree, one-shots → `CrossFadeInFixedTime` to a state named by `CharacterAction`; also yaws the model to face travel).

**Shared brain (no dual path):** the "which `StanceId` am I in" decision was lifted out of `BakedPlayerVisual`
into **`PlayerStanceBrain.Decide(...)`** (pure struct-returning). BOTH `BakedPlayerVisual` (billboard host) and
`Character3DVisual` (3D host) call it, so attack/jump/roll/hurt/pickup/cast/die/fall/walk/run is decided ONCE.
Each host still owns its own rig-riding + grounding (presentation-specific).

**Action→clip SSOT:** `Character3DClipMap.ClipFor` (CharacterAction → Quaternius take suffix, e.g. Attack→SwordSlash,
Cast→Shoot_OneHanded, Hurt→RecieveHit, Die→Death). BOTH the editor controller-builder and `Character3DPose` read it,
so state names can't drift.

**Wiring:** `Tools ▸ unwritten ▸ Player ▸ Use 3D Character (Systems scene)` (`Use3DCharacter.cs`) — builds
`Quat/Player3D/QuatPlayerController.controller` (Locomotion blend + 16 one-shot states) and drops the 3D rig on the
player (model root scaled to `Build3DCharacterSpike.TargetHeight` 0.327u, Animator + Character3DPose + StanceController
+ Character3DVisual). Reversible via **Revert 3D Character**. Requires the model's native **Generic** rig (see
[[quaternius-humanoid-drops-clips]] — never force Humanoid).

**Gotchas / open items:**
- Test the player in **Systems.unity** (or a real map), NOT the spike scene — `SystemsPlayMode` sets the boot-map to
  the active scene at Play, and a scene not in Build Settings errors ("couldn't be loaded"). See [[persistent-systems-layer]].
- `Character3DPose` reads state names via `Animator.HasState(0, hash)`; a missing action silently falls back to Idle.
- Cast has no native Quat clip (mapped to Shoot_OneHanded) — revisit if it reads wrong.
- Back-to-back attacks need a 1-frame gap to re-trigger the CrossFade (fine at normal ASPD).
- Builds on the [[stance-system]] choke point; mobs/NPCs could get the same 3D treatment later via their own IStancePose host.
