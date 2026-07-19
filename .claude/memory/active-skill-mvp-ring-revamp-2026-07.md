---
name: active-skill-mvp-ring-revamp-2026-07
description: 2026-07-19 StatId ring REVAMPED + affinity/effect-kinds/Ultimate added for the active-skill MVP; older StatId refs are stale.
metadata: 
  node_type: memory
  type: project
  originSessionId: 898e1ee0-c2cc-40b1-a5ba-ae390cd842fe
---

# Active-skill MVP built 2026-07-19 (from doc `q170cz40rsxav6kq0r6pe431bn89me24`)

**StatId ring was REVAMPED** to the doc's 10-node ring — older memories/refs to the old
`StatId` are STALE. New enum (`SkillRing.cs`): `Atk, MAtk, HP, MP, Def, Accuracy, CastSpeed,
Evasion, AttackSpeed, Critical`. **Removed** `MDef`, `CritChance`, `CritDamage`. **Added**
`Accuracy`, `CastSpeed`, `Critical`. Ring order (`SkillRing.Ring`): `Def(top), HP, Atk,
Accuracy, CastSpeed, MAtk(bottom), MP, Evasion, Critical, AtkSpd` — Def↔MAtk are the poles.
Wiring in `Character.cs`: **Def node is dual** (its % scales both `Defense` AND `MagicDefense`);
**Critical node** drives BOTH crit rate and crit damage (`CritChance01` + `CritMultiplier` read
`PassivePercent(StatId.Critical)`); **Accuracy** → `Hit` via `Scale(..,Accuracy)`; **CastSpeed**
→ `CastSpeedMult += PassivePercent(CastSpeed)`. All ring bonuses are % (no flat). Save `rankStats`
is `StatId[]` — pre-revamp saves map onto wrong nodes; loader drops unknown ids (no shipped saves).

**Affinity** = weighted stat-list on each `SkillEffect` (`affinity` = its MAIN OUTPUT, `durationAffinity`
= its seconds; struct `StatWeight{stat,weight}`). **NEVER call it "potency"** — user banned the word;
label channels by what they boost (damage / heal amount / proc chance / slow amount / duration …); see
`SkillComposerWindow.AffinityOutputLabel`. `affinity` scales: damage (Damage/Execute via RollPower),
heal/DoT/buff/slow magnitude (ScaledMagnitude), or — for a STATUS proc (chance>0) — the CHANCE itself
(`Fires`), since a stun has no magnitude. Multiplier = `1 + Σ(normWeight × Character.RingBonus(stat))`
(gated passive %). Empty list ⇒ ×1. Pure CC (stun/freeze/silence) hides the output channel (nothing to scale).

**New effect kinds** (`SkillEffectKind`): Guard (wraps `Character.BeginGuard`), Taunt
(`MonsterAI3D.Taunt`), Teleport (`Player3DController.Teleport` — instant blink to the range-clamped cast
point; player-target effects now apply to the caster regardless of tick radius), Buff (timed Damage/AtkSpeed/CastSpeed
on Character; `BuffStat` enum), Regen (`Character.BeginRegen`, ticked in Update), Cleanse
(`StatusController.Cleanse`), Vulnerability (`Health.BeginVulnerable`), and `Execute` (bonus dmg vs
low HP). **SINGLE-RESPONSIBILITY effects — NEVER mix sub-effects into one effect** (user hard rule):
Damage owns only `hitCount` (multi-hit); PROCS are SEPARATE effects with a `chance` (0..1) field —
Stun = a Status effect w/ chance, Execute = its own kind w/ chance. `SkillEffectApplier` runs a
per-binding HIT LOOP (length = Damage.hitCount); each hit lands the strike + rolls every proc
(`Fires()`); non-proc effects (chance 0) apply once on hit 0. Slow/Stun/Burn reuse existing `Ailment`.

**Ultimate** = `SkillDef.tier`(Base/Ultimate) + `chargeCost`; shared meter on `Character`
(`UltCharge`, fills from damage dealt via `Health.ApplyLeech`, damage taken, and casts via
`SkillRunner.Spend`); dedicated ULT hotbar cell (10th runtime-cloned cell, key **R**) in
`HotbarPanel`; `HotbarBindings.BindUlt`/`Ult` + `AutoBind` routes learned ultimates to it.

**6 MVP skills authored** under `3_Data/Resources/Skills/`: `skill.meteor` (Burst ULTIMATE),
`skill.ember_bolt`, `skill.blink_step`, `skill.bulwark`, `skill.mending_tide`, `skill.war_banner`,
`skill.hex_field`. **VFX is PLACEHOLDER** — all reuse the `meteor` VfxSequence's single-fire
`meteor_ball` tick (guid 59bc86ccc164d7e4085172138da6f480); 5 of them still need real VfxSequences
authored in the Vfx Composer (the doc's flagged bottleneck). Numbers are first-pass feel drafts.
Affinity Heatmap was deliberately NOT built (deferred analytics). See [[active-skill-archetype-ring-mapping]].

**Icons + buff bar (2026-07-19 follow-up):** all 7 skills got icons from `1_Authored/Art/Billboards/Ui/Skill Icon/`
(Multiple-mode sprites — reference the sub-sprite `internalID` as fileID, NOT 21300000; Meteor's old ref was
broken). New **buff bar** on the right edge: `PlayerBuffs` registry (`Character.Buffs`, display mirror written by
`SkillEffectApplier.RegisterBuff` when a Buff/Guard/Regen/Haste lands, tagged with the source skill's icon+name) +
`BuffBarPanel` (self-builds on GameUICanvas — added in `UIShellBuilder`, so **Build UI Shell needed**). Also fixed a
delivery bug: **player-target SELF effects (Teleport/Buff/Guard/Regen/Cleanse) now apply to the caster regardless of
tick radius** (`SkillEffectApplier.IsSelfKind`); zone effects (Heal, etc.) still require the player inside the radius
(preserves Sanctuary). Blink was changed from a roll (`TriggerDash`, removed) to a true instant `Teleport`.
