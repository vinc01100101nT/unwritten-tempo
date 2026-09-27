---
status: "Built 2026-09-26 — the sandbox layout: every creature has one home biome, one element and one level, and each map names the creatures that live there. Not yet walked in play."
owner: vince
last-updated: 2026-09-26
tags:
  - world-content
---

# Creatures and Where They Live

**The world is a sandbox, not a ladder** (decided with the user on 2026-09-26). A creature is not tied to one map: it
lives wherever it makes sense for it to live, from one end of the world to the other, the way a Poring turns up in
field after field. A map can hold weak and strong creatures side by side, of several elements, as long as each belongs
there.

## The two rules

- **A creature's properties are its own.** Its **home biome**, **element**, **level** and size are written on the
  creature and nowhere else. A map never changes them: the same creature is the same level and element in every map it
  appears in.
- **A map names the creatures that live in it.** Each map's rows in the world spawn list (`Resources/WorldSpawns`)
  name its creatures one by one and how many of each it keeps alive; the total per map is unchanged (about 31 in a
  field, 26 on a dungeon floor).

## How the layout was worked out

1. **Each map's identity** — its biome and its place (a meadow, a mine, a lava tube, a drowned chapel) — and the level
   its creatures mostly sit at, chosen for the map when it was laid out (a dungeon's floors go deeper floor by floor).
2. **Each creature's nature**, read off its picture: a blue slime, a mole, a flaming skull, a shark. That decides its
   **home biome** and the other biomes it plausibly lives in.
3. **Its element** fits its home biome — the biome's main element or one of its "also found" elements (the
   [World Map](world-map.md)'s biome table) — and stays within the level tiers: levels 1–33 only Neutral, Earth and
   Water; 34–66 add Wind and Fire; 67–99 add Poison, Holy, Dark, Ghost and Undead.
4. **Its level** places it where its kind belongs: critters low, large beasts higher (size nudges it up a little).
5. **A map takes** the creatures that live in its biome **near its level** (within 6 levels of its range) as its
   residents, up to five, plus up to two **visitors** from further along the ladder — one weaker, and, from level 9 up,
   one up to 15 levels stronger. A visitor keeps a quarter of a resident's share. No map starts below level 9 with a
   stronger visitor, so the fields beside Lastline stay safe.
6. **Elites** have a home biome, element and level too. Each stands in the fields of its home biome within 12 levels
   of its own; a dungeon's bottom floor holds the elite named for it. **An elite summons the normals of its own home
   biome** nearest its level at or below it, and only reaches outside its biome when its biome has too few.
7. **Drops are unchanged**: what a creature drops still follows its own level (the material grade bands in the
   [Mob Roster](mob-roster.md)), so grade-C quench material still comes only from creatures of levels 57–70, wherever
   they live.

## The normals

| Creature | Level | Element | Home biome | Also lives in | Maps |
| --- | --- | --- | --- | --- | --- |
| Slime | 1 | Neutral | Grassland | Forest, Highland, Coast, Steppe | 3 |
| Butterfly | 2 | Neutral | Grassland | Forest, Steppe, Sacred | 3 |
| Mouse | 3 | Earth | Grassland | Highland, Steppe, Desert | 6 |
| Larva2 | 4 | Neutral | Forest | Grassland | 4 |
| Mushroom | 5 | Earth | Forest | Swamp | 1 |
| Snake | 6 | Earth | Grassland | Steppe, Forest | 5 |
| YellowsBat | 8 | Neutral | Highland | Forest | 5 |
| Mollusc2 | 10 | Earth | Forest | Swamp, Coast | 5 |
| SpiderYellow | 11 | Earth | Highland | Forest, Desert | 6 |
| Owl | 13 | Neutral | Forest | Steppe, Snow | 5 |
| Racoon | 14 | Neutral | Forest | Grassland | 6 |
| KappaGreen | 15 | Water | Coast | Swamp | 2 |
| Bamboo | 16 | Earth | Forest | Peaks | 3 |
| Fish | 17 | Water | Coast | Swamp | 2 |
| Mollusc | 18 | Water | Coast | — | 2 |
| SpiderRed | 19 | Neutral | Highland | Haunted, Forest | 9 |
| Axolot | 20 | Water | Coast | Swamp | 2 |
| Panda | 21 | Neutral | Forest | Peaks | 4 |
| AxolotBlue | 23 | Water | Snow | Coast | 4 |
| Mole2 | 25 | Earth | Snow | Highland | 7 |
| GoldRacoon | 28 | Neutral | Grassland | Steppe | 5 |
| Bear | 31 | Earth | Snow | Forest, Highland | 8 |
| Dragon | 34 | Earth | Grassland | Forest, Peaks | 4 |
| Beast2 | 34 | Earth | Highland | Snow | 5 |
| Mole | 36 | Neutral | Highland | Desert | 2 |
| Lizard | 37 | Earth | Highland | Desert, Steppe | 4 |
| BambooYellow | 38 | Earth | Forest | Steppe | 5 |
| Slime3 | 39 | Water | Snow | Coast, Mist | 5 |
| Cyclope2 | 41 | Earth | Highland | Forest | 3 |
| Snake2 | 42 | Wind | Steppe | Desert | 5 |
| Snake3 | 43 | Earth | Forest | Swamp | 1 |
| Owl2 | 45 | Wind | Steppe | Peaks | 5 |
| DragonYellow | 46 | Fire | Desert | Volcanic | 3 |
| TRex | 48 | Earth | Steppe | Desert | 6 |
| Lizard2 | 50 | Fire | Desert | Volcanic | 5 |
| Spirit2 | 54 | Fire | Desert | Volcanic | 6 |
| Snake4 | 57 | Earth | Desert | Volcanic | 6 |
| Eye2 | 58 | Fire | Volcanic | Desert | 8 |
| Reptile | 59 | Water | Peaks | Highland | 2 |
| KappaRed | 60 | Water | Coast | Swamp | 4 |
| FishRed | 62 | Wind | Coast | Peaks | 5 |
| Octopus | 63 | Water | Coast | — | 3 |
| Eye | 64 | Wind | Peaks | Mist, Sacred | 4 |
| Grey Trex | 65 | Water | Coast | — | 3 |
| Flam | 67 | Fire | Volcanic | — | 6 |
| HeartGreen | 68 | Poison | Swamp | Forest | 3 |
| Cyclope | 69 | Fire | Volcanic | Desert | 9 |
| LanternRed | 71 | Undead | Haunted | Volcanic | 10 |
| Slime4 | 72 | Poison | Swamp | Forest, Corrupted | 4 |
| Beast | 73 | Fire | Volcanic | Corrupted | 12 |
| BlueBat | 74 | Dark | Haunted | Highland, Mist | 4 |
| Skull | 76 | Undead | Haunted | Volcanic | 8 |
| GreenOctopus | 77 | Poison | Swamp | Coast | 6 |
| RedOctopus | 78 | Fire | Volcanic | Coast | 5 |
| Octopus2 | 79 | Water | Haunted | Coast, Swamp | 10 |
| Reptile2 | 80 | Poison | Swamp | — | 5 |
| Spirit | 85 | Holy | Sacred | Peaks | 2 |
| ButterflyBlue | 87 | Holy | Sacred | Peaks | 2 |
| LanternGreen | 88 | Undead | Haunted | Swamp | 11 |
| Larva | 90 | Poison | Corrupted | Swamp | 8 |
| SkullBlue | 91 | Ghost | Mist | Haunted, Snow | 6 |
| MouseBlack | 92 | Dark | Corrupted | Haunted | 11 |
| Mushroom2 | 93 | Ghost | Mist | Sacred | 3 |
| Slime2 | 94 | Dark | Corrupted | Swamp | 8 |
| Flam2 | 95 | Ghost | Mist | Haunted | 5 |
| HeartRed | 96 | Dark | Corrupted | Haunted | 9 |

## The elites

| Elite | Level | Element | Home biome | Stands in |
| --- | --- | --- | --- | --- |
| GiantSlime | 8 | Neutral | Grassland | Lastline Field 1, Parley Field 1, Lastline Field 4 |
| DemonCyclop | 14 | Earth | Highland | Old Oak Dungeon 2, Gemmara Field 1 |
| GiantBamboo2 | 15 | Earth | Forest | Old Oak Field 1, Lastline Field 2, Harrowen Field 1, Gemmara Field 2, Harrowen Field 2 |
| GiantRacoon | 22 | Neutral | Grassland | Lastline Field 3, Parley Field 2, Lastline Field 4 |
| SquidGreen | 24 | Water | Coast | Pluvenne Field 1, Lastline Field 5 |
| GiantBlueSamurai | 33 | Water | Snow | Hilt Field 2, Hilt Field 1 |
| DemonCyclop2 | 35 | Earth | Highland | Hilt Dungeon 3 |
| GiantRacoonGold | 44 | Neutral | Steppe | Velmora Field 1, Tornel Field 1, Pennaryl Field 2 |
| GiantBamboo | 47 | Earth | Forest | Pennaryl Field 1 |
| TenguBlue | 58 | Wind | Peaks | Brimstone Field 2, Velmora Field 3 |
| GiantRedSamurai | 60 | Fire | Desert | Velmora Field 2, Velmora Dungeon 3 |
| GiantFrog2 | 66 | Water | Coast | Luminhal Field 1, Lastline Dungeon 2 |
| GiantFrog | 78 | Poison | Swamp | Kharnel Field 3, Kharnel Field 4, Kharnel Field 9, Kharnel Dungeon 6 |
| GiantFlam | 80 | Fire | Volcanic | Cindrel Field 1, Cindrel Field 2, Cindrel Dungeon 3 |
| GiantSpirit | 90 | Ghost | Mist | Kharnel Field 6 |
| TenguRed | 92 | Holy | Sacred | Halcyra Field 1, Halcyra Field 2 |
| GiantSlime2 | 95 | Dark | Corrupted | Kharnel Field 5, Kharnel Field 7, Kharnel Field 10, Kharnel Dungeon 9 |
| SquidRed | 99 | Undead | Haunted | Kharnel Dungeon 4 |

## Every map

Levels is the range the map was laid out for; creatures are listed with their own levels, residents first, then visitors.

| Map | Biome | Levels | Creatures | Elite |
| --- | --- | --- | --- | --- |
| Lastline Field 1 (`lst_fild01`) | Grassland | 1–3 | Slime 1, Butterfly 2, Mouse 3, Larva2 4, Snake 6 | GiantSlime |
| Parley Field 1 (`prl_fild01`) | Grassland | 4–6 | Larva2 4, Snake 6, Mouse 3, Butterfly 2, Slime 1 | GiantSlime |
| Old Oak Field 1 (`olk_fild01`) | Forest | 6–8 | Snake 6, YellowsBat 8, Mushroom 5, Larva2 4, Mollusc2 10, Butterfly 2 | GiantBamboo2 |
| Old Oak Dungeon 1 (`olk_dun01`) | Highland | 8–11 | YellowsBat 8, SpiderYellow 11, Mouse 3, Slime 1 | — |
| Old Oak Dungeon 2 (`olk_dun02`) | Highland | 12–15 | SpiderYellow 11, YellowsBat 8, SpiderRed 19, Mouse 3, Mole2 25 | DemonCyclop |
| Lastline Field 2 (`lst_fild02`) | Forest | 9–12 | Mollusc2 10, SpiderYellow 11, YellowsBat 8, Owl 13, Racoon 14, Snake 6, Bamboo 16 | GiantBamboo2 |
| Gemmara Field 1 (`gmr_fild01`) | Highland | 13–16 | SpiderYellow 11, SpiderRed 19, YellowsBat 8, Mouse 3, Mole2 25 | DemonCyclop |
| Pluvenne Field 1 (`plv_fild01`) | Coast | 17–20 | Fish 17, Mollusc 18, Axolot 20, KappaGreen 15, AxolotBlue 23, Mollusc2 10 | SquidGreen |
| Lastline Field 3 (`lst_fild03`) | Grassland | 21–24 | GoldRacoon 28, Racoon 14, Dragon 34 | GiantRacoon |
| Hilt Field 2 (`hlt_fild02`) | Snow | 25–28 | Mole2 25, AxolotBlue 23, Bear 31, Beast2 34, Owl 13, Slime3 39 | GiantBlueSamurai |
| Hilt Field 1 (`hlt_fild01`) | Snow | 29–33 | Bear 31, Beast2 34, Mole2 25, AxolotBlue 23, Slime3 39, Owl 13 | GiantBlueSamurai |
| Hilt Dungeon 1 (`hlt_dun01`) | Highland | 24–27 | Mole2 25, Bear 31, SpiderRed 19, SpiderYellow 11, Beast2 34 | — |
| Hilt Dungeon 2 (`hlt_dun02`) | Highland | 28–31 | Bear 31, Mole2 25, Beast2 34, Mole 36, Lizard 37, SpiderRed 19, Cyclope2 41 | — |
| Hilt Dungeon 3 (`hlt_dun03`) | Highland | 32–36 | Beast2 34, Mole 36, Bear 31, Lizard 37, Cyclope2 41, Mole2 25 | DemonCyclop2 |
| Harrowen Field 1 (`hrw_fild01`) | Forest | 26–30 | Bear 31, Dragon 34, Panda 21, SpiderRed 19, BambooYellow 38 | GiantBamboo2 |
| Parley Field 2 (`prl_fild02`) | Grassland | 30–34 | Dragon 34, GoldRacoon 28, Racoon 14 | GiantRacoon |
| Pennaryl Field 1 (`pnr_fild01`) | Forest | 34–38 | Dragon 34, BambooYellow 38, Bear 31, Cyclope2 41, Snake3 43, Panda 21 | GiantBamboo |
| Velmora Field 1 (`vlm_fild01`) | Steppe | 39–44 | Snake2 42, BambooYellow 38, Owl2 45, Lizard 37, TRex 48, GoldRacoon 28 | GiantRacoonGold |
| Tornel Field 1 (`trn_fild01`) | Steppe | 45–49 | Owl2 45, TRex 48, Snake2 42, BambooYellow 38 | GiantRacoonGold |
| Velmora Field 2 (`vlm_fild02`) | Desert | 50–54 | Lizard2 50, Spirit2 54, TRex 48, Snake4 57, DragonYellow 46, Snake2 42, Eye2 58 | GiantRedSamurai |
| Brimstone Field 2 (`brm_fild02`) | Peaks | 55–60 | Reptile 59, FishRed 62, Eye 64, Owl2 45 | TenguBlue |
| Velmora Field 3 (`vlm_fild03`) | Peaks | 61–66 | FishRed 62, Eye 64, Reptile 59, Owl2 45 | TenguBlue |
| Velmora Dungeon 1 (`vlm_dun01`) | Desert | 50–53 | Lizard2 50, Spirit2 54, TRex 48, DragonYellow 46, Snake4 57, Snake2 42, Eye2 58 | — |
| Velmora Dungeon 2 (`vlm_dun02`) | Desert | 54–57 | Spirit2 54, Snake4 57, Eye2 58, Lizard2 50, TRex 48, DragonYellow 46, Cyclope 69 | — |
| Velmora Dungeon 3 (`vlm_dun03`) | Desert | 58–62 | Eye2 58, Snake4 57, Spirit2 54, Lizard2 50, Cyclope 69 | GiantRedSamurai |
| Brimstone Field 1 (`brm_fild01`) | Volcanic | 60–64 | Eye2 58, Snake4 57, Flam 67, Cyclope 69, Spirit2 54, Lizard2 50, LanternRed 71 | — |
| Cindrel Field 1 (`cdl_fild01`) | Volcanic | 67–71 | Flam 67, Cyclope 69, LanternRed 71, Beast 73, Skull 76, Eye2 58, RedOctopus 78 | GiantFlam |
| Cindrel Field 2 (`cdl_fild02`) | Volcanic | 72–76 | Beast 73, Skull 76, LanternRed 71, RedOctopus 78, Cyclope 69, Flam 67 | GiantFlam |
| Cindrel Dungeon 1 (`cdl_dun01`) | Volcanic | 70–72 | LanternRed 71, Cyclope 69, Beast 73, Flam 67, Skull 76, Eye2 58, RedOctopus 78 | — |
| Cindrel Dungeon 2 (`cdl_dun02`) | Volcanic | 73–76 | Beast 73, Skull 76, LanternRed 71, RedOctopus 78, Cyclope 69, Flam 67 | — |
| Cindrel Dungeon 3 (`cdl_dun03`) | Volcanic | 77–80 | RedOctopus 78, Skull 76, Beast 73, LanternRed 71, Cyclope 69 | GiantFlam |
| Luminhal Field 1 (`lmh_fild01`) | Coast | 64–68 | Grey Trex 65, Octopus 63, FishRed 62, KappaRed 60, Slime3 39, GreenOctopus 77 | GiantFrog2 |
| Kharnel Field 2 (`khn_fild02`) | Haunted | 67–71 | LanternRed 71, BlueBat 74, Skull 76, SpiderRed 19, Octopus2 79 | — |
| Kharnel Field 1 (`khn_fild01`) | Haunted | 72–76 | BlueBat 74, Skull 76, LanternRed 71, Octopus2 79, SpiderRed 19, LanternGreen 88 | — |
| Kharnel Dungeon 1 (`khn_dun01`) | Haunted | 86–88 | LanternGreen 88, SkullBlue 91, MouseBlack 92, Octopus2 79, Flam2 95 | — |
| Kharnel Dungeon 2 (`khn_dun02`) | Haunted | 89–92 | SkullBlue 91, MouseBlack 92, LanternGreen 88, Flam2 95, HeartRed 96, Octopus2 79 | — |
| Kharnel Dungeon 3 (`khn_dun03`) | Haunted | 93–95 | Flam2 95, MouseBlack 92, HeartRed 96, SkullBlue 91, LanternGreen 88, Octopus2 79 | — |
| Kharnel Dungeon 4 (`khn_dun04`) | Haunted | 96–99 | HeartRed 96, Flam2 95, MouseBlack 92, SkullBlue 91, LanternGreen 88 | SquidRed |
| Kharnel Field 3 (`khn_fild03`) | Swamp | 77–81 | GreenOctopus 77, Octopus2 79, Reptile2 80, Slime4 72, HeartGreen 68, LanternGreen 88 | GiantFrog |
| Kharnel Field 4 (`khn_fild04`) | Swamp | 82–86 | Reptile2 80, LanternGreen 88, Octopus2 79, Larva 90, GreenOctopus 77, Slime4 72, Slime2 94 | GiantFrog |
| Kharnel Field 5 (`khn_fild05`) | Corrupted | 87–91 | Larva 90, MouseBlack 92, Slime2 94, HeartRed 96, Beast 73 | GiantSlime2 |
| Kharnel Field 7 (`khn_fild07`) | Corrupted | 96–99 | HeartRed 96, Slime2 94, MouseBlack 92, Larva 90, Beast 73 | GiantSlime2 |
| Kharnel Field 6 (`khn_fild06`) | Mist | 92–95 | Mushroom2 93, Flam2 95, SkullBlue 91, BlueBat 74 | GiantSpirit |
| Halcyra Field 1 (`hcr_fild01`) | Sacred | 83–90 | Spirit 85, ButterflyBlue 87, Mushroom2 93, Eye 64 | TenguRed |
| Halcyra Field 2 (`hcr_fild02`) | Sacred | 91–99 | Mushroom2 93, ButterflyBlue 87, Spirit 85, Eye 64 | TenguRed |
| Gemmara Field 2 (`gmr_fild02`) | Forest | 14–18 | Racoon 14, Bamboo 16, Owl 13, SpiderRed 19, SpiderYellow 11, Mollusc2 10, Panda 21 | GiantBamboo2 |
| Lastline Field 4 (`lst_fild04`) | Grassland | 10–14 | Racoon 14, Snake 6, Larva2 4, Mouse 3, GoldRacoon 28 | GiantSlime, GiantRacoon |
| Harrowen Field 2 (`hrw_fild02`) | Forest | 20–24 | Panda 21, SpiderRed 19, Bamboo 16, Racoon 14, Owl 13, Bear 31 | GiantBamboo2 |
| Lastline Field 5 (`lst_fild05`) | Coast | 16–20 | Fish 17, Mollusc 18, Axolot 20, KappaGreen 15, AxolotBlue 23, Mollusc2 10 | SquidGreen |
| Lastline Dungeon 1 (`lst_dun01`) | Coast | 58–61 | KappaRed 60, FishRed 62, Octopus 63, Grey Trex 65, Slime3 39 | — |
| Lastline Dungeon 2 (`lst_dun02`) | Coast | 62–66 | FishRed 62, Octopus 63, Grey Trex 65, KappaRed 60, Slime3 39, GreenOctopus 77 | GiantFrog2 |
| Pennaryl Field 2 (`pnr_fild02`) | Steppe | 36–40 | Lizard 37, BambooYellow 38, Snake2 42, Owl2 45, GoldRacoon 28, TRex 48 | GiantRacoonGold |
| Brimstone Field 3 (`brm_fild03`) | Volcanic | 62–66 | Flam 67, Cyclope 69, Eye2 58, Snake4 57, LanternRed 71, Spirit2 54, Beast 73 | — |
| Kharnel Field 8 (`khn_fild08`) | Haunted | 80–85 | Octopus2 79, LanternGreen 88, Skull 76, BlueBat 74, SkullBlue 91, LanternRed 71, MouseBlack 92 | — |
| Kharnel Field 9 (`khn_fild09`) | Swamp | 88–92 | LanternGreen 88, Larva 90, Slime2 94, Reptile2 80 | GiantFrog |
| Kharnel Dungeon 5 (`khn_dun05`) | Swamp | 70–74 | Slime4 72, HeartGreen 68, GreenOctopus 77, Octopus2 79, Reptile2 80, KappaRed 60, LanternGreen 88 | — |
| Kharnel Dungeon 6 (`khn_dun06`) | Swamp | 75–79 | GreenOctopus 77, Octopus2 79, Reptile2 80, Slime4 72, HeartGreen 68, LanternGreen 88 | GiantFrog |
| Kharnel Field 10 (`khn_fild10`) | Corrupted | 84–88 | Larva 90, MouseBlack 92, Slime2 94, Beast 73, HeartRed 96 | GiantSlime2 |
| Kharnel Dungeon 7 (`khn_dun07`) | Corrupted | 86–90 | Larva 90, MouseBlack 92, Slime2 94, HeartRed 96, Beast 73 | — |
| Kharnel Dungeon 8 (`khn_dun08`) | Corrupted | 91–95 | MouseBlack 92, Slime2 94, Larva 90, HeartRed 96, Beast 73 | — |
| Kharnel Dungeon 9 (`khn_dun09`) | Corrupted | 96–99 | HeartRed 96, Slime2 94, MouseBlack 92, Larva 90, Beast 73 | GiantSlime2 |

## What this does not do yet

- **Every map holds 3 to 7 kinds and 2 to 4 elements, and the biome's main element is the most common (or tied) in 51
  of the 61.** The roster has 66 normals for 61 maps across 14 biomes, and Peaks and Sacred have only two natives
  each; creatures made for the thinner biomes are what closes the rest.
- **The shadows still summon by rank**: the highest shadow calls the highest elite, whatever either one is (the
  Blossom Wind shadow calls the drowned squid of Kharnel Dungeon 4).
- **Experience and gold were not changed** when creatures moved level, so a creature moved from level 40 to 63 still
  pays what it paid at 40.

## Related

- [Mob Roster — Normal, Elite, Shadow](mob-roster.md) — the three kinds, summoning and drops.
- [World Map](world-map.md) — the regions, the biome table and every map.
- [Story & Setting (Canon)](story-setting-canon.md) — what a creature *is* in the fiction.
