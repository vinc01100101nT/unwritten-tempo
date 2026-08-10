---
name: skill-effects-caster-vs-target-passes
description: "Skill effects resolve in TWO passes — caster-side (Dash/Guard/Buff…) always applies, target-side only to gathered entities; partition lives in SkillEffect.IsSelfVerb."
metadata: 
  node_type: memory
  type: project
  originSessionId: 036aac99-29a9-45db-bff5-8157919fb418
---

**Symptom (2026-08-10):** Lunge (`skill.sword.lunge`) with a Single-Target tick aimed at a mob never dashed. Dash speed / cast range were irrelevant.

**Root cause:** `SkillEffectApplier.ApplyBurst` had ONE pipeline — gather who the tick reaches, then apply *all* effects to whoever was gathered. Its Single-scope branch dispatched mob hits and `return`ed early, so self effects (Dash, Guard, Buff, Regen, Cleanse, ManaLeech, Reflect, Shield, Teleport) were only reachable when the pick was NOT a mob. Whether you dashed depended on an unrelated question — "did the gather step include the player?" Same latent bug for any Mobs-only AoE that also carried a self-buff.

**Fix (structural, not per-skill):** `ApplyBurst` now runs two passes.
- Pass 1 `ApplyCasterEffects` → `ApplyOneToCaster`: every self verb, applied once to the caster, regardless of scope/affects/targets (runs even when `targets == None`). No hit loop — a multi-hit flurry must not dash you N times.
- Pass 2: the existing gather, but the caster kinds were REMOVED from `ApplyOneToPlayer`'s switch (plus an early `IsSelfVerb` return), so the two switches are a true partition — a Self-targeted tick physically cannot double-apply a caster effect.

**Single source of truth:** `SkillEffect.IsSelfVerb(kind)` (SkillEffect.cs) — it already existed for legacy targeting migration; do NOT add a second classifier (I nearly shipped a duplicate `SkillEffectSide` class). `SkillTickBinding.HasTargetEffects` / `HasCasterEffects` wrap it for the editor.

**Authoring surface:** Skill Composer hides the radius/affects rows on a tick with no target effects (they'd gather nobody), relabels the mode dropdown "targets"→"aim" there, adds a second readout line naming the caster effects that always apply, and warns when the same caster kind appears twice on one tick (keyed by `buffStat` for Stat Buff, where two entries are legitimate).

**Gotcha:** `TargetMode.Self` is NOT a safe way to say "this is a self effect" — it means *no aiming*, so a Dash under it has no destination and glides to where you already stand. Aim and "who gets hit" are separate questions.

## The aim is a two-stage SEQUENCE (2026-08-10, same session)

A cast asks at most two questions, both derived from the effects, never authored (`SkillDef.NeedsUnitPick` / `NeedsAimPoint`; `Reticle` is now derived from those two so it can't disagree):

1. **Which entity** — only when a Single-target tick has target-side effects. The caster side never asks; the entity is the caster.
2. **Which point** — when a ground tick needs its spot, OR any effect RELOCATES what it lands on (`SkillEffect.NeedsPoint` = Dash/Teleport, renamed from `IsSelfPointVerb` because a relocation can now be target-side).

So "dash the mob I clicked over there" and "dash myself over there" are one rule with stage 1 present or absent. Implemented by chaining the two targeters that already existed (`UnitTargeter` → `GroundTargeter`); stage 1's pick is banked in `SkillRunner.pendingUnit` (a field, not a closure, to keep the confirm delegates cached). Neither targeter's `Cancel()` fires its callback, so `pendingUnit` is cleared on every `Activate` and `OnDisable`.

`VfxContext.effectPoint` is a NEW field: `target` used to double as "the picked unit's position" and "the aimed point", which are different values once both stages exist. `target` now only means where the VFX plays. Every context is built by the single `SkillRunner.BuildCtx` (solo + combo + passive) so no path can forget the field — a missed `effectPoint` would dash the caster to world origin.

**Composer:** a tick is authored as two composable sections — "To the caster" / "To the target", one shared `b.effects` list rendered filtered by side. Targeting lives inside the target section. Effect-kind menus are per-side, so a kind can't hop sections.

**DIRECTION (user, 2026-08-10):** every effect kind should eventually work on BOTH sides — no caster-exclusive kinds. Blocking work: `Health` stores the states (`UnitStates` has GuardCut/ShieldPool/ReflectBonus/ManaLeechPct) but only READS `VulnMult`, so Guard/Shield/Regen/Reflect on a mob currently do nothing; mobs also need a glide-to-point mover for Dash/Teleport. Plan: [[unified-unit-stats]] doc at `docs/active-skill-system-v3/unified-unit-stats.md` — players and mobs 1:1 (same stats+formulas, mobs wear equipment+rings and drop what they wear, NO tier and NO global multiplier). Its step 4 (Health reads its own states) is independently shippable and is the actual unblocker.

Related: [[name-knobs-concretely-one-job]], [[combat-network-readiness-seams]].
