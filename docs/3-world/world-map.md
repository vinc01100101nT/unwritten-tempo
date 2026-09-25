---
status: "PROPOSAL, partly built. Agreed with the user on 2026-09-25: three regions that are level tiers (Might 1–33, Finesse 34–66, Arcane 67–99), so players travel the whole world; a big city for each one-family weapon and a small town for each two-family weapon, on the border between its two regions; Lastline in Might; the map in use for now is the painted world-map picture in the project (it can be replaced); the 59 maps of the three regions are generated and wired (2026-09-26, not yet walked in play), with the hand-made lastline as Lastline; five dungeons of 2 to 4 floors with an elite on each bottom floor; elites live everywhere, like normal creatures; the fourteen shadows' elements below; every shadow waits at its own place, a special map away from where players level, entered by quest, key, guide, world clock or a hidden way. The game uses no real-country culture. NOT settled: everything listed under Still open."
owner: vince
last-updated: 2026-09-25
tags:
  - world-content
---

# The World Map — Three Regions

A full redesign of the world. **Only `lastline` survives**, as the town every new character starts in. Every other map
today is set aside.

## The shape: three regions, three level tiers

The world has **three regions, one per weapon family**, and they are **level tiers**: a player starts in Might and
travels the whole world as they level, never staying in one family's land.

| Tier | Region | Levels | Land | Biomes | Big cities |
| --- | --- | --- | --- | --- | --- |
| 1 | **Might** | 1–33 | the main continent, and the forest half of the north island | Grassland, Forest, Highland, Coast, Snow | Shield, Greatsword, Warhammer, Spear |
| 2 | **Finesse** | 34–66 | the west continent | Forest, Steppe, Desert, Peaks | Bow, Dagger |
| 3 | **Arcane** | 67–99 | overseas: the north volcano, the floating island above the clouds in the far north-west, and the dark south (the south-west peninsula, the swamp lagoon, the south-east island) | Volcanic, Mist, Haunted, Corrupted, Sacred, Swamp | Staff, Wand |

- **Lastline** (`lastline`) sits in the middle of the main continent, on the scribe's last written line, inside Might.
  It belongs to no one.
- **The elements get harder with each tier.** Might holds Neutral, Earth and Water, which today's converters answer;
  Finesse adds Wind and Fire; Arcane holds Ghost, Undead, Shadow, Holy and Poison, the elements whose counter (skill
  buffs and cards) is still to come.
- **No real-country culture.** No city is a real country and no name comes from a real country's language. The theme
  names are drawn from is not chosen yet.

**The map in use for now** is a GPT painting (1536×1024); it can be replaced. The zones are laid out on it in the
world creator, and the in-game world map (N) shows that tool's PNG export exactly as exported — the painting with
every map's box, its name and the lines of every door between maps on it — kept in the project as `unity/Assets/3_Data/Resources/WorldMap/world-map.png`.
The clean painting and the layout sit beside the tool: `tools/world-creator/worlds/wm1.png` and `world-regions.json`.

## Cities and towns

- **A big city for each one-family weapon** (Might, Finesse or Arcane only): eight cities.
- **A small town for each two-family weapon**, standing on the border between its two regions: six towns. You walk
  between the two regions the weapon mixes.
- **Each one lives where its weapon belongs**: the bow with the hunters in the woods, the greatsword on a snowy
  summit, and so on.

