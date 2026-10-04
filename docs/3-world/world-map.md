---
status: "PROPOSAL, partly built. Agreed with the user on 2026-09-25: three regions, one per weapon family — Midlaine (Might), Lirenne (Finesse) and The Far Shores (Arcane) — whose creatures mostly sit at levels 1–33, 34–66 and 67–99; since 2026-09-26 the whole world is a sandbox with no linear progression, committed game-wide; a big city for each one-family weapon and a small town for each two-family weapon, on the border between its two regions; Lastline in Might; the map in use for now is the painted world-map picture in the project (it can be replaced); the 75 maps of the three regions are generated and wired (2026-09-26, not yet walked in play), with the hand-made lastline as Lastline; every city, town and special place is named, and fields and dungeons are named after their town (2026-09-26); eight dungeons of 2 to 4 floors with an elite on each bottom floor; elites live everywhere, like normal creatures; the fourteen shadows' elements below; every shadow waits at its own place, a special map away from where players level, entered by quest, key, guide, world clock or a hidden way. The game uses no real-country culture. NOT settled: everything listed under Still open."
owner: vince
last-updated: 2026-09-26
tags:
  - world-content
---

# The World Map — Three Regions

A full redesign of the world. **Only `lastline` survives**, as the town every new character starts in. Every other
old map was deleted on 2026-09-26.

## The shape: a sandbox in three regions

**The world is a sandbox, not a ladder** — committed game-wide on 2026-09-26. No region, road or map is a step a
player must take in order: from the first day they can go anywhere a door leads. Every map holds creatures of more than
one level and element, each living where it makes sense for it to live — a slime in field after field across the
world, a drowned squid only in the deepest dark. A map is known for the level most of its creatures sit at, never gated
by it. Two things still follow a creature's own level, wherever it lives: its element tier (below) and what it drops
(the material grade, [Mob Roster](mob-roster.md)). Every creature and every map's creatures are in
[Creatures and Where They Live](biome-mob-families.md).

The world has **three regions, one per weapon family**. Each region's creatures mostly sit in one stretch of levels,
so a region has a feel, not a lock.

| # | Region | Family | Its creatures mostly | Land | Biomes | Big cities |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | **Midlaine** | Might | 1–33 | the main continent, and the forest half of the north island | Grassland, Forest, Highland, Coast, Snow | Old Oak, Hilt, Gemmara, Pluvenne |
| 2 | **Lirenne** | Finesse | 34–66 | the west continent | Forest, Steppe, Desert, Peaks | Pennaryl, Velmora |
| 3 | **The Far Shores** | Arcane | 67–99 | overseas: the north volcano, the floating island above the clouds in the far north-west, and the dark south (the south-west peninsula, the swamp lagoon, the south-east island) | Volcanic, Mist, Haunted, Corrupted, Sacred, Swamp | Cindrel, Halcyra |

- **Lastline** (`lastline`) sits in the middle of the main continent, on the last line the artist wrote, inside Midlaine.
  It belongs to no one. The fields beside it hold only weak creatures (no stronger visitor below level 9), so a new
  character's first steps are safe; nothing stops them walking further.
- **The elements get harder with each tier, by the creature's level.** A creature of levels 1–33 is only Neutral,
  Earth or Water, which today's converters answer; 34–66 adds Wind and Fire; 67–99 adds Ghost, Undead, Dark, Holy
  and Poison, the elements whose counter (skill buffs and cards) is still to come.
- **No real-country culture.** No city is a real country and no name comes from a real country's language. How place
  names are made is under Cities and towns.

**The map in use for now** is a GPT painting (1536×1024); it can be replaced. The zones are laid out on it in the
world creator, and the in-game world map (M, then World map) shows that tool's PNG export exactly as exported — the painting with
every map's box, its name and the lines of every door between maps on it — kept in the project as `unity/Assets/3_Data/Resources/WorldMap/world-map.png`.
The clean painting and the layout sit beside the tool: `tools/world-creator/worlds/wm1.png` and `world-regions.json`.

## Cities and towns

