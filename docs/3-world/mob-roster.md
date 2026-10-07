---
status: "BUILT 2026-09-22, not yet play-tested. First-guess numbers are listed under Still open."
owner: vince
last-updated: 2026-10-06
tags:
  - world-content
---

# Mob Roster — Normal, Elite, Shadow

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

Every creature in the game is one of three kinds: **normal**, **elite** or **shadow**. The kind decides what it
wears, how many summons it may keep out, where it lives and what it drops. How strong it is still comes from its
**level** and its stats through the same formulas a player's do — the kind never carries a stat multiplier.

**A creature's weapon mastery and skills live in the same store as a player's and follow the same rules** (decided
2026-10-04): mastery in ONE weapon only, and only the skills it has bought with that mastery's points. Nothing is
opened for free or drawn at random. Its weapon and its mastery in it are set per creature in the **Mob Table**
(the weapon with the rest of its gear under ▸, the mastery in its own column) and put on it when it is made; **every creature's mastery is 0 today**. Nothing
spends a creature's points yet, so **no creature casts a skill**, summoning included.

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
| How many | the rest of the roster | 18 | 14, one per weapon type |
| Levels | **1 to 96**, each set by what the creature is | **8 to 99**, each set by what the creature is | **105 to 170**, one every 5 levels |
| Wears | its authored pieces, **Raw +0** at level 1 rising to **E +15** at 99 | its authored pieces, **D +0** at level 6 rising to **B +30** at 99 | a complete set at **A +30**, plus ring ranks |
| Element level | **1** | **2** | **3** |
| Weapon mastery | in its one weapon, set in the Mob Table — 0 today | in its one weapon, set in the Mob Table — 0 today | in its one weapon, set in the Mob Table — 0 today |
| Summons, once it has bought the summon skill | nothing | normals | elites |
| Lives | every map it belongs in, from one end of the world to the other | the fields of its home biome near its level, and one on each dungeon's bottom floor | each at its own place, a special map away from where players level |
| Comes back after | **5 seconds** plus up to 3 | **30 minutes to 3 hours** | **4 to 6 hours** |
| Looks | flat | flat | a 3D character |
| Size | **Small to Large** — mostly Small or Medium | **mostly Large** — 16 Large, 2 Medium | always **1** (Medium) |

## Gear state

- **Every creature's gear state is authored on its own catalog entry**, as three settings shared by every piece it
  wears: its **grade** (Raw to A), its **folds** (never past the grade's cap: Raw 5, F 10, E 15, D 20, C 25, B 30,
  A 35), and its **Ink** (Watery, Thin, Faint, Clear or Sharp). The grade decides how many stats are written on each
  piece (Raw none, F one, up to A six); the Ink decides what each is worth. They are edited in the Mob Table's
  equipment panel, and no rule in the code overrides them for any kind.
- **Which stats are written, a weapon's trademark and an armour piece's word are rolled fresh every life**, exactly
  as a station rolls them, at every grade — a Raw piece included, as a forged one is.
