---
status: "LIVE — every number below was read from the saved item assets and the crafting arithmetic on 2026-10-13. Change the asset or the code, then this page."
owner: vince
last-updated: 2026-10-13
tags:
  - systems-combat-design
---

# Crafting Tables — what every station takes, grade by grade

The reference companion to [the V4 design](README.md). That page says WHY; this one is only the numbers as
they are in the game today. Sell prices live on the item assets under `unity/Assets/3_Data/Resources/Items/`; every
cost and per-grade rule lives in `unity/Assets/Scripts/CraftingMath.cs`, and what a kill drops in
`unity/Assets/Scripts/CreatureLoot.cs`. Nothing here is typed from memory.

## 1. The three stations and their two materials

A piece is worked at the station of its own material — metal at a smith, cloth at a loom, leather at a tannery. A
station does four things: **Fold, Quench, Ink, Recycle**. All three stations take the same two materials: every
fold is paid in **Temper Dust** and every quench in **Quench Oil**, the amount set by the piece's CURRENT grade.

Neither has tiers, so neither merges. Both come from creatures only, and no shop sells them. Each kill gives 0.6
Temper Dust and 0.06 Quench Oil for each step of the creature's level (1–14 is step 1, 15–28 step 2, 29–42 step 3,
43–56 step 4, 57–70 step 5, 71–84 step 6, 85 and above step 7), times 10 for an elite and 20 for a shadow, times
what the grade and folds of its gear pay ([What a Kill Pays](../../3-world/what-a-kill-pays.md)) — whole units,
and the part left over is the chance of one more (see [Mob Roster](../../3-world/mob-roster.md)).

| What a shop pays for one | Gold |
| --- | --- |
| Any weapon or armour piece | 5 |
| Temper Dust | 2 |
| Quench Oil | 10 |
| Watery / Thin / Faint / Clear / Sharp Ink | 2 / 4 / 8 / 16 / 32 |
| Coarse / Fine / Pure Salt | 2 / 6 / 18 |

No shop sells any of these.

## 2. Where a piece comes from — dropped, never made

Every kill has a **10%** chance to drop **one** random piece of what the creature wears, as a new copy: always
**Raw +0**, its trademark (a weapon) or word (an armour piece) rolled, and its rarity rolled by the creature's kind.
A new character starts wearing a Raw +0 Common Sword, Chainmail and Iron Sabatons with no trademark and no word.

| Dropped by | Rarity of the piece |
| --- | --- |
| A normal creature | Common 53.2% · Uncommon 31.9% · Rare 14.9% (shares of 50 : 30 : 14) |
| An elite | always Epic |
| A shadow | always Legendary |

What a creature wears, and every piece that was not dropped, is Common.

## 3. Fold — Temper Dust by the grade, plus gold

Each fold costs the grade's step in **Temper Dust** and **50 gold × the step**. A piece cannot be folded
past its grade's cap; past there the only way on is a quench.

| Grade | Temper Dust per fold | Gold per fold | Fold cap | Ink slots |
| --- | --- | --- | --- | --- |
| Raw | 1 | 50 | +5 | 0 |
| F | 2 | 100 | +10 | 1 |
| E | 3 | 150 | +15 | 2 |
| D | 4 | 200 | +20 | 3 |
| C | 5 | 250 | +25 | 4 |
| B | 6 | 300 | +30 | 5 |
| A | 7 | 350 | +35 | 6 |

### What one fold is worth on the piece

The finished number is `folds × what one fold buys × (1 + quality lift + rarity lift)`, rounded half up (attack
speed to one decimal) — except shoes' movement speed, which is exactly `folds × 2%` at every grade and every rarity.
The server writes it onto the copy.

A weapon's fold buys attack. **An armour piece's MATERIAL decides what a fold buys, and its SLOT how much**: body
and garment take the full amount, head and shoes half. Priced with Ink's own exchange rate — 1 defence =
8 HP = 4 MP = 0.5% attack speed, and evasion at double defence, since each point takes 1% off the hits that land — so
a fold is worth 1.8 defence, split between two stats. The 1.8 was sized on 2026-10-07 so a level-1 character in the +5
starter set (Sword 9 attack, Chainmail 5 defence and 36 HP, Iron Sabatons 2 defence and 18 HP) beats four ungeared
level-1 creatures attacking together.

| Piece | Each fold adds (body, garment) | Head and shoes |
| --- | --- | --- |
| Weapon (sword, bow, …) | +1.8 Attack | — |
| Weapon — Staff | +1.8 Magic Attack | — |
| Metal armour | +0.9 Defence and +7.2 HP | +0.45 Defence and +3.6 HP |
| Leather armour | +0.45% Attack Speed and +0.45 Evasion | +0.225% Attack Speed and +0.225 Evasion |
| Cloth armour | +0.9 Magic Defence and +3.6 MP | +0.45 Magic Defence and +1.8 MP |
| Shoes, any material — on top of their material's row | — | +2% Movement Speed, neither lift applied |

