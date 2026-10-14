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
   of its own; a dungeon's bottom floor holds the elite named for it. **Once it has bought the summon skill, an elite summons the
   normals of its own home biome** nearest its level at or below it, and only reaches outside its biome when its biome has too few.
7. **Drops are unchanged**: what a creature drops still follows its own level (the seven level steps in the
   [Mob Roster](mob-roster.md)), so a creature of levels 57–70 still gives five steps' worth of Temper Dust and Quench Oil, wherever
   they live.

## The normals

| Creature | Level | Element | Home biome | Also lives in | Maps |
| --- | --- | --- | --- | --- | --- |
| Plopling | 1 | Neutral | Grassland | Forest, Highland, Coast, Steppe | 3 |
| Flutterby | 2 | Neutral | Grassland | Forest, Steppe, Sacred | 3 |
| Squeak | 3 | Earth | Grassland | Highland, Steppe, Desert | 6 |
| Crawlet | 4 | Neutral | Forest | Grassland | 4 |
| Capling | 5 | Earth | Forest | Swamp | 1 |
| Hisser | 6 | Earth | Grassland | Steppe, Forest | 5 |
| Blinkbat | 8 | Neutral | Highland | Forest | 5 |
| Mossnail | 10 | Earth | Forest | Swamp, Coast | 5 |
| Tarantix | 11 | Earth | Highland | Forest, Desert | 6 |
| Hootie | 13 | Neutral | Forest | Steppe, Snow | 5 |
| Rascal | 14 | Neutral | Forest | Grassland | 6 |
| Kappi | 15 | Water | Coast | Swamp | 2 |
| Shootling | 16 | Earth | Forest | Peaks | 3 |
| Gillie | 17 | Water | Coast | Swamp | 2 |
| Shellby | 18 | Water | Coast | — | 2 |
| Webber | 19 | Neutral | Highland | Haunted, Forest | 9 |
| Newtie | 20 | Water | Coast | Swamp | 2 |
| Pando | 21 | Neutral | Forest | Peaks | 4 |
| Mudpup | 23 | Water | Snow | Coast | 4 |
| Burrowbo | 25 | Earth | Snow | Highland | 7 |
| Pilfer | 28 | Neutral | Grassland | Steppe | 5 |
| Grumbear | 31 | Earth | Snow | Forest, Highland | 8 |
| Wyrmlet | 34 | Earth | Grassland | Forest, Peaks | 4 |
| Mossgore | 34 | Earth | Highland | Snow | 5 |
| Digger | 36 | Neutral | Highland | Desert | 2 |
| Scaly | 37 | Earth | Highland | Desert, Steppe | 4 |
| Canewick | 38 | Earth | Forest | Steppe | 5 |
| Dewdrop | 39 | Water | Snow | Coast, Mist | 5 |
| Peeper | 41 | Earth | Highland | Forest | 3 |
| Viper | 42 | Wind | Steppe | Desert | 5 |
| Coily | 43 | Earth | Forest | Swamp | 1 |
| Emberhoot | 45 | Wind | Steppe | Peaks | 5 |
| Emberwyrm | 46 | Fire | Desert | Volcanic | 3 |
| Chomper | 48 | Earth | Steppe | Desert | 6 |
| Sunscale | 50 | Fire | Desert | Volcanic | 5 |
| Willo | 54 | Fire | Desert | Volcanic | 6 |
| Adder | 57 | Earth | Desert | Volcanic | 6 |
| Bloodshot | 58 | Fire | Volcanic | Desert | 8 |
| Hornscale | 59 | Water | Peaks | Highland | 2 |
| Rivershell | 60 | Water | Coast | Swamp | 4 |
| Snapper | 62 | Wind | Coast | Peaks | 5 |
| Octi | 63 | Water | Coast | — | 3 |
| Iris | 64 | Wind | Peaks | Mist, Sacred | 4 |
| Gnasher | 65 | Water | Coast | — | 3 |
| Cinder | 67 | Fire | Volcanic | — | 6 |
| Bittersweet | 68 | Poison | Swamp | Forest | 3 |
| Cyclo | 69 | Fire | Volcanic | Desert | 9 |
| Emberlurk | 71 | Undead | Haunted | Volcanic | 10 |
| Moldy | 72 | Poison | Swamp | Forest, Corrupted | 4 |
| Horngore | 73 | Fire | Volcanic | Corrupted | 12 |
| Drizzle | 74 | Dark | Haunted | Highland, Mist | 4 |
| Blazeskull | 76 | Undead | Haunted | Volcanic | 8 |
| Inky | 77 | Poison | Swamp | Coast | 6 |
| Sucker | 78 | Fire | Volcanic | Coast | 5 |
| Squirt | 79 | Water | Haunted | Coast, Swamp | 10 |
| Swampfang | 80 | Poison | Swamp | — | 5 |
| Wisp | 85 | Holy | Sacred | Peaks | 2 |
| Skywing | 87 | Holy | Sacred | Peaks | 2 |
| Glowcoil | 88 | Undead | Haunted | Swamp | 11 |
| Grubble | 90 | Poison | Corrupted | Swamp | 8 |
| Frostskull | 91 | Ghost | Mist | Haunted, Snow | 6 |
| Nightsqueak | 92 | Dark | Corrupted | Haunted | 11 |
| Frostcap | 93 | Ghost | Mist | Sacred | 3 |
| Goopie | 94 | Dark | Corrupted | Swamp | 8 |
| Frostwick | 95 | Ghost | Mist | Haunted | 5 |
| Lovebite | 96 | Dark | Corrupted | Haunted | 9 |

