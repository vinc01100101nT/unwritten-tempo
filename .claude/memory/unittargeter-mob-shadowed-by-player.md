---
name: unittargeter-mob-shadowed-by-player
description: "Single-target skill aim couldn't target mobs behind the player / in a crowd — bespoke first-hit raycast shadowed by nearer capsule."
metadata: 
  node_type: memory
  type: project
  originSessionId: 29030dd3-50d4-42f4-bc71-97ef2a0051f4
---

**Symptom:** with 2+ mobs on screen, a single-target skill's aim reticle (UnitTargeter) could not target the mobs — worked fine with one mob.

**Root cause:** `UnitTargeter.PickUnit` used its OWN first-hit `Physics.Raycast` (no magnetism) instead of the canonical pick. The ray returns the FIRST collider it meets; the player stands in the middle of the pack, so any mob positioned *behind* the player on screen (higher up) had its capsule shadowed by the player's capsule — the code hit the player first and explicitly returned "self" (`can't target yourself`), so those mobs were unreachable. One mob (usually in front) worked; more mobs → some behind the player → "can't target the mobs." No magnetism also made small clustered feet-capsules hard to hit.

**Fix (`UnitTargeter.cs` PickUnit):** route the MOB pick through `Player3DController.MonsterUnderCursor(cam)` — the same snap-magnetised, dead-filtered, frame-memoized pick the sword cursor / MobHoverPanel / click-to-attack already use (PERFORMANCE.md names it THE under-cursor-mob system). It skips past the player's body to find the mob and snaps to nearby mobs. Self-cast still found via a direct ray (MonsterUnderCursor only reports mobs); a mask-priority decides which kind wins when both are under the cursor. Collapsed a dual targeting path (centralize rule). Confirmed fixed live by user 2026-07-19.

**Lesson:** don't reinvent under-cursor picking with a raw first-hit raycast — a nearer collider (player or another mob) shadows the intended target. Reuse the magnetised `MonsterUnderCursor` choke. See [[perf-runtime-hotpath-patterns]].
