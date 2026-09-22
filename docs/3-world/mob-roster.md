---
status: "BUILT 2026-09-22, not yet play-tested. First-guess numbers are listed under Still open."
owner: vince
last-updated: 2026-09-22
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
spawn area: which map, which creatures (a weighted mix of a biome's normals inside a level range, or one named
creature — the way an elite or a shadow is placed), how many, where in the map (the numbers `/where` prints) and how
far they spread. The server reads a map's rows the moment it loads that map. Nothing about creatures is saved inside a
map, and the map generator no longer puts any there.

## The three kinds

| | Normal | Elite | Shadow |
| --- | --- | --- | --- |
| How many | the rest of the roster | 18 | 12, one per weapon type |
| Levels | spread evenly over **1–99** | **6 to 99**, one every 5.5 levels, rounded | **105 to 160**, one every 5 levels |
| Extra skill slots | 1 | 2 | 4 |
| Wears | a weapon and armour | a weapon and armour | a **complete set of the highest-grade gear**, plus ring ranks |
| Weapon mastery | its level | its level | its level |
| Summons | nothing | normals | elites |
| Lives | everywhere in the field | the field | the end-game maps |
| Comes back after | **5 seconds** plus up to 3 | **30 minutes to 3 hours** | **4 to 6 hours** |
| Looks | flat | flat | a 3D character |

## Normal

- The creatures that fill the field. One extra skill slot, and a weapon and armour to wear — each body slot has a 40%
  chance of a random piece, always Raw and unfolded.
- **The 66 normals are spread evenly over levels 1 to 99** in the order they stood before (by old level, then by
  name), so each biome keeps one stretch of the ladder. Each one's stats were re-spent to its new level's full point
  budget in the same proportions it had.

## Elite

- The stronger creatures of each area. The 18 creatures that were bosses are the 18 elites.
- **Their levels** are 99 split into 18 equal steps of 5.5, rounded off, starting from the first step rather than
  from 1: **6, 11, 17, 22, 28, 33, 39, 44, 50, 55, 61, 66, 72, 77, 83, 88, 94, 99.**
- Two extra skill slots, a weapon and armour, and they **summon normals**.
- **They drop much more folding material, and the rarer Inks and Salts** (see Drops).

## Shadow

- **The strongest creatures in the game.** They are the 3D characters, each wearing **one weapon type**, so there
  are twelve of them — one for each weapon.
- **Levels 105, 110, 115 … 160**, one shadow per step — above the 1–99 range every other creature uses.
- **The weapon order runs from the least-played weapon at 105 to the most-played at 160.** A shadow is what a
  weapon's end-game materials come from, so the weapons fewest people choose are the easiest to finish — a reason
  to pick them. The order is a first guess, made before there is any player data.
- **Each shadow has a real name, in the language of its weapon's best-known fighting tradition.** The sword's
  matches its Japanese weapon names.

  | Level | Weapon | Name | Theme |
  | --- | --- | --- | --- |
  | 105 | Shield | **Varro** | Roman shield wall |
  | 110 | Wand | **Brigid** | Irish, the healer |
  | 115 | Warhammer | **Radomir** | Slavic mace-bearer |
  | 120 | Spear | **Kallias** | Greek spearman |
  | 125 | Axe | **Brandr** | Norse |
  | 130 | Firearm | **Inés** | Spanish musketeer |
  | 135 | Scythe | **Morwenna** | Cornish |
  | 140 | Dagger | **Fiora** | Italian stiletto |
  | 145 | Staff | **Azar** | Persian, "fire" |
  | 150 | Greatsword | **Ulrich** | German two-hander |
  | 155 | Bow | **Temülün** | Mongol horse archer |
  | 160 | Sword | **Tsubame** | Japanese |
- **Each shadow summons one elite kind, matched to its place on the ladder.** The top twelve elites, counted
  backwards from 99, pair with the shadows from the top down:

  | Shadow level | 160 | 155 | 150 | 145 | 140 | 135 | 130 | 125 | 120 | 115 | 110 | 105 |
  | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
  | Summons the elite of level | 99 | 94 | 88 | 83 | 77 | 72 | 66 | 61 | 55 | 50 | 44 | 39 |

  The six lowest elites (6 to 33) are summoned by no shadow.
- Four extra skill slots, a complete set of the highest-grade gear, and ring ranks. Normals and elites have no
  ring ranks.
- **They give off an aura**, so a shadow looks intimidating. **Not built yet** — the look is chosen once the
  shadows can be seen in the game.
- **Its gear is the best a forge can make:** every piece at grade A, folded to +35, every one of its six stat slots
  written with Sharp Ink, and a trademark on the weapon — each roll made the way a station makes it. It wears exactly
  its own pieces and never picks up a random one.
- **Weapon mastery equals its level, capped at 70 by its A-grade weapon**, so the Sword shadow knows every sword skill.
- **Ring ranks:** 40 in each of the three stats its weapon's mastery raises.
- **Drops:** the same as an elite, at higher chances. When weapon transcending exists, its materials are
  **specific to each weapon**, and **each shadow drops only its own weapon's**.
- **They are meant to need a whole guild to bring down.**
- **All twelve exist.** Only **Tsubame** (Sword) holds a weapon; the other eleven fight with basic attacks, their four
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

Every kill also has a 30% chance of one random consumable (a potion or a food). Materials are never in that roll.
Several of one material go down as one pile.

## Server restarts

- **A map server restart wipes every respawn timer**, and **every elite and every shadow is present when the
  server starts.**

## Still open

- **The shadows' names against the canon.** The canon today says the Twelve Unwritten — the twelve weapons —
  have no recorded names, only what they did, and that writing an Unwritten's name is the end-game reward of
  weapon mastery. Either the canon is rewritten, or a shadow is an echo of an Unwritten wearing a name of its own.
- **First guesses, all to be tuned in play:** the summon skill's 20-second cooldown; a summon strays at most 8 units
  from its summoner before walking back; every drop number in the Drops table; each shadow's stat spread (a physical
  spread for most, an INT-heavy one for the Staff, Wand and Scythe), its ring ranks, and its 2,000 experience and
  3,000 to 6,000 gold.
- **Normals' and elites' experience and gold were not changed** when they were re-levelled, so a creature moved from
  level 36 to 99 still pays what it paid at 36.
- **A shadow's 3D body is drawn at random from the cast each life**, like every 3D creature's, so a named shadow can
  look different each time it comes back.
- **Test placement:** the level-55 elite Giant Red Samurai and Tsubame stand in `field_0` so respawn and restarts can be
  tried. They move when the maps are done.
- **TODO — the end-game maps**, and where elites live in the field. Both are part of the world shape, which comes
  after this.

## Related

- [Gameplay System — V4](../2-systems/gameplay-system-v4/README.md) — forging, the seven material grades, Ink, Salt
  and mastery: what creatures drop and what shadows wear.
- [Biome Mob Families](biome-mob-families.md) — how creatures are grouped by biome and drawn by level today.
- [Story & Setting (Canon)](story-setting-canon.md) — the Twelve Unwritten.
- [Weapon Roster — 12 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the twelve
  weapon types a shadow each carries.
