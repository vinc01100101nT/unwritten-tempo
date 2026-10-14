---
status: "Agreed 2026-10-13. BUILT, not play-tested: the 124 materials, their prices, the drops, an elite's piles and no auto-loot on an elite. Also built: what a saved character pays ([Saved Characters](saved-characters.md))."
owner: vince
last-updated: 2026-10-13
tags:
  - world-content
  - economy
---

# Creature Materials — What Each Creature Drops and What It Sells For

> **Never append. Always update in place.** Every price and time in the tables follows from the rules above them and
> from the creature files and the world spawn list as they stood on 2026-10-13.

## What the player gets

- **No kill drops Unsigned.** A player earns Unsigned by selling what creatures drop to a shop.
- **Every creature has one material that only it drops.** Some normal creatures also have a second, rarer one with a
  coloured name.
- **A material sells for more the higher its creature's level, and the better its creature's gear.** The price follows from
  the level, the tier, and the grade and folds of the creature's gear ([What a Kill Pays](what-a-kill-pays.md)); no price is typed on a material. Every
  price in this page's tables is the plain price, before gear. What a shop really pays is that times the
  creature's grade multiplier and fold multiplier, listed for every creature in [Creature Gear](creature-gear.md).
- **The materials are what hat quests and material quests ask for** (planned after this table is approved).
- **Each material weighs 10**, so a hunter fills up and walks back to town to sell.

## The price rule

- **A normal kill is worth 10u × the creature's level, on average.**
- **The ordinary material** drops on 1 kill in 2, so one sells for **20 × level**.
- **A rare material sells by its tier**, whichever creature drops it: 25 × level for Uncommon, 125 for Rare, 500 for Epic,
  2,500 for Legendary. At a normal creature's drop chances that adds a quarter more Unsigned per kill, on average.
- **An elite** drops its ordinary material on every kill as 3 to 5 separate piles of 4 to 7 each, 22 on average, and each
  one sells by the same 20 × level. That is 44 times a normal kill. Every elite also has a rare material, always one single drop.
- **Nothing an elite drops is ever auto-looted.** It all falls on the ground, so an elite has to be hunted by hand.

## The drop tiers, and why each chance was chosen

The yardstick is **500 kills an hour**, one kill every 7.2 seconds. It is the fight simulation of 2026-10-07: 459 to 599
kills an hour between levels 10 and 99 when fighting without resting. It was not re-run for this table.

| Tier | Drop chance | Kills per drop | Hunting only that creature | Sells for | Why this chance |
| --- | --- | --- | --- | --- | --- |
| Common (every creature) | 50% | 2 | one every 14 seconds | 20 × level | It is the income, so it must be seen in every fight. At 1 in 2 a hunter picks up 250 an hour, 2,500 weight. The carry limit is 2,000 + 30 per point of STR, so the bag fills in about 49 minutes at 1 STR and 2.0 hours at 99 STR. At 1 in 1 those times halve. |
| Uncommon | 10% | 10 | one every 72 seconds | 25 × level | About one a minute, so a recipe can ask for dozens in one sitting. It is also the highest chance at which the rare material still sells for more than the ordinary one (25 × level against 20 × level). |
| Rare | 2% | 50 | one every 6 minutes | 125 × level | One every few minutes: a goal for one evening when a recipe asks for ten. |
| Epic | 0.5% | 200 | one every 24 minutes | 500 × level | Two or three an hour. A recipe asks for a few, and each one is worth stopping for. |
| Legendary | 0.1% | 1000 | one every 2 hours | 2,500 × level | One per two hours when hunting nothing else, so a recipe asks for one. It sits only on creatures that many maps keep in large numbers, so every player gets a roll. |

Each tier is 4 or 5 times rarer than the one before, so each is a different length of hunt: seconds, a minute,
minutes, half an hour, hours. A tier is the only setting a rare material has; its chance and price both follow from it.

### An elite's chances

An elite cannot use the chances above. It comes back 30 minutes to 3 hours after it dies, 1 hour 45 minutes on
average, so 1 in 1,000 would be one drop in about 73 days of its spawns on one map. Its chance is per elite kill:

