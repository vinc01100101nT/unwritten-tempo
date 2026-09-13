---
name: multihit-sfx-onhit-cue-gated
description: "Multi-hit per-strike sound cut off at target death — the audible cue is OnHitBySkill VfxCue gated by wasAlive, NOT CombatSfx.skillHit (which is empty)."
metadata: 
  node_type: memory
  type: project
  originSessionId: 38619d71-818e-4575-9f2e-2e1880b984f1
---

**Symptom:** A magic multi-hit skill (Ember Bolt, hitCount 5) played its per-strike impact sound while the mob was alive but the sound STOPPED the instant the mob's HP hit 0, even though the damage numbers/flurry kept cascading over the corpse (which lingers ~3s via `DeathDissolve`).

**Root cause (measured, not guessed — took 2 failed reasoning-only fixes first):** The per-hit skill-impact sound the player actually hears is NOT `CombatSfx.SkillHit`. Console probe proved `CombatSfx.skillHit` (the SfxSet on the `CombatSfx` component) is EMPTY — no clip wired — so that whole channel is silent. The real sound is a `Sound` step inside the `on_hit_skill` VfxSequence, fired per hit by `VfxCue.Fire(VfxCueId.OnHitBySkill, transform)` in `Health`. In `Health.TakeMagicDamageOverkill` that `VfxCue.Fire` sat INSIDE the `if (wasAlive)` block — so once the mob was dead, `wasAlive == false` and the cue (sound + hit spark) never fired again → sound cut off at death.

**Fix:** `Health.cs` `TakeMagicDamageOverkill` — moved `VfxCue.Fire(VfxCueId.OnHitBySkill, transform)` OUT of the `if (wasAlive)` block so it fires on every overkill strike; only Flash / ApplyLeech / Die stay alive-gated. Added a matching PHYSICAL sink `TakeDamageOverkill` (physical had no overkill variant) and routed `SkillEffectApplier.ApplyHitToMob`'s physical branch through it — so ALL multi-hit damage (magic + physical) keeps numbers, sparks AND sound running to the end of the count.

**Key measured values:** `on_hit_skill.asset` step 2 = kind 4 (Sound), clip guid `980af43cfb955fd4ea5e49914920d501`. `CombatSfx.skillHit` empty (probe `empty=True` on every beat). Flurry driven by `MultiHitBurst.RoutineList` (single-target hitCount>1 → `SkillEffectApplier.DispatchMobHits`), beats ~0.2s apart on separate frames.

**Gotchas:**
- Two skill-hit sound PATHS exist (dual path): `CombatSfx.SkillHit` (dormant/empty) and the `OnHitBySkill` VfxCue (the live one). Don't wire a fix into `CombatSfx.skillHit` — it's silent. If ever populated it would DOUBLE the VfxCue sound.
- `Health.TakeDamage` (the normal physical path) still bails at `if (IsDead) return 0;` — that's correct for single-hit/basic attacks. Multi-hit physical instead goes through `TakeDamageOverkill`, which keeps sounding past death. Since 2026-09-13 a beat on a creature that was ALREADY dead goes to `Health.ShowOverkillBeat` instead: digits + hit cue (so the sound still runs to the end) and no gameplay at all — no charge, leech, passive, parry/barrier or run-of-hits (`SkillEffectApplier.Strike` corpse branch, `CombatAuthority.Land` presentationOnly).
- The measurement that cracked it: a probe in `CombatSfx.SkillHit` logging `empty=` + a probe in `MultiHitBurst` logging per-beat null/invoke state. See [[name-knobs-concretely-one-job]] and the project's second-fix rule.