| Weapon | Kind | Region | Where on the map | Biome | Why it lives there |
| --- | --- | --- | --- | --- | --- |
| Shield | City | Might | the open grasslands north-west of Lastline | Grassland | a walled city guarding the starting plains |
| Greatsword | City | Might | the snowy peaks of the north-east highlands | Snow | a huge sword standing on a snowy summit |
| Warhammer | City | Might | the rocky foot of those highlands, among the pines | Highland | a mining and forge city; hammers break stone |
| Spear | City | Might | the east sea cliffs | Coast | harpoons and fishing spears by the sea |
| Bow | City | Finesse | the green woodland on the west continent's north-east | Forest | hunters live in the woods |
| Dagger | City | Finesse | the sandy south of the west continent's golden plains | Desert | a desert city of narrow alleys where assassins hide |
| Staff | City | Arcane | the north volcano | Volcanic | the one who burned the field, living at the fire |
| Wand | City | Arcane | the sacred peak on a **floating island above the clouds**, in the far north-west | Sacred | healers on the holy mountain, above the world |
| Sword | Town | Might–Finesse | the green east side of the land where the continents join | Grassland | a duellists' crossroads where fighters from both lands meet |
| Axe | Town | Might–Finesse | the wooded south side of the join | Forest | woodcutters; the axe's first job |
| Kick | Town | Might–Finesse | the west side of the join, where the steppe starts | Steppe | open plains to run and train on |
| Firearm | Town | Finesse–Arcane | the west continent's south, beside its burning vent | Volcanic | gunpowder from the sulphur of the burning ground |
| Fist | Town | Might–Arcane | the rocky island group off the east coast | Coast | a monastery on sea stacks, training among the waves |
| Scythe | Town | Might–Arcane | the dark south-west peninsula | Haunted | reapers among the graves |

**The Sacred biome looks like a place up among the clouds** (rebuilt 2026-09-26, not yet seen in play): patchy white
clouds float over every Sacred map at treetop height and drift slowly across it, with the ground showing through the
gaps, a clear patch around you, and some fog.

**Town levels:** Might–Finesse about 28–40 (the step from tier 1 to tier 2); Finesse–Arcane and Might–Arcane about
60–70.

## Dungeons

**Five dungeons at different levels**, each of **2 to 4 floors**. Each floor goes a few levels deeper, and **an
elite waits on each bottom floor**. The names are working labels.

| # | Dungeon | Where | Floors | Levels |
| --- | --- | --- | --- | --- |
| 1 | Old Tunnels | Might, near the Shield city | 2 | 8–15 |
| 2 | Highland Mines | Might, the north-east stone highlands | 3 | 24–36 |
| 3 | Canyon Temple | Finesse, the red rock mountains on the west edge | 3 | 50–62 |
| 4 | Lava Tubes | Arcane, under the north volcano | 3 | 70–80 |
| 5 | The Haunted Deep | Arcane, the dark south-west peninsula | 4 | 86–99 |

**What lives on a floor** is drawn by level: every ordinary creature today belongs to one biome, and each biome holds
one stretch of levels (commons 1–9, Grassland 10–27, Snow 28–45, Desert 46–63, Volcanic 64–81, Corrupted 82–99). So a
dungeon's creatures follow its levels, not its look, until the new biomes have creatures of their own.

**Elites live everywhere**, like normal creatures: in fields, in dungeons and on special maps.

## The shadows and their places

There are **fourteen shadows**, one per weapon. Each waits at **its own place, a special map away from where players
level**: a shadow needs a guild to bring down, and meeting one in a levelling field would only spoil the levelling.

| Weapon | Element | The place | Way in |
| --- | --- | --- | --- |
| Shield | Neutral | **The Last Bridge** — one stone bridge over a bottomless gorge, the only way across. The shadow stands in the middle of it and does not move. It can be seen from a level-1 field. | **Quest**: the bridge-keeper's chain opens the gate at the end. |
| Wand | Holy | **The Hill of the Flame** — a ring of standing stones round a fire that has never gone out. Everything that falls near the shadow gets up again. | **Quest**: a pilgrimage, carrying the flame between shrines. |
| Sword | Wind | **The Thousand Gates** — a path climbing through a thousand gates above the clouds, to a tree in blossom. | **Quest**: each gate is a trial. |
| Warhammer | Earth | **The Iron Gate** — a gate the height of a mountain, cut into the mountain itself. The shadow is the reason it has never opened. | **Key**: the Iron Key, forged from high-value elite drops. |
| Dagger | Poison | **The Masked Ballroom** — a ballroom in a sinking palace where every dancer wears a mask. The shadow is one of them, and arrives as the dance ends. | **Key**: the Mask, a rare and costly item. |
| Axe | Fire | **The Burning Keel** — a longship stranded in a river of cooled lava, at the lip of a live volcano. | **Guide**: a lava-walker leads the party across. |
| Staff | Fire | **The Everburning Plain** — a plain where the ground itself is on fire, around a fire temple. | **Guide**: the fire temple's keeper grants passage. |
| Bow | Wind | **The Sky Cairn** — a lone stone cairn on an endless steppe. The shadow rides circles round it, always out of reach. | **Guide**: a steppe rider takes the party out. |
| Spear | Water | **The Tide Temple** — a temple on a rock out at sea. | **World clock**: walkable only while the tide is out. |
| Firearm | Shadow | **The Dusk Tower** — a bell tower above a city where the sun is always setting. The shadow fires once, from the top. | **World clock**: the door opens only at dusk. |
| Greatsword | Ghost | **The Walking Road** — a road through fog that keeps going as long as you walk it, ending at a black castle. | **World clock**: the road appears only in fog. |
| Scythe | Undead | **The Drowned Chapel** — a chapel at the bottom of a black cove, whose bell still rings underwater. | **Hidden**: find the way down yourself. |
| Fist | Holy | *not named yet* — a mountain monastery under the aurora. | **Quest**: the monastery's trials. |
| Kick | Wind | *not named yet* | **Hidden**: a path nobody marks. |