- **A big city for each one-family weapon** (Might, Finesse or Arcane only): eight cities.
- **A small town for each two-family weapon**, standing on the border between its two families' regions: six towns. You walk
  between the two regions the weapon mixes.
- **Each one lives where its weapon belongs**: the bow with the hunters in the woods, the greatsword on a snowy
  summit, and so on.

| Name | Code | Weapon | Kind | Family | Where on the map | Biome | Why it lives there |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Old Oak | `olk` | Shield | City | Might | the open grasslands north-west of Lastline | Grassland | a walled city guarding the starting plains |
| Hilt | `hlt` | Greatsword | City | Might | the snowy peaks of the north-east highlands | Snow | a huge sword standing on a snowy summit |
| Gemmara | `gmr` | Warhammer | City | Might | the rocky foot of those highlands, among the pines | Highland | a mining and forge city; hammers break stone |
| Pluvenne | `plv` | Spear | City | Might | the east sea cliffs | Coast | harpoons and fishing spears by the sea |
| Pennaryl | `pnr` | Bow | City | Finesse | the green woodland on the west continent's north-east | Forest | hunters live in the woods |
| Velmora | `vlm` | Dagger | City | Finesse | the sandy south of the west continent's golden plains | Desert | a desert city of narrow alleys where assassins hide |
| Cindrel | `cdl` | Staff | City | Arcane | the north volcano | Volcanic | the one who burned the field, living at the fire |
| Halcyra | `hcr` | Wand | City | Arcane | the sacred peak on a **floating island above the clouds**, in the far north-west | Sacred | healers on the holy mountain, above the world |
| Parley | `prl` | Sword | Town | Might–Finesse | the green east side of the land where the continents join | Grassland | a duellists' crossroads where fighters from both lands meet |
| Harrowen | `hrw` | Axe | Town | Might–Finesse | the wooded south side of the join | Forest | woodcutters; the axe's first job |
| Tornel | `trn` | Kick | Town | Might–Finesse | the west side of the join, where the steppe starts | Steppe | open plains to run and train on |
| Brimstone | `brm` | Firearm | Town | Finesse–Arcane | the west continent's south, beside its burning vent | Volcanic | gunpowder from the sulphur of the burning ground |
| Luminhal | `lmh` | Fist | Town | Might–Arcane | the rocky island group off the east coast | Coast | a monastery on sea stacks, training among the waves |
| Kharnel | `khn` | Scythe | Town | Might–Arcane | the dark south-west peninsula | Haunted | reapers among the graves |

**How places are named** (locked 2026-09-26): about a third are real English words or phrases, from one syllable
(Hilt) up to three words (The Far Shores), and the rest are made-up names that sound like Ragnarok places; each ties to
its shadow's style, its weapon and its setting. Each city and town has a three-letter code. A **field** is named after
the town you walk to it from and numbered — Lastline Field 1, `lst_fild01`. A **dungeon** whose entrance is in one of a
town's fields takes that town's code and a floor number — Old Oak Dungeon 1, `olk_dun01`; only a dungeon with no town
near it would get a name of its own, and today there is none. Lastline keeps `lastline`, and a special place's id is its
name (`aurora_steps`). The world map (M, then World map) shows these names.

**The Sacred biome looks like a place up among the clouds** (rebuilt 2026-09-26, not yet seen in play): patchy white
clouds float over every Sacred map at treetop height and drift slowly across it, with the ground showing through the
gaps, a clear patch around you, and some fog.

**Around the towns** the creatures mostly sit at about 28–40 on the Midlaine–Lirenne border and about 60–70 on the
borders of The Far Shores — a feel, never a gate.

## Dungeons

**Eight dungeons**, each of **2 to 4 floors**. Each floor goes a few levels deeper, and **an elite waits on each
bottom floor** of the first five; the three added on 2026-09-26 have none of their own yet.

