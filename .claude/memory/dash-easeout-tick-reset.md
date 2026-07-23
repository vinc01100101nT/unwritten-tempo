---
name: dash-easeout-tick-reset
description: "Dash ease-out looked broken because the dash VFX re-fires its \"dash\" tick 3× per cast, restarting BeginGlideTo and resetting the ease baseline."
metadata: 
  node_type: memory
  type: project
  originSessionId: d4cd7234-39ce-4f18-a4c2-4c9be78d7e4d
---

**Symptom:** Dash-effect "ease out" (decelerate near arrival) never visibly triggered — the glide ran at constant speed to the destination — no matter the ease math (tried distance-fraction, time-scaled, then fixed 75%-mark; all correct, none showed).

**Root cause (measured, not the math):** `skill_dash.asset` (the Dash skill's VfxSequence) has **3 dust-puff steps all bound to `tickId: dash`** at delays 0 / 0.15 / 0.30s. `SkillRunner.OnTick` routes every `dash` tick to the Dash effect with `aimPoint: lastCtx.target` (a FIXED destination), so `Player3DController.BeginGlideTo` is called **3× per dash to the same point**. Each call re-ran and **reset `dashTotalDist`** to the *shrinking* remaining distance — re-baselining the ease-out zone (last 25% of total) every 0.15s. Ease-out only "started" after the final reset at t=0.30, by which point almost no distance remained → looked like constant speed.

**Real fix = CONFIG, not code:** it was an authoring mistake — the two extra trail-puff steps should be pure-visual (empty `tickId`); only ONE step should own `tickId: dash`. A `tickId` is an ID and must be UNIQUE per composition. User cleared the duplicates. (Likely caused by the new Duplicate button copying `tickId` verbatim.) A `BeginGlideTo` idempotency guard was briefly added then REVERTED — with the config correct it's dead code, and it would mask author mistakes.

**Guardrail added instead:** `VfxComposerWindow.DrawTick` now shows a `MessageType.Warning` HelpBox when `CountTickId(id) > 1` — surfaces a duplicate-id clash at author time rather than hiding it in code.

**Lesson / gotcha:** a tick-driven skill effect that starts a stateful action (a glide, a channel) fires once PER step that reuses its `tickId` — so a duplicated `tickId` silently re-fires the bound effect and resets any per-invocation baseline (`dashTotalDist`), which is what killed the ease-out. When an effect's *math* is provably correct but the behavior is wrong, suspect the **invocation pattern** (how many times / with what state it's called), not the formula — measure it from the asset + the routing, per [[assume-compiled-never-blame-recompile]]. Prefer surfacing bad config (an editor warning) over defensively absorbing it in runtime code.
