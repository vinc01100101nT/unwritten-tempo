---
name: cast-reticle-soft-fade-persists-on-fail
description: Ground-aim cast circle "persisted" after a failed release — soft-fade reticle masked on success, visible on fail. Fix = hard-clear.
metadata:
  type: project
---

**Symptom:** The ground targeting cast circle appeared to persist after a failed skill release (e.g. no MP). Only ground-aimed skills; single-target (UnitTargeter) was fine.

**Root cause (from code asymmetry, not live-measured):** `UnitTargeter`'s reticle is a `LineRenderer` disabled INSTANTLY on cancel/confirm. `GroundTargeter`'s reticle is a Vfx particle circle (`Vfx.AttachGroundCircle`), and `StopCircle` called the SOFT `EffectHandle.Stop()` — stop emitting, then fade over the prefab's particle lifetime (`fadeTail`). On a SUCCESSFUL cast the wind-up immediately spawns a fresh `castCircle` (same visual) that masks the fade; on a FAILED release nothing replaces it, so the slow fade is visible and reads as "stuck." Every cancel path DID call Stop — the bug was the fade, not a missing teardown.

**Fix:** Added `EffectHandle.Stop(bool immediate)` — `immediate` uses `StopEmittingAndClear` + `Return()` this frame (no fade). `GroundTargeter.StopCircle` now calls `circle.Stop(true)`, so the reticle vanishes crisply on confirm AND any failed release (MP/cooldown/silence). Wind-up `castCircle` in `SkillRunner.EndCast` stays SOFT (it's an effect that should fade, not a reticle). Files: `Vfx.cs`, `GroundTargeter.cs`.

**Gotcha:** A targeting reticle must hard-clear; only real effects should soft-fade. Don't "fix" a lingering reticle by hunting for a missing Stop() — check whether the Stop is soft-fading. Ember Bolt (single-target) is NOT the buggy path; the leak is ground-aim skills (Meteor).