## The elites

| Elite | Level | Element | Home biome | Stands in |
| --- | --- | --- | --- | --- |
| Gloop King | 8 | Neutral | Grassland | Lastline Field 1, Parley Field 1, Lastline Field 4 |
| Oculord | 14 | Earth | Highland | Old Oak Dungeon 2, Gemmara Field 1 |
| Autumn Stalk | 15 | Earth | Forest | Old Oak Field 1, Lastline Field 2, Harrowen Field 1, Gemmara Field 2, Harrowen Field 2 |
| Bandit Boss | 22 | Neutral | Grassland | Lastline Field 3, Parley Field 2, Lastline Field 4 |
| Deep Maw | 24 | Water | Coast | Pluvenne Field 1, Lastline Field 5 |
| Frost Ronin | 33 | Water | Snow | Hilt Field 2, Hilt Field 1 |
| Mossmaw | 35 | Earth | Highland | Hilt Dungeon 3 |
| Golden Bandit | 44 | Neutral | Steppe | Velmora Field 1, Tornel Field 1, Pennaryl Field 2 |
| Elder Stalk | 47 | Earth | Forest | Pennaryl Field 1 |
| Karasu | 58 | Wind | Peaks | Brimstone Field 2, Velmora Field 3 |
| Blood Ronin | 60 | Fire | Desert | Velmora Field 2, Velmora Dungeon 3 |
| Great Croak | 66 | Water | Coast | Luminhal Field 1, Lastline Dungeon 2 |
| Toadmaw | 78 | Poison | Swamp | Kharnel Field 3, Kharnel Field 4, Kharnel Field 9, Kharnel Dungeon 6 |
| Inferno Mask | 80 | Fire | Volcanic | Cindrel Field 1, Cindrel Field 2, Cindrel Dungeon 3 |
| Wraithmother | 90 | Ghost | Mist | Kharnel Field 6 |
| Oni Tengu | 92 | Holy | Sacred | Halcyra Field 1, Halcyra Field 2 |
| Venom Gloop | 95 | Dark | Corrupted | Kharnel Field 5, Kharnel Field 7, Kharnel Field 10, Kharnel Dungeon 9 |
| Abyss Maw | 99 | Undead | Haunted | Kharnel Dungeon 4 |

## Every map

Levels is the range the map was laid out for; creatures are listed with their own levels, residents first, then visitors.

