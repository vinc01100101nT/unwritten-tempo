---
name: npc-walk-feet-bury-dual-grounder
description: Live-3D town NPC feet buried ~skinWidth (2cm) while WALKING (idle fine) — two grounders fighting.
metadata: 
  node_type: memory
  type: reference
  originSessionId: 7267e5ec-04a8-408b-a4c3-ccb46759fd1f
---

**Symptom:** Live-3D townsfolk (AmbientLife spawns in lastline) sank their feet ~2cm below the floor
WHILE WALKING; idle was fine. Player grounded perfectly. Ate ~5 prior rounds of offset guesses.

**Root cause (measured live):** DUAL grounding path — two owners of the NPC's foot Y.
- `TownNPC3DVisual.LateUpdate` `[DefaultExecutionOrder(-100)]` seats the CHILD model: `model.position.y = groundY`. Correct, runs FIRST.
- `TownNPC.LateUpdate` (default order 0, runs LATER) also grounded, but pinned the ROOT: `transform.position.y = groundY`, and ONLY when `agent.Moving`.
- The root physically rests at `groundY + skinWidth` (0.02). TownNPC dropped the root by that 0.02; the child model (parented to root, localPos fixed) was dragged down 0.02 with it → buried feet. Idle skipped it (`!Moving` early-return), so idle looked fine.

**Proof:** live sample showed moving NPCs `modelY == grdY − 0.02` for EVERY one; idle NPCs `modelY == grdY`. 0.02 = CharacterController skinWidth exactly.

**Why the old grounder existed:** `TownNPC.LateUpdate` root-grounding was written when townsfolk were BILLBOARDS (sprite pivot IS the root, so grounding the root = grounding the feet). It predates live-3D townsfolk.

**KEY FACT:** townsfolk are now ALL live-3D — `AmbientLife.SpawnOne` always does `root.AddComponent<TownNPC3DVisual>()` (AmbientLife.cs:136), NO billboard branch exists. So the billboard-era root-grounder was pure DEAD code fighting the live-3D grounder.

**Fix (final):** DELETED `TownNPC.LateUpdate` entirely (not gated — there are no billboard townsfolk to keep it for). Also removed the now-orphaned `groundMask` field + the `ground` param on `TownNPC.Init`, and dropped that arg at the `AmbientLife.cs:138` call. Sole grounder for townsfolk = `TownNPC3DVisual` (grounds the child model). No offset math changed.

**Gotcha for next time:** do NOT assume billboard townsfolk still exist — they don't. The whole baked-character billboard system was later DELETED ([[baked-character-billboards-removed]]); `TownNPC` keeps only the `IStancePose` seam feeding a live-3D `Character3DPose`. See [[direction-2p5d-pivot]] (PLAYER+NPC live-3D, only MOBS billboard).

**Lesson:** a "walk buries / idle fine" foot bug where the gap == skinWidth ⇒ suspect a SECOND grounder writing the root at a later execution order, not the offset formula. Related: [[mob-float-skinwidth]], [[player-feet-float-skinwidth-fallback]]. Diagnose by logging `modelY` vs raycast `grdY` split by Moving state — do NOT re-tune offsets.
