---
status: "LIVE — every number below was read from the saved item assets and the crafting arithmetic on 2026-09-21. Change the asset or the code, then this page."
owner: vince
last-updated: 2026-09-21
tags:
  - systems-combat-design
---

# Crafting Tables — what every station takes, band by band

The reference companion to [the V4 design](README.md). That page says WHY; this one is only the numbers as
they are in the game today. Costs live on the item assets under `unity/Assets/3_Data/Resources/Items/`; every
per-grade rule lives in `unity/Assets/Scripts/CraftingMath.cs`. Nothing here is typed from memory.

## 1. The three stations and their bands

A piece is worked at the station of its own material, and every fold or quench is paid in the band material
of the piece's CURRENT grade. A design is always forged from band 1.

| Band | Grade it folds and quenches | Smith (metal) | Loom (cloth) | Tannery (leather) |
| --- | --- | --- | --- | --- |
| 1 | Raw | Copper | Burlap | Rawhide |
| 2 | F | Bronze | Hemp | Hide |
| 3 | E | Iron | Linen | Buckskin |
| 4 | D | Steel | Wool | Leather |
| 5 | C | Silver | Cotton | Cordovan |
| 6 | B | Mithril | Silk | Shagreen |
| 7 | A | Adamant | Brocade | Wyrmhide |

Band materials never merge into each other. They come from creatures only.

## 2. Forge — what each design costs

Paid once, in the band-1 material of its craft, plus gold. The result is always a **Raw +0** piece with nothing
written on it. These are every design in the game today.

| Design | Slot | Station | Material | Units | Gold | Card slots |
| --- | --- | --- | --- | --- | --- | --- |
| Sword | Weapon | Smith | Copper | 8 | 800 | 2 |
| Bow | Weapon | Smith | Copper | 8 | 800 | 2 |
| Wooden Buckler | Shield | Tannery | Rawhide | 5 | 400 | 0 |
| Leather Cap | Head | Tannery | Rawhide | 5 | 400 | 0 |
| Padded Vest | Armor | Loom | Burlap | 5 | 400 | 0 |
| Traveller's Cloak | Garment | Loom | Burlap | 5 | 400 | 0 |

## 3. Fold — one unit of the band, plus gold

Each fold costs **1 unit** of the grade's band material and **50 gold × band number**. A piece cannot be folded
past its grade's cap; past there the only way on is a quench.

| Grade | Band material (smith / loom / tannery) | Gold per fold | Fold cap | Ink slots |
| --- | --- | --- | --- | --- |
| Raw | Copper / Burlap / Rawhide | 50 | +5 | 0 |
| F | Bronze / Hemp / Hide | 100 | +10 | 1 |
| E | Iron / Linen / Buckskin | 150 | +15 | 2 |
| D | Steel / Wool / Leather | 200 | +20 | 3 |
| C | Silver / Cotton / Cordovan | 250 | +25 | 4 |
| B | Mithril / Silk / Shagreen | 300 | +30 | 5 |
| A | Adamant / Brocade / Wyrmhide | 350 | +35 | 6 |

### What one fold is worth on the piece

The finished number is `folds × (1 + quality lift)`, rounded half up. The server writes it onto the copy.

| Piece | Each fold adds |
| --- | --- |
| Weapon (sword, bow, …) | +1 Attack |
| Weapon — Staff | +1 Magic Attack |
| Armor, Shield | +1 Defence |
| Garment | +1 Magic Defence |
| Head | +1 Defence **and** +1 Magic Defence per **2** folds |

| Grade | Quality lift on the fold total |
| --- | --- |
| Raw | +0% |
| F | +0% |
| E | +5% |
| D | +10% |
| C | +15% |
| B | +22% |
| A | +30% |

Example: an A-grade sword at +35 is 35 × 1.30 = 45.5 → **+46 Attack**.

## 4. Quench — ten units of the band, plus gold, plus an optional Salt

Each quench costs **10 units** of the grade's band material and **500 gold × band number**. Every fold is
spent whichever way it goes. A grade that falls also loses the LAST stat written on the piece. A is never
quenched — it is the top.

| From | Band material (smith / loom / tannery) | Gold | Up | Unchanged | Down |
| --- | --- | --- | --- | --- | --- |
| Raw → F | 10 Copper / Burlap / Rawhide | 500 | 80% | 20% | — |
| F → E | 10 Bronze / Hemp / Hide | 1,000 | 60% | 40% | — (F is the floor) |
| E → D | 10 Iron / Linen / Buckskin | 1,500 | 50% | 30% | 20% |
| D → C | 10 Steel / Wool / Leather | 2,000 | 40% | 35% | 25% |
| C → B | 10 Silver / Cotton / Cordovan | 2,500 | 35% | 35% | 30% |
| B → A | 10 Mithril / Silk / Shagreen | 3,000 | 25% | 40% | 35% |

### Salt

One Salt may be put in, chosen in the window; it is spent whatever the quench does. Its points move into
**Up**, taken from **Unchanged** first and then from **Down**; Down never goes below **5%** where it exists, and
Up never passes **95%** (anything past that stays with Unchanged).