| # | Dungeon | Floors | Entrance | Where | Levels |
| --- | --- | --- | --- | --- | --- |
| 1 | Old Oak Dungeon (`olk_dun01`–`02`) | 2 | Old Oak Field 1 | Midlaine, the old tunnels near Old Oak | 8–15 |
| 2 | Hilt Dungeon (`hlt_dun01`–`03`) | 3 | Hilt Field 2 | Midlaine, the mines of the north-east stone highlands | 24–36 |
| 3 | Velmora Dungeon (`vlm_dun01`–`03`) | 3 | Velmora Field 2 | Lirenne, a temple in the red rock mountains on the west edge | 50–62 |
| 4 | Cindrel Dungeon (`cdl_dun01`–`03`) | 3 | Cindrel Field 2 | The Far Shores, the lava tubes under the north volcano | 70–80 |
| 5 | Kharnel Dungeon (`khn_dun01`–`04`) | 4 | Kharnel Field 1 | The Far Shores, the haunted deep of the south-west peninsula | 86–99 |
| 6 | Lastline Dungeon (`lst_dun01`–`02`) | 2 | Lastline Field 5 | Midlaine, sea caves on the coast south-east of Lastline | 58–66 |
| 7 | Kharnel Dungeon (`khn_dun05`–`06`) | 2 | Kharnel Field 9 | The Far Shores, under the swamp lagoon | 70–79 |
| 8 | Kharnel Dungeon (`khn_dun07`–`09`) | 3 | Kharnel Field 7 | The Far Shores, the south-east island | 86–99 |

**What lives on a floor** is chosen like every map: the creatures that belong in its biome and place near its levels,
listed in [Creatures and Where They Live](biome-mob-families.md) — bats and moles in the mines, lava creatures in the
tubes, the drowned and the dead under the south-west peninsula.

**Elites live everywhere**, like normal creatures: in fields, in dungeons and on special maps.

## The shadows and their places

There are **fourteen shadows**, one per weapon. Each waits at **its own place, a special map away from where players
level**: a shadow needs a guild to bring down, and meeting one in a levelling field would only spoil the levelling.

| Weapon | Style | Element | The place | Way in |
| --- | --- | --- | --- | --- |
| Shield | **Still Oak** | Neutral | **The Iron Gate** — a gate the height of a mountain, cut into the mountain itself. The shadow stands in its gateway — the one who stood in front, and the reason it has never opened. | **Key**: the Iron Key, forged from high-value elite drops. |
| Wand | **Morning Dew** | Holy | **The Hill of the Flame** — a ring of standing stones round a fire that has never gone out. Everything that falls near the shadow gets up again. | **Quest**: a pilgrimage, carrying the flame between shrines. |
| Sword | **Blossom Wind** | Wind | **The Thousand Gates** — a path climbing through a thousand gates above the clouds, to a tree in blossom. | **Quest**: each gate is a trial. |
| Warhammer | **Stone Bloom** | Earth | **The Mountain's Root** — the cavern at the very bottom of the Iron Gate's mountain, where the stone never ends. The shadow stands on the rock the whole mountain rests on. | **Quest**: the bridge-keeper's chain opens **the Last Bridge** — one stone bridge over a bottomless gorge, seen from a level-1 field — which leads into the mountain from the side and down. |
| Dagger | **Night Orchid** | Poison | **The Masked Ballroom** — a ballroom in a sinking palace where every dancer wears a mask. The shadow is one of them, and arrives as the dance ends. | **Key**: the Mask, a rare and costly item. |
| Axe | **Forest Fire** | Fire | **The Burning Keel** — a longship stranded in a river of cooled lava, at the lip of a live volcano. | **Guide**: a lava-walker leads the party across. |
| Staff | **Ashen Field** | Fire | **The Everburning Plain** — a plain where the ground itself is on fire, around a fire temple. | **Guide**: the fire temple's keeper grants passage. |
| Bow | **Feather Rain** | Wind | **The Sky Cairn** — a lone stone cairn on an endless steppe. The shadow rides circles round it, always out of reach. | **Guide**: a steppe rider takes the party out. |
| Spear | **Silver Rain** | Water | **The Tide Temple** — a temple on a rock out at sea. | **World clock**: walkable only while the tide is out. |
| Firearm | **Dusk Crow** | Dark | **The Dusk Tower** — a bell tower above a city where the sun is always setting. The shadow fires once, from the top. | **World clock**: the door opens only at dusk. |
| Greatsword | **Silent Snow** | Ghost | **The Walking Road** — a road through fog that keeps going as long as you walk it, ending at a black castle. | **World clock**: the road appears only in fog. |
| Scythe | **Black Lily** | Undead | **The Drowned Chapel** — a chapel at the bottom of a black cove, whose bell still rings underwater. | **Hidden**: find the way down yourself. |
| Fist | **Northern Lights** | Neutral | **Aurora Steps** — a mountain monastery under the aurora. | **Quest**: the monastery's trials. |
| Kick | **Autumn Gale** | Wind | **Unmarked Path** | **Hidden**: a path nobody marks. |

