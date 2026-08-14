---
name: damage-buff-summed-not-first-pile
description: "Damage rolls read only the FIRST DamageBuff pile (States.Get returns first-of-kind), so Momentum + Lock Blades gave only one of them — the 'buffs ADD' rule was documented but not honoured in code."
metadata: 
  node_type: memory
  type: project
  originSessionId: ea2b8e03-67d0-4d72-917c-7b6916fc1138
---

**Symptom.** With two damage buffs live from different skills, only one lifted your damage — and *which* one depended on cast order. Momentum ×3 (+9%) plus Lock Blades (+30%) gave **×1.09 instead of ×1.39 — 27.5% of damage silently lost.**

**Root cause.** Buff piles are keyed by the GRANTING skill (`State.sourceSkillId`), so two skills buffing the same stat are two separate `DamageBuff` entries. `Character.DamageBuffMult` fetched `States.Get(StateId.DamageBuff)` → `Find(id)` → **the first entry of that kind**, and returned only that one.

**The tell that made it obvious:** its two siblings, `AspdBuffMult` and `CastBuffMult`, both read the SUMMED store value (`States.AttackSpeedBuffMult` / `.CastSpeedBuffMult`). Damage was the odd one out of three. And `UnitStates.DamageBuffMult` — the correct summing version — had **zero callers**. Two paths, one dead, one wrong.

**Fix (2026-08-13, commit `54473237`).** `UnitStates.DamageBuffMult` now walks EVERY `DamageBuff` pile and folds each pile's own ramp; `Character.DamageBuffMult` became a one-liner reading it, identical in shape to its two siblings.

**Non-obvious design point — it is computed LIVE, not cached.** Every other derived scalar in `UnitStates` is cached and only recomputed when a stack expires (`nextExpiry` drives `EnsureFresh`). A ramp grows on its OWN clock (a step every `rampInterval`) with no expiry to wake the cache, so a cached figure would sit stale between steps. The loop is over the handful of live states and allocates nothing, so it's safe per-hit. **Don't "optimise" it back into the cache without also scheduling ramp-step wake-ups.**

**Verified:** single-pile cases (including ramp and ramp-cap) byte-identical; only multi-pile cases changed.

**Recurring lesson (this is the third instance in this repo):** when one member of a uniform set misbehaves, **check every member of that set** — the sibling that's wired correctly tells you what right looks like. Also: a "single source of truth" with a second, unused implementation next to it is a bug waiting to be picked. See [[find-or-create-hides-duplicates]] and [[additive-not-compounding-buffs]].