| Map | Biome | Levels | Creatures | Elite |
| --- | --- | --- | --- | --- |
| Lastline Field 1 (`lst_fild01`) | Grassland | 1–3 | Plopling 1, Flutterby 2, Squeak 3, Crawlet 4, Hisser 6 | Gloop King |
| Parley Field 1 (`prl_fild01`) | Grassland | 4–6 | Crawlet 4, Hisser 6, Squeak 3, Flutterby 2, Plopling 1 | Gloop King |
| Old Oak Field 1 (`olk_fild01`) | Forest | 6–8 | Hisser 6, Blinkbat 8, Capling 5, Crawlet 4, Mossnail 10, Flutterby 2 | Autumn Stalk |
| Old Oak Dungeon 1 (`olk_dun01`) | Highland | 8–11 | Blinkbat 8, Tarantix 11, Squeak 3, Plopling 1 | — |
| Old Oak Dungeon 2 (`olk_dun02`) | Highland | 12–15 | Tarantix 11, Blinkbat 8, Webber 19, Squeak 3, Burrowbo 25 | Oculord |
| Lastline Field 2 (`lst_fild02`) | Forest | 9–12 | Mossnail 10, Tarantix 11, Blinkbat 8, Hootie 13, Rascal 14, Hisser 6, Shootling 16 | Autumn Stalk |
| Gemmara Field 1 (`gmr_fild01`) | Highland | 13–16 | Tarantix 11, Webber 19, Blinkbat 8, Squeak 3, Burrowbo 25 | Oculord |
| Pluvenne Field 1 (`plv_fild01`) | Coast | 17–20 | Gillie 17, Shellby 18, Newtie 20, Kappi 15, Mudpup 23, Mossnail 10 | Deep Maw |
| Lastline Field 3 (`lst_fild03`) | Grassland | 21–24 | Pilfer 28, Rascal 14, Wyrmlet 34 | Bandit Boss |
| Hilt Field 2 (`hlt_fild02`) | Snow | 25–28 | Burrowbo 25, Mudpup 23, Grumbear 31, Mossgore 34, Hootie 13, Dewdrop 39 | Frost Ronin |
| Hilt Field 1 (`hlt_fild01`) | Snow | 29–33 | Grumbear 31, Mossgore 34, Burrowbo 25, Mudpup 23, Dewdrop 39, Hootie 13 | Frost Ronin |
| Hilt Dungeon 1 (`hlt_dun01`) | Highland | 24–27 | Burrowbo 25, Grumbear 31, Webber 19, Tarantix 11, Mossgore 34 | — |
| Hilt Dungeon 2 (`hlt_dun02`) | Highland | 28–31 | Grumbear 31, Burrowbo 25, Mossgore 34, Digger 36, Scaly 37, Webber 19, Peeper 41 | — |
| Hilt Dungeon 3 (`hlt_dun03`) | Highland | 32–36 | Mossgore 34, Digger 36, Grumbear 31, Scaly 37, Peeper 41, Burrowbo 25 | Mossmaw |
| Harrowen Field 1 (`hrw_fild01`) | Forest | 26–30 | Grumbear 31, Wyrmlet 34, Pando 21, Webber 19, Canewick 38 | Autumn Stalk |
| Parley Field 2 (`prl_fild02`) | Grassland | 30–34 | Wyrmlet 34, Pilfer 28, Rascal 14 | Bandit Boss |
| Pennaryl Field 1 (`pnr_fild01`) | Forest | 34–38 | Wyrmlet 34, Canewick 38, Grumbear 31, Peeper 41, Coily 43, Pando 21 | Elder Stalk |
| Velmora Field 1 (`vlm_fild01`) | Steppe | 39–44 | Viper 42, Canewick 38, Emberhoot 45, Scaly 37, Chomper 48, Pilfer 28 | Golden Bandit |
| Tornel Field 1 (`trn_fild01`) | Steppe | 45–49 | Emberhoot 45, Chomper 48, Viper 42, Canewick 38 | Golden Bandit |
| Velmora Field 2 (`vlm_fild02`) | Desert | 50–54 | Sunscale 50, Willo 54, Chomper 48, Adder 57, Emberwyrm 46, Viper 42, Bloodshot 58 | Blood Ronin |
| Brimstone Field 2 (`brm_fild02`) | Peaks | 55–60 | Hornscale 59, Snapper 62, Iris 64, Emberhoot 45 | Karasu |
| Velmora Field 3 (`vlm_fild03`) | Peaks | 61–66 | Snapper 62, Iris 64, Hornscale 59, Emberhoot 45 | Karasu |
| Velmora Dungeon 1 (`vlm_dun01`) | Desert | 50–53 | Sunscale 50, Willo 54, Chomper 48, Emberwyrm 46, Adder 57, Viper 42, Bloodshot 58 | — |
| Velmora Dungeon 2 (`vlm_dun02`) | Desert | 54–57 | Willo 54, Adder 57, Bloodshot 58, Sunscale 50, Chomper 48, Emberwyrm 46, Cyclo 69 | — |
| Velmora Dungeon 3 (`vlm_dun03`) | Desert | 58–62 | Bloodshot 58, Adder 57, Willo 54, Sunscale 50, Cyclo 69 | Blood Ronin |
| Brimstone Field 1 (`brm_fild01`) | Volcanic | 60–64 | Bloodshot 58, Adder 57, Cinder 67, Cyclo 69, Willo 54, Sunscale 50, Emberlurk 71 | — |
| Cindrel Field 1 (`cdl_fild01`) | Volcanic | 67–71 | Cinder 67, Cyclo 69, Emberlurk 71, Horngore 73, Blazeskull 76, Bloodshot 58, Sucker 78 | Inferno Mask |
| Cindrel Field 2 (`cdl_fild02`) | Volcanic | 72–76 | Horngore 73, Blazeskull 76, Emberlurk 71, Sucker 78, Cyclo 69, Cinder 67 | Inferno Mask |
| Cindrel Dungeon 1 (`cdl_dun01`) | Volcanic | 70–72 | Emberlurk 71, Cyclo 69, Horngore 73, Cinder 67, Blazeskull 76, Bloodshot 58, Sucker 78 | — |
| Cindrel Dungeon 2 (`cdl_dun02`) | Volcanic | 73–76 | Horngore 73, Blazeskull 76, Emberlurk 71, Sucker 78, Cyclo 69, Cinder 67 | — |
| Cindrel Dungeon 3 (`cdl_dun03`) | Volcanic | 77–80 | Sucker 78, Blazeskull 76, Horngore 73, Emberlurk 71, Cyclo 69 | Inferno Mask |
| Luminhal Field 1 (`lmh_fild01`) | Coast | 64–68 | Gnasher 65, Octi 63, Snapper 62, Rivershell 60, Dewdrop 39, Inky 77 | Great Croak |
| Kharnel Field 2 (`khn_fild02`) | Haunted | 67–71 | Emberlurk 71, Drizzle 74, Blazeskull 76, Webber 19, Squirt 79 | — |
| Kharnel Field 1 (`khn_fild01`) | Haunted | 72–76 | Drizzle 74, Blazeskull 76, Emberlurk 71, Squirt 79, Webber 19, Glowcoil 88 | — |
| Kharnel Dungeon 1 (`khn_dun01`) | Haunted | 86–88 | Glowcoil 88, Frostskull 91, Nightsqueak 92, Squirt 79, Frostwick 95 | — |
| Kharnel Dungeon 2 (`khn_dun02`) | Haunted | 89–92 | Frostskull 91, Nightsqueak 92, Glowcoil 88, Frostwick 95, Lovebite 96, Squirt 79 | — |
| Kharnel Dungeon 3 (`khn_dun03`) | Haunted | 93–95 | Frostwick 95, Nightsqueak 92, Lovebite 96, Frostskull 91, Glowcoil 88, Squirt 79 | — |
| Kharnel Dungeon 4 (`khn_dun04`) | Haunted | 96–99 | Lovebite 96, Frostwick 95, Nightsqueak 92, Frostskull 91, Glowcoil 88 | Abyss Maw |
| Kharnel Field 3 (`khn_fild03`) | Swamp | 77–81 | Inky 77, Squirt 79, Swampfang 80, Moldy 72, Bittersweet 68, Glowcoil 88 | Toadmaw |
| Kharnel Field 4 (`khn_fild04`) | Swamp | 82–86 | Swampfang 80, Glowcoil 88, Squirt 79, Grubble 90, Inky 77, Moldy 72, Goopie 94 | Toadmaw |
| Kharnel Field 5 (`khn_fild05`) | Corrupted | 87–91 | Grubble 90, Nightsqueak 92, Goopie 94, Lovebite 96, Horngore 73 | Venom Gloop |
| Kharnel Field 7 (`khn_fild07`) | Corrupted | 96–99 | Lovebite 96, Goopie 94, Nightsqueak 92, Grubble 90, Horngore 73 | Venom Gloop |
| Kharnel Field 6 (`khn_fild06`) | Mist | 92–95 | Frostcap 93, Frostwick 95, Frostskull 91, Drizzle 74 | Wraithmother |
| Halcyra Field 1 (`hcr_fild01`) | Sacred | 83–90 | Wisp 85, Skywing 87, Frostcap 93, Iris 64 | Oni Tengu |
| Halcyra Field 2 (`hcr_fild02`) | Sacred | 91–99 | Frostcap 93, Skywing 87, Wisp 85, Iris 64 | Oni Tengu |
| Gemmara Field 2 (`gmr_fild02`) | Forest | 14–18 | Rascal 14, Shootling 16, Hootie 13, Webber 19, Tarantix 11, Mossnail 10, Pando 21 | Autumn Stalk |
| Lastline Field 4 (`lst_fild04`) | Grassland | 10–14 | Rascal 14, Hisser 6, Crawlet 4, Squeak 3, Pilfer 28 | Gloop King, Bandit Boss |
| Harrowen Field 2 (`hrw_fild02`) | Forest | 20–24 | Pando 21, Webber 19, Shootling 16, Rascal 14, Hootie 13, Grumbear 31 | Autumn Stalk |
| Lastline Field 5 (`lst_fild05`) | Coast | 16–20 | Gillie 17, Shellby 18, Newtie 20, Kappi 15, Mudpup 23, Mossnail 10 | Deep Maw |
| Lastline Dungeon 1 (`lst_dun01`) | Coast | 58–61 | Rivershell 60, Snapper 62, Octi 63, Gnasher 65, Dewdrop 39 | — |
| Lastline Dungeon 2 (`lst_dun02`) | Coast | 62–66 | Snapper 62, Octi 63, Gnasher 65, Rivershell 60, Dewdrop 39, Inky 77 | Great Croak |
| Pennaryl Field 2 (`pnr_fild02`) | Steppe | 36–40 | Scaly 37, Canewick 38, Viper 42, Emberhoot 45, Pilfer 28, Chomper 48 | Golden Bandit |
| Brimstone Field 3 (`brm_fild03`) | Volcanic | 62–66 | Cinder 67, Cyclo 69, Bloodshot 58, Adder 57, Emberlurk 71, Willo 54, Horngore 73 | — |
| Kharnel Field 8 (`khn_fild08`) | Haunted | 80–85 | Squirt 79, Glowcoil 88, Blazeskull 76, Drizzle 74, Frostskull 91, Emberlurk 71, Nightsqueak 92 | — |
| Kharnel Field 9 (`khn_fild09`) | Swamp | 88–92 | Glowcoil 88, Grubble 90, Goopie 94, Swampfang 80 | Toadmaw |
| Kharnel Dungeon 5 (`khn_dun05`) | Swamp | 70–74 | Moldy 72, Bittersweet 68, Inky 77, Squirt 79, Swampfang 80, Rivershell 60, Glowcoil 88 | — |
| Kharnel Dungeon 6 (`khn_dun06`) | Swamp | 75–79 | Inky 77, Squirt 79, Swampfang 80, Moldy 72, Bittersweet 68, Glowcoil 88 | Toadmaw |
| Kharnel Field 10 (`khn_fild10`) | Corrupted | 84–88 | Grubble 90, Nightsqueak 92, Goopie 94, Horngore 73, Lovebite 96 | Venom Gloop |
| Kharnel Dungeon 7 (`khn_dun07`) | Corrupted | 86–90 | Grubble 90, Nightsqueak 92, Goopie 94, Lovebite 96, Horngore 73 | — |
| Kharnel Dungeon 8 (`khn_dun08`) | Corrupted | 91–95 | Nightsqueak 92, Goopie 94, Grubble 90, Lovebite 96, Horngore 73 | — |
| Kharnel Dungeon 9 (`khn_dun09`) | Corrupted | 96–99 | Lovebite 96, Goopie 94, Nightsqueak 92, Grubble 90, Horngore 73 | Venom Gloop |

## What this does not do yet

- **Every map holds 3 to 7 kinds and 2 to 4 elements, and the biome's main element is the most common (or tied) in 51
  of the 61.** The roster has 66 normals for 61 maps across 14 biomes, and Peaks and Sacred have only two natives
  each; creatures made for the thinner biomes are what closes the rest.
- **A shadow's summons still go by rank**: the highest shadow calls the highest elite, whatever either one is (the
  Blossom Wind shadow calls the drowned squid of Kharnel Dungeon 4).
- **What a kill pays follows the creature's level**: its experience, what its own material sells for, and the Unsigned a
  character saved from it pays are all worked out from the level in its file, so a creature moved from level 40 to 63
  pays as a level-63 creature.

## Related

- [Mob Roster — Normal, Elite, Shadow](mob-roster.md) — the three kinds, summoning and drops.
- [World Map](world-map.md) — the regions, the biome table and every map.
- [Story & Setting (Canon)](story-setting-canon.md) — what a creature *is* in the fiction.
