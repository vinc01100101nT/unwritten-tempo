---
name: magnitude-always-bonus-fraction
description: "SkillEffect.magnitude is ALWAYS a bonus fraction (0.3 = +30%) — it used to mean absolute-multiplier when non-stacking and per-stack-bonus when stacking, so 1.03 ran as +103%/stack."
metadata: 
  node_type: memory
  type: project
  originSessionId: ea2b8e03-67d0-4d72-917c-7b6916fc1138
---

**Symptom.** Momentum was authored `magnitude: 1.03` intending ~+3% damage per stack. It actually gave **+103% per stack**. Lock Blades (`1.05`, 6 stacks per cast) gave **+630%** for 5s.

**Root cause — one field, two meanings.** `UnitStates.State.Mult` was `stacking ? 1f + magnitude : magnitude`. So a NON-stacking buff stored an absolute multiplier (1.3 = ×1.3) while a STACKING one stored a per-stack bonus (0.03 = +3%). The Skill Composer and the tooltip only ever spoke the first: the slider was labelled *"flat multiplier ×"*, clamped **1..3**, and the tooltip printed `magnitude - 1`. **The correct value was literally unauthorable** — the user tried to fix it in the composer and the slider bottomed out at 1.0 (= +100%).

**Fix (2026-08-13, commit `666a4305`).** Magnitude is now ALWAYS a bonus fraction, no branch:
- `State.Mult => 1f + magnitude`, Slow reads `1f - magnitude`, unconditionally (`UnitStates.cs`).
- Each entry point converts its own natural units ONCE: `StatusController.Haste` and `Character.Buff*` still take multipliers and subtract 1; the ailment Slow still authors a target-speed factor (0.4 = moves at 40%) and converts to `1 - mg`; `SkillEffectApplier.ApplyBuff` adds 1 to the authored fraction.
- Composer slider `0..2`, labelled **"bonus (0.3 = +30%)"**; Haste slider `-0.9..2` (negative slows — its mob-debuff job); tooltip appends **"per stack"** on a stacking buff.
- Migrated assets: Overclock `1.3 → 0.3` (same +30%), Momentum `→ 0.03`, Lock Blades `→ 0.05`.

**Verified** by offline replay: every unrelated path (GM `/speed`, dev haste slider, authored slows, Root, Overclock) byte-identical; only the stacking buffs changed, to their intended values.

**Gotchas / do not undo:**
- Additive channels (Guard cut, Vulnerability, DoT, shield, leech, reflect, blind) were ALWAYS fractions and are untouched — don't "unify" them again.
- `Character.DamageBuffMult`'s ramp still works in multiplier space (`m = s.Mult` then `+= rampPerStep`), so `Mult` staying `1 + x` is load-bearing.
- Every authored ramp in the project is currently `0` — the ramp is dormant but real; don't delete it.

**The lesson is the project's own hard rule:** name a knob for the exact thing it changes and give it ONE meaning. A field whose meaning depends on a sibling boolean is the same disease as the retired "potency/Strength" channel. See [[name-knobs-concretely-one-job]] and [[additive-not-compounding-buffs]].
