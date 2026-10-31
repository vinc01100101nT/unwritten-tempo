---
status: "BUILT on every creature: weapon, armour and stat build on its file, its grade from its level and its folds, card slots and base rolled each life. Not play-tested."
owner: vince
last-updated: 2026-10-31
tags:
  - world-content
---

# Creature Gear — What Every Creature Wears and Wields

> **Never append. Always update in place.** Which creature holds which weapon and armour, and its stats, are read
> from the game's own data (see **Where to read each creature's gear**), never typed here — except the shadows', which
> are fixed by design.

## The rules

- **Every creature wears a full set**: a weapon, a body piece, shoes and a garment. No creature wears headgear
  (decided 2026-10-16): a headgear system with special stats, never worn by a creature, comes later.
- **A creature wears exactly the pieces authored for it and nothing else.** An empty slot stays empty.
- **Its grade follows its level** — the gear a player of that level is expected to have: levels 1–99 split into seven
  even stretches, 1–14 Raw, 15–28 F, 29–42 E, 43–56 D, 57–70 C, 71–84 B, 85 and up A (decided 2026-10-20). Every
  shadow, at 105 and over, wears A. The grade is never typed on a creature.
- **Its Ink follows its grade**: F Watery, E Thin, D Faint, C Clear, B and A Sharp. Raw writes nothing. Which stats
  are written is rolled fresh every life.
- **Its folds are rolled every life, by its kind**, never past the +10 every grade stops at: a normal +0 to +10, an
  elite +4 to +10, a shadow +7 to +10, each fold about 70% as likely as the one before. Its sheet shows the fewest its
  kind wears (+0, +4 or +7), so the sheet never depends on a roll.
- **Its card slots and base are rolled every life too.** A weapon takes 0–2 slots on a normal, 2–3 on an elite, 3–4 on
  a shadow; an armour piece 1 on a quarter of normals, 1–2 on an elite, 2 on a shadow. Each piece's base is rolled
  85% to 115%, as a dropped piece's is.
- **A weapon's trademark and an armour piece's word are rolled fresh every life**, as a station rolls them.
- **What it wears pays nothing on its own.** How hard a creature is and what its kill pays come from its level and its
  multiplier ([What a Kill Pays](what-a-kill-pays.md)); what it drops is set by its multiplier
  ([Creature Materials](creature-materials.md)).
- **What it drops is a copy of what it wears**: on one kill in ten, an exact new copy of one of its pieces — grade,
  folds, Ink, card slots, base, trademark or word ([Mob Roster](mob-roster.md), Drops).
- **Its armour follows what the creature is**: metal for shell, scale, horn, bone or bark; leather for hide, fur,
  feather or a fast body; cloth for soft, plant, flame or spirit.
- **Its weapon is its own**, the same wherever it lives. A weapon decides how a creature attacks and which skills it
  can roll.
- **The two creatures that never move keep a weapon that shoots**: Shootling the Sniper (decided 2026-10-14) and
  Canewick the Bow, so each can hit from where it stands.
- **A creature that rolls its gear each life** picks one weapon type at random every time it comes to life, wears the
  armour that weapon's build wears (below), and spends its stat points again in that build. Set per creature in the
  Mob Table; Blood Ronin, Frost Ronin, Inferno Mask and Wraithmother do.
- **Its stats are built for its weapon.** Each weapon has a build, and a creature's stat budget is spent in that
  build's proportions. A weapon with two builds gives the second to a creature in metal armour.

| Set | Body | Shoes | Garment |
| --- | --- | --- | --- |
| Metal | Chainmail | Iron Sabatons | Chain Mantle |
| Leather | Leather Jerkin | Leather Boots | Hide Cloak |
| Cloth | Padded Vest | Woven Shoes | Traveller's Cloak |

## The builds

The six numbers are the shares of STR, AGI, VIT, INT, DEX and LUK the stat budget is spent in.

| Weapon | Build | STR : AGI : VIT : INT : DEX : LUK | What it is built for |
| --- | --- | --- | --- |
| Sword | Duelist | 3 : 3 : 2 : 1 : 2 : 1 | fast precise strikes |
| Greatsword | Cleaver | 4 : 1 : 3 : 1 : 1 : 2 | huge blows and crits |
| Axe | Berserker | 4 : 3 : 1 : 1 : 1 : 2 | all attack and speed, no defence |
| Warhammer | Breaker | 3 : 1 : 4 : 1 : 2 : 1 | attack behind heavy defence |
| Spear | Lancer | 3 : 1 : 3 : 1 : 3 : 1 | accurate, sturdy |
| Fist | Striker | 4 : 2 : 1 : 2 : 2 : 1 | offence: one enormous physical blow |
| Fist | Guardian (in metal armour) | 2 : 1 : 5 : 1 : 2 : 1 | defence: stands in front and takes the hits |
| Bow | Archer | 1 : 3 : 1 : 1 : 5 : 1 | ranged attack from DEX, hard to hit |
| Dagger | Cutthroat | 3 : 4 : 1 : 1 : 2 : 1 | fastest swings, evasive |
| Katar | Assassin | 3 : 3 : 1 : 1 : 1 : 3 | crit chains |
| Shuriken | Thrower | 1 : 3 : 2 : 1 : 4 : 1 | thrown: ranged, from DEX |
| Shuriken | Blade Dancer (in metal armour) | 4 : 3 : 2 : 1 : 1 : 1 | swung: melee, from STR |
| Sniper | Marksman | 1 : 1 : 1 : 4 : 3 : 2 | a magic shot that crits |
| Staff | Mage | 1 : 1 : 2 : 5 : 2 : 1 | destructive magic |
| Wand | Priest | 1 : 1 : 3 : 4 : 2 : 1 | magic with staying power |
| Scythe | Reaper | 2 : 1 : 3 : 4 : 1 : 1 | curses over a long fight |

