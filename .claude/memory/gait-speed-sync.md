---
name: gait-speed-sync
description: "Walk/run/sprint leg pace: ground-locked when the clip is measured, else locomotionAnimSpeed × Slow/Haste × gear; no other animation may read it"
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f19ef35-84b0-4638-9858-bfcbef315bba
---

Built 2026-06-25. Locomotion animation speed + footstep cadence scale with the unit's ACTUAL movement speed (a move-speed buff makes legs churn + steps fire faster; a Slow does the opposite).

**Today (2026-10-12): the leg pace belongs to WALK, RUN AND SPRINT ONLY.** `StanceController.ResolveSpeedMult` plays a
walk cycle locked to the ground when its stride was measured, else at `LocomotionAnimFor` = `Player3DController.LocomotionAnimSpeedMult`
(player) / the told number (watched body) / `CharacterSpec.LocomotionAnimMult` (NPC). A walk cycle's saved
`speedSource` is never read, and no other stance can reach the pace — the old `MoveGait` speed source (int 3) was
retired, because it let idle, the jump and skill releases follow movement speed (the two-minute freeze,
[[paced-release-held-two-minutes]]). Idle and Jump became Baked with today's look folded in (×0.75 and ×1.5). The
knob is `CharacterSpec.locomotionAnimSpeed` (was `gaitSpeed`, carried by `FormerlySerializedAs`), shown nowhere but
Character Spec. The pace reads `StatusController.PaceMult` (Slow × Haste; a root does not zero it).

**⚠️ Three gears + formula change (Sprint added 2026-07-21):** locomotion is now **Walk (Shift) / Run (default) / Sprint (V toggle)**. Sprint = the OLD full-speed run; Run demoted to `runSpeedMult` (default **0.5**) of Sprint; Walk = `walkSpeedMult`. `LocomotionAnimSpeedMult = gait × animTier` where `gait = locomotionAnimSpeed × status.PaceMult` and `animTier = (Sprinting||WalkingSlow) ? 1 : runSpeedMult`. So the demoted Run visibly JOGS (legs churn at 0.5 so half-speed travel doesn't foot-skate); Sprint & Walk keep full gait cadence. Sprint drives `StanceId.Sprint` (falls back to Run's clip until a distinct Sprint clip is authored — `Character3DVisual` computes `hasSprint` from `StanceCatalog.Get(Sprint).clip`). Stamina lives on `Character` (`Stamina`/`maxStamina`/`Stamina01`/`OnStaminaChanged`/`SetStamina`); `Player3DController.UpdateSprintAndStamina` owns the rates (drain while sprinting; regen idle>walk>other) + the 0→lock-until-20% gate + fires `VfxCue.OnSprint`. HUD `StaminaBar` (white fill) sits above `UltChargeBar`.

**Travel vs leg pace:** travel is `CharacterSpec.FullSpeed` × gear tier × `status.MoveMult`; `locomotionAnimSpeed`
never changes how fast anyone travels.

**⚠️ Live-3D bug (fixed 2026-07-06):** on the live-3D player (`Character3DVisual`→`StanceController`→`Character3DPose`), `SetActionSpeed` used to `if (currentState != Loco) anim.speed = mult;` — i.e. it SKIPPED the locomotion blend, the exact stances (Walk/Run) whose cadence is the leg pace. Result: Slow/Haste changed the pace but the Run/Walk clip cadence never moved (looked identical to `Baked`). Root cause was conflating the blend PARAM (`PSpeed`, picks walk↔run pose) with clip SPEED (`anim.speed`, leg cadence) — they're orthogonal. Fix: `Character3DPose.SetActionSpeed` always sets `anim.speed = Max(0.01f, mult)` (Loco included); `StanceController.ApplyStance` calls `pose.Play()` (resets anim.speed=1) then `SetActionSpeed` every LateUpdate, so it's the sole live speed owner. Wiring was fine (`Character3DVisual.Start`→`stance.SetLocomotionSource(player)`).

**Buff mechanic** — `StatusController.Haste(mult, secs)` is a real timed speed-buff channel (separate from the Slow ailment, which is `Clamp01` so it can only slow). `MoveMult` now returns `slowFactor(≤1) × hasteMult(≥1)`. DevSettings (P) ▸ "Combat Test (RO)" ▸ **"Speed buff ME ×2 (5s)"** and the existing "Slow ME" exercise both directions.

**NPCs** — self-drive `BakedBillboard8`; its self-drive path scales Walk/Run fps by `speed/refSpeed` (gated `!hasOverride`, so it never touches the player's explicit override). ⚠️ Don't hardcode NPC ref speeds — `TownNPC` calls `visual.MatchGaitTuning(playerBillboard, walkRef, runRef)` once (lazily, static-cached for the whole town) to COPY the player's tuned thresholds + per-action fps and anchor refSpeeds to the player's real Walk/Run ground speeds (`runRef = moveSpeed*UnitScale.ForPlayer`, `walkRef = runRef*walkSpeedMult`). So re-tuning the player's `BakedBillboard8` auto-propagates to townsfolk — no second set of numbers. Mobs don't use BakedBillboard8.

Related: [[world-liveliness-ambient-npcs]], [[bake-8dir-sprites-pipeline]], [[ro-combat-fundamentals]], [[perf-runtime-hotpath-patterns]].
