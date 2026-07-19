---
name: multihit-yellow-total-never-wired
description: "Skill-composer multi-hit (hitCount>1) applied damage but showed no RO yellow total — MultiHitBurst was dead code, never called."
metadata: 
  node_type: memory
  type: project
  originSessionId: fb4f3099-71c4-45bd-95c3-4a1952cc358c
---

**Symptom:** A skill's Damage effect with `hits`/`hitCount` = 5 (Skill Composer) dealt 5 hits in game but showed no RO-style rising yellow combo total — just overlapping per-hit numbers.

**Root cause (measured):** `SkillEffectApplier.HitLoops` DID run the damage loop `hitCount` times, but (1) synchronously in one frame (no stagger) and (2) the yellow accumulating total is drawn only by `MultiHitBurst`, whose `Run` was **never called by anything** — `git log -S MultiHitBurst -- SkillEffectApplier.cs` was empty. The feature was authored (`MultiHitBurst.cs` + `DamagePopup.SpawnHeld/SetHeld/Release`) but never connected to the skill path. PERF_LOG called it "kept (planned)". Nothing was removed — it was never wired.

**Fix:** `SkillEffectApplier.ApplyBurst` now collects mobs into a reused `burstTargets` list and calls `DispatchMobHits`: `HitLoops<=1` → unchanged instant path; `>1` → `MultiHitBurst.Run(list, hits, 0.07s, onHit)` which cascades the strikes over time and pops one rising yellow total per mob. Added `MultiHitBurst.Run(List<MonsterAI3D>, hits, interval, onHit)` explicit-snapshot overload + `RoutineList`. Per-hit uses `TakeMagicDamageOverkill` (magic, shows past death) or `TakeDamage` (physical, stops at kill — minor gap).

**Gotchas:** `MultiHitBurst.Run` copies the target list (caller reuses `burstTargets`). The `onHit` lambda allocs a closure per multi-hit CAST (not per frame — acceptable). `hitCount` = "strikes per landing" (RO flurry), distinct from the VFX tick's `tickCount` (repeat-over-time). See [[name-knobs-concretely-one-job]].
