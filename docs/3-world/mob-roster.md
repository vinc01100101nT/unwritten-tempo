---
status: "BUILT, not yet play-tested. Every creature's level and multiplier are on its file; where it lives is worked out from the world layout. First-guess numbers are listed under Still open."
owner: vince
last-updated: 2026-10-31
tags:
  - world-content
---

# Mob Roster — Normal, Elite, Shadow

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

Every creature in the game is one of three kinds: **normal**, **elite** or **shadow**. The kind decides how many
summons it may keep out, how long it stays gone, how its gear's folds and card slots are rolled, how often it gives up
its find, and whether it is a 3D character. How strong it is comes from its **level** and its stats through the same
formulas a player's do, and from one number typed on each creature, its **multiplier**: its health × the multiplier
and its attack × the cube root of it, and everything its kill pays × the multiplier
([What a Kill Pays](what-a-kill-pays.md)).

**Every normal creature also comes in two harder versions, Tainted and Blighted.** A version is a creature of its
own, with its own id, name, multiplier and material; everything else — its body, level, element, gear and stats — is
copied from its original whenever a creature file is saved, so retuning the original retunes both.

**A creature's weapon mastery and skills live in the same store as a player's and follow the same rules** (decided
2026-10-04): mastery in ONE weapon only, and only the skills it has bought with that mastery's points. Nothing is
opened for free or drawn at random. Its weapon and its mastery in it are set per creature in the **Mob Table**
(the weapon with the rest of its gear under ▸, the mastery in its own column) and put on it when it is made; **every
creature's mastery is 0 today**. Nothing spends a creature's points yet, so no creature casts a weapon skill.
**Summoning is the one exception**: every elite and shadow holds the summon skill from birth, whatever weapon it
wields or none (decided 2026-10-17).

**Where creatures live is one list, worked out from the world layout.** The world spawn list
(`Resources/WorldSpawns`) holds the seamless world `world`'s rows, each naming one creature and the patch of ground it
keeps up; `tools/world-spawns/build_world_spawns.py` writes them from the layout and is never hand-edited. A patch's
level climbs with its distance from the nearest town, so the land round every town is the gentlest; the originals
live nearest the towns and their Tainted and Blighted versions farther out. The rules, and how to rebuild them, are
the `world-authoring` skill. Nothing about creatures is saved inside the world's ground.

**How many creatures there are, by kind and version:** `python3 tools/project-status.py`.

## The three kinds

| | Normal | Elite | Shadow |
| --- | --- | --- | --- |
| How many | the rest of the roster, each with a Tainted and a Blighted version | 14 that live in the world and 4 that are only summoned | one per weapon type |
| Levels | **1 to 96**, each set by what the creature is | **50 to 80**, each set by what the creature is | **105 to 170**, one every 5 levels |
| Multiplier | an original **×1 or ×2**, its Tainted version **×3 or ×5**, its Blighted version **×10** | **×30** | **×90** at 105, climbing to **×150** at 170 |
| Wears | a full set at the grade its level gives, folded **+0 to +10** each life | a full set at the grade its level gives, folded **+4 to +10** each life | a full set at grade **A**, folded **+7 to +10** each life, plus ring ranks |
| Element level | **1** | **2** | **3** |
| Weapon mastery | in its one weapon, set in the Mob Table — 0 today | in its one weapon, set in the Mob Table — 0 today | in its one weapon, set in the Mob Table — 0 today |
| Summons | nothing | Tainted and Blighted normals | elites |
| Lives | the patches of its own kind of ground, from one end of the world to the other | the two patches nearest its level, away from the towns — or nowhere, if it is only summoned | nowhere on its own: a summoner at its place calls it |
| Comes back after | **5 seconds** plus up to 3 | **30 minutes to 3 hours** | never on its own — it is summoned again |
| Looks | flat | flat | a 3D character |
| Size | **Small to Large** — mostly Small or Medium | **mostly Large** | always **1** (Medium) |

## Gear

- **Every creature wears a full set, at the grade its level gives**, with its folds, card slots, base, Ink lines,
  trademark and word rolled each life. The rules, the stat builds and the shadows' sets are in
  [Creature Gear](creature-gear.md).
- **What it wears decides nothing it pays.** Its multiplier does ([What a Kill Pays](what-a-kill-pays.md)).

## Sizes