| Tier | Chance per elite kill | Elite kills per drop | On one map | Sells for | Which elites | Why this chance |
| --- | --- | --- | --- | --- | --- | --- |
| Rare | 50% | 2 | one every 3.5 h | 125 × level | on 1 or 2 maps | Every second kill: a hunter who catches the elite twice has one. |
| Epic | 20% | 5 | one every 8.75 h | 500 × level | on 3 maps | One per long day of that elite on one map, or a few hours when its three maps are all hunted. |
| Legendary | 5% | 20 | one every 35 h | 2,500 × level | on 4 or 5 maps | The rarest material in the game: days of one map, about a working day when all its maps are hunted. |

The rarer tier goes to the elites that live on more maps, so no material has both the lowest chance and a single
place in the world. An elite has no Uncommon material: catching the elite is already the hard part.

## The normal creatures

"Alive" is how many of the creature the whole world keeps alive at once, and "Maps" how many maps it lives on, both
from the world spawn list as it is today. "Best map" is the map where it is the largest share of the normal creatures,
and the time is how long one rare drop takes there when killing everything on the map at 500 kills an hour.

| Creature | Level | Ordinary material | Sells for | Rare material | Tier | Sells for | Alive | Maps | Best map (share) | One rare drop there |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Plopling | 1 | Plopling Jelly | 20 | — | — | — | 15 | 3 | `lst_fild01` (23%) | — |
| Flutterby | 2 | Flutterby Wing | 40 | — | — | — | 23 | 3 | `lst_fild01` (32%) | — |
| Squeak | 3 | Squeak Whisker | 60 | — | — | — | 13 | 6 | `lst_fild01` (13%) | — |
| Crawlet | 4 | Crawlet Bristle | 80 | — | — | — | 29 | 4 | `lst_fild04` (29%) | — |
| Capling | 5 | Capling Spore | 100 | — | — | — | 3 | 1 | `olk_fild01` (10%) | — |
| Hisser | 6 | Hisser Fang | 120 | — | — | — | 16 | 5 | `lst_fild04` (16%) | — |
| Blinkbat | 8 | Blinkbat Wing | 160 | Blinkbat Eye | Rare | 1,000 | 50 | 5 | `olk_dun01` (42%) | 14 min |
| Mossnail | 10 | Mossnail Shell | 200 | — | — | — | 8 | 5 | `olk_fild01` (10%) | — |
| Tarantix | 11 | Tarantix Leg | 220 | — | — | — | 44 | 6 | `olk_dun01` (42%) | — |
| Hootie | 13 | Hootie Feather | 260 | — | — | — | 17 | 5 | `lst_fild02` (19%) | — |
| Rascal | 14 | Rascal Tail | 280 | — | — | — | 48 | 6 | `lst_fild03` (45%) | — |
| Kappi | 15 | Kappi Dish | 300 | Kappi Beak | Uncommon | 375 | 16 | 2 | `plv_fild01` (26%) | 5 min |
| Shootling | 16 | Shootling Sprout | 320 | — | — | — | 12 | 3 | `hrw_fild02` (19%) | — |
| Gillie | 17 | Gillie Fin | 340 | — | — | — | 6 | 2 | `plv_fild01` (10%) | — |
| Shellby | 18 | Shellby Spiral | 360 | — | — | — | 6 | 2 | `plv_fild01` (10%) | — |
| Webber | 19 | Webber Silk | 380 | Webber Spinneret | Legendary | 47,500 | 55 | 9 | `hlt_dun01` (38%) | 5.2 h |
| Newtie | 20 | Newtie Tail | 400 | — | — | — | 16 | 2 | `plv_fild01` (26%) | — |
| Pando | 21 | Pando Fur | 420 | — | — | — | 19 | 4 | `hrw_fild01` (32%) | — |
| Mudpup | 23 | Mudpup Gill | 460 | Mudpup Crest | Rare | 2,875 | 31 | 4 | `plv_fild01` (26%) | 23 min |
| Burrowbo | 25 | Burrowbo Claw | 500 | — | — | — | 21 | 7 | `hlt_dun01` (19%) | — |
| Pilfer | 28 | Pilfer Pouch | 560 | Pilfer Lucky Coin | Epic | 14,000 | 37 | 5 | `prl_fild02` (52%) | 47 min |
| Grumbear | 31 | Grumbear Pelt | 620 | Grumbear Claw | Epic | 15,500 | 54 | 8 | `hrw_fild01` (32%) | 1.2 h |
| Mossgore | 34 | Mossgore Antler | 680 | Mossgore Moss Tuft | Uncommon | 850 | 28 | 5 | `hlt_dun03` (27%) | 4 min |
| Wyrmlet | 34 | Wyrmlet Skin | 680 | — | — | — | 25 | 4 | `prl_fild02` (35%) | — |
| Digger | 36 | Digger Snout | 720 | — | — | — | 8 | 2 | `hlt_dun02` (15%) | — |
| Scaly | 37 | Scaly Shed Skin | 740 | — | — | — | 18 | 4 | `pnr_fild02` (19%) | — |
| Canewick | 38 | Canewick Stalk | 760 | Canewick Blossom | Rare | 4,750 | 29 | 5 | `pnr_fild02` (29%) | 21 min |
| Dewdrop | 39 | Dewdrop Bead | 780 | — | — | — | 9 | 5 | `hlt_fild01` (13%) | — |
| Peeper | 41 | Peeper Horn | 820 | — | — | — | 10 | 3 | `pnr_fild01` (16%) | — |
| Viper | 42 | Viper Venom Sac | 840 | — | — | — | 10 | 5 | `trn_fild01` (10%) | — |
| Coily | 43 | Coily Scale | 860 | — | — | — | 3 | 1 | `pnr_fild01` (10%) | — |
| Emberhoot | 45 | Emberhoot Plume | 900 | — | — | — | 32 | 5 | `trn_fild01` (35%) | — |
| Emberwyrm | 46 | Emberwyrm Hide | 920 | — | — | — | 10 | 3 | `vlm_fild02` (16%) | — |
| Chomper | 48 | Chomper Tooth | 960 | Chomper Golden Tooth | Epic | 24,000 | 51 | 6 | `trn_fild01` (45%) | 53 min |
| Sunscale | 50 | Sunscale Plate | 1,000 | — | — | — | 17 | 5 | `vlm_fild02` (16%) | — |
| Willo | 54 | Willo Ember | 1,080 | Willo Heartflame | Legendary | 135,000 | 52 | 6 | `vlm_dun03` (58%) | 3.5 h |
| Adder | 57 | Adder Fang | 1,140 | — | — | — | 15 | 6 | `vlm_dun03` (12%) | — |
| Bloodshot | 58 | Bloodshot Eye | 1,160 | — | — | — | 16 | 8 | `vlm_dun03` (15%) | — |
| Hornscale | 59 | Hornscale Horn | 1,180 | Hornscale Scale | Uncommon | 1,475 | 28 | 2 | `brm_fild02` (45%) | 3 min |
| Rivershell | 60 | Rivershell Carapace | 1,200 | — | — | — | 24 | 4 | `lst_dun01` (31%) | — |
| Snapper | 62 | Snapper Lid | 1,240 | — | — | — | 18 | 5 | `brm_fild02` (16%) | — |
| Octi | 63 | Octi Tentacle | 1,260 | Octi Beak | Uncommon | 1,575 | 22 | 3 | `lst_dun01` (27%) | 4 min |
| Iris | 64 | Iris Lens | 1,280 | — | — | — | 20 | 4 | `brm_fild02` (29%) | — |
| Gnasher | 65 | Gnasher Fin | 1,300 | Gnasher Jaw | Rare | 8,125 | 22 | 3 | `lst_dun01` (27%) | 22 min |
| Cinder | 67 | Cinder Ash | 1,340 | — | — | — | 12 | 6 | `brm_fild01` (10%) | — |
| Bittersweet | 68 | Bittersweet Seed | 1,360 | — | — | — | 10 | 3 | `khn_dun05` (23%) | — |
| Cyclo | 69 | Cyclo Horn | 1,380 | — | — | — | 34 | 9 | `brm_fild01` (19%) | — |
| Emberlurk | 71 | Emberlurk Scale | 1,420 | Emberlurk Golden Eye | Legendary | 177,500 | 66 | 10 | `khn_fild02` (32%) | 6.2 h |
| Moldy | 72 | Moldy Slime | 1,440 | — | — | — | 8 | 4 | `khn_fild03` (10%) | — |
| Horngore | 73 | Horngore Tusk | 1,460 | Horngore Crown Horn | Epic | 36,500 | 48 | 12 | `cdl_dun03` (27%) | 1.5 h |
| Drizzle | 74 | Drizzle Droplet | 1,480 | — | — | — | 26 | 4 | `khn_fild02` (32%) | — |
| Blazeskull | 76 | Blazeskull Jaw | 1,520 | Blazeskull Flame Core | Rare | 9,500 | 37 | 8 | `khn_fild02` (19%) | 31 min |
| Inky | 77 | Inky Ink Sac | 1,540 | Inky Fin | Uncommon | 1,925 | 31 | 6 | `khn_dun06` (27%) | 4 min |
| Sucker | 78 | Sucker Cup | 1,560 | — | — | — | 24 | 5 | `cdl_dun03` (27%) | — |
| Squirt | 79 | Squirt Tentacle | 1,580 | — | — | — | 49 | 10 | `khn_dun06` (27%) | — |
| Swampfang | 80 | Swampfang Hide | 1,600 | — | — | — | 28 | 5 | `khn_dun06` (27%) | — |
| Wisp | 85 | Wisp Light | 1,700 | Wisp Core | Rare | 10,625 | 24 | 2 | `hcr_fild01` (39%) | 16 min |
| Skywing | 87 | Skywing Dust | 1,740 | — | — | — | 18 | 2 | `hcr_fild01` (29%) | — |
| Glowcoil | 88 | Glowcoil Gland | 1,760 | Glowcoil Glowing Eye | Epic | 44,000 | 58 | 11 | `khn_dun01` (42%) | 57 min |
| Grubble | 90 | Grubble Spine | 1,800 | Grubble Chrysalis | Legendary | 225,000 | 73 | 8 | `khn_fild10` (42%) | 4.8 h |
| Frostskull | 91 | Frostskull Shard | 1,820 | Frostskull Crown | Rare | 11,375 | 37 | 6 | `khn_fild06` (29%) | 21 min |
| Nightsqueak | 92 | Nightsqueak Tail | 1,840 | — | — | — | 32 | 11 | `khn_dun01` (15%) | — |
| Frostcap | 93 | Frostcap Spore | 1,860 | — | — | — | 32 | 3 | `khn_fild06` (45%) | — |
| Goopie | 94 | Goopie Goo | 1,880 | — | — | — | 43 | 8 | `khn_fild10` (26%) | — |
| Frostwick | 95 | Frostwick Cold Ember | 1,900 | — | — | — | 14 | 5 | `khn_fild06` (16%) | — |
| Lovebite | 96 | Lovebite Petal | 1,920 | Lovebite Heart | Epic | 48,000 | 71 | 9 | `khn_dun04` (38%) | 1.0 h |