- **All ten elements are covered**; Wind is held by three shadows, Fire and Holy by two each.
- The shadows' names are not settled (see Still open), so this table names them by weapon.

## Biomes and elements

Every creature map has a **biome**, and the biome sets which element **most** of its creatures carry. It is a
majority, never a lock: a snow field is mostly Water, but some of its creatures are Earth, Shadow or Wind. Every one
of the ten elements is the majority somewhere.

| Biome | Most creatures | Also found | Exists today? |
| --- | --- | --- | --- |
| Grassland | Neutral | Earth, Wind, Poison | yes |
| Forest | Earth | Poison, Wind, Neutral | yes (in the map generator; no world map uses it yet) |
| Highland | Earth | Wind, Neutral | yes (in the map generator; no world map uses it yet) |
| Peaks | Wind | Holy, Water | yes (in the map generator; no world map uses it yet) |
| Steppe | Wind | Neutral, Earth | yes (in the map generator; no world map uses it yet) |
| Coast | Water | Wind, Undead | yes (in the map generator; no world map uses it yet) |
| Snow | Water | Wind, Earth, Shadow | yes |
| Volcanic | Fire | Earth, Undead | yes |
| Desert | Fire | Earth, Poison, Undead | yes |
| Swamp | Poison | Water, Undead | yes (in the map generator; no world map uses it yet) |
| Sacred | Holy | Neutral, Wind, Ghost | yes (in the map generator; no world map uses it yet) |
| Haunted | Undead | Ghost, Shadow, Water | yes (in the map generator; no world map uses it yet) |
| Mist | Ghost | Shadow, Undead, Wind | yes (in the map generator; no world map uses it yet) |
| Corrupted | Shadow | Undead, Ghost, Poison | yes |

**Each region uses its own biomes** (the region table above). A map's biome sets its creatures' majority element; a
shadow's element is its own and does not have to match the biome around its place.

## The castles

Castles are the seats guild wars will be fought over; how many there are, and where, is open.

- **Castle gameplay is deferred**: how a guild takes one, holds it, and what owning it gives. When it is designed, one
  rule already holds: **a castle must not give power**, for the same reason weapons carry no parallel progression.

## Getting around

- **Might and Finesse are joined by land**, where the two continents meet; the Might–Finesse towns stand there.
- **Arcane is across the sea.** The harbour towns (Fist, Scythe and Firearm) are the ways over. The Wand's floating
  island is reached from above the sea; how is open.
- **Teleports and other service townspeople are deferred.**

## Built (2026-09-26, not yet walked in play)

- **Every map of the three regions exists as a scene**: 8 cities, 6 towns, 30 fields and the 15 dungeon floors, each
  generated in its own biome and joined by doors, from the layout `tools/world-creator/worlds/world-regions.json`
  (74 maps, 77 doors; the 14 shadow places in it are not generated).
- **Lastline is `lastline`, the hand-made town, kept as it is.** Nothing in it was removed. Its two existing gates were
  pointed at the new world (west gate → the level 1–3 field, east gate → the level 21–24 field), and two doors were
  added at its south and north gates (→ the south-west forest, 26–30, and the north forest, 9–12).
