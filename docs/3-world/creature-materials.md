---
status: "Agreed 2026-10-13; reworked and BUILT 2026-10-16 (a creature's grade decides its one material), not play-tested: the 84 materials, their prices, the drops, an elite's drops and no auto-loot on an elite. Also built: what a saved character pays ([Saved Characters](saved-characters.md))."
owner: vince
last-updated: 2026-10-16
tags:
  - world-content
  - economy
---

# Creature Materials — What Each Creature Drops and What It Sells For

> **Never append. Always update in place.** Every price and chance in the tables follows from the rules above them
> and from the creature files and the world spawn list as they stood on 2026-10-16.

## What the player gets

- **No kill drops Unsigned.** A player earns Unsigned by selling what creatures drop to a shop.
- **Every normal creature and elite drops exactly one material, and only it drops that material.** Shadows have no
  material.
- **A creature's grade decides its material's tier.** The better a creature's gear, the rarer and more valuable what it
  drops — and the harder it is to take.
- **A material sells for more the higher its creature's level, and the better its creature's gear.** The price follows
  from the level, the tier, and the grade and folds of the creature's gear ([What a Kill Pays](what-a-kill-pays.md));
  no price is typed on a material.
- **The materials are what hat quests and material quests ask for** (planned).
- **Each material weighs 10**, so a hunter fills up and walks back to town to sell.

## The rule

- **A normal kill is worth 10u × the creature's level, on average, whatever its tier.** Each tier's price is its drop
  chance turned around: 50% at 20 × level, 10% at 100, 2% at 500, 0.5% at 2,000, 0.1% at 10,000. Every one of those
  averages 10 × level a kill.
- **An elite kill is worth 100 normal kills** of its level, on average.
- **A shop pays that price × the creature's grade multiplier × its fold multiplier** ([What a Kill Pays](what-a-kill-pays.md)),
  rounded to the nearest whole Unsigned.
- **Drops never grow with the number of attackers.** However many hunt a creature, its kill drops what one hunter's would.
- **Nothing an elite drops is ever auto-looted.** It all falls on the ground, so an elite has to be hunted by hand.

## Which tier each grade drops

| Grade | Normal creature | Elite |
| --- | --- | --- |
| Raw | Common | — |
| F | Uncommon | — |
| E | Rare | — |
| D | Epic | Rare |
| C | Legendary | Epic |
| B | — | Legendary |
| A | — | — |

- **A normal creature never wears B or A.**
- **An elite never wears below D:** it hits hard, it is tough, and it is party content. It never wears A.
- **Shadows wear A and drop no material.**

## The drop tiers, and why each chance was chosen

The yardstick is **500 kills an hour**, one kill every 7.2 seconds. It is the fight simulation of 2026-10-07: 459 to 599
kills an hour between levels 10 and 99 when fighting without resting. It was not re-run for this table.

| Tier | Drop chance | Kills per drop | Hunting only that creature | Sells for (before gear) | Why this chance |
| --- | --- | --- | --- | --- | --- |
| Common | 50% | 2 | one every 14 seconds | 20 × level | Seen in every fight. A hunter picks up 250 an hour, 2,500 weight. The carry limit is 2,000 + 30 per point of STR, so the bag fills in about 49 minutes at 1 STR and 2.0 hours at 99 STR. |
| Uncommon | 10% | 10 | one every 72 seconds | 100 × level | About one a minute, so a recipe can ask for dozens in one sitting. |
| Rare | 2% | 50 | one every 6 minutes | 500 × level | One every few minutes: a goal for one evening when a recipe asks for ten. |
| Epic | 0.5% | 200 | one every 24 minutes | 2,000 × level | Two or three an hour. A recipe asks for a few, and each one is worth stopping for. |
| Legendary | 0.1% | 1000 | one every 2 hours | 10,000 × level | One per two hours when hunting nothing else, so a recipe asks for one. |

Each tier is 4 or 5 times rarer than the one before, so each is a different length of hunt: seconds, a minute,
minutes, half an hour, hours. The rarer the tier, the fewer pieces a hunter carries for the same Unsigned.

### An elite's drops

An elite cannot use the chances above. It comes back 30 minutes to 3 hours after it dies, 1 hour 45 minutes on
average, so 1 in 1,000 would be one drop in about 73 days of its spawns on one map. Its drop is per elite kill, and
every grade is worth 100 normal kills:

| Grade | Tier | What one kill drops | Worth, on average |
| --- | --- | --- | --- |
| D | Rare | 2, every kill, as two separate piles of one | 2 × 500 × level = 1,000 × level |
| C | Epic | a 50% chance of 1 | 0.5 × 2,000 × level = 1,000 × level |
| B | Legendary | a 10% chance of 1 | 0.1 × 10,000 × level = 1,000 × level |

An elite drops nothing else of its own: it has no Common material.

**Accepted:** a high-level player soloing elites on low maps can earn more than farming normal creatures at their own
level. That is the reward for hunting by hand.

## The normal creatures

"Before gear" is the tier's price at the creature's level; "A shop pays" is that times the creature's grade and fold
multipliers. "Alive" is how many of the creature the whole world keeps alive at once, and "Maps" how many maps it
lives on, both from the world spawn list as it is today.

| Creature | Level | Grade and folds | Material | Tier | Drop chance | Before gear | A shop pays | Alive | Maps |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Plopling | 1 | Raw +0 | Plopling Jelly | Common | 50% | 20 | 20 | 15 | 3 |
| Flutterby | 2 | Raw +0 | Flutterby Wing | Common | 50% | 40 | 40 | 23 | 3 |
| Squeak | 3 | Raw +0 | Squeak Whisker | Common | 50% | 60 | 60 | 13 | 6 |
| Crawlet | 4 | Raw +0 | Crawlet Bristle | Common | 50% | 80 | 80 | 29 | 4 |
| Capling | 5 | Raw +10 | Capling Spore | Common | 50% | 100 | 150 | 3 | 1 |
| Hisser | 6 | Raw +0 | Hisser Fang | Common | 50% | 120 | 120 | 16 | 5 |
| Blinkbat | 8 | E +7 | Blinkbat Eye | Rare | 2% | 4,000 | 8,100 | 50 | 5 |
| Mossnail | 10 | Raw +10 | Mossnail Shell | Common | 50% | 200 | 300 | 8 | 5 |
| Tarantix | 11 | Raw +2 | Tarantix Leg | Common | 50% | 220 | 242 | 44 | 6 |
| Hootie | 13 | Raw +8 | Hootie Feather | Common | 50% | 260 | 364 | 17 | 5 |
| Rascal | 14 | Raw +2 | Rascal Tail | Common | 50% | 280 | 308 | 48 | 6 |
| Kappi | 15 | F +9 | Kappi Beak | Uncommon | 10% | 1,500 | 2,719 | 16 | 2 |
| Shootling | 16 | Raw +8 | Shootling Sprout | Common | 50% | 320 | 448 | 12 | 3 |
| Gillie | 17 | Raw +10 | Gillie Fin | Common | 50% | 340 | 510 | 6 | 2 |
| Shellby | 18 | Raw +10 | Shellby Spiral | Common | 50% | 360 | 540 | 6 | 2 |
| Webber | 19 | C +8 | Webber Spinneret | Legendary | 0.1% | 190,000 | 532,000 | 55 | 9 |
| Newtie | 20 | Raw +8 | Newtie Tail | Common | 50% | 400 | 560 | 16 | 2 |
| Pando | 21 | Raw +8 | Pando Fur | Common | 50% | 420 | 588 | 19 | 4 |
| Mudpup | 23 | E +9 | Mudpup Crest | Rare | 2% | 11,500 | 25,013 | 31 | 4 |
| Burrowbo | 25 | Raw +6 | Burrowbo Claw | Common | 50% | 500 | 650 | 21 | 7 |
| Pilfer | 28 | D +8 | Pilfer Lucky Coin | Epic | 0.5% | 56,000 | 137,200 | 37 | 5 |
| Grumbear | 31 | D +8 | Grumbear Claw | Epic | 0.5% | 62,000 | 151,900 | 54 | 8 |
| Mossgore | 34 | F +8 | Mossgore Moss Tuft | Uncommon | 10% | 3,400 | 5,950 | 28 | 5 |
| Wyrmlet | 34 | Raw +6 | Wyrmlet Skin | Common | 50% | 680 | 884 | 25 | 4 |
| Digger | 36 | Raw +10 | Digger Snout | Common | 50% | 720 | 1,080 | 8 | 2 |
| Scaly | 37 | Raw +8 | Scaly Shed Skin | Common | 50% | 740 | 1,036 | 18 | 4 |
| Canewick | 38 | E +9 | Canewick Blossom | Rare | 2% | 19,000 | 41,325 | 29 | 5 |
| Dewdrop | 39 | Raw +10 | Dewdrop Bead | Common | 50% | 780 | 1,170 | 9 | 5 |
| Peeper | 41 | Raw +10 | Peeper Horn | Common | 50% | 820 | 1,230 | 10 | 3 |
| Viper | 42 | Raw +10 | Viper Venom Sac | Common | 50% | 840 | 1,260 | 10 | 5 |
| Coily | 43 | Raw +10 | Coily Scale | Common | 50% | 860 | 1,290 | 3 | 1 |
| Emberhoot | 45 | Raw +6 | Emberhoot Plume | Common | 50% | 900 | 1,170 | 32 | 5 |
| Emberwyrm | 46 | Raw +10 | Emberwyrm Hide | Common | 50% | 920 | 1,380 | 10 | 3 |
| Chomper | 48 | D +8 | Chomper Golden Tooth | Epic | 0.5% | 96,000 | 235,200 | 51 | 6 |
| Sunscale | 50 | Raw +8 | Sunscale Plate | Common | 50% | 1,000 | 1,400 | 17 | 5 |
| Willo | 54 | C +8 | Willo Heartflame | Legendary | 0.1% | 540,000 | 1,512,000 | 52 | 6 |
| Adder | 57 | Raw +8 | Adder Fang | Common | 50% | 1,140 | 1,596 | 15 | 6 |
| Bloodshot | 58 | Raw +8 | Bloodshot Eye | Common | 50% | 1,160 | 1,624 | 16 | 8 |
| Hornscale | 59 | F +8 | Hornscale Scale | Uncommon | 10% | 5,900 | 10,325 | 28 | 2 |
| Rivershell | 60 | Raw +6 | Rivershell Carapace | Common | 50% | 1,200 | 1,560 | 24 | 4 |
| Snapper | 62 | Raw +8 | Snapper Lid | Common | 50% | 1,240 | 1,736 | 18 | 5 |
| Octi | 63 | F +8 | Octi Beak | Uncommon | 10% | 6,300 | 11,025 | 22 | 3 |
| Iris | 64 | Raw +8 | Iris Lens | Common | 50% | 1,280 | 1,792 | 20 | 4 |
| Gnasher | 65 | E +9 | Gnasher Jaw | Rare | 2% | 32,500 | 70,688 | 22 | 3 |
| Cinder | 67 | Raw +8 | Cinder Ash | Common | 50% | 1,340 | 1,876 | 12 | 6 |
| Bittersweet | 68 | Raw +10 | Bittersweet Seed | Common | 50% | 1,360 | 2,040 | 10 | 3 |
| Cyclo | 69 | Raw +6 | Cyclo Horn | Common | 50% | 1,380 | 1,794 | 34 | 9 |
| Emberlurk | 71 | C +8 | Emberlurk Golden Eye | Legendary | 0.1% | 710,000 | 1,988,000 | 66 | 10 |
| Moldy | 72 | Raw +10 | Moldy Slime | Common | 50% | 1,440 | 2,160 | 8 | 4 |
| Horngore | 73 | D +8 | Horngore Crown Horn | Epic | 0.5% | 146,000 | 357,700 | 48 | 12 |
| Drizzle | 74 | Raw +6 | Drizzle Droplet | Common | 50% | 1,480 | 1,924 | 26 | 4 |
| Blazeskull | 76 | E +7 | Blazeskull Flame Core | Rare | 2% | 38,000 | 76,950 | 37 | 8 |
| Inky | 77 | F +8 | Inky Fin | Uncommon | 10% | 7,700 | 13,475 | 31 | 6 |
| Sucker | 78 | Raw +6 | Sucker Cup | Common | 50% | 1,560 | 2,028 | 24 | 5 |
| Squirt | 79 | Raw +2 | Squirt Tentacle | Common | 50% | 1,580 | 1,738 | 49 | 10 |
| Swampfang | 80 | Raw +6 | Swampfang Hide | Common | 50% | 1,600 | 2,080 | 28 | 5 |
| Wisp | 85 | E +9 | Wisp Core | Rare | 2% | 42,500 | 92,438 | 24 | 2 |
| Skywing | 87 | Raw +8 | Skywing Dust | Common | 50% | 1,740 | 2,436 | 18 | 2 |
| Glowcoil | 88 | D +8 | Glowcoil Glowing Eye | Epic | 0.5% | 176,000 | 431,200 | 58 | 11 |
| Grubble | 90 | C +8 | Grubble Chrysalis | Legendary | 0.1% | 900,000 | 2,520,000 | 73 | 8 |
| Frostskull | 91 | E +7 | Frostskull Crown | Rare | 2% | 45,500 | 92,138 | 37 | 6 |
| Nightsqueak | 92 | Raw +6 | Nightsqueak Tail | Common | 50% | 1,840 | 2,392 | 32 | 11 |
| Frostcap | 93 | Raw +6 | Frostcap Spore | Common | 50% | 1,860 | 2,418 | 32 | 3 |
| Goopie | 94 | Raw +2 | Goopie Goo | Common | 50% | 1,880 | 2,068 | 43 | 8 |
| Frostwick | 95 | Raw +8 | Frostwick Cold Ember | Common | 50% | 1,900 | 2,660 | 14 | 5 |
| Lovebite | 96 | D +8 | Lovebite Heart | Epic | 0.5% | 192,000 | 470,400 | 71 | 9 |

