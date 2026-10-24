---
status: planned
owner: vince
last-updated: 2026-10-24
tags:
  - systems-combat-design
---

# Skill Balancing System — Plan

**Where it stands.** Step 1, the simple balance report, is built. This page is the FULL system, planned on
2026-10-24 by a planner and attacked by a red team, and it is **waiting**: it is built only after play-tests have
corrected the formula's guesses. Building it first would enforce unproven numbers on every skill — Derive would write
them in and the commit check would defend them.

| Step | What | State |
| --- | --- | --- |
| 1 | The simple report: `python tools/skill-balance/extract.py`, and the calculator page beside it; every run rewrites the status page [data/skill-balance.md](../../../data/skill-balance.md) — which skills are balanced, which are not, and why | Built |
| 2 | Play-test the report's shortlist; the results correct the constants below | Waiting on the user |
| 3 | The full system on this page, built on the corrected constants | Waiting on step 2 |

## The simple version (step 1)

- **Budget** = 0.1 × cooldown (0.075 for an ultimate) + 0.0625 × MP + (wind-up + the longer of the release lock and
  the time the caster is held in place) ÷ the weapon's swing time. An 8 MP, 5 s skill is worth 1.0 basic attack.
- **Value** = each damage part × hits × chance × repeats × copies × targets × 0.85 when it lands 8 m or more away
  (never on a tick that dashes or teleports the caster in) ×
  1.5 when a release-early wind-up is held to the end, plus the effects whose worth comes from a game rule: hits
  stopped, the enemy's next attacks cut (Stone), the enemy's attack speed or accuracy cut (Static included), your own
  attack speed, defence and magic defence (half of enemy blows each), the enemy's defence cut, and MP drained.