66 normal creatures; 22 of them have a rare material: 5 Uncommon, 7 Rare, 6 Epic, 4 Legendary.

**Which creatures got a rare material, and why:** the rarer the tier, the more of the creature the world keeps alive.
A low drop chance is rolled by each player separately, so it stays rare however many players hunt; putting it on a
creature that is everywhere means nobody has to queue for it.

## Spawn amounts — no change proposed

The world spawn list already makes some creatures hard to find, so no amount is changed by this proposal.

- **The 12 scarcest normal creatures** (10 or fewer alive in the whole world): Capling (3 alive, 1 map, level 5), Coily (3 alive, 1 map, level 43), Gillie (6 alive, 2 maps, level 17), Shellby (6 alive, 2 maps, level 18), Digger (8 alive, 2 maps, level 36), Moldy (8 alive, 4 maps, level 72), Mossnail (8 alive, 5 maps, level 10), Dewdrop (9 alive, 5 maps, level 39), Bittersweet (10 alive, 3 maps, level 68), Emberwyrm (10 alive, 3 maps, level 46), Peeper (10 alive, 3 maps, level 41), Viper (10 alive, 5 maps, level 42). None of them has a rare material: being few is what makes their ordinary material hard to gather.
- **A normal creature comes back 5 to 8 seconds after it dies**, so a low count does not make a creature rare for one
  hunter who looks for it. It limits how many hunters can farm it at once, and how often a hunter who kills everything
  on the map meets it.