Common body armour at A +35: **41 Defence and 328 HP** (metal), **20.5% Attack Speed and 20 Evasion** (leather),
**41 Magic Defence and 164 MP** (cloth).

| Grade | Quality lift on the fold total |
| --- | --- |
| Raw | +0% |
| F | +0% |
| E | +5% |
| D | +10% |
| C | +15% |
| B | +22% |
| A | +30% |

| Rarity | Rarity lift on the fold total, added to the quality lift |
| --- | --- |
| Common | +0% |
| Uncommon | +10% |
| Rare | +20% |
| Epic | +35% |
| Legendary | +50% |

Example: a Common A-grade sword at +35 is 35 × 1.8 × 1.30 = 81.9 → **+82 Attack**; at +25 it is 25 × 1.8 × 1.30 =
58.5, which rounds up to **+59 Attack**. A Legendary one at +35 is 35 × 1.8 × 1.80 = 113.4 → **+113 Attack**.

## 4. Quench — Quench Oil by the grade, plus gold, plus an optional Salt

**A piece must be folded to its grade's cap before it can be quenched** — the cap IS the gate, because the folds
are what a quench spends. Each quench then costs the grade's step in **Quench Oil** and **500 gold × the
step**. Every fold is spent whichever way it goes. A grade that falls also loses the LAST stat written on
the piece. A is never quenched — it is the top.

| From | Quench Oil | Gold | Up | Unchanged | Down |
| --- | --- | --- | --- | --- | --- |
| Raw → F | 1 | 500 | 80% | 20% | — |
| F → E | 2 | 1,000 | 60% | 40% | — (F is the floor) |
| E → D | 3 | 1,500 | 50% | 30% | 20% |
| D → C | 4 | 2,000 | 40% | 35% | 25% |
| C → B | 5 | 2,500 | 35% | 35% | 30% |
| B → A | 6 | 3,000 | 25% | 40% | 35% |

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

| Grade | Weapon Mastery cap | Training speed |
| --- | --- | --- |
| Raw | 10 | ×0.5 |
| F | 20 | ×0.75 |
| E | 30 | ×1.0 |
| D | 40 | ×1.1 |
| C | 50 | ×1.2 |
| B | 60 | ×1.35 |
| A | 70 | ×1.5 |

Mastery already earned is never lost; a poorer weapon in hand only caps how much of it is USED and how much can
be EARNED. Skill levels are not touched by the grade: every level bought counts with any weapon of the skill's
type. Training speed applies to Weapon Mastery.

## 7. Recycle — half of what the piece provably consumed

Recycling destroys the piece and returns, each halved and rounded down: for **every grade below the current
one**, that grade's full fold cap AND one quench (both at that grade's own prices — the gate above is what makes
those folds provable); the folds standing on the piece now, at its own grade's price; and one Ink per written stat,
its tier read off the value. **Folds made at Raw return nothing**, standing or climbed — no Temper Dust and no gold
— though the Raw quench does. Failed quenches, the folds they spent, and Salt are not recorded and come back as
nothing. A piece the first tutorial still needs cannot be recycled.

| Piece | Comes back |
| --- | --- |
| Raw +5 Sword (ready to quench) | nothing |
| F +4 Sword | 4 Temper Dust, 450 g |
| E +0 Sword | 10 Temper Dust, 1 Quench Oil, 1,250 g |
| A +35 Sword | 347 Temper Dust, 10 Quench Oil, 22,625 g |
| … with 2 Watery Ink stats written | + 1 Watery Ink |

## 8. Worked example — a dropped Common sword from Raw to A

| Step | Pays | Result |
| --- | --- | --- |
| A kill drops it | nothing | Raw +0 |
| Fold ×5 | 5 Temper Dust + 250 g | Raw +5 (+9 Attack) |
| Quench (allowed: at the +5 cap) | 1 Quench Oil + 500 g, 80% | F +0 |
| Fold ×10 | 20 Temper Dust + 1,000 g | F +10 (+18 Attack) |
| Quench | 2 Quench Oil + 1,000 g, 60% | E +0 (2 Ink slots) |
| Fold ×15 | 45 Temper Dust + 2,250 g | E +15 (+28 Attack) |
| Quench | 3 Quench Oil + 1,500 g, 50/30/20 | D +0 (3 slots) — or back to F +0, and the last written stat gone |
| … | | |
| Fold ×35 at A | 245 Temper Dust + 12,250 g | A +35 (+82 Attack), 6 Ink slots |