A creature that rolls its gear each life spends its points by a simpler weighting of the same three families: a
caster's weapon (staff, wand, scythe) cloth and INT; a shooting weapon (bow, sniper, shuriken) or a quick blade
(dagger, katar) leather and AGI or DEX; everything swung for strength metal and STR and VIT (`CreatureLoot.BuildFor`).

## Where to read each creature's gear

- **Every normal and elite's weapon, armour, level, multiplier and stats:** its own file, shown and edited in the
  editor's **Mob Table** (the weapon with the rest of its gear under ▸).
- **What a creature drops, its worn pieces included:** the GM command `/mobinfo <creature>`
  ([GM Commands from a Terminal](../0-process/gm-commands-from-a-terminal.md)).
- **A Tainted or Blighted version** wears and wields exactly what its original does: everything but its id, name,
  multiplier and material is copied from the original whenever a creature file is saved.

## The shadows

Every shadow wears a full set — body, shoes and garment — of one material, at grade A with Sharp Ink. Its weapon is the
one it stands for, and its stats are spent in that weapon's build.

**Its armour is the material that buys what its weapon's mastery raises:** leather (attack speed and evasion) for a
weapon whose mastery raises attack speed or evasion; otherwise cloth (magic defence and mana) for one whose mastery
raises mana or magic attack; otherwise metal (defence and health). Asterion is the one exception: the Fist shadow
wears metal and takes the Fist's Guardian build (decided 2026-10-13). Between them the fourteen wear all nine
armour pieces.

| Shadow | Level | Weapon | Armour | Build | STR / AGI / VIT / INT / DEX / LUK |
| --- | --- | --- | --- | --- | --- |
| Riven | 105 | Shuriken | Leather | Thrower | 36 / 71 / 56 / 36 / 84 / 36 |
| Asterion | 110 | Fist | Metal | Guardian | 59 / 39 / 99 / 38 / 59 / 38 |
| Nysara | 115 | Katar | Leather | Assassin | 78 / 78 / 40 / 40 / 40 / 78 |
| Seraphine | 120 | Wand | Cloth | Priest | 43 / 42 / 81 / 96 / 64 / 42 |
| Tharos | 125 | Warhammer | Metal | Breaker | 85 / 45 / 99 / 44 / 67 / 44 |
| Orion | 130 | Spear | Metal | Lancer | 89 / 46 / 88 / 46 / 88 / 46 |
| Varkhaz | 135 | Axe | Leather | Berserker | 99 / 94 / 51 / 51 / 51 / 75 |
| Vael | 140 | Sniper | Cloth | Marksman | 55 / 54 / 54 / 99 / 98 / 79 |
| Morwen | 145 | Scythe | Cloth | Reaper | 84 / 59 / 99 / 99 / 59 / 59 |
| Velmora | 150 | Dagger | Leather | Cutthroat | 99 / 99 / 65 / 64 / 88 / 64 |
| Kaelen | 155 | Staff | Cloth | Mage | 71 / 70 / 95 / 99 / 94 / 70 |
| Mordane | 160 | Greatsword | Metal | Cleaver | 99 / 73 / 99 / 73 / 73 / 99 |
| Celestia | 165 | Bow | Leather | Archer | 85 / 99 / 85 / 84 / 99 / 84 |
| Veyra | 170 | Sword | Leather | Duelist | 99 / 99 / 99 / 78 / 99 / 78 |

## Still open

- **Not every creature's stats fit its weapon's build at its level today.** Gloop King, level 50, carries a level-8
  spread, and Abyss Maw, level 70, a level-99 one; Lovebite holds a Fist on a Berserker spread. Spending them again is
  to be decided.
- **The fold chances, the card-slot ranges and the 85–115% base are first guesses.**
- **A weapon's skill is rolled fresh each life**, and what a creature pays does not count it.
- **Each armour material is a first reading of the creature's portrait, and each build's shares are first guesses.**

## Related

- [What a Kill Pays](what-a-kill-pays.md) · [Mob Roster](mob-roster.md) · [Creature Materials](creature-materials.md)