- **All ten elements are covered**; Wind is held by three shadows, Fire and Neutral by two each.
- **Each shadow goes by its style**, never a personal name; the styles and how their forms are named are in the
  [Story & Setting](story-setting-canon.md) canon. The styles are locked (2026-09-26).

## Biomes and elements

Every creature map has a **biome**, and the biome sets which element **most** of its creatures carry. It is a
majority, never a lock: a snow field is mostly Water, but some of its creatures are Earth, Dark or Wind. Every one
of the ten elements is the majority somewhere.

| Biome | Most creatures | Also found | Exists today? |
| --- | --- | --- | --- |
| Grassland | Neutral | Earth, Wind, Poison | yes |
| Forest | Earth | Poison, Wind, Neutral | yes |
| Highland | Earth | Wind, Neutral | yes |
| Peaks | Wind | Holy, Water | yes |
| Steppe | Wind | Neutral, Earth | yes |
| Coast | Water | Wind, Undead | yes |
| Snow | Water | Wind, Earth, Dark | yes |
| Volcanic | Fire | Earth, Undead | yes |
| Desert | Fire | Earth, Poison, Undead | yes |
| Swamp | Poison | Water, Undead | yes |
| Sacred | Holy | Neutral, Wind, Ghost | yes |
| Haunted | Undead | Ghost, Dark, Water | yes |
| Mist | Ghost | Dark, Undead, Wind | yes |
| Corrupted | Dark | Undead, Ghost, Poison | yes |

**Each region uses its own biomes** (the region table above). A map's biome sets its creatures' majority element; a
shadow's element is its own and does not have to match the biome around its place.

## The castles

Castles are the seats guild wars will be fought over; how many there are, and where, is open.

- **Castle gameplay is deferred**: how a guild takes one, holds it, and what owning it gives. When it is designed, one
  rule already holds: **a castle must not give power**, for the same reason weapons carry no parallel progression.

## Getting around

- **Midlaine and Lirenne are joined by land**, where the two continents meet; the Might–Finesse towns stand there.
- **The Far Shores are across the sea.** The harbour towns (Luminhal, Kharnel and Brimstone) are the ways over. The Wand's floating
  island is reached from above the sea; how is open.
- **Teleports and other service townspeople are deferred.**

## Built (2026-09-26, not yet walked in play)

- **Every map of the three regions exists as a scene**: 8 cities, 6 towns, 39 fields and 22 dungeon floors, each
  generated in its own biome and joined by doors, from the layout `tools/world-creator/worlds/world-regions.json`
  (90 maps, 100 doors; the 14 shadow places in it are not generated). Sixteen of them — nine fields and seven dungeon
  floors — were added on 2026-09-26 to fill the empty land between the first ones.
- **Lastline is `lastline`, the hand-made town, kept as it is.** Nothing in it was removed. Its west gate leads to
  Lastline Field 1 (levels 1–3), its east gate to Lastline Field 3 (21–24), its south gate to Harrowen Field 1 (26–30)
  and its north gate to Lastline Field 2 (9–12).