- **An elite comes back 30 minutes to 3 hours after it dies** (1 hour 45 minutes on average), so one elite on one map
  gives about 13 ordinary materials an hour, and one rare material every 3.5 to 35 hours, to everybody hunting it together.

## The elites

Every elite drops its ordinary material on every kill, in 3 to 5 piles of 4 to 7, and has a rare material of its own.
"All its maps" is how often the rare material drops somewhere in the world, for everybody hunting that elite together.

| Elite | Level | Maps | Ordinary material | Drops | One sells for | Rare material | Tier | Chance per kill | Sells for | A kill is worth, on average | One rare drop, all its maps |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Gloop King | 8 | 3 | Gloop King's Jelly | 100%, 3 to 5 piles of 4 to 7 | 160 | Gloop King's Eye | Epic | 20% | 4,000 | 4,320 | 2.9 h |
| Oculord | 14 | 2 | Oculord Pupil | 100%, 3 to 5 piles of 4 to 7 | 280 | Oculord Iris | Rare | 50% | 1,750 | 7,035 | 1.75 h |
| Autumn Stalk | 15 | 5 | Autumn Stalk Leaf | 100%, 3 to 5 piles of 4 to 7 | 300 | Autumn Stalk Seed | Legendary | 5% | 37,500 | 8,475 | 7 h |
| Bandit Boss | 22 | 3 | Bandit Boss's Mask | 100%, 3 to 5 piles of 4 to 7 | 440 | Bandit Boss's Loot Sack | Epic | 20% | 11,000 | 11,880 | 2.9 h |
| Deep Maw | 24 | 2 | Deep Maw Tongue | 100%, 3 to 5 piles of 4 to 7 | 480 | Deep Maw Pearl | Rare | 50% | 3,000 | 12,060 | 1.75 h |
| Frost Ronin | 33 | 2 | Frost Ronin Blade Shard | 100%, 3 to 5 piles of 4 to 7 | 660 | Frost Ronin Crest | Rare | 50% | 4,125 | 16,583 | 1.75 h |
| Mossmaw | 35 | 1 | Mossmaw Moss | 100%, 3 to 5 piles of 4 to 7 | 700 | Mossmaw Eye | Rare | 50% | 4,375 | 17,588 | 3.5 h |
| Golden Bandit | 44 | 3 | Golden Bandit's Mask | 100%, 3 to 5 piles of 4 to 7 | 880 | Golden Bandit's Gold Tooth | Epic | 20% | 22,000 | 23,760 | 2.9 h |
| Elder Stalk | 47 | 1 | Elder Stalk Bark | 100%, 3 to 5 piles of 4 to 7 | 940 | Elder Stalk Heartwood | Rare | 50% | 5,875 | 23,618 | 3.5 h |
| Karasu | 58 | 2 | Karasu Feather | 100%, 3 to 5 piles of 4 to 7 | 1,160 | Karasu Black Plume | Rare | 50% | 7,250 | 29,145 | 1.75 h |
| Blood Ronin | 60 | 2 | Blood Ronin Blade Shard | 100%, 3 to 5 piles of 4 to 7 | 1,200 | Blood Ronin Crest | Rare | 50% | 7,500 | 30,150 | 1.75 h |
| Great Croak | 66 | 2 | Great Croak Skin | 100%, 3 to 5 piles of 4 to 7 | 1,320 | Great Croak Throat Sac | Rare | 50% | 8,250 | 33,165 | 1.75 h |
| Toadmaw | 78 | 4 | Toadmaw Wart | 100%, 3 to 5 piles of 4 to 7 | 1,560 | Toadmaw Toadstone | Legendary | 5% | 195,000 | 44,070 | 8.75 h |
| Inferno Mask | 80 | 3 | Inferno Mask Fragment | 100%, 3 to 5 piles of 4 to 7 | 1,600 | Inferno Mask Ember Eye | Epic | 20% | 40,000 | 43,200 | 2.9 h |
| Wraithmother | 90 | 1 | Wraithmother Veil | 100%, 3 to 5 piles of 4 to 7 | 1,800 | Wraithmother Tear | Rare | 50% | 11,250 | 45,225 | 3.5 h |
| Oni Tengu | 92 | 2 | Oni Tengu Mask | 100%, 3 to 5 piles of 4 to 7 | 1,840 | Oni Tengu Horn | Rare | 50% | 11,500 | 46,230 | 1.75 h |
| Venom Gloop | 95 | 4 | Venom Gloop Core | 100%, 3 to 5 piles of 4 to 7 | 1,900 | Venom Gloop Eye | Legendary | 5% | 237,500 | 53,675 | 8.75 h |
| Abyss Maw | 99 | 1 | Abyss Maw Fang | 100%, 3 to 5 piles of 4 to 7 | 1,980 | Abyss Maw Heart | Rare | 50% | 12,375 | 49,748 | 3.5 h |