66 normal creatures: 44 drop a Common material, 5 Uncommon, 7 Rare, 6 Epic and 4 Legendary.

**Where the rarer tiers sit:** the higher grades are on creatures the world keeps in large numbers — the four
Legendary droppers have 52 to 73 alive each. A low drop chance is rolled by each player separately, so it stays rare
however many players hunt; putting it on a creature that is everywhere means nobody has to queue for it.

## Spawn amounts

- **The 12 scarcest normal creatures** (10 or fewer alive in the whole world): Capling (3 alive, 1 map, level 5), Coily (3 alive, 1 map, level 43), Gillie (6 alive, 2 maps, level 17), Shellby (6 alive, 2 maps, level 18), Digger (8 alive, 2 maps, level 36), Moldy (8 alive, 4 maps, level 72), Mossnail (8 alive, 5 maps, level 10), Dewdrop (9 alive, 5 maps, level 39), Bittersweet (10 alive, 3 maps, level 68), Emberwyrm (10 alive, 3 maps, level 46), Peeper (10 alive, 3 maps, level 41), Viper (10 alive, 5 maps, level 42). All twelve wear Raw and drop a Common material: being few is what makes their material hard to gather.
- **A normal creature comes back 5 to 8 seconds after it dies**, so a low count does not make a creature rare for one
  hunter who looks for it. It limits how many hunters can farm it at once, and how often a hunter who kills everything
  on the map meets it.
