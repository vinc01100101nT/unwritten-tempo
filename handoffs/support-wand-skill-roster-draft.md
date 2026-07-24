# Handoff — Support Wand skill roster draft

**Task** — Draft the support-wand active-skill roster for the V3 channels + role-locked-scaling system. Ref: doc "Active Skill System — V3 (Channels + Role-Locked Scaling)". Sibling: `handoffs/sword-skill-roster-draft.md`.

**IMPORTANT — division of labor:** the USER composes the skills themselves in the Skill Composer (Unity). This session only produces the **roster DRAFT** (list + per-skill spec/channels/affinity). Do NOT hand-author `.asset` files unless the user explicitly asks.

## Status — IN PROGRESS (draft proposed, awaiting user's compose)
Draft delivered. No assets written. No code changed.

## Real taxonomy (measured — `SkillDef.cs`)
Use the ACTUAL enums, not loose labels. An earlier version of this draft wrongly used "Support"/"Debuff" as archetypes — neither exists.
- **Archetype** (`SkillDef.cs:62`): `Burst=0, Tempo=1, Fortress=2, Buff=3, Curse=4`. There is **no heal/support archetype** — the healer identity is expressed as **Fortress (sustain) + Buff (empower) + Curse (weaken)**.
- **Family** (`SkillDef.cs:68`): `Might, Arcane, Finesse` — the RPS cluster; also the DEFAULT damage type (Might→Physical, Arcane→Magic, Finesse→Crit), overridable per-effect. Wand = **Arcane**.
- **Shape** (`SkillDef.cs:72`): `Single, Area, Aura, Self, Dash`.

## Scaling table this draft assumes (locked-but-non-final)
- **All `durationAffinity` → MP.** MP is the single support-specialization axis (bigger heals + longer durations both grow with MP). Grounded in `SkillEffect.cs:196` rampCap tooltip + Buff/Shield tooltips.
- `dot_damage` → matches the DoT's own `damageType` (Physical→Atk, Magic→MAtk); no code change (affinity list is the lever).
- Open rows (deferred): slow/accuracy/dmg-taken debuffs (MAtk vs MP), Haste move_mult (AttackSpeed vs flat).
- Effect-kind ints & StatId ints: see `handoffs/sword-skill-roster-draft.md`.

## THE DRAFT (deliverable) — proposed 5-skill support-wand kit
Family = Arcane (damage defaults to Magic→MAtk). Every knob points at MP except Hex's potency (MAtk). Numbers are starters (tunable).

| Skill | Archetype | Family | Effects (kind) | DamageType | Affinity | VFX |
|---|---|---|---|---|---|---|
| **Mend** (new) | Fortress | Arcane | Heal, flat burst | — | heal amount → MP | cue_on_heal ✓ |
| **Renewal** (new) | Fortress | Arcane | Regen (heal-over-time) | — | per-tick → MP; dur → MP | cue_on_heal ✓ |
| **Ward** (new) | Fortress | Arcane | Shield (absorb barrier) | — | barrier HP → MP; dur → MP | sanctuary ✓ |
| **Blessing** (new) | Buff | Arcane | Buff (cast/attack-speed up), flat | — | amount FLAT; dur → MP | ⚠ no aura seq |
| **Hex** (new) | Curse | Arcane | Vulnerability (mark mob takes +dmg) | Magic | dmg-taken frac → MAtk; dur → MP | ⚠ no curse seq |

Archetype coverage: Fortress ×3 (burst heal, HoT, shield — the sustain core), Buff (Blessing), Curse (Hex). No Burst/Tempo — correct for a support wand: it keeps people alive and softens targets, doesn't nuke or dash. MP-heavy = healer; investing MAtk bends toward battlemage.

**Open design choice:** Blessing (Buff) vs Purify (Cleanse). Current draft picks Blessing to cover the Buff archetype (4 of 5 archetypes). Swapping to Purify drops Buff and doubles down on Fortress/utility (debuff-stripping). User's call.

## Caveats (still true)
- **Solo-slice targeting:** Heal/Regen/Ward/Blessing land on the CASTER only until a party system exists (`SkillEffect.cs` `TODO(party)`). Today this plays as a self-sustain survival caster, NOT a group healer — its true support value unlocks with parties.
- **No wand Item asset confirmed** — needs its own weapon Item (e.g. `Willow Wand`) with these 5 in `grantedSkillIds`. NOT yet checked whether one exists.
- **VFX gaps:** Blessing (aura) and Hex (curse) have no sequence — reuse-temp or author first.

## Uncommitted working-tree changes
This handoff file only. No assets/code touched.

## What's left / open questions for user
1. Approve or adjust the 5-skill kit.
2. Blessing vs Purify (see open design choice).
3. Whether to build/confirm a wand Item asset (`grantedSkillIds`).
4. VFX: reuse-temp vs author aura/curse sequences.
5. Settle the 2 open scaling rows (debuff stat, Haste move_mult) — non-blocking.
6. Then the USER composes in the Skill Composer.

## Next single step
Get user's yes/adjust on the kit + the Blessing-vs-Purify choice, then they compose. (Do not author assets unless asked.)

## How to resume
Read `handoffs/support-wand-skill-roster-draft.md`. ROSTER DRAFT task — user composes, we only draft. Kit table is the deliverable; confirm/adjust, then user authors in Skill Composer.
