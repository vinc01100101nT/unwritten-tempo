---
status: "BUILT 2026-10-13 and compiled; folds on the +10 limit and material prices re-baked 2026-10-14; not play-tested. Every creature's grade and folds, and every material price, are written."
owner: vince
last-updated: 2026-10-14
tags:
  - world-content
  - economy
---

# What a Kill Pays — Level, Kind, Grade and Folds

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

## What the player gets

- **A creature in better gear is worth more.** Its kill pays more experience, its own materials sell for more, and
  a character saved from it pays more Unsigned — all three by the same rule.
- **Four things decide what a kill pays, and nothing else does:** the creature's level, its kind, the grade of its
  gear and the folds on its gear.
- **Nothing is measured.** No fight is simulated and no number is worked out per creature; the pay can be read
  straight off the four things above.

## How hard a creature is

- **Level sets the body.** A creature wearing nothing gets its health, attack, defence and every other stat from
  its level.
- **Gear is the only difficulty dial.** The grade and the folds of what a creature wears are chosen for that
  creature. They do not have to follow its level: a low-level creature can wear high-grade, heavily folded gear and
  be hard for its level, and a high-level one can wear raw unfolded gear and be easy for its level.
- **Grade and folds are two separate variables.** Folds raise what each piece gives. Grade raises what each fold
  adds and what the quench that reached it added, and also opens ink slots and a higher weapon mastery limit. Every
  grade stops at +10 folds.

## The rule

**Pay = what the level and kind give × the grade multiplier × the fold multiplier.**

| Grade | Raw | F | E | D | C | B | A |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Grade multiplier | 1.00 | 1.25 | 1.50 | 1.75 | 2.00 | 2.25 | 2.50 |

- **Grade multiplier:** 25% more for each grade above Raw.
- **Fold multiplier:** 1 plus 5% for each fold. No folds is 1.00; ten folds, the most any grade takes, is 1.50.
- **A creature in Raw gear with no folds pays exactly what its level and kind give.**
- **The two are separate terms**, each read off the creature's gear and multiplied together.
- **The most a kill's gear can pay** is grade A at +10: 2.50 × 1.50 = 3.75 times.

**Example.** Blood Ronin is a level 60 elite in grade D gear with 10 folds. Grade D is 1.75. Ten folds is
1 + 10 × 5% = 1.50. So it pays 1.75 × 1.50 = about 2.63 times what a level 60 elite in Raw unfolded gear pays.

## What it multiplies

- **Experience.** The rule multiplies the experience of the level in the creature's file. Levels a creature gains
  by hitting people during the fight add their plain difference on top, so letting a geared creature hit you is not
  multiplied by its gear.
- **Material prices.** Each of the creature's own materials sells for its rule price times the two multipliers,
  rounded to the nearest whole Unsigned.
- **Saved-character Unsigned.** The Unsigned a never-fights character pays is counted in kills of its creature, times the
  two multipliers.
- **Kind.** An elite's 10 times and a shadow's 20 times still apply on top.

## What it costs to run

- **Per kill:** one lookup of the creature's grade and folds and two multiplications, on the server.
- **Per sale or tooltip:** nothing; the price is a number saved on the material's item.
- **Per server start and per build:** nothing is worked out per creature.

## Where the numbers live, and how they stay right

- **A creature's grade is saved on its file; its folds are fixed by its kind** (0 normal, 4 elite, 7 shadow —
  `CreatureLoot.FileFolds`). The server reads them at the kill and at a rescue.
- **Each material's tier and price are saved on its item**, so a player's machine works nothing out — and nobody
  types them: the editor works both out from the creature (`DerivedItemValues`) every time an item or creature file
  is saved. `DerivedItemValues.Sync(false)` through the live editor reports anything out of step; `Sync(true)` fixes it.
- **`tools/check-creature-materials.py`** names every tier and price that is not the rule's (a file edited as text
  skips the save), and any creature file that carries a measured number of its own. It needs no Unity.
- `CreaturePay` holds the rule as plain arithmetic, pinned in `tools/game-math-tests/Checks.cs`.

## Still open

- **The 25% a grade and the 5% a fold are first guesses.**
- **A fold is not worth the same at every level.** A Common metal body piece at Raw +5 gives 50 health on a level 5
  creature and on a level 99 one alike — a large share of the small creature's health and a sliver of the big one's.
  So a flat percent a fold pays a high-level creature more than its gear makes it harder, and a low-level one less. This is a fault in what gear
  gives, to be fixed there, not by bending the pay rule.
- **Which grade and how many folds each creature wears** ([Creature Gear](creature-gear.md)) rests on first
  guesses: which grade each drop tier gives, and the five bands of how hard a creature is to find.

## Related

- [Creature Gear](creature-gear.md) — what every creature wears, its stat build, and what its grade and folds pay.
- [Creature Materials](creature-materials.md) — the materials whose prices the rule multiplies.
- [Saved Characters](saved-characters.md) — the Unsigned a saved character pays.
- [Loot-Owner Pointing System](../2-systems/loot-owner-pointing.md) — who a kill's experience is split among.
