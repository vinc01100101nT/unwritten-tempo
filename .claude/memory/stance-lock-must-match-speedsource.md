---
name: stance-lock-must-match-speedsource
description: "A skill's aftercast root must come from the same speed resolution that plays the release; releases are now FIT to the swing time (attack speed), like creatures' — never paced by walking gait."
metadata: 
  node_type: memory
  type: project
  originSessionId: d4cd7234-39ce-4f18-a4c2-4c9be78d7e4d
---

**Today (2026-10-12):** every skill release is FIT to the caster's swing time — `SkillRunner.PlayCycles` plays
`PlayActionFor(releaseStance, Caster.SwingTime, 1, fitToWindow: true)` and roots for exactly that, the same beat a
creature's release is announced with; the nine release rows in `StanceLibrary.asset` (ids 14–22) are FitWindow. Attack
speed shortens a release; movement speed, slows, shoes and the legs' gear do not (they used to, through MoveGait — see
[[paced-release-held-two-minutes]]).

**Symptom that taught the rule (earlier):** a release stance played on MoveGait while its aftercast root was timed by
attack speed, so the root outlasted the animation and the character paused after it.

**Fix that stays:** one shared `StanceController.ResolveSpeedMult(binding, window)` drives BOTH playback (`ApplyStance`)
and the lock (`PlayActionFor` / `ActionDuration`), so lock length == animation length for every speed source. A
non-fit (intrinsic-speed) hold is never timed slower than quarter speed (`MinPaceForAHold`).

**Lesson / gotcha:** whenever a lock/root/cooldown is meant to last "as long as an animation," it MUST be derived from the SAME speed resolution that drives the animation — a single owner (`ResolveSpeedMult`), not a parallel formula. And a skill's timing must never hang off how fast its caster WALKS. See [[direction-2p5d-pivot]] (player+NPC share one stance path).