- **An elite comes back 30 minutes to 3 hours after it dies** (1 hour 45 minutes on average), so one elite on one map
  pays out one kill's drop about every 1 hour 45 minutes, to everybody hunting it together.

## The elites

"Worth, on average" is what a shop pays for what one kill drops, on average.

| Elite | Level | Maps | Grade and folds | Material | Tier | What one kill drops | Before gear | A shop pays | Worth, on average |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Gloop King | 8 | 1 | C +9 | Gloop King's Eye | Epic | 50% chance of 1 | 16,000 | 46,400 | 23,200 |
| Oculord | 14 | 2 | D +10 | Oculord Iris | Rare | 2 every kill | 7,000 | 18,375 | 36,750 |
| Autumn Stalk | 15 | 5 | B +8 | Autumn Stalk Seed | Legendary | 10% chance of 1 | 150,000 | 472,500 | 47,250 |
| Bandit Boss | 22 | 3 | C +9 | Bandit Boss's Loot Sack | Epic | 50% chance of 1 | 44,000 | 127,600 | 63,800 |
| Deep Maw | 24 | 2 | D +10 | Deep Maw Pearl | Rare | 2 every kill | 12,000 | 31,500 | 63,000 |
| Frost Ronin | 33 | 2 | D +10 | Frost Ronin Crest | Rare | 2 every kill | 16,500 | 43,313 | 86,626 |
| Mossmaw | 35 | 1 | D +10 | Mossmaw Eye | Rare | 2 every kill | 17,500 | 45,938 | 91,876 |
| Golden Bandit | 44 | 3 | C +9 | Golden Bandit's Gold Tooth | Epic | 50% chance of 1 | 88,000 | 255,200 | 127,600 |
| Elder Stalk | 47 | 1 | D +10 | Elder Stalk Heartwood | Rare | 2 every kill | 23,500 | 61,688 | 123,376 |
| Karasu | 58 | 2 | D +10 | Karasu Black Plume | Rare | 2 every kill | 29,000 | 76,125 | 152,250 |
| Blood Ronin | 60 | 2 | D +10 | Blood Ronin Crest | Rare | 2 every kill | 30,000 | 78,750 | 157,500 |
| Great Croak | 66 | 2 | D +10 | Great Croak Throat Sac | Rare | 2 every kill | 33,000 | 86,625 | 173,250 |
| Toadmaw | 78 | 4 | B +9 | Toadmaw Toadstone | Legendary | 10% chance of 1 | 780,000 | 2,544,750 | 254,475 |
| Inferno Mask | 80 | 3 | C +9 | Inferno Mask Ember Eye | Epic | 50% chance of 1 | 160,000 | 464,000 | 232,000 |
| Wraithmother | 90 | 1 | D +10 | Wraithmother Tear | Rare | 2 every kill | 45,000 | 118,125 | 236,250 |
| Oni Tengu | 92 | 2 | D +10 | Oni Tengu Horn | Rare | 2 every kill | 46,000 | 120,750 | 241,500 |
| Venom Gloop | 95 | 4 | B +9 | Venom Gloop Eye | Legendary | 10% chance of 1 | 950,000 | 3,099,375 | 309,938 |
| Abyss Maw | 99 | 1 | D +10 | Abyss Maw Heart | Rare | 2 every kill | 49,500 | 129,938 | 259,876 |

