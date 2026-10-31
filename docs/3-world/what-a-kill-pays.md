---
status: "BUILT and compiled; not play-tested. Every creature's multiplier is typed on its file, and every material price is worked out from it by the editor."
owner: vince
last-updated: 2026-10-31
tags:
  - world-content
  - economy
---

# What a Kill Pays — Level and Multiplier

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

## What the player gets

- **A harder creature is worth more.** Its kill pays more experience, more Temper Dust and Quench Oil, better chances
  of its Inks, Salts and own material, and a character saved from it pays more Unsigned — all by the same rule.
- **Two things decide what a kill pays in experience, materials and Unsigned, and nothing else does:** the
  creature's level and its multiplier.
- **Nothing is measured.** No fight is simulated and no number is worked out per creature; the pay can be read
  straight off those two.

## How hard a creature is

- **Level sets the body.** Its health, attack, defence and every other stat come from its level and its stats,
  through the same formulas a player's do.
- **Level also sets its gear.** Every piece it wears comes to life at the grade a player of its level is expected to
  have (levels 1–99 in seven even stretches: 1–14 Raw, 15–28 F, 29–42 E, 43–56 D, 57–70 C, 71–84 B, 85 and up A), at
  folds rolled each life ([Creature Gear](creature-gear.md)).
- **The multiplier is the one difficulty dial.** It is typed on each creature, ×1 or more, and says how much harder it
  is than an ordinary creature of its level: its health × the multiplier, its attack and magic attack × the cube root
  of it (×30 hits about 3.1 times as hard), every other stat a ×1 creature's.

## The rule

**Pay = what the level gives × the multiplier.**

- **A ×1 creature pays exactly what its level gives.**
- **A creature's kind does not change its experience or its materials**: a normal, an elite and a shadow of the same
  level and multiplier pay the same of both. Kind decides only the chance of its find ([Mob Roster](mob-roster.md),
  Drops).
- **Gear pays nothing on its own.** Grade and folds follow level and kind; the multiplier is the only thing on top.

**Example.** Blood Ronin is a level 80 elite at ×30. It pays 30 times what a ×1 creature of level 80 pays: 30 times
the experience, the Temper Dust and the Quench Oil, and 30 times the chance of each Ink, Salt and own material it can
drop.

## What it multiplies

- **Experience.** The experience of the level in the creature's file, times the multiplier. Levels a creature gains
  by hitting people during the fight add their difference on top, inside the same multiplication.
- **Temper Dust and Quench Oil.** A ×1 kill gives 0.6 Temper Dust and 0.02 Quench Oil on average — the whole part
  every time, and one more on a chance of what is left over. The multiplier multiplies both.
- **Inks and Salts.** Each has a chance on a ×1 kill, times the multiplier; past 100% it is more than one. The rarer
  ones drop only from a high enough multiplier ([Mob Roster](mob-roster.md), Drops).
- **The creature's own material.** Its tier is the highest its multiplier opens, and its chance is the tier's ×1
  chance times the multiplier ([Creature Materials](creature-materials.md)).
- **Saved-character Unsigned.** The Unsigned a never-fights character pays is counted in kills of its creature, times
  the multiplier ([Saved Characters](saved-characters.md)).

## What it costs to run

- **Per kill:** the creature's multiplier read off it and one multiplication per thing paid, on the server.
- **Per sale or tooltip:** nothing; the price is a number saved on the material's item.
- **Per server start and per build:** nothing is worked out per creature.

## Where the numbers live, and how they stay right

- **A creature's level and multiplier are saved on its file**, and a Tainted or Blighted version keeps its own
  multiplier while copying everything else from its original. The Mob Table shows and edits both.
- **Each material's tier and price are saved on its item**, so a player's machine works nothing out — and nobody
  types them: the editor works both out from the creature (`DerivedItemValues`) every time an item or creature file
  is saved. `DerivedItemValues.Sync(false)` through the live editor reports anything out of step; `Sync(true)` fixes it.
- **`tools/check-creature-materials.py`** names every tier and price that is not the rule's (a file edited as text
  skips the save). It needs no Unity.
- `CreaturePay` holds the rule as plain arithmetic, pinned in `tools/game-math-tests/Checks.cs`.
- **How many creatures there are at each kind and version:** `python3 tools/project-status.py`.

## Still open

- **Every creature's multiplier is a first guess**, and so are the 0.6 Temper Dust and 0.02 Quench Oil a ×1 kill gives.
- **The cube root for attack** is borrowed from how Ragnarok's bosses hit, to be tuned in play.

## Related

- [Creature Gear](creature-gear.md) — what every creature wears and its stat build.
- [Creature Materials](creature-materials.md) — the materials whose tier and chance the multiplier decides.
- [Saved Characters](saved-characters.md) — the Unsigned a saved character pays.
- [Loot-Owner Pointing System](../2-systems/loot-owner-pointing.md) — who a kill's experience is split among.