- **Creatures in every field and dungeon floor** (the world spawn list, 85 rows; shadows are not placed yet): each
  field keeps about 31 normals alive drawn from the level range in its name, and each dungeon floor 26, its dungeon's
  levels split evenly across its floors (Old Tunnels 8–11 / 12–15; Highland Mines 24–27 / 28–31 / 32–36; Canyon
  Temple 50–53 / 54–57 / 58–62; Lava Tubes 70–72 / 73–76 / 77–80; Haunted Deep 86–88 / 89–92 / 93–95 / 96–99). A
  range that crosses from one biome's creatures into the next draws from both. Every elite stands in each field
  whose range holds its level, and each bottom floor holds the strongest elite at or below its dungeon's top level
  (Old Tunnels 11, Mines 33, Canyon Temple 61, Lava Tubes 77, Haunted Deep 99). Cities and towns have none.
- **The in-game world map (N)** shows the world creator's PNG export of the layout, as exported; the game adds nothing to it but the "you are here" marker.

## Still open

- **Every name**: the shadows', the cities', the towns', the dungeons'. The naming theme is not chosen.
- **The cities' level bands.** A first split: Shield 1–8, Warhammer 9–16, Spear 17–24, Greatsword 25–33 (the snowy
  summit tops Might, beside today's Snow creatures at 28–45), Bow 34–49, Dagger 50–66, Staff 67–82, Wand 83–99.
- **The Fist and Kick shadows' places**: names and exact spots.
- **Whether one key lets the whole party in** (and is used up), or every member needs their own. Proposed: one per
  party.
- **Other special maps**, ideas only: the crossing ships, a climbing tower, hidden places, night and weather places,
  a fortress, and the Silencer's place (the story's deepest blank page) as the final map.
- **How many castles**, and where.
- **The shadow places are not built.** The layout holds them in a block below the map, each with one door from the map
  it is reached from; none was generated, and their ways in (quest, key, guide, world clock, hidden) need systems the
  game does not have yet.
- **Map names shown in the game are working labels**: the weapon's name for a city or town, "Lv 1–3" style for a
  field, and working names for the dungeons (Old Tunnels, Highland Mines, Canyon Temple, Lava Tubes, The Haunted Deep).

## What this needs that the game does not have yet

- **Elements on every creature.** Today the 96 creatures use only five elements (Neutral 16, Water 19, Earth 25,
  Fire 18, Wind 18), and the shadows only four. Every creature and all fourteen shadows need re-assigning.
- **A counter for Holy, Shadow and Poison.** At a shadow's element level (3), nothing a player can attack with today
  does more than 100% to a Holy, Shadow or Poison shadow. A Ghost shadow takes 0 from a plain attack, so a converter
  is required to hurt it. **The counter will come from skill buffs and cards**, both still to be built.
- **Quests.** The game has no quest system; four shadows' places are reached by quest.
- **A world clock that opens doors**: a tide, dusk and fog (three shadows' places). Not checked: whether the server
  knows the time of day and the weather.
- **Guides**: a townsperson who moves a whole party to another map.
- **Keys**: an item that opens a door.
- **The Fist and Kick shadows**, and their weapons.
- **Guilds and castle gameplay.** There are no guilds in the game yet; castle gameplay is deferred.
- **Buildings for the cities and towns.** All fourteen biomes are in the map generator, each on its own ground tiles, but
  no world map uses the nine new ones yet, no creature belongs to them yet, and props exist for nine of the fourteen.
  What each biome looks like is [Biomes — Ground Tiles and Props](biomes.md).

## Related

- [Mob Roster — Normal, Elite, Shadow](mob-roster.md) — every creature, its kind and its level.
- [Elements, Element Levels & Converters](../2-systems/gameplay-system-v4/elements-and-converters.md) — the chart
  and what a player can attack with today.
- [Story & Setting (Canon)](story-setting-canon.md) — the last line, the Fourteen and their epithets.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — each weapon's
  identity and its family, which decide its city or town.
- [Biomes — Ground Tiles and Props](biomes.md) — each biome's ground tiles and props, and the props still to make.
- [Biome Mob Families](biome-mob-families.md) — how a spawn area picks its creatures by biome and level, and the biome
  list the game has today.