18 elites: 11 drop a Rare material, 4 Epic and 3 Legendary.

## Every map — what an hour of hunting sells for

Killing the map's normal creatures in the proportions it keeps them, at 500 kills an hour, and selling every material
at the average every tier pays. Elites and saved characters are not counted. "Before gear" is 10 × the creatures'
levels; "With gear" multiplies each creature's share by its grade and fold multipliers, which is what a shop pays.

| Map | Normal creatures alive | Average level | Unsigned per kill, before gear | Unsigned per kill, with gear | Unsigned per hour, with gear |
| --- | --- | --- | --- | --- | --- |
| `brm_fild01` | 31 | 60 | 597 | 1,179 | 589,452 |
| `brm_fild02` | 31 | 60 | 596 | 923 | 461,516 |
| `brm_fild03` | 31 | 66 | 662 | 1,381 | 690,444 |
| `cdl_dun01` | 26 | 72 | 717 | 1,504 | 752,058 |
| `cdl_dun02` | 26 | 73 | 733 | 1,448 | 723,779 |
| `cdl_dun03` | 26 | 74 | 741 | 1,561 | 780,510 |
| `cdl_fild01` | 31 | 72 | 718 | 1,520 | 759,806 |
| `cdl_fild02` | 31 | 73 | 733 | 1,447 | 723,597 |
| `gmr_fild01` | 31 | 13 | 128 | 270 | 135,226 |
| `gmr_fild02` | 31 | 15 | 147 | 252 | 126,048 |
| `hcr_fild01` | 31 | 87 | 872 | 1,449 | 724,581 |
| `hcr_fild02` | 31 | 87 | 872 | 1,449 | 724,581 |
| `hlt_dun01` | 26 | 24 | 239 | 527 | 263,337 |
| `hlt_dun02` | 26 | 32 | 318 | 567 | 283,519 |
| `hlt_dun03` | 26 | 35 | 348 | 605 | 302,673 |
| `hlt_fild01` | 31 | 29 | 290 | 548 | 273,988 |
| `hlt_fild02` | 31 | 28 | 277 | 542 | 271,242 |
| `hrw_fild01` | 31 | 28 | 276 | 530 | 265,097 |
| `hrw_fild02` | 31 | 18 | 182 | 345 | 172,629 |
| `khn_dun01` | 26 | 89 | 887 | 1,744 | 871,851 |
| `khn_dun02` | 26 | 91 | 910 | 1,875 | 937,611 |
| `khn_dun03` | 26 | 91 | 912 | 1,880 | 940,188 |
| `khn_dun04` | 26 | 93 | 932 | 1,941 | 970,486 |
| `khn_dun05` | 26 | 75 | 753 | 1,109 | 554,731 |
| `khn_dun06` | 26 | 78 | 777 | 1,121 | 560,606 |
| `khn_dun07` | 26 | 92 | 915 | 1,974 | 986,750 |
| `khn_dun08` | 26 | 92 | 915 | 1,974 | 986,750 |
| `khn_dun09` | 26 | 92 | 915 | 1,974 | 986,750 |
| `khn_fild01` | 31 | 70 | 704 | 1,301 | 650,581 |
| `khn_fild02` | 31 | 68 | 684 | 1,357 | 678,516 |
| `khn_fild03` | 31 | 78 | 779 | 1,150 | 574,903 |
| `khn_fild04` | 31 | 83 | 829 | 1,570 | 784,984 |
| `khn_fild05` | 31 | 92 | 918 | 2,003 | 1,001,435 |
| `khn_fild06` | 31 | 91 | 909 | 1,389 | 694,302 |
| `khn_fild07` | 31 | 92 | 918 | 2,003 | 1,001,435 |
| `khn_fild08` | 31 | 81 | 811 | 1,472 | 736,226 |
| `khn_fild09` | 31 | 89 | 892 | 1,912 | 956,032 |
| `khn_fild10` | 31 | 90 | 902 | 1,879 | 939,315 |
| `lmh_fild01` | 31 | 62 | 620 | 1,059 | 529,661 |
| `lst_dun01` | 26 | 62 | 616 | 1,040 | 520,053 |
| `lst_dun02` | 26 | 63 | 628 | 1,080 | 540,188 |
| `lst_fild01` | 31 | 3 | 27 | 27 | 13,710 |
| `lst_fild02` | 31 | 11 | 111 | 153 | 76,500 |
| `lst_fild03` | 31 | 22 | 223 | 422 | 211,065 |
| `lst_fild04` | 31 | 11 | 108 | 153 | 76,613 |
| `lst_fild05` | 31 | 19 | 187 | 327 | 163,581 |
| `olk_dun01` | 26 | 8 | 84 | 124 | 61,788 |
| `olk_dun02` | 26 | 13 | 128 | 264 | 132,212 |
| `olk_fild01` | 31 | 6 | 64 | 105 | 52,597 |
| `plv_fild01` | 31 | 19 | 187 | 327 | 163,581 |
| `pnr_fild01` | 31 | 35 | 354 | 661 | 330,581 |
| `pnr_fild02` | 31 | 40 | 403 | 710 | 355,105 |
| `prl_fild01` | 31 | 3 | 28 | 28 | 14,194 |
| `prl_fild02` | 31 | 28 | 283 | 531 | 265,387 |
| `trn_fild01` | 31 | 45 | 454 | 880 | 439,815 |
| `vlm_dun01` | 26 | 50 | 502 | 1,041 | 520,731 |
| `vlm_dun02` | 26 | 52 | 523 | 1,120 | 559,827 |
| `vlm_dun03` | 26 | 56 | 558 | 1,212 | 606,077 |
| `vlm_fild01` | 31 | 42 | 420 | 812 | 405,944 |
| `vlm_fild02` | 31 | 50 | 503 | 1,054 | 526,903 |
| `vlm_fild03` | 31 | 60 | 596 | 923 | 461,516 |

