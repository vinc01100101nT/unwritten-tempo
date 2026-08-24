---
name: compose-skill
description: Use when composing, authoring, editing or reviewing a game SKILL for this project — a new active or passive, a change to an existing skill's effects/cost/cooldown/triggers, or anything touching the Skill Composer or the skill .asset files under Resources/Skills. Covers which side an effect lands on, the pricing rules, and the mandatory verification pass.
---

# Composing a skill

A skill is a `SkillDef` asset in `unity/Assets/3_Data/Resources/Skills/`. Every skill in that folder is loaded at runtime, so an asset saved there is live — there is no separate registration step.

**Nothing in this file is a memorized value.** Every enum below is serialized BY INT and positional, so read the real list before you type a number:

```bash
grep -n "enum SkillEffectKind" -A 200 unity/Assets/Scripts/SkillEffect.cs   # kinds, in order
grep -n "enum SubStat" -A 30 unity/Assets/Scripts/SkillEffect.cs            # sub-stats, in order
grep -n "enum SkillTrigger" -A 60 unity/Assets/Scripts/SkillDef.cs          # triggers, in order
grep -n "enum StateId" -A 60 unity/Assets/Scripts/UnitStates.cs             # piles, in order
```

## 1. WHO an effect lands on — say it, never assume it

**Every effect answers `side` outright: `Caster` or `Target`. There is no third option, deliberately.**

There used to be an `Auto` meaning "this KIND's traditional side", and it shipped a broken skill: sub-stat changes were traditionally SELF, so an effect authored to "cut THEIR attack speed" and left on Auto cut **the player's own** attack speed — saved clean, passed the data checker, and looked right until someone read the "To the caster" heading in the composer (Offbeat, 2026-08-24). Auto was deleted the same day and all 46 authored sides were rewritten to what they already resolved to, so no skill changed behaviour.

**So the rule is now enforced by the data model rather than by discipline** — but the reading still matters, because **no checker can flag the wrong side**: every side is legal for every kind, and a self-inflicted debuff is a real design (Overclock deliberately doubles its own incoming damage). PROVE it per skill before you report anything:

```bash
.claude/skills/compose-skill/audit-effect-sides.py sword
```

It prints who every effect lands on, reading both enums out of the C# at run time. Read each row and ask "is that where I meant it?"

**Do not reintroduce a per-kind default.** A side that resolves differently depending on the effect's kind is unreadable off the asset, and that is exactly what turned an authored enemy debuff into a self-inflicted one.

## 2. Where it lives, and the naming rule

- Folder: `unity/Assets/3_Data/Resources/Skills/` — nothing else is loaded.
- **The FILE NAME must equal the** `id` **field.** The composer builds the path from the id (`{id}.asset`) and deliberately never uniquifies the filename, so a mismatch means the composer writes a second asset next to yours.
- Convention for the id: `skill.<weapon>.<name>` for a weapon skill (`skill.sword.parry`), `skill.<name>` for the shared/legacy ones.
- Every asset needs a `.meta` beside it with a unique guid.
- `published: 1` = it can roll onto a dropped weapon. `published: 0` = composed but unreachable.
- `vfx` must be set — `SkillRunner.Activate` refuses to run a skill whose vfx is null. Reuse what the sibling skills use rather than inventing an effect (`grep '^ vfx:' skill.sword.*.asset` shows the two in play).

## 3. Prices and cooldowns are RULES, not taste

`docs/active-skill-system-v3/skill-numbers-balance-baseline.md` owns them. Read it; the short form:

- **Passives are free** — 0 mana, 0 cooldown, always. The build slot is the price.
- **Mana by step:** reaction 8 · standard 12 · heavy 16 · ultimate 40.
- **Cooldown ≥ 1.5 × the skill's own duration**, so an effect can never be permanently up. Instant skills take their step's band (\~5s reaction, \~6s standard, \~8s heavy). Ultimates are 45–60s.
- A window (seconds) and a count of charges are different numbers and are never expressed in terms of each other.

The weapon's own roster doc (e.g. `docs/active-skill-system-v3/sword-skill-roster.md`) owns what the skill IS, including its identity bans. Read it before designing — Sword, for instance, may never have crit or AoE.

## 4. Mandatory verification — none of these are optional

```bash
tools/check-skill-data.py                              # the data: gates, piles, triggers, effect wiring
.claude/skills/compose-skill/audit-effect-sides.py     # who each effect lands on
tools/check-unity-code.sh                              # only if you touched C#
```

`check-skill-data.py` prints how many skills it read — confirm that number includes yours, because a clean run over a file it never opened looks identical to a clean run over one it did.

**What the checkers CANNOT catch, so you must:**

- **Which side an effect lands on** — every side is legal, so nothing can flag the wrong one. That is the whole reason the audit above exists.
- **Whether the numbers mean what you intended** — a 0.3 that should have been 3, a duration that should have been charges.
- **Whether it plays well.** A clean run means Unity will load it, never that it is fun or balanced.

## 5. Finish the job

- **Read the asset back off disk** and confirm the values landed. Never report a value you only wrote.
- **Update the weapon's roster doc** in the same change — those docs state which skills are composed, and a stale "designed, not composed" line is a lie the next reader will act on.
- The player wiki (`wiki/content/*.json`) describes skills to players; update it when a skill becomes real.
- Tell the user to click into Unity once so it imports new assets. Never force a recompile yourself.

## 6. Authoring in the editor

The window is **Tools ▸ unwritten ▸ Composers ▸ Skill Composer**. Use it when the user is driving. When you are composing on their behalf, editing the `.asset` files directly is fine and faster — they are plain YAML — but then the verification pass in §4 is what stands in for the window's own guardrails.

Related windows: **Tools ▸ unwritten ▸ Catalogs ▸ Skill Channels & Effects** (what every effect kind and channel means) and **Tools ▸ unwritten ▸ Composers ▸ Vfx Composer**.