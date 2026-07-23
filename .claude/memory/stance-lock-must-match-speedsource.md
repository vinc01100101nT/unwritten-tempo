---
name: stance-lock-must-match-speedsource
description: "A skill's aftercast root lock must be computed from the release stance's ACTUAL speedSource, not always attack-speed — else a MoveGait release stance over-roots (pause after the animation)."
metadata: 
  node_type: memory
  type: project
  originSessionId: d4cd7234-39ce-4f18-a4c2-4c9be78d7e4d
---

**Symptom:** After casting a skill whose release stance uses **MoveGait** speed, the character paused briefly before it could move/cast again — the aftercast root outlasted the visible animation.

**Root cause:** `SkillRunner.PlayCycles` roots the caster for `releaseDur = StanceController.PlayActionFor(releaseStance, 0, MotionScale)` where `MotionScale = 1/AttackSpeedMult`. The OLD `PlayActionFor` computed the lock as `naturalDuration × durationScale` — i.e. always **attack-speed**-scaled — while `ApplyStance` actually plays the clip at its `speedSource` multiplier (MoveGait → `speedMult × GaitSpeedMult`). For a MoveGait release stance with fast gait, the animation finished early but the attack-scaled lock lingered → the pause. The two used DIFFERENT speed resolutions.

**Fix:** extracted the speed resolution into one shared `StanceController.ResolveSpeedMult(binding, window)` used by BOTH `ApplyStance` (playback) and `PlayActionFor` (lock). `PlayActionFor` now returns the real play time: for an intrinsic-speed stance (MoveGait/AttackSpeed/CastSpeed/Baked) `dur = natural / ResolveSpeedMult(b)`; only FitWindow keeps the caller-fed `natural × durationScale` window. Lock length == animation length for every speed source. `StanceController.cs`.

**Lesson / gotcha:** whenever a lock/root/cooldown is meant to last "as long as an animation," it MUST be derived from the SAME speed resolution that drives the animation — a single owner (`ResolveSpeedMult`), not a parallel formula. A second formula that assumes one speed source (attack-speed) silently over/under-roots any stance authored with a different source. Sibling of single-source-of-truth. See [[direction-2p5d-pivot]] (player+NPC share one stance path).