- **A creature wears exactly the pieces authored for it and nothing else.** An empty slot stays empty.
- **The starting values follow level.** Folds rise evenly with level across each kind's range, and the grade is the
  lowest one (from that kind's starting grade) whose cap holds those folds; the Ink follows the grade — F Watery,
  E Thin, D Faint, C Clear, B and A Sharp (Raw writes nothing). So normals run Raw +0 at level 1, F from level
  37, E from level 70, E +15 at 99; elites run D +0 at level 6, C from level 72, B from level 88, B +30 at 99; every
  shadow is A +30.
  Any single creature can be changed after that.

## Sizes

- **A creature's size is one number: how many times a character's height it stands.** **1** is a character — the
  player, the townsfolk and every shadow. It runs from **0.3** to **5.2**: a raccoon is **0.3**, a bear **1.6**, a
  giant squid **4**. **No creature is ever smaller than Plopling, the level-1 creature, at 0.3** — a hard rule (the
  user, 2026-10-05), so even the mouse, Squeak, is 0.3. Each creature's art is measured and fitted to that height, so it stands exactly
  that tall whatever padding its picture carries.
- **Three size groups, worked out from that number and never typed:** under **0.6** is **Small**, **0.6 to 1.5** is
  **Medium**, over **1.5** is **Large**. The group is what cards and the weapon-vs-size chart will read; the number
  is what the body and reach use.
- **Melee reach counts the same number**, so how big a creature looks and how far it reaches can never disagree.
  Against a character, a blow reaches half of (1 + the creature's size) times its full reach — **0.65×** against a
  raccoon, **1.3×** against a bear, **2.5×** against a giant squid — and the creature's own blows reach the same
  distance back.
- **Its collision body grows with it.** A creature wider than the usual routing footprint steers around obstacles
  with its own width.
- **Some creatures fly** — the winged ones: Flutterby, Skywing, Blinkbat, Drizzle, Emberhoot, Hootie, Karasu and
  Oni Tengu, each set on its own catalog entry (**flies**). A flier's picture is drawn in the air with the middle of
  its body level with a character's head (0.327 high), and its nameplate, damage numbers, aim point and click spot
  rise with it. Its walking body stays on the ground, so where it goes, what blocks it and how far it reaches are
  exactly a ground creature's. One twice a character's height or taller is not raised (its middle is already
  there); a shadow never flies.
- **Size changes nothing else**: not movement speed, not stats. The weapon-vs-size damage chart is not switched on.
- **The split:** normals **40 Small, 17 Medium, 9 Large**; elites **16 Large, 2 Medium**; all shadows **1**
  (Medium). Each creature's size is set on its own catalog entry, in the Mob Table's **size** column, with its group
  shown beside it; a shadow is always 1 whatever its entry says, so its size is locked there.

## Normal

- The creatures that fill the field, wearing whatever pieces are authored for them — from **Raw +0**
  at level 1 to **E +15** at level 99 (see Gear state).
- **Each of the 66 normals has its own home biome, element and level**, set from what it is (2026-09-26): critters
  low, large beasts higher. It lives in every map where it belongs, not in one. The full table is
  [Creatures and Where They Live](biome-mob-families.md). Each one's stats were re-spent to its level's full point
  budget in the same proportions it had.

## Elite

- The stronger creatures of each area. The 18 creatures that were bosses are the 18 elites.
- **Each elite has its own home biome, element and level** (8 to 99), and at least one elite calls each biome home;
  the table is [Creatures and Where They Live](biome-mob-families.md).
- Whatever pieces are authored for them — from **D +0** at level 6 to **B +30** at level 99 (see Gear state) — and,
  once they have bought the summon skill, they **summon normals**: the three nearest their level at or below it, from their own home
  biome first.
- **They drop much more folding material, and the rarer Inks and Salts** (see Drops).

## Shadow

- **The strongest creatures in the game.** They are the 3D characters, each wearing **one weapon type**, so there
  are fourteen of them — one for each weapon, all fourteen in the game.
- **Levels 105, 110, 115 … 170**, one shadow per step — above the 1–99 range every other creature uses.
- **The weapon order runs from the least-played weapon at 105 to the most-played at 165.** A shadow is what a
  weapon's end-game materials come from, so the weapons fewest people choose are the easiest to finish — a reason
  to pick them. The order is a first guess, made before there is any player data.
- **Veyra's shadow is always level 170, whatever the play counts say** (decided 2026-10-06). It is the one shadow
  Chaos really lives in (see [Story & Setting](story-setting-canon.md)), and it carries Morwen's black lily mark. Only
  the other thirteen follow the play counts.
- **A shadow shows its Kashira's name and title** — *Veyra, the Last Petal* — and is also called by its style (see
  [Story & Setting](story-setting-canon.md), which holds the fourteen names, styles and how their forms are named).
  The names, titles and styles are locked (2026-10-06), and each shadow's data carries its name, title and element.

  | Level | Weapon | Shadow | Style | Element |
  | --- | --- | --- | --- | --- |
  | 105 | Shuriken | **Riven, the Red Autumn** | Autumn Gale | Wind |
  | 110 | Fist | **Asterion, the Light Beyond Night** | Northern Lights | Neutral |
  | 115 | Katar | **Nysara, the Face Without a Name** | Veiled Mirage | Ghost |
  | 120 | Wand | **Seraphine, Before the Dawn** | Morning Mist | Holy |
  | 125 | Warhammer | **Tharos, the Starbreaker** | Earthrend Descent | Earth |
  | 130 | Spear | **Orion, the River That Remembers** | Bending River | Water |
  | 135 | Axe | **Varkhaz, the Ever-Burning** | Wildfire | Fire |
  | 140 | Sniper | **Vael, the Moonless Eye** | Dark Moon | Dark |
  | 145 | Scythe | **Morwen, the Last Bloom** | Black Lily | Undead |
  | 150 | Dagger | **Velmora, the Poisoned Rose** | Nightshade Bloom | Poison |
  | 155 | Staff | **Kaelen, the Ember Beneath Snow** | Frozen Ashes | Fire |
  | 160 | Greatsword | **Mordane, King Beneath the Waves** | Raging Tide | Water |
  | 165 | Bow | **Celestia, the Wandering Sky** | Passing Cloud | Wind |
  | 170 | Sword | **Veyra, the Last Petal** | Blossom Wind | Wind |
- **The shadows carry all ten elements** (decided in the [World Map](world-map.md)): Wind three times, Water and Fire
  twice each, the rest once. Only the Water, Earth, Fire and Wind shadows can be answered with a converter today; the
  counter for the others is to come from skill buffs and cards. At level 3 the element matters, and its own element
  lands very differently on each: it heals the Water, Fire, Wind, Holy and Dark shadows, does nothing to the Earth,
  Poison and Undead ones, lands normally on the Neutral one, and lands for 175% on the Ghost one. It swings with its
  element too; the weapon it holds has none, and a weapon with no element never overrides a creature's own.
- **Each shadow summons one elite kind, matched to its place on the ladder.** The top fourteen elites by level pair
  with the shadows from the top down:

  | Shadow | Veyra 170 | Celestia 165 | Mordane 160 | Kaelen 155 | Velmora 150 | Morwen 145 | Vael 140 |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | Summons | Abyss Maw 99 | Venom Gloop 95 | Oni Tengu 92 | Wraithmother 90 | Inferno Mask 80 | Toadmaw 78 | Great Croak 66 |

  | Shadow | Varkhaz 135 | Orion 130 | Tharos 125 | Seraphine 120 | Nysara 115 | Asterion 110 | Riven 105 |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | Summons | Blood Ronin 60 | Karasu 58 | Elder Stalk 47 | Golden Bandit 44 | Mossmaw 35 | Frost Ronin 33 | Deep Maw 24 |

  The four lowest elites (8 to 22) are summoned by no shadow. The pairing follows level only, so a shadow's elite need
  not share its element or place.
- A complete set of gear at **A +30**, and ring ranks. Normals and elites have no ring ranks.
- **They give off an aura**, so a shadow looks intimidating. **Not built yet** — the look is chosen once the
  shadows can be seen in the game.
- **Its gear:** every piece at grade A, folded to +30, every one of its six stat slots written with Sharp Ink, and a
  trademark on the weapon — each roll made the way a station makes it.
- **Weapon mastery: 0 today**, set in the Mob Table like every creature's.
- **Ring ranks:** 40 in each of the three stats its weapon's mastery raises.
- **Drops:** the same as an elite, at higher chances. When weapon transcending exists, its materials are
  **specific to each weapon**, and **each shadow drops only its own weapon's**.
- **They are meant to need a whole guild to bring down.**
- **All fourteen exist.** Only **Veyra** (the Sword shadow) holds a weapon; the other thirteen fight with basic
  attacks until their weapon's skills exist, and then each is handed its weapon.
- They carry their name and title and an always-visible health bar.
- **Each shadow wears one fixed look of its own** — a body and its outfit colours, the same every life and in front of
  everybody (decided and built 2026-10-06). Both are set on its catalog entry, and only in the **Mob Table**: open a
  shadow's row (**▸**) and its look panel shows a 3D preview you drag to turn, a body picker offering **every**
  character body the game can load, and one colour swatch per part of the chosen body. Picking another body clears
  the colours. Nothing is written until **Save All**. The server takes the body from the entry and refuses to make a
  shadow whose body it cannot load (written to its log); `tools/check-shadow-looks.py` fails such a shadow, or a colour
  on a part its body does not have, before it ships. The starting bodies:

  | Shadow | Body | Shadow | Body |
  | --- | --- | --- | --- |
  | Riven | Ninja_Male | Varkhaz | Viking_Male |
  | Asterion | Knight_Golden_Male | Vael | Soldier_Male |
  | Nysara | Ninja_Sand_Female | Morwen | Witch |
  | Seraphine | Knight_Golden_Female | Velmora | Ninja_Female |
  | Tharos | Knight_Male | Kaelen | Wizard |
  | Orion | BlueSoldier_Male | Mordane | Pirate_Male |
  | Celestia | Cowboy_Female | Veyra | Kimono_Female |

## Summoning

- **Summoning is a skill**, so a creature summons only once it has bought it — none has yet. It has a cooldown like
  any other, and the summoner uses it **every time it is ready**.
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
Converter; a creature whose element has no converter (Neutral, Poison, Holy, Dark, Ghost, Undead) never drops one. Materials are never in that roll.
Several of one material go down as one pile.

## Server restarts

- **A map server restart wipes every respawn timer**, and **every elite and every shadow is present when the
  server starts.**

## Still open

- **Shuriken, Fist and Katar at the bottom of the ladder** (105, 110 and 115) is a first guess at the least-played
  weapons, made before there is any player data, like the rest of the order.
- **First guesses, all to be tuned in play:** the summon skill's 20-second cooldown; a summon strays at most 8 units
  from its summoner before walking back; every drop number in the Drops table; each shadow's stat spread (a physical
  spread for most, an INT-heavy one for the Staff, Wand and Scythe), its ring ranks, and its 2,000 experience.
- **Normals' and elites' experience and gold ranges were not changed** when they were re-levelled, so a creature moved
  from its old level to its current one still pays the experience it did before — the Octi, now level 63, still gives
  5 experience — and its gold range (2 to 6 for the Octi) is still what a character saved from it is multiplied from.
- **No death pays gold** (decided 2026-09-30): gold comes only from saving a character that never fights — see
  [Saved Characters](saved-characters.md).
- **The fourteen shadows' bodies are starting picks** (set 2026-10-06 from each Kashira's gender and theme, no colours
  changed yet), listed under Shadow; each is the user's to change in the Mob Table.
- **TODO — the shadows' places and the elites' spots** are designed in the [World Map](world-map.md) and not built.

## Related

- [Gameplay System — V4](../2-systems/gameplay-system-v4/README.md) — forging, the seven material grades, Ink, Salt
  and mastery: what creatures drop and what shadows wear.
- [Creatures and Where They Live](biome-mob-families.md) — every creature's home biome, element and level, and every map's creatures.
- [Story & Setting (Canon)](story-setting-canon.md) — the artist, Chaos and the Fourteen Kashira.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen
  weapon types a shadow each carries.