- **A creature's size is one number: how many times a character's height it stands.** **1** is a character — the
  player, the townsfolk and every shadow. It runs from **0.5** to **4.2**: a raccoon is **0.5**, a bear **1.8**, a
  giant squid **4.2**. **No creature is ever smaller than Plopling, the level-1 creature, at 0.5** — a hard rule (the
  user, 2026-10-05), so even the mouse, Squeak, is 0.5 — half a character's height. Each creature's art is measured and fitted to that height, so it stands exactly
  that tall whatever padding its picture carries.
- **Three size groups, worked out from that number and never typed:** under **0.8** is **Small**, **0.8 to 1.7** is
  **Medium**, over **1.7** is **Large**. The group is what cards and the weapon-vs-size chart will read; the number
  is what the body and reach use.
- **Melee reach counts the same number**, so how big a creature looks and how far it reaches can never disagree.
  Against a character, a blow reaches half of (1 + the creature's size) times its full reach — **0.75×** against a
  raccoon, **1.4×** against a bear, **2.6×** against a giant squid — and the creature's own blows reach the same
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
- **A creature's movement speed is its own number, 0 to 2**, set in the Mob Table's **movement speed** column beside
  its size. It multiplies the shared pace: **1** is the pace a person travels at (a run of 1 unit a second, a walk of
  0.4), **2** the fastest there is, and **0** never moves from where it stands — it still fights whatever comes into
  its reach, and cannot be pushed or pulled. A creature wanders at the walk and chases at the run, each times this
  number. Set from what the creature is: **0** Canewick, Shootling · **0.3** Mossnail · **0.4** Capling, Frostcap, Moldy, Shellby · **0.5**
  Bittersweet, Dewdrop, Goopie, Plopling, Rivershell · **0.6** Crawlet, Grubble, Elder Stalk,
  Gloop King, Venom Gloop · **0.7** Emberlurk, Emberwyrm, Wyrmlet, Autumn Stalk · **0.8** Cyclo, Iris, Octi, Pando,
  Peeper, Sucker, Abyss Maw, Oculord · **0.9** Bloodshot, Frostwick, Inky, Kappi, Mudpup, Snapper, Deep Maw, Mossmaw,
  Toadmaw · **1** Burrowbo, Digger, Drizzle, Frostskull, Gillie, Hornscale, Lovebite, Squirt, Willo, Inferno Mask,
  Wraithmother and every shadow · **1.1** Chomper, Cinder, Coily, Flutterby, Glowcoil, Grumbear, Horngore, Swampfang,
  Great Croak · **1.2** Blazeskull, Gnasher, Hisser, Hootie, Mossgore, Newtie, Wisp, Bandit Boss · **1.3** Adder,
  Emberhoot, Scaly, Viper, Frost Ronin · **1.4** Rascal, Sunscale, Tarantix, Webber, Blood Ronin, Golden Bandit,
  Oni Tengu · **1.5** Blinkbat, Squeak, Karasu · **1.6** Nightsqueak, Pilfer, Skywing.
- **Each creature's size is set on its own catalog entry**, in the Mob Table's **size** column, with its group shown
  beside it; a shadow is always 1 whatever its entry says, so its size is locked there. A Tainted or Blighted version
  stands exactly as tall as its original.

## Normal

- The creatures that fill the field.
- **Each normal has its own home biome, element and level**, set from what it is (2026-09-26): critters low, large
  beasts higher. It lives on every patch of its kind of ground near its level, not in one place, and its Tainted and
  Blighted versions on patches farther from the towns (the `world-authoring` skill). Each one's stats are spent in the
  build of the weapon it holds; its weapon, armour and build are in [Creature Gear](creature-gear.md).
- **The elements follow the level ladder:** levels 1–33 are only Neutral, Earth or Water; 34–66 add Wind and Fire;
  67–99 add Poison, Holy, Dark, Ghost and Undead.

## Elite

- The stronger creatures of each area, at **×30**.
- **Each elite has its own home biome, element and level** (50 to 80), and at least one elite calls each biome home.
- **Fourteen live in the world**, each on the two patches nearest its level away from the towns, of its own kind of
  ground first, at most one elite to a patch. **Four are only summoned** — Blood Ronin, Frost Ronin, Inferno Mask and
  Wraithmother: the world never spawns them, and each has a summoner on the dark isle who calls it for the materials
  its recipe asks (`Resources/SummonRecipes`). Those four roll their weapon each life ([Creature Gear](creature-gear.md)).
- **They summon normals**: the three Tainted or Blighted normals nearest their level at or below it, from their own
  home biome first — five of the nearest, three of the next, two of the one after, ten in all. With no Tainted or
  Blighted normal at their level or below, they call plain normals.
- **Nothing an elite drops is ever auto-looted** ([Creature Materials](creature-materials.md)).

## Shadow

- **The strongest creatures in the game.** They are the 3D characters, each wearing **one weapon type**, one for
  each weapon.
- **Levels 105, 110, 115 … 170**, one shadow per step — above the 1–99 range every other creature uses — and a
  multiplier that climbs with it, **×90** at 105 to **×150** at 170.
- **The weapon order runs from the least-played weapon at 105 to the most-played at 165.** A shadow is what a
  weapon's end-game materials come from, so the weapons fewest people choose are the easiest to finish — a reason
  to pick them. The order is a first guess, made before there is any player data.
- **Veyra's shadow is always level 170, whatever the play counts say** (decided 2026-10-06). It is the one shadow
  Chaos really lives in (see [Story & Setting](story-setting-canon.md)), and it carries Morwen's black lily mark. Only
  the other thirteen follow the play counts.
- **A shadow is never in the world on its own.** Each has a summoner at its place — its landmark, Tharos's at the
  Mountain's Root cave — who calls it for the materials its recipe asks, one at a time; a summoned boss does not come
  back once beaten.
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
- **The shadows carry all ten elements**: Wind three times, Water and Fire
  twice each, the rest once. Only the Water, Earth, Fire and Wind shadows can be answered with a converter today; the
  counter for the others is to come from skill buffs and cards. At level 3 the element matters, and its own element
  lands very differently on each: it heals the Water, Fire, Wind, Holy and Dark shadows, does nothing to the Earth,
  Poison and Undead ones, lands normally on the Neutral one, and lands for 175% on the Ghost one. It swings with its
  element too; the weapon it holds has none, and a weapon with no element never overrides a creature's own.
- **Each shadow summons one elite kind, five of it, matched to its place on the ladder.** The shadows ranked from the
  highest level down pair with the elites ranked the same way, worked out from the roster every time: the level-170
  shadow calls the highest-level elite, and each step down the shadows is a step down the elites. The four elites at
  the bottom of the ladder are summoned by no shadow. The pairing follows level only, so a shadow's elite need not
  share its element or place.
- A complete set of gear at **grade A** — its weapon and a full set of one armour material, chosen by its weapon
  ([Creature Gear](creature-gear.md)) — and ring ranks. Normals and elites have no ring ranks.
- **They give off an aura**, so a shadow looks intimidating. **Not built yet** — the look is chosen once the
  shadows can be seen in the game.
- **Its gear:** every piece at grade A, folded +7 to +10 each life, every one of its six stat slots written with Sharp
  Ink, and a trademark on the weapon — each roll made the way a station makes it.
- **Weapon mastery: 0 today**, set in the Mob Table like every creature's, so every shadow fights with basic attacks
  and its summon.
- **Ring ranks:** 33 in each of the three stats its weapon's mastery raises.
- **Drops:** by its multiplier, like every creature (see Drops). When weapon transcending exists, its materials are
  **specific to each weapon**, and **each shadow drops only its own weapon's**.
- **They are meant to need a whole guild to bring down.**
- **Each holds its weapon**, with its stats spent in that weapon's build ([Creature Gear](creature-gear.md)).
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

- **Summoning is a skill that belongs to the kind, not to a weapon**: every elite and shadow holds it from birth,
  whatever it wields, and no normal creature has it. It has a cooldown like
  any other, and the summoner uses it **every time it is ready**.
- **Each summoner has a fixed maximum number of summons**: ten for an elite, five for a shadow. When it is at its
  maximum, it does not summon, even with the skill ready.
- **Summoned creatures always follow their summoner**, staying within a range of it.
- **They disappear when their summoner dies.**
- **They give no experience and no loot.** They exist to get in the way and to make elites and shadows harder.
- **A summoned creature never summons.** An elite called by a shadow does not call normals of its own.
- **A summoner calls while it is fighting.** When it has a target and the skill is ready, it summons before any other
  skill, topping every group back up to its share in one cast.
- **A summon that dies, or is sent away, is kept and reused** by the next cast rather than made again.

## Drops

**Everything a kill drops follows its multiplier, never its level, its kind or its gear** — except the chance of its
find, which its kind sets. A death is rolled once and paid to the one person who earned it.

- **Temper Dust and Quench Oil, on every kill:** a ×1 kill gives **0.6** Temper Dust and **0.02** Quench Oil on
  average, times the multiplier. The whole part is given; the part left over is the chance of one more. A level-1 ×1
  creature gives a 60% chance of one Temper Dust and a 2% chance of one Quench Oil; Veyra, at ×150, gives 90 Temper
  Dust and 3 Quench Oil.
- **Inks and Salts:** each has a chance on a ×1 kill, times the multiplier, and drops only from the multiplier it
  opens at and up. Past 100% it is more than one.

  | | Opens from | Chance at ×1 |
  | --- | --- | --- |
  | Watery Ink | ×1 | 4% |
  | Thin Ink | ×3 | 1% |
  | Faint Ink | ×10 | 1% |
  | Clear Ink | ×30 | 0.5% |
  | Sharp Ink | ×90 | 0.167% (15% at ×90) |
  | Coarse Salt | ×1 | 10% |
  | Fine Salt | ×3 | 0.5% |
  | Pure Salt | ×30 | 0.1% |

- **One piece of its gear, on 10% of kills, for every kind:** one of the pieces it wears, picked at random, as a NEW
  copy — nothing leaves the creature. It is an exact copy of that piece: grade, folds, Ink lines, card slots, base,
  trademark or word. Gear has no rarity (decided 2026-10-18).
- **Its own material:** every creature drops ONE material nothing else does, its tier set by its multiplier — the
  chances, the prices and the full list are [Creature Materials](creature-materials.md).
- **An Ink crate**, of the same rarity as its own material, at a chance that makes it worth a tenth of what the kill
  already pays.
- **Its find:** 5% for a normal, 50% for an elite, 100% for a shadow. A creature of Fire, Water, Earth or Wind gives up
  one of its element's two converters — a fire creature the Flame Elemental Converter or the Flame Armor Converter; a
  creature of any other element gives up that element's food
  ([Town Supplies and Hunting Finds](../2-systems/town-supplies-and-hunting-finds.md)). Potions, wings and the Antidote
  are sold by the Tool Dealer and never dropped. Materials are never in that roll.
- Several of one material go down as one pile.
- **A rebel pays three kills of its creature**: its experience and every drop roll three times over, at the creature's
  own multiplier, so no higher tier opens ([Saved Characters](saved-characters.md)).
- **What a creature drops, and at what chance:** the GM command `/mobinfo <creature>`
  ([GM Commands from a Terminal](../0-process/gm-commands-from-a-terminal.md)).

## Server restarts

- **A game server restart wipes every respawn timer**, so **every elite that lives in the world is there again after a
  start.** A summoned boss out when the server stops is gone.

## Still open

- **Shuriken, Fist and Katar at the bottom of the ladder** (105, 110 and 115) is a first guess at the least-played
  weapons, made before there is any player data, like the rest of the order.
- **First guesses, all to be tuned in play:** the summon skill's 20-second cooldown; a summon strays at most 8 units
  from its summoner before walking back; every multiplier, and every drop number on this page; each shadow's stat
  spread (a physical spread for most, an INT-heavy one for the Staff, Wand and Scythe) and its ring ranks.
- **No death pays Unsigned** (decided 2026-09-30). Hunting earns Unsigned by selling what creatures drop
  ([Creature Materials](creature-materials.md)) and by saving a character that never fights
  ([Saved Characters](saved-characters.md)); both follow the level in the creature's file and its multiplier
  ([What a Kill Pays](what-a-kill-pays.md)).
- **The fourteen shadows' bodies are starting picks** (set 2026-10-06 from each Kashira's gender and theme, no colours
  changed yet), listed under Shadow; each is the user's to change in the Mob Table.
- **Dungeon floors:** the spawn tool works out three floors under every dungeon door and cave but the Mountain's Root,
  Tainted above and Blighted at the bottom, but writes them only once the floor maps exist (the `world-authoring`
  skill).

## Related

- [Gameplay System — V4](../2-systems/gameplay-system-v4/README.md) — dropped gear, folding, the seven grades, Ink, Salt
  and mastery: what creatures drop and what shadows wear.
- [Story & Setting (Canon)](story-setting-canon.md) — the artist, Chaos and the Fourteen Kashira.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen
  weapon types a shadow each carries.
