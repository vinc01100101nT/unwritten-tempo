---
status: "BUILT 2026-09-22, not yet play-tested. First-guess numbers are listed under Still open."
owner: vince
last-updated: 2026-09-26
tags:
  - world-content
---

# Mob Roster — Normal, Elite, Shadow

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

Every creature in the game is one of three kinds: **normal**, **elite** or **shadow**. The kind decides how many
extra skill slots it has, what it wears, whether it summons, where it lives and what it drops. How strong it is
still comes from its **level** and its stats through the same formulas a player's do — the kind never carries a
stat multiplier.

This is decided before the world shape on purpose: the world is laid out around the creatures' level ladder, and
the ladder is set here.

**Where creatures live is one list, not the maps.** The world spawn list (`Resources/WorldSpawns`) holds one row per
spawn area: which map, which creatures (one named creature per row — how every map lists its creatures since the
sandbox layout of 2026-09-26 — or a weighted mix of a biome's normals inside a level range, which only the test maps
still use), how many, where in the map (the numbers `/where` prints: x west to east, y south to north, z height) and how
far they spread. The server reads a map's rows the moment it loads that map. Nothing about creatures is saved inside a
map, and the map generator no longer puts any there.

## The three kinds

| | Normal | Elite | Shadow |
| --- | --- | --- | --- |
| How many | the rest of the roster | 18 | 14, one per weapon type (12 in the game today) |
| Levels | **1 to 96**, each set by what the creature is | **8 to 99**, each set by what the creature is | **105 to 160**, one every 5 levels |
| Extra skill slots | 1 | 2 | 4 |
| Wears | its authored pieces, **Raw +0** at level 1 rising to **E +15** at 99 | its authored pieces, **D +0** at level 6 rising to **B +30** at 99 | a complete set at **A +30**, plus ring ranks |
| Element level | **1** | **2** | **3** |
| Weapon mastery | its level | its level | its level |
| Summons | nothing | normals | elites |
| Lives | every map it belongs in, from one end of the world to the other | the fields of its home biome near its level, and one on each dungeon's bottom floor | each at its own place, a special map away from where players level |
| Comes back after | **5 seconds** plus up to 3 | **30 minutes to 3 hours** | **4 to 6 hours** |
| Looks | flat | flat | a 3D character |
| Size | **Small to XL** — mostly Small or Medium, only 2 XL | **mostly XL** — 12 XL, 2 Large, 2 Medium, 2 Small | always **Medium** |

## Gear state

- **Every creature's gear state is authored on its own catalog entry**, as three settings shared by every piece it
  wears: its **grade** (Raw to A), its **folds** (never past the grade's cap: Raw 5, F 10, E 15, D 20, C 25, B 30,
  A 35), and its **Ink** (Watery, Thin, Faint, Clear or Sharp). The grade decides how many stats are written on each
  piece (Raw none, F one, up to A six); the Ink decides what each is worth. They are edited in the Mob Table's
  equipment panel, and no rule in the code overrides them for any kind.
- **Which stats are written, and a weapon's trademark, are rolled fresh every life**, exactly as a station rolls them.
  A weapon gets a trademark only when its grade is above Raw.
