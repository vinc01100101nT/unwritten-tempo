---
name: flurry-beats-lost-mastery-lift
description: Later hits of a multi-hit skill lost the caster's mastery/efficiency lift — burst-scoped statics must ride every delayed callback
metadata:
  type: project
---

**Symptom (fixed 2026-10-06):** in a multi-hit skill, only the first strike carried the caster's skill-mastery and
efficiency lift; every later strike (0.2s apart) landed without it, or with another cast's.

**Root cause:** `SkillEffectApplier.masteryLift` is a static set for the duration of `ApplyBurst` and restored in its
`finally`. `MultiHitBurst` runs hit 0 synchronously and the rest on later frames from a coroutine, after the burst has
returned — so `OutputMult` read whatever the static held then.

**Fix:** the two flurry callbacks (mobs and people) capture `masteryLift` (and the bounce `hopScale`, added the same
day) at schedule time and set/restore them around each later beat, the same way the passive cause and
`RepeatsDamageTotal` already rode the callback.

**How to apply:** any NEW burst-scoped static that output reads must be captured into every delayed path the burst
schedules (flurries, bounce hops, anything landing on a later frame). See [[one-arrow-for-every-bow]].
