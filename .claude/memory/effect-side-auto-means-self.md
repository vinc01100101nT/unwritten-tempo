---
name: effect-side-auto-means-self
description: "An authored effect's `side: Auto` means the KIND's traditional side, not \"debuffs go to the enemy\" — SubStatDebuff on Auto lands on the CASTER and every checker passes."
metadata: 
  node_type: memory
  type: project
  originSessionId: af75b40f-fbdb-4ceb-8721-ffc3482df11a
---

**Symptom:** the composed sword skill Offbeat ("cut the target's attack speed 30%") cut the PLAYER'S OWN attack speed. It saved clean, `tools/check-skill-data.py` passed, and the composer showed the effect under its "To the caster" heading — the only place the truth was visible. The user spotted it in a screenshot.

**Root cause:** the effect's `side` was left at `Auto`. `Auto` resolves through `SkillEffect.IsSelfVerb` (`SkillEffect.cs`), and **`SubStatDebuff` is in that self-verb list** along with `SubStatBuff`, `Cleanse`, `Shield`, `Parry`, `Brace`, `BoostOutgoingDamage`, the modifiers and the point verbs. So Auto meant *caster*. Kinds NOT in that list (`Damage`, `Status`, `Taunt`, `Knockback`, `Pull`, `Execute`, `ReduceOutgoingDamage`, `Heal`) do resolve to the target under Auto, which is exactly why the mistake looks impossible until it happens.

**Fix, and then the real fix.** First: author `side: Target` explicitly (the shipped library already did — Pressure says Target out loud, Overclock says Caster out loud for its deliberate self-debuff; Offbeat was the lone deviation). Then, same day, **`EffectSide.Auto` was DELETED entirely** at the user's instruction: the setting now offers only `Caster` (0) and `Target` (1). All 46 authored sides across 31 skills were rewritten in the same change to whatever they resolved to under the old rule, and an audit diff proved every effect still lands where it did. `IsSelfVerb` — the per-kind "traditional side" list — is gone with it. **Do not reintroduce a per-kind default.**

**Gotcha that makes it dangerous:** NO checker can catch it, because every side is legal for every kind. A self attack-speed debuff is a real design (Overclock). So the only defence is reading where each effect lands.

**The durable artifact:** the project skill `.claude/skills/compose-skill/` — its `SKILL.md` leads with this trap, and `audit-effect-sides.py` prints who every authored effect actually lands on, deriving the self-verb list from the C# at run time so it can't drift. Run it after composing anything.

Related: [[design-statements-are-spec-not-claims]].
