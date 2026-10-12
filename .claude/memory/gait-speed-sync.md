---
name: gait-speed-sync
description: "Walk/run/sprint legs play locked to real ground speed (every body measured); there is no gait-speed knob any more — deleted 2026-10-12"
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f19ef35-84b0-4638-9858-bfcbef315bba
---

**Today (2026-10-12): the legs follow the ground, and nothing else sets their pace.** `StanceController.ResolveSpeedMult`
plays a walk cycle (Walk/Run/Sprint) at `StrideMath.LockedPlayback(groundSpeed, clipSpeed)`: the body's real ground
speed ÷ how fast that clip walks on that body (`Character3DPose.TryClipSpeed`). All three clips are measured on every
body in the roster (51 in `StanceLibrary.asset`), and an unmeasured human borrows `speedPerHumanScale × humanScale` with a
one-time warning — so Slow, Haste, shoes and the walk/run/sprint gear all reach the legs through ground speed alone. A
clip that cannot answer (no animator, a non-human unmeasured body) plays at its own speed × mult.

**Deleted the same day, because the lock made them dead:** `CharacterSpec.gaitSpeed` (briefly `locomotionAnimSpeed`),
`Player3DController.GaitSpeedMult`, `StatusController.PaceMult`, the `MoveGait` speed source (int 3 kept dead), and the
cadence a player's machine sent with its position (`netGait`) for watched bodies — a watched body already gets the
travel speed (`TellTravelSpeed`), which is all the lock needs. Idle and Jump, which used to follow MoveGait, became
Baked ×0.75 and ×1.5 (the old look). MoveGait on skill releases caused [[paced-release-held-two-minutes]].

**Three gears (Sprint added 2026-07-21):** Walk (Shift) / Run (default) / Sprint (V toggle). Travel =
`CharacterSpec.FullSpeed` × gear (`walkGearMult` / `runGearMult` 0.5 / 1) × `status.MoveMult`. Sprint drives
`StanceId.Sprint` (falls back to Run's clip until a distinct Sprint clip is authored — `Character3DVisual` computes
`hasSprint` from `StanceCatalog.Get(Sprint).clip`). Stamina lives on `Character` (`Stamina`/`maxStamina`/`Stamina01`/
`OnStaminaChanged`/`SetStamina`); `Player3DController.UpdateSprintAndStamina` owns the rates (drain while sprinting; regen
idle>walk>other) + the 0→lock-until-20% gate + fires `VfxCue.OnSprint`. HUD `StaminaBar` (white fill) sits above
`UltChargeBar`.

**⚠️ Live-3D bug (fixed 2026-07-06):** `Character3DPose.SetActionSpeed` used to skip the locomotion blend
(`if (currentState != Loco) anim.speed = mult;`), so the Walk/Run cadence never moved. The blend PARAM (`PSpeed`, picks
walk↔run pose) and clip SPEED (`anim.speed`, leg cadence) are orthogonal. Fix: `SetActionSpeed` always sets
`anim.speed = Max(0.01f, mult)` (Loco included); `StanceController.ApplyStance` calls `pose.Play()` then `SetActionSpeed`
every LateUpdate, so it is the sole live speed owner.

**Buff mechanic** — `StatusController.Haste(mult, secs)` is a real timed speed-buff channel (separate from the Slow
ailment, which can only slow). `MoveMult` returns `slowFactor(≤1) × hasteMult(≥1)`, 0 while rooted.

Related: [[world-liveliness-ambient-npcs]], [[ro-combat-fundamentals]], [[perf-runtime-hotpath-patterns]].