- **A creature wears exactly the pieces authored for it and nothing else.** An empty slot stays empty.
- **The starting values follow level.** Folds rise evenly with level across each kind's range, and the grade is the
  lowest one (from that kind's starting grade) whose cap holds those folds; the Ink follows the grade — F Watery,
  E Thin, D Faint, C Clear, B and A Sharp (Raw writes nothing). So normals run Raw +0 at level 1, F from level
  37, E from level 70, E +15 at 99; elites run D +0 at level 6, C from level 72, B from level 88, B +30 at 99; every
  shadow is A +30.
  Any single creature can be changed after that.

## Sizes

- **Four sizes, each a multiple of a character's height:** Small **0.3×**, Medium **1×**, Large **2.4×**, XL
  **5.2×**. Medium is exactly a character's size — the player, the townsfolk and every shadow. Each creature's art is
  measured and fitted to its size, so every creature of one size stands the same height whatever padding its picture
  carries.
- **Melee reach counts the same multiple, from the same table**, so how big a creature looks and how far it reaches
  can never disagree. Against a character, a blow reaches **0.65×** its full reach against a Small creature, **1×**
  against a Medium one, **1.7×** against a Large one and **3.1×** against an XL one — and the creature's own blows
  reach the same distance back.
- **Its collision body grows with it.** A creature wider than the usual routing footprint steers around obstacles
  with its own width.
- **Size changes nothing else**: not movement speed, not stats. The weapon-vs-size damage chart is not switched on,
  and it has no XL column yet.
- **The split:** normals **26 Small, 28 Medium, 10 Large, 2 XL**; elites **2 Small, 2 Medium, 2 Large, 12 XL**; all
  shadows Medium. Each creature's size is set on its own catalog entry; a shadow is Medium whatever its entry says.

## Normal

- The creatures that fill the field. One extra skill slot, and whatever pieces are authored for it — from **Raw +0**
  at level 1 to **E +15** at level 99 (see Gear state).
- **Each of the 66 normals has its own home biome, element and level**, set from what it is (2026-09-26): critters
  low, large beasts higher. It lives in every map where it belongs, not in one. The full table is
  [Creatures and Where They Live](biome-mob-families.md). Each one's stats were re-spent to its level's full point
  budget in the same proportions it had.

## Elite

- The stronger creatures of each area. The 18 creatures that were bosses are the 18 elites.
- **Each elite has its own home biome, element and level** (8 to 99), and at least one elite calls each biome home;
  the table is [Creatures and Where They Live](biome-mob-families.md).
- Two extra skill slots, whatever pieces are authored for them — from **D +0** at level 6 to **B +30** at level 99
  (see Gear state) — and they **summon normals**: the three nearest their level at or below it, from their own home
  biome first.
- **They drop much more folding material, and the rarer Inks and Salts** (see Drops).

## Shadow

- **The strongest creatures in the game.** They are the 3D characters, each wearing **one weapon type**, so there
  are fourteen of them — one for each weapon. Twelve are in the game; the Fist and Kick shadows (both weapons added
  2026-09-25) are not made yet.
- **Levels 105, 110, 115 … 160**, one shadow per step — above the 1–99 range every other creature uses.
- **The weapon order runs from the least-played weapon at 105 to the most-played at 160.** A shadow is what a
  weapon's end-game materials come from, so the weapons fewest people choose are the easiest to finish — a reason
  to pick them. The order is a first guess, made before there is any player data.
- **A shadow goes by its weapon's STYLE**, never a personal name — the Fourteen left no names, only how they fought
  (see [Story & Setting](story-setting-canon.md), which holds the fourteen styles and how their forms are named). The
  styles are locked (2026-09-26).

  | Level | Weapon | Style | Element |
  | --- | --- | --- | --- |
  | 105 | Shield | **Still Oak** | Neutral |
  | 110 | Wand | **Morning Dew** | Holy |
  | 115 | Warhammer | **Stone Bloom** | Earth |
  | 120 | Spear | **Silver Rain** | Water |
  | 125 | Axe | **Forest Fire** | Fire |
  | 130 | Firearm | **Dusk Crow** | Shadow |
  | 135 | Scythe | **Black Lily** | Undead |
  | 140 | Dagger | **Night Orchid** | Poison |
  | 145 | Staff | **Ashen Field** | Fire |
  | 150 | Greatsword | **Silent Snow** | Ghost |
  | 155 | Bow | **Feather Rain** | Wind |
  | 160 | Sword | **Blossom Wind** | Wind |
  | *not set* | Fist | **Northern Lights** | Neutral |
  | *not set* | Kick | **Autumn Gale** | Wind |
- **The shadows carry all ten elements** (decided in the [World Map](world-map.md)): Wind three times, Fire and Neutral
  twice each, the rest once. Only the Water, Earth, Fire and Wind shadows can be answered with a converter today; the
  counter for the others is to come from skill buffs and cards. At level 3 the element matters, and its own element
  lands very differently on each: it heals the Water, Fire, Wind, Holy and Shadow shadows, does nothing to the Earth,
  Poison and Undead ones, lands normally on the Neutral one, and lands for 175% on the Ghost one. It swings with its
  element too; the weapon it holds has none, and a weapon with no element never overrides a creature's own.
- **Each shadow summons one elite kind, matched to its place on the ladder.** The top twelve elites by level pair
  with the shadows from the top down:

  | Shadow | Blossom Wind 160 | Feather Rain 155 | Silent Snow 150 | Ashen Field 145 | Night Orchid 140 | Black Lily 135 |
  | --- | --- | --- | --- | --- | --- | --- |
  | Summons | SquidRed 99 | GiantSlime2 95 | TenguRed 92 | GiantSpirit 90 | GiantFlam 80 | GiantFrog 78 |

  | Shadow | Dusk Crow 130 | Forest Fire 125 | Silver Rain 120 | Stone Bloom 115 | Morning Dew 110 | Still Oak 105 |
  | --- | --- | --- | --- | --- | --- | --- |
  | Summons | GiantFrog2 66 | GiantRedSamurai 60 | TenguBlue 58 | GiantBamboo 47 | GiantRacoonGold 44 | DemonCyclop2 35 |

  The six lowest elites (8 to 33) are summoned by no shadow. The pairing follows level only, so a shadow's elite need
  not share its element or place.
- Four extra skill slots, a complete set of gear at **A +30**, and ring ranks. Normals and elites have no ring ranks.
- **They give off an aura**, so a shadow looks intimidating. **Not built yet** — the look is chosen once the
  shadows can be seen in the game.
- **Its gear:** every piece at grade A, folded to +30, every one of its six stat slots written with Sharp Ink, and a
  trademark on the weapon — each roll made the way a station makes it.
- **Weapon mastery equals its level, capped at 70 by its A-grade weapon**, so the Sword shadow knows every sword skill.
- **Ring ranks:** 40 in each of the three stats its weapon's mastery raises.
- **Drops:** the same as an elite, at higher chances. When weapon transcending exists, its materials are
  **specific to each weapon**, and **each shadow drops only its own weapon's**.
- **They are meant to need a whole guild to bring down.**
- **The twelve made so far exist.** Only **Blossom Wind** (the Sword shadow) holds a weapon; the other eleven fight with basic attacks, their four
  extra skills and summon until their weapon's skills exist, and then each is handed its weapon.
- They carry their name and an always-visible health bar.

## Summoning

- **Summoning is a skill.** It has a cooldown like any other, and the summoner uses it **every time it is
  ready**.
- **Each summoner has a fixed maximum number of summons.** When it is at its maximum, it does not summon, even
  with the skill ready.
- **Summoned creatures always follow their summoner**, staying within a range of it.
- **They disappear when their summoner dies.**
- **They give no experience and no loot.** They exist to get in the way and to make elites and shadows harder.
- **A summoned creature never summons.** An elite called by a shadow does not call normals of its own.
- **A summoner calls while it is fighting.** When it has a target and the skill is ready, it summons before any other
  skill, topping every group back up to its share in one cast.
- **A summon that dies, or is sent away, is kept and reused** by the next cast rather than made again.

## Drops

**The grade a creature drops follows its level.** The seven material grades split levels 1 to 99 into seven even
stretches (1–14 Raw, 15–28 F, 29–42 E, 43–56 D, 57–70 C, 71–84 B, 85–99 A); a shadow drops grade A. A creature drops
its own grade's **metal, cloth and leather**, plus a chance of an Ink or a Salt:

| | Each of its grade's metal, cloth, leather | Ink | Salt |
| --- | --- | --- | --- |
| Normal | 20%, 1 | Watery 4%, Thin 1% | Coarse 2% |
| Elite | 60%, 5 to 10 | Faint 30%, Clear 15%, Sharp 5% | Fine 20%, Pure 5% |
| Shadow | 100%, 5 to 10 | Faint 60%, Clear 30%, Sharp 15% | Fine 40%, Pure 15% |

Every kill also has a 30% chance of one random consumable (a potion or a food), and a creature with an element adds its
own element's two converters to that roll — a fire creature can drop the Flame Elemental Converter or the Flame Armor
Converter; a creature whose element has no converter (Neutral, Poison, Holy, Shadow, Ghost, Undead) never drops one. Materials are never in that roll.
Several of one material go down as one pile.

## Server restarts

- **A map server restart wipes every respawn timer**, and **every elite and every shadow is present when the
  server starts.**

## Still open

- **The Fist and Kick shadows**: their level (the ladder of 105 to 160 holds twelve) and the elite each summons.
  Their styles and elements are set: Northern Lights, Neutral; Autumn Gale, Wind.
- **First guesses, all to be tuned in play:** the summon skill's 20-second cooldown; a summon strays at most 8 units
  from its summoner before walking back; every drop number in the Drops table; each shadow's stat spread (a physical
  spread for most, an INT-heavy one for the Staff, Wand and Scythe), its ring ranks, and its 2,000 experience and
  3,000 to 6,000 gold.
- **Normals' and elites' experience and gold were not changed** when they were re-levelled, so a creature moved from
  its old level to its current one still pays what it did before — the Octopus, now level 63, still gives 5
  experience and 2 to 6 gold.
- **A shadow's 3D body is drawn at random from the cast each life**, like every 3D creature's, so a named shadow can
  look different each time it comes back.
- **TODO — the shadows' places and the elites' spots** are designed in the [World Map](world-map.md) and not built.

## Related

- [Gameplay System — V4](../2-systems/gameplay-system-v4/README.md) — forging, the seven material grades, Ink, Salt
  and mastery: what creatures drop and what shadows wear.
- [Creatures and Where They Live](biome-mob-families.md) — every creature's home biome, element and level, and every map's creatures.
- [Story & Setting (Canon)](story-setting-canon.md) — the Fourteen Unwritten.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen
  weapon types a shadow each carries.