| Salt | Tier | Shift into Up | Merges |
| --- | --- | --- | --- |
| Coarse Salt | 1 | +10 points | 10 → 1 Fine Salt |
| Fine Salt | 2 | +20 points | 10 → 1 Pure Salt |
| Pure Salt | 3 | +30 points | — |

Worked example, B → A with Pure Salt: 25 / 40 / 35 becomes **55 / 10 / 35** (30 points taken from Unchanged, none
needed from Down).

## 5. Ink — one stat per Ink, the player picks the slot

Using an Ink writes **one** stat at the slot chosen: an empty slot is filled, an occupied one is replaced. A stat
already on the piece cannot roll again. The Ink is always spent; there is no failure outcome. The grade sets how
many slots the piece has (table in §3).

| Ink | Tier | Merges |
| --- | --- | --- |
| Watery Ink | 1 | 10 → 1 Thin Ink |
| Thin Ink | 2 | 10 → 1 Faint Ink |
| Faint Ink | 3 | 10 → 1 Clear Ink |
| Clear Ink | 4 | 10 → 1 Sharp Ink |
| Sharp Ink | 5 | — |

### What each tier writes

| Stat | Watery | Thin | Faint | Clear | Sharp |
| --- | --- | --- | --- | --- | --- |
| Attack, Magic Attack | +2 | +4 | +6 | +8 | +10 |
| Defence, Magic Defence | +2 | +4 | +6 | +8 | +10 |
| Accuracy, Evasion | +2 | +4 | +6 | +8 | +10 |
| Max HP | +16 | +32 | +48 | +64 | +80 |
| Max MP | +8 | +16 | +24 | +32 | +40 |
| Crit chance, Crit damage | +0.6% | +1.2% | +1.8% | +2.4% | +3% |
| Attack Speed, Cast Speed | +1% | +2% | +3% | +4% | +5% |
| Any primary (STR / AGI / VIT / INT / DEX / LUK) | +3 | +6 | +9 | +12 | +15 |

### Which stat lands

Eighteen stats are on the table: twelve sub-stats at weight 80 each and six primaries at weight 20 each
(1,080 in all). On an empty piece each sub-stat is **7.4%** and each primary **1.85%**. A stat already on the piece
is taken off the table and the rest share its weight — with one sub-stat written, each remaining sub-stat is 8%
and each primary 2%.

## 6. What the grade of the weapon in hand does to mastery

| Grade | Weapon Mastery cap | Skill Mastery cap | Training speed |
| --- | --- | --- | --- |
| Raw | 10 | 4 | ×0.5 |
| F | 20 | 8 | ×0.75 |
| E | 30 | 12 | ×1.0 |
| D | 40 | 16 | ×1.1 |
| C | 50 | 20 | ×1.2 |
| B | 60 | 25 | ×1.35 |
| A | 70 | 30 | ×1.5 |

Mastery already earned is never lost; a poorer weapon in hand only caps what is USED and what can be EARNED.

## 7. Recycle — half of what the piece provably consumed

Recycling destroys the piece and returns, each halved and rounded down: the forge bill (band 1); **10 units and
the quench gold of every grade below the current one** (one successful quench per rung climbed); the folds
standing on the piece now, at its own band; and one Ink per written stat, its tier read off the value. Failed
quenches, the folds they spent, and Salt are not recorded and come back as nothing.

| Piece | Consumed (counted) | Comes back |
| --- | --- | --- |
| Raw +3 Sword | 8 + 3 Copper, 800 + 150 g | 5 Copper, 475 g |
| E +0 Sword | 8 + 10 Copper, 10 Bronze, 800 + 500 + 1,000 g | 9 Copper, 5 Bronze, 1,150 g |
| A +35 Sword | 18 Copper, 10 each of Bronze–Mithril, 35 Adamant, 23,550 g | 9 Copper, 5 each of Bronze–Mithril, 17 Adamant, 11,775 g |
| … with 2 Watery Ink stats written | + 2 Watery Ink | + 1 Watery Ink |

## 8. Worked example — a sword from nothing to A

| Step | Pays | Result |
| --- | --- | --- |
| Forge | 8 Copper + 800 g | Raw +0 |
| Fold ×5 | 5 Copper + 250 g | Raw +5 (+5 Attack) |
| Quench | 10 Copper + 500 g, 80% | F +0 |
| Fold ×10 | 10 Bronze + 1,000 g | F +10 (+10 Attack) |
| Quench | 10 Bronze + 1,000 g, 60% | E +0 (2 Ink slots) |
| Fold ×15 | 15 Iron + 2,250 g | E +15 (+16 Attack) |
| Quench | 10 Iron + 1,500 g, 50/30/20 | D +0 (3 slots) — or back to F +0, and the last written stat gone |
| … | | |
| Fold ×35 at A | 35 Adamant + 12,250 g | A +35 (+46 Attack), 6 Ink slots |