18 elites, each with a rare material: 11 Rare, 4 Epic, 3 Legendary.

## Every map — what an hour of hunting sells for

Killing the map's normal creatures in the proportions it keeps them, at 500 kills an hour, and selling every material.
Elites and saved characters are not counted. "Rare share" is how much of the map's normal creatures have a rare material.

| Map | Normal creatures alive | Average level | Rare share | Unsigned per kill | Unsigned per hour |
| --- | --- | --- | --- | --- | --- |
| `brm_fild01` | 31 | 60 | 45% | 660 | 330,242 |
| `brm_fild02` | 31 | 60 | 45% | 662 | 331,210 |
| `brm_fild03` | 31 | 66 | 52% | 750 | 374,798 |
| `cdl_dun01` | 26 | 72 | 65% | 836 | 417,837 |
| `cdl_dun02` | 26 | 73 | 58% | 838 | 419,183 |
| `cdl_dun03` | 26 | 74 | 69% | 867 | 433,654 |
| `cdl_fild01` | 31 | 72 | 68% | 841 | 420,645 |
| `cdl_fild02` | 31 | 73 | 58% | 839 | 419,637 |
| `gmr_fild01` | 31 | 13 | 65% | 150 | 74,919 |
| `gmr_fild02` | 31 | 15 | 23% | 158 | 78,911 |
| `hcr_fild01` | 31 | 87 | 39% | 955 | 477,258 |
| `hcr_fild02` | 31 | 87 | 39% | 955 | 477,258 |
| `hlt_dun01` | 26 | 24 | 73% | 285 | 142,452 |
| `hlt_dun02` | 26 | 32 | 50% | 356 | 177,981 |
| `hlt_dun03` | 26 | 35 | 50% | 389 | 194,423 |
| `hlt_fild01` | 31 | 29 | 68% | 339 | 169,677 |
| `hlt_fild02` | 31 | 28 | 74% | 331 | 165,726 |
| `hrw_fild01` | 31 | 28 | 48% | 312 | 156,089 |
| `hrw_fild02` | 31 | 18 | 35% | 201 | 100,524 |
| `khn_dun01` | 26 | 89 | 69% | 1,041 | 520,433 |
| `khn_dun02` | 26 | 91 | 73% | 1,078 | 538,990 |
| `khn_dun03` | 26 | 91 | 73% | 1,079 | 539,567 |
| `khn_dun04` | 26 | 93 | 77% | 1,110 | 555,240 |
| `khn_dun05` | 26 | 75 | 27% | 806 | 402,788 |
| `khn_dun06` | 26 | 78 | 31% | 837 | 418,606 |
| `khn_dun07` | 26 | 92 | 69% | 1,073 | 536,250 |
| `khn_dun08` | 26 | 92 | 69% | 1,073 | 536,250 |
| `khn_dun09` | 26 | 92 | 69% | 1,073 | 536,250 |
| `khn_fild01` | 31 | 70 | 55% | 793 | 396,532 |
| `khn_fild02` | 31 | 68 | 61% | 783 | 391,411 |
| `khn_fild03` | 31 | 78 | 32% | 843 | 421,613 |
| `khn_fild04` | 31 | 83 | 58% | 952 | 476,210 |
| `khn_fild05` | 31 | 92 | 71% | 1,080 | 539,919 |
| `khn_fild06` | 31 | 91 | 29% | 975 | 487,540 |
| `khn_fild07` | 31 | 92 | 71% | 1,080 | 539,919 |
| `khn_fild08` | 31 | 81 | 55% | 926 | 463,145 |
| `khn_fild09` | 31 | 89 | 68% | 1,043 | 521,452 |
| `khn_fild10` | 31 | 90 | 61% | 1,038 | 518,750 |
| `lmh_fild01` | 31 | 62 | 58% | 715 | 357,500 |
| `lst_dun01` | 26 | 62 | 54% | 702 | 350,962 |
| `lst_dun02` | 26 | 63 | 62% | 729 | 364,519 |
| `lst_fild01` | 31 | 3 | 0% | 27 | 13,710 |
| `lst_fild02` | 31 | 11 | 26% | 116 | 58,226 |
| `lst_fild03` | 31 | 22 | 45% | 254 | 127,097 |
| `lst_fild04` | 31 | 11 | 10% | 115 | 57,419 |
| `lst_fild05` | 31 | 19 | 52% | 211 | 105,645 |
| `olk_dun01` | 26 | 8 | 42% | 93 | 46,346 |
| `olk_dun02` | 26 | 13 | 62% | 148 | 74,231 |
| `olk_fild01` | 31 | 6 | 42% | 72 | 35,968 |
| `plv_fild01` | 31 | 19 | 52% | 211 | 105,645 |
| `pnr_fild01` | 31 | 35 | 52% | 399 | 199,355 |
| `pnr_fild02` | 31 | 40 | 45% | 447 | 223,468 |
| `prl_fild01` | 31 | 3 | 0% | 28 | 14,194 |
| `prl_fild02` | 31 | 28 | 52% | 319 | 159,677 |
| `trn_fild01` | 31 | 45 | 55% | 517 | 258,629 |
| `vlm_dun01` | 26 | 50 | 54% | 570 | 285,192 |
| `vlm_dun02` | 26 | 52 | 62% | 602 | 300,962 |
| `vlm_dun03` | 26 | 56 | 58% | 636 | 317,981 |
| `vlm_fild01` | 31 | 42 | 58% | 480 | 240,242 |
| `vlm_fild02` | 31 | 50 | 55% | 572 | 286,129 |
| `vlm_fild03` | 31 | 60 | 45% | 662 | 331,210 |

