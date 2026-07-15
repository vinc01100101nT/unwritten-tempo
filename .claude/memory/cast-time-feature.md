# Cast time (spell wind-up) feature — wired 2026-07-03

RO-style cast wind-up: a skill can now delay before it fires, showing a cast bar + a looping aura, and
firing an `OnCastStart` cue. Before this, `SkillRunner` cast INSTANTLY and three pieces existed but were
disconnected (the stat, the CastBar UI, nothing between them).

## The three pieces (were half-built, now connected)
- **Stat (pre-existing):** `Character.CastTimeMult` (Character.cs:338) = `1 − DEX/150`, clamp01 → instacast at 150 DEX. Surfaced in `DerivedSnapshot.castTimeMult`.
- **UI (pre-existing, was DEAD):** `CastBar.cs` (`Set`/`Hide`, floating fill bar, one shared world-space canvas). Was never called by anything until now.
- **Data (added):** `SkillDef.castTime` (seconds, `[Min(0)]`, default 0). Editable in the Skill Composer — slider row "cast time (s)" 0–10 in the Cost/cadence section (`SkillComposerWindow.cs`).

## The wiring — all in `SkillRunner.cs`
- `Cast`/`CastTargeted` → `StartCastOrPlay()` (the single fork). `castTime <= 0` → `PlayCycles()` immediately (instant, NO bar, NO cue). `castTime > 0` → `castRoutine = StartCoroutine(CastWindup())`.
- `CastWindup()`: `dur = def.castTime * CastTimeMult`. If `dur > 0`: `castAura = VfxCue.Begin(OnCastStart, transform)` (persistent looping aura), then per-frame fill `CastBar.Set(UnitAnchor.Head(transform), t/dur, def.displayName, def.tint)` until `t >= dur`; interrupt check each frame. Then `EndCast()` + `PlayCycles()`.
- `EndCast()` = the ONE teardown choke point: `castAura.Stop()` + `CastBar.Hide()` + `castRoutine=null`. Called on complete, interrupt, AND `OnDisable` — so the aura/bar can never outlive the wind-up.
- **Interrupts:** `Character.IsDead`, `StatusController.Player.Silenced`, or `CannotAct` (Stun/Freeze). MP is ALREADY spent at confirm (`Spend()` in `Cast`/`CastTargeted`) → an interrupted cast is a wasted cast (RO-style).
- **Re-cast guard:** `Activate()` returns false while `castRoutine != null` (can't start a 2nd cast mid-wind-up).

## OnCastStart cue (`VfxCueId`, append-only enum)
- Added `OnCastStart` at the END of `VfxCueLibrary.cs` enum (+ its row in `VfxCueComposerWindow.cs` under a new "Skill lifecycle" section — the window HAND-LISTS `Row(id)` calls, it does NOT auto-render from the enum; `Reconcile()` only adds the DATA row).
- Fired via `VfxCue.Begin` (persistent), so **the bound sequence MUST be an Attach/looping effect** — a one-shot bound there flashes once then `Stop()` could hit a recycled pooled runner (the standard `*Start`-cue rule).

## Gotchas
- **Gated on AUTHORED castTime, not effective.** `castTime == 0` → no cue, no bar (user's explicit rule). If authored `> 0` but DEX drives effective `dur <= 0`, the wind-up is skipped entirely (no aura/flicker) — only authored castTime drives the feature.
- **Cooldown starts at cast START** (MP spent + `nextUse` set in `Spend()` at confirm, before the wind-up), not at completion. One-line move if we ever want cooldown-after-cast.
- New `.cs`/enum rows need `refresh_unity scope=all mode=force` before the Composer windows see them (the un-imported-files gotcha).

## Ease-in/loop/ease-out on the aura (authoring, NOT code)
Continuous aura with soft in/out = a PARTICLE-authoring workflow, not a sequence trick:
- **Ease-in** = the particle system's own Size/Alpha-over-Lifetime + emission ramp on Play.
- **Continuous middle** = `Attach` step + `persistent` (loops until Stop).
- **Ease-out (soft tail)** = `EffectHandle.Stop()` → `ParticleSystem.Stop(true, StopEmitting)` (Vfx.cs:174): stops NEW emission, lets alive particles finish their `startLifetime`. Give particles an alpha/size fade over lifetime → graceful tail for free.
- A DISTINCT outro animation at Stop is NOT supported: `VfxDirector.Finish()` (VfxDirector.cs:274) raises a stopping Attach's `emitEventOnEnd` to the gameplay `onTick` ONLY, deliberately not to fresh OnEvent VISUAL steps. Would need an engine change (keep pooled runner alive for the outro) — deferred.