- **Creatures in every field and dungeon floor — a sandbox (2026-09-26; shadows are not placed yet).** Every
  creature has one home biome, element and level of its own, and each map names the creatures that live in it — its
  residents near its levels and a weaker and a stronger visitor — so one creature can live from one end of the world
  to the other. Each field keeps about 31 alive and each dungeon floor 26, its dungeon's levels split across its floors
  (Old Oak 8–11 / 12–15; Hilt 24–27 / 28–31 / 32–36; Velmora 50–53 / 54–57 / 58–62; Cindrel 70–72 / 73–76 / 77–80;
  Kharnel 1–4: 86–88 / 89–92 / 93–95 / 96–99; Lastline 58–61 / 62–66; Kharnel 5–6: 70–74 / 75–79; Kharnel 7–9:
  86–90 / 91–95 / 96–99). Elites stand in the fields of their home biome
  near their level, and the first five dungeons' bottom floors each hold their own. Every creature, every map and the rules are in
  [Creatures and Where They Live](biome-mob-families.md); the fourteen shadows' elements are in the table above.
  Cities and towns have none.
- **The in-game world map (M, then World map)** shows the world creator's PNG export of the layout, as exported; the game adds nothing to it but the "you are here" marker.

## Still open

- **The cities' level bands.** A first split: Shield 1–8, Warhammer 9–16, Spear 17–24, Greatsword 25–33 (the snowy
  summit tops Midlaine, beside today's Snow creatures at 28–45), Bow 34–49, Dagger 50–66, Staff 67–82, Wand 83–99.
- **The Fist and Kick shadows' places**: where on the map Aurora Steps and the Unmarked Path lie.
- **Whether one key lets the whole party in** (and is used up), or every member needs their own. Proposed: one per
  party.
- **Other special maps**, ideas only: the crossing ships, a climbing tower, hidden places, night and weather places,
  a fortress, and a place for the final fight against the paranoia as the final map.
- **Kharnel holds 10 fields and 9 dungeon floors**, because the swamp lagoon and the south-east island have no town
  of their own and their maps take the nearest town's code. A town there would give them their own.
- **The Lastline sea caves are levels 58–66**, one field from the starting town. A sandbox allows it; whether a new
  character should find a dungeon that strong so close is open.
- **The three dungeons added on 2026-09-26 have no elite** on their bottom floor.
- **How many castles**, and where.
- **The shadow places are not built.** The layout holds them in a block below the map, each with one door from the map
  it is reached from; none was generated, and their ways in (quest, key, guide, world clock, hidden) need systems the
  game does not have yet. The layout has place maps for the Last Bridge and the Iron Gate but none for the
  Mountain's Root; it gets one when the shadow places are built.

## What this needs that the game does not have yet

- **A counter for Holy, Dark and Poison.** At a shadow's element level (3), nothing a player can attack with today
  does more than 100% to a Holy, Dark or Poison shadow. A Ghost shadow takes 0 from a plain attack, so a converter
  is required to hurt it. **The counter will come from skill buffs and cards**, both still to be built.
- **Quests.** The game has no quest system; four shadows' places are reached by quest.
- **A world clock that opens doors**: a tide, dusk and fog (three shadows' places). Not checked: whether the server
  knows the time of day and the weather.
- **Guides**: a townsperson who moves a whole party to another map.
- **Keys**: an item that opens a door.
- **The Fist and Kick weapons.** Their shadows exist; the weapons do not.
- **Guilds and castle gameplay.** There are no guilds in the game yet; castle gameplay is deferred.
- **Buildings for the cities and towns.** All fourteen biomes are in the map generator, each on its own ground tiles, and
  the new world's maps use all of them, and every biome has creatures of its own; props exist for nine of the
  fourteen.
  What each biome looks like is [Biomes — Ground Tiles and Props](biomes.md).

## Related

- [Mob Roster — Normal, Elite, Shadow](mob-roster.md) — every creature, its kind and its level.
- [Elements, Element Levels & Converters](../2-systems/gameplay-system-v4/elements-and-converters.md) — the chart
  and what a player can attack with today.
- [Story & Setting (Canon)](story-setting-canon.md) — the last line, the Fourteen and their epithets.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — each weapon's
  identity and its family, which decide its city or town.
- [Biomes — Ground Tiles and Props](biomes.md) — each biome's ground tiles and props, and the props still to make.
- [Creatures and Where They Live](biome-mob-families.md) — every creature's home biome, element and level, and every
  map's creatures.