The rare share runs from 23% (`gmr_fild02`) to 77% (`khn_dun04`). A rare material adds a quarter, so the most a map's
mix can lift its Unsigned over a map of the same level with none is 19%.

## Still open

- **Every material name is a draft**, written from each creature's portrait.
- **The fourteen shadows are not in this table.** The Mob Roster already plans a material of their own for them (each
  weapon's transcending material), and they have no place in the world yet.
- **First guesses:** 500 kills an hour as the yardstick, 1 in 2 for the ordinary material, the four rare chances, the
  three elite chances, an elite's 3 to 5 piles of 4 to 7, the quarter a rare material adds, and weight 10.
- **What a shop pays for the grade materials, Inks, Salts and consumables** waits until income is settled, with every
  other price.
- **Where the materials sit in the bag:** a bag tab holds 24 kinds.
- **An elite's materials do not grow with the number of players.** However many hunt it, it dies once per return.

## Related

- [Loot-Owner Pointing System](../2-systems/loot-owner-pointing.md) — who a kill's loot and experience belong to.
- [Mob Roster](mob-roster.md) — the three kinds of creature and what they drop today.
- [Where Each Creature Lives](biome-mob-families.md) — every creature's level, element and maps.
- [What a Kill Pays](what-a-kill-pays.md) — the grade and fold multipliers a geared creature's prices are multiplied by.
- [Saved Characters](saved-characters.md) — the Unsigned and material a saved character pays.
- [Hair and Headgear](../2-systems/hair-and-headgear.md) — the fifteen hats the hat quests will hand out.
