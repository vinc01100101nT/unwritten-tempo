# Handoff — Sword (Swordsman) skill roster draft

**Task** — Draft the Iron Sword / Swordsman active-skill roster for the V3 channels + role-locked-scaling system. Ref: doc "Active Skill System — V3 (Channels + Role-Locked Scaling)".

**IMPORTANT — division of labor:** the USER composes the skills themselves in the Skill Composer (Unity). This session's job is ONLY to produce the **roster DRAFT** (the list + per-skill spec/channels/affinity) for them to author. Do NOT hand-author the `.asset` files unless the user explicitly asks — they just need the draft to compose from.

## Status — IN PROGRESS (draft proposed, awaiting user's compose)

Roster draft delivered and (pending) approved. No assets written. No code changed.

## Scaling table this draft assumes (locked-but-non-final, user's words)

Two rows were corrected this session vs the earlier draft:

- **All** `durationAffinity` **→ MP.** Grounded in code intent, not memory: `SkillEffect.cs:196` rampCap tooltip says *"Longer duration (more MP)… the support's contribution"*; Buff/Shield tooltips frame duration scaling as sustain/MP. Makes **MP the single support-specialization axis** (bigger heals + longer durations both grow with MP).
- `dot_damage` **→ matches the DoT's own** `damageType` (Physical→Atk, Magic→MAtk), NOT MAtk-only. `damageType` (Physical/Magic/Crit) is a field on EVERY `SkillEffect` (`SkillEffect.cs:137`), so a DoT already carries the tag — the author just weights the affinity list to match it. DoT damage today = `magnitude × affinityMult` (does NOT auto-scale off caster Atk/MAtk), so the affinity list is the only lever → no code change needed.
- Two rows still open (user deferred): slow/accuracy/dmg-taken debuffs (MAtk vs MP), and Haste move_mult (AttackSpeed vs flat).

## Ground truth gathered (measured this session)

- **Iron Sword** (`unity/Assets/3_Data/Resources/Items/Iron Sword.asset`) currently grants: `skill.bulwark`, `skill.war_banner`, `skill.blink_step`. → **No damage or gap-close skill in the kit today**; only offense is the basic swing. Bash/Charge from the Class doc were the retired code-skills, not SkillDefs.
- `skill.bulwark.asset` = Guard(kind 4) + Taunt(kind 5). Still in **OLD asset format** (`magic:`, `maxLevel`, `scale`, legacy `targets/scope`, no `targetingMigrated`) → wants a format cleanup + Guard duration repointed to MP.
- `skill.blink_step.asset` = Teleport only (kind 6); its description promises an atk/cast-speed surge the binding does NOT contain (looks incomplete). Leaving the sword kit regardless.
- **SkillEffectKind ints** (serialized): Damage=0, Heal=1, Status=2, Haste=3, Guard=4, Taunt=5, Teleport=6, Buff=7, Regen=8, Cleanse=9, Vulnerability=10, Execute=11, Dash=12, Knockback=13, Pull=14, ManaLeech=15, Reflect=16, Shield=17.
- **StatId ints**: Atk=0, MAtk=1, HP=2, MP=3, Def=4, Accuracy=5, CastSpeed=6, Evasion=7, AttackSpeed=8, Critical=9.
- **VfxSequences that exist** (`3_Data/Resources/VfxSequences/`): skill_projectile_fire, skill_meteor, skill_dash, skill_teleport, sanctuary, + cue_on\_\* hit/heal/cast cues. **No melee slash/spin sequence** → VFX gap for Bash/Whirlwind (reuse an impact temporarily, author proper slash later).

## THE DRAFT (deliverable) — proposed 5-skill Swordsman kit

Physical front-line bruiser. All offense scales off **Atk**, all defense/utility off **Def/MP**. Numbers are starters (tunable).

| Skill | Archetype | Effects (kind) | DamageType | Affinity | VFX |
| --- | --- | --- | --- | --- | --- |
| **Bash** (new) | Burst | Damage heavy 1-hit + Status **Stun** proc | Physical | dmg→Atk; stun dur→MP, chance raw | ⚠ no slash seq |
| **Charge** (new) | Tempo | **Dash** to point + Damage on arrival + brief Stun | Physical | dmg→Atk | skill_dash ✓ |
| **Whirlwind** (new) | Burst | Damage AoE around self, multi-hit | Physical | dmg→Atk, hits→Atk | ⚠ no spin seq |
| **Bulwark** (keep) | Fortress | Guard + Taunt | — | guard→Def; dur→MP | sanctuary ✓ |
| **War Banner** (keep) | Buff | Damage-buff (ramp), flat | — | amount FLAT; dur→MP | existing ✓ |

Coverage: Burst ×2 (single Bash + crowd Whirlwind), Tempo (Charge), Fortress (Bulwark), Buff (War Banner). No heal (correct for Swordsman — sustain via Guard).

If the user DOES want the assets authored:

- New SkillDefs: `skill.bash`, `skill.charge`, `skill.whirlwind`.
- Iron Sword `grantedSkillIds` → `[bash, charge, whirlwind, bulwark, war_banner]` (drops `blink_step`; asset kept for a later kit).
- Migrate `bulwark` to new format + Guard duration → MP.
- Delete the 3 empty `skill.new*.asset` placeholders.

## Uncommitted working-tree changes

None from this session — read-only investigation only. (Pre-existing unrelated staged/modified files from before this task remain, e.g. SkillEffect.cs, SkillComposerWindow.cs, SkillEffectCatalogWindow.cs — NOT touched by this session.)

## What's left / open questions for user

1. Approve or adjust the 5-skill kit above.
2. Melee-VFX gap: reuse an existing impact temporarily for Bash/Whirlwind, or make slash/spin VFX first?
3. Settle the 2 open scaling rows (debuff stat, Haste move_mult) — optional, non-blocking.
4. Then the USER composes the skills in the Skill Composer from this draft.

## Next single step

Get the user's yes/adjust on the kit table, then they compose. (Do not author assets unless they ask.)

## How to resume

Read `handoffs/sword-skill-roster-draft.md`. This is a ROSTER DRAFT task — user composes skills, we only draft. Kit table above is the deliverable; confirm/adjust with user, then they author in Skill Composer.