The two newbie maps, `lst_fild01` and `prl_fild01`, hold only Raw +0 creatures, so their gear adds nothing.

## Still open

- **Every material name is a draft**, written from each creature's portrait.
- **The fourteen shadows drop no material.** The Mob Roster plans a material of their own for them (each weapon's
  transcending material), and they have no place in the world yet.
- **First guesses:** 500 kills an hour as the yardstick, the five drop chances, an elite kill worth 100 normal kills,
  and weight 10.
- **What a shop pays for the grade materials, Inks, Salts and consumables** waits until income is settled, with every
  other price.
- **Where the materials sit in the bag:** a bag tab holds 24 kinds.

## Related

- [Loot-Owner Pointing System](../2-systems/loot-owner-pointing.md) — who a kill's loot and experience belong to.
- [Mob Roster](mob-roster.md) — the three kinds of creature and what they drop today.
- [Where Each Creature Lives](biome-mob-families.md) — every creature's level, element and maps.
- [Creature Gear](creature-gear.md) — every creature's grade and folds.
- [What a Kill Pays](what-a-kill-pays.md) — the grade and fold multipliers a geared creature's prices are multiplied by.
- [Saved Characters](saved-characters.md) — the Unsigned and material a saved character pays.
- [Hair and Headgear](../2-systems/hair-and-headgear.md) — the fifteen hats the hat quests will hand out.