- **Read from the game's rules, not guessed:** marks cashed in (priced at 2 marks), bonus crit chance (× the base
  crit multiplier), hits that land at intervals (read from the skill's effect sequence), and the beats of a run
  (charges spent, a window held, or the authored count).
- **Passives** are priced per second of fighting: one firing's worth × how often its moment comes, against a budget
  of 0.1 a second (what a second of an active's cooldown buys). Only moments with a rhythm the game sets are counted —
  a basic attack or a hit dealt (once a swing), a blow taken (the enemy-attack rate), and casting a named skill (once
  per its cooldown); a kill, a dodge, a parry or low health is a guess, so those passives are judged by hand.
- **Provisional prices** cover every other effect — slows and movement speed, dashes and teleports, accuracy and
  evasion buffs, taunt, Cleanse, resets, placed and copied marks, extended marks, burn and other flat HP, disable
  immunity, sure hits, stacks and charges added to another skill — and the moments a kill, a dodge, a parry or low
  health come. Each rests on a stated guess about how a fight goes (the list heads the status page); a skill priced
  with any of them is marked provisional until play-testing settles the guess.
- **Extra on top, never priced:** growth that needs an investment (a built-up pile, hit chance, speed, max MP) — the
  base must balance, the extra is the reward.
- **Not priced:** the orbs and Release (the Staff's resource system), and Held Breath (a loan; its payoff is
  Returning Wind's bank).
- **Targets:** one target 1 (plus bounces), a projectile line 2, an area under 2 m radius 1.5, 2 m or more 2.
- **Swing time** is each weapon's own item: 0.8 s + 0.005 s per point of weight (Bow 1.1, Sword 1.25, Staff 1.2).
- **Release lock:** one swing, or a Bow skill animation's measured length (First 0.70 s, 0.35 s after a wind-up;
  Second 0.70 / 0.667 s; Third 0.70 s).
- It reports only — it never changes a skill. Beyond ±40% is the play-test shortlist; ±15% is balanced. Anything it
  cannot price is listed with the reason.
- The formula lives once, in `tools/skill-balance/formula.js`, which both the report and the calculator load.

**The guesses step 2 must correct:** an area hits 2 enemies, an enemy attacks once a second, a ranged hit is worth
85% of a melee one, and the cooldown, ultimate and mana rates themselves.

## The full system (step 3)

**What the user gets:** one written rule every weapon skill is checked against; a checker that fails any skill
more than ±15% off, after any session that edits a skill and before every commit; a live budget / value / % off in
the Skill Composer; and Derive — tick numbers, they scale together to value = budget, written into the skill but
never saved until Save.

**Architecture.**
- One formula file in the editor scripts, with no Unity or game dependency, fed raw field-name/value pairs. All
  working out (hits per cast, beats, locks) happens inside it, so nothing is computed twice.
- The Skill Composer feeds it from the selected skill; an offline Python checker feeds it from the saved files
  (reusing the skill-data checker's reader), compiles the formula file alone with Unity's bundled compiler (0.78 s
  measured) and runs it. Field defaults are read from the C# initializers, never retyped.
- A **balance list** marks every one of the 150 saved fields a skill can hold (SkillDef 61, trigger entry 9, tick
  binding 15, SkillEffect 58, sub-fields 7) as priced, deliberately not priced, or a gap. The checker fails on any
  declared field missing from it — the guard against another missed variable.
- A prover plants faults and demands each is named: doubled damage, halved cooldown, an overrun only the
  release-early factor explains, a changed clip length, an unlisted new field, a changed game constant, a hit count
  raised through an effect sequence, an empty skill folder, a field missing on disk, and a within-tolerance case that
  must pass.
- Wiring: a balance group in the turn-end check and two lines in the pre-commit check, the commit gate staying off
  until the skills are rebalanced. Mana is never derived — it stays the 8 / 12 / 16 / 40 price step.
- Nothing in the game changes; the checker costs about 2 s per run.

**Variables the planner's sweep added** (each read from the code): a release-early wind-up held to the end lands
1.5×; hits per cast come from the effect sequences (interval hits = floor(length ÷ interval) + 1); defence is a cut
of every non-crit physical blow, 0.01 = 1%, capped at 90% with all hard defence; the movement bonus is capped at
250%, so speed scalers have a ceiling; attack speed cancels out of attack-speed-sized charges.

**Red team findings, each folded into the plan:**
1. An effect the formula cannot price must make the skill "cannot be priced" — never let Derive fill the budget
   with the priced parts (Weathering's reset is worth a whole ultimate). A reset is worth the named skill's budget.
2. Runs whose length is not on the skill (held pile, spent charges, cast speed, bonus beats) each need a counting
   rule; one without a rule cannot be priced (Cloudburst reads as 1 volley and fires 6 to 31).
3. Free extra triggers count: value per press = 1 + the free firings it funds (Piercing Gust with Crossed Branches
   funds up to 4 free dashes). The pair is judged together.
4. Mana is added to the budget, never swapped for the cooldown — the rates above are halved to keep the old scale.
5. Capped scalers: two readouts — with no extra investment (must pass) and at the cap (shown for judgement).
6. Every checker fault names the file that caused it, and the checker stops loudly on fewer skills than expected,
   so the turn-end filter cannot swallow it.
7. The passive rate is derived from the active rate, never calibrated from the passives being judged.
8. Enemy blows split into physical and magic shares, so a defence and a magic-defence buff are not both counted
   against every blow.
9. Save writes only the open skill, and switching away from an unsaved derived skill asks first.
10. Flat numbers (heals, shields, damage over time, Burn, flat adds) must pass at two reference levels.
11. Both feeders hand over raw fields only; clip lengths are read the way the game reads them (0.6 s when missing).
12. The 0.85 ranged factor applies only to hits landing at range — never to dashes or self-only skills.

**Open decisions, with the recommendation:** health points per basic attack for flat numbers (two reference
levels); marks and basic-attack piles at the cap in the cap readout; passive rate from the active rate; enemy blows
50% physical / 50% magic; per-weapon swing.

**Found in passing:** Last Petal's description promises "+40% per Rising Breeze stack", but the game adds a flat 0.4
damage per stack after the multiplier (`ScaledNumber.Resolve`, SkillDef.cs:171-184).

## Related

- [Skill Numbers & Balance Baseline](skill-numbers-balance-baseline.md) — the price steps and cooldown rules this checks against.
- [Active Skill System — V3](README.md) — the channels and effects being priced.
