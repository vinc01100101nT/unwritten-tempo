---
name: gait-speed-sync
description: "Walk/run animation + footstep cadence are bound to actual move speed; NPC gait auto-copies the player's tuned BakedBillboard8 values"
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f19ef35-84b0-4638-9858-bfcbef315bba
---

Built 2026-06-25. Locomotion animation speed + footstep cadence scale with the unit's ACTUAL movement speed (a move-speed buff makes legs churn + steps fire faster; a Slow does the opposite).

**Player** — `Player3DController.GaitSpeedMult` = current speed ÷ tuned baseline (1 normal, >1 buffed, <1 slowed). Footstep interval divides by it; `BakedPlayerVisual` calls `baked.SetActionSpeed(loco, GaitSpeedMult)` so the Walk/Run clip fps tracks it. Baseline = `moveSpeed * UnitScale.ForPlayer * (WalkingSlow ? walkSpeedMult : 1)`.

**Travel vs Gait split (defaults set 2026-06-26):** two distinct serialized knobs on `Player3DController` (also live in DevSettings P ▸ Player as "Travel speed"/"Gait speed (×)"):
- `moveSpeed` = **TRAVEL** (default **1**) — base translation folded into `baseSpeed`; `GaitSpeedMult` divides it back out, so changing it moves you faster WITHOUT scaling animation.
- `gaitSpeed` = **GAIT** (default **1.5**) — a baseline multiplier the permanent twin of the DevSettings Haste buff; multiplied into `spd` but NOT `baseSpeed`, so `GaitSpeedMult = gaitSpeed × status.MoveMult` scales movement AND animation/footsteps together. Stacks multiplicatively with live Slow/Haste. `CurrentRunSpeed` (dodge-roll basis) includes it.
Player is added at runtime (no prefab/scene override), so the C# defaults take effect directly.

**⚠️ Live-3D bug (fixed 2026-07-06):** on the live-3D player (`Character3DVisual`→`StanceController`→`Character3DPose`), `SetActionSpeed` used to `if (currentState != Loco) anim.speed = mult;` — i.e. it SKIPPED the locomotion blend, the exact stances (Walk/Run) whose `StanceSpeedSource.MoveGait` feeds `GaitSpeedMult`. Result: Slow/Haste changed `GaitSpeedMult` but the Run/Walk clip cadence never moved (looked identical to `Baked`). Root cause was conflating the blend PARAM (`PSpeed`, picks walk↔run pose) with clip SPEED (`anim.speed`, leg cadence) — they're orthogonal. Fix: `Character3DPose.SetActionSpeed` always sets `anim.speed = Max(0.01f, mult)` (Loco included); `StanceController.ApplyStance` calls `pose.Play()` (resets anim.speed=1) then `SetActionSpeed` every LateUpdate, so it's the sole live speed owner. Wiring was fine (`Character3DVisual.Start`→`stance.SetGaitSource(player)`).

**Buff mechanic** — `StatusController.Haste(mult, secs)` is a real timed speed-buff channel (separate from the Slow ailment, which is `Clamp01` so it can only slow). `MoveMult` now returns `slowFactor(≤1) × hasteMult(≥1)`. DevSettings (P) ▸ "Combat Test (RO)" ▸ **"Speed buff ME ×2 (5s)"** and the existing "Slow ME" exercise both directions.

**NPCs** — self-drive `BakedBillboard8`; its self-drive path scales Walk/Run fps by `speed/refSpeed` (gated `!hasOverride`, so it never touches the player's explicit override). ⚠️ Don't hardcode NPC ref speeds — `TownNPC` calls `visual.MatchGaitTuning(playerBillboard, walkRef, runRef)` once (lazily, static-cached for the whole town) to COPY the player's tuned thresholds + per-action fps and anchor refSpeeds to the player's real Walk/Run ground speeds (`runRef = moveSpeed*UnitScale.ForPlayer`, `walkRef = runRef*walkSpeedMult`). So re-tuning the player's `BakedBillboard8` auto-propagates to townsfolk — no second set of numbers. Mobs don't use BakedBillboard8.

Related: [[world-liveliness-ambient-npcs]], [[bake-8dir-sprites-pipeline]], [[ro-combat-fundamentals]], [[perf-runtime-hotpath-patterns]].
