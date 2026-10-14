---
status: "LIVE — every number below was read from the saved item assets, the character settings and the crafting arithmetic on 2026-10-14. Change the asset or the code, then this page."
owner: vince
last-updated: 2026-10-14
tags:
  - systems-combat-design
---

# Crafting Tables — what every station takes, grade by grade

The reference companion to [the V4 design](README.md). That page says WHY; this one is only the numbers as
they are in the game today. Sell prices live on the item assets under `unity/Assets/3_Data/Resources/Items/`; every
base, cost, chance and per-grade rule lives in `unity/Assets/Scripts/CraftingMath.cs`, and what a kill drops in
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

| What a shop pays for one | Unsigned |
| --- | --- |
| Any weapon or armour piece | 5 |
| Temper Dust | 2 |
| Quench Oil | 10 |
| Watery / Thin / Faint / Clear / Sharp Ink | 2 / 4 / 8 / 16 / 32 |
| Coarse / Fine / Pure Salt | 2 / 6 / 18 |

No shop sells any of these.

## 2. Where a piece comes from — dropped, never made

Every kill has a **10%** chance to drop **one** random piece of what the creature wears, as a new copy: always
**Raw +0**, its base (§3) and its trademark (a weapon) or word (an armour piece) rolled, and its rarity rolled by
the creature's kind.
A new character starts wearing a Raw +0 Common Sword, Chainmail and Iron Sabatons with no trademark and no word.

| Dropped by | Rarity of the piece |
| --- | --- |
| A normal creature | Common 53.2% · Uncommon 31.9% · Rare 14.9% (shares of 50 : 30 : 14) |
| An elite | always Epic |
| A shadow | always Legendary |

What a creature wears, and every piece that was not dropped, is Common and unrolled (§3).

## 3. What a piece is worth

**Worth = base × roll × rarity × (1 + every fold and quench percentage, added together)**, rounded half up once, at
the end (attack speed to one decimal). The server writes it onto the copy.

### The base, Common, at a 100% roll

A weapon's base is **50 × its swing time in seconds**, divided by 1 + the off hand's power (0.5) for a pair weapon,
so every weapon deals the same damage a second at equal gear. The swing times are the character settings' own.

| Weapon | Swing time | Base |
| --- | --- | --- |
| Katar (a pair) | 0.85 s | 28 |
| Axe (a pair) | 1.1 s | 37 |
| Dagger, Fist | 0.8 s | 40 |
| Shuriken | 0.9 s | 45 |
| Sword, Wand | 1.0 s | 50 |
| Spear, Bow | 1.1 s | 55 |
| Staff | 1.2 s | 60 |
| Scythe | 1.25 s | 63 |
| Greatsword, Warhammer | 1.3 s | 65 |
| Sniper | 1.4 s | 70 |

Staff, Wand and Scythe give Magic Attack; every other weapon gives Attack.

**An armour piece's MATERIAL decides what its base buys, and its SLOT how much.** A body or a garment is worth one
sixth of a sword's base on Ink's exchange rate — 1 defence = 8 HP = 4 MP = 0.5% attack speed, and evasion at double
defence — split between its material's two stats; head and shoes half. Sized so a full metal set's defence at
equal gear is 25% of a sword's attack. No piece gives movement speed.

| Material | Body or garment | Head or shoes |
| --- | --- | --- |
| Metal | 4 Defence and 33 HP | 2 Defence and 17 HP |
| Leather | 2.1% Attack Speed and 2 Evasion | 1% Attack Speed and 1 Evasion |
| Cloth | 4 Magic Defence and 17 MP | 2 Magic Defence and 8 MP |

### The roll

Only a piece a creature **drops** rolls its base, at **85% to 115%** in whole percents, evenly, on the server, saved
on the copy. A new character's pieces, a shop's, a reward, a GM grant and what a creature wears are unrolled and
count as 100%. The tooltip's Base line is base × roll, before rarity: **Base 54 (108%)**; an armour piece shows its
first stat; an unrolled piece shows **Base 50**, with no percent; gear shown with no copy behind it (a shop's row for
sale) has no Base line.

### Rarity multiplies the base

| Rarity | Common | Uncommon | Rare | Epic | Legendary |
| --- | --- | --- | --- | --- | --- |
| × base | 1.00 | 1.10 | 1.20 | 1.35 | 1.50 |

### Folds and quenches add percentages of the base

| Grade | Each fold adds | The quench that reached it adds | Total at +0 | Total at +10 | A Common sword at +10 | Ink slots |
| --- | --- | --- | --- | --- | --- | --- |
| Raw | 10% | — | 0% | 100% | 100 | 0 |
| F | 20% | 100% | 200% | 400% | 250 | 1 |
| E | 30% | 200% | 600% | 900% | 500 | 2 |
| D | 40% | 300% | 1,200% | 1,600% | 850 | 3 |
| C | 50% | 400% | 2,000% | 2,500% | 1,300 | 4 |
| B | 60% | 500% | 3,000% | 3,600% | 1,850 | 5 |
| A | 70% | 600% | 4,200% | 4,900% | **2,500** | 6 |

Common body armour at A +10: **208 Defence and 1,667 HP** (metal), **104.2% Attack Speed and 104 Evasion**
(leather), **208 Magic Defence and 833 MP** (cloth). A full Common A +10 metal set is **624 Defence**, 25% of the
sword's 2,500. A Legendary A +10 sword at a 115% roll is 50 × 1.15 × 1.50 × 50 = 4,312.5 → **4,313 Attack**.

## 4. Fold and quench — what each try costs, and its odds

A station's fee is priced in **kills of income at the grade's level**: a normal kill is worth 10u × the creature's
level ([Creature Materials](../../3-world/creature-materials.md)), and a grade's level is the middle of its seventh
of levels 1–99. **A fold try costs 0.6 kills of income, a quench try 6.** Each try also costs the grade's step in
**Temper Dust** (a fold) or **Quench Oil** (a quench). Every try is paid for, whether it succeeds or not.

| Grade | Raw | F | E | D | C | B | A |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Level it is priced at | 7 | 21 | 35 | 50 | 64 | 78 | 92 |
| Temper Dust per fold | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| Unsigned per fold | 42 | 126 | 210 | 300 | 384 | 468 | 552 |
| Quench Oil per quench | 1 | 2 | 3 | 4 | 5 | 6 | — |
| Unsigned per quench | 420 | 1,260 | 2,100 | 3,000 | 3,840 | 4,680 | — |

### Fold

Every grade's fold cap is **+10**. A piece cannot be folded past it, and **a piece must be at +10 to be quenched** —
the cap is the gate. **+0 to +4 always succeed.** From +4 a fold can fail; **a failed fold takes off one fold and
never goes below +4.** The station shows "Chance: X% · If it fails: back to +N".

| Grade | +4→5 | +5→6 | +6→7 | +7→8 | +8→9 | +9→10 |
| --- | --- | --- | --- | --- | --- | --- |
| Raw | 90% | 80% | 70% | 60% | 50% | 40% |
| F | 76% | 68% | 60% | 51% | 42% | 34% |
| E | 65% | 58% | 50% | 43% | 36% | 29% |
| D | 54% | 48% | 42% | 36% | 30% | 24% |
| C | 45% | 40% | 35% | 30% | 25% | 20% |
| B | 36% | 32% | 28% | 24% | 20% | 16% |
| A | 22% | 20% | 17% | 15% | 12% | 10% |

A tutorial's free fold is checked before the roll and used up only on a success, so a failed free fold stays free;
it is free only while the piece is below the tutorial step's own fold target.

### Quench

A quench either moves the piece **up one grade, to +0**, or fails — there is no "unchanged" outcome. A is the top
and is never quenched.

| From | Raw | F | E | D | C | B |
| --- | --- | --- | --- | --- | --- | --- |
| Chance to go up | 40% | 30% | 18% | 11% | 6.5% | 3.5% |

### Salt

One Salt may go in, chosen in the window, and it is spent whatever happens. **What a failure does is the Salt's
answer**, and the better Salts also raise the chance. The chance to go up never passes **95%**.

| Salt | Rarity | If the quench fails | Chance to go up | Merges |
| --- | --- | --- | --- | --- |
| None | — | Drops one grade, to +0 (a Raw piece stays Raw, at +0), and the last Ink stat is lost | as the table | — |
| Coarse Salt | Uncommon | Keeps its grade, at +4 | as the table | 20 → 1 Fine Salt |
| Fine Salt | Epic | Keeps its grade, at +4 | × 1.25 | 5 → 1 Pure Salt |
| Pure Salt | Legendary | Keeps its grade, at +9 | × 1.5 | — |

The station shows "Chance to rise: X% · If it fails: drops to E +0", or "… stays F, back to +4".

**Where Salt drops, and what it is worth.** A Salt's worth is the kills of a normal creature it takes to find one,
at the yardstick of 500 kills an hour ([Creature Materials](../../3-world/creature-materials.md)); the merges follow
that worth.

| Salt | A normal kill | An elite kill | A shadow kill | Kills to find one |
| --- | --- | --- | --- | --- |
| Coarse | 10% | — | — | 10 |
| Fine | 0.5% | 20% | 40% | 200 |
| Pure | 0.1% | 5% | 15% | 1,000 |

Nobody is told which Salt to use. Each is the cheapest at some grades, worked out from what it saves against what it
costs:

| Climb | Cheapest Salt | Average kills for the climb, Salt and fees included |
| --- | --- | --- |
| Raw → F | none | 210 |
| F → E | Coarse | 430 |
| E → D | Coarse | 1,250 |
| D → C | Coarse | 4,730 |
| C → B | Fine | 19,800 |
| B → A | Pure | 102,700 |
| A +0 → A +10 | — | 110,200 |
| **Raw +0 → A +10** | | **239,300 kills, about 479 hours**, of which the fees are about 116 |

## 5. Ink — one stat per Ink, the player picks the slot

Using an Ink writes **one** stat at the slot chosen: an empty slot is filled, an occupied one is replaced. A stat
already on the piece cannot roll again. The Ink is always spent; there is no failure outcome. The grade sets how
many slots the piece has (the last column of the table in §3).

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
one**, that grade's ten folds AND one quench (both at that grade's own prices — the gate above is what makes
those folds provable); the folds standing on the piece now, at its own grade's price; and one Ink per written stat,
its tier read off the value. **Folds made at Raw return nothing**, standing or climbed — no Temper Dust and no Unsigned
— though the Raw quench does. Failed tries, folds or quenches, and Salt are not recorded and come back as nothing.
A piece the first tutorial still needs cannot be recycled.

| Piece | Comes back |
| --- | --- |
| Raw +10 Sword (ready to quench) | nothing |
| F +0 Sword | 210u |
| F +4 Sword | 4 Temper Dust, 462u |
| E +0 Sword | 10 Temper Dust, 1 Quench Oil, 1,470u |
| A +10 Sword | 135 Temper Dust, 10 Quench Oil, 17,850u |
| … with 2 Watery Ink stats written | + 1 Watery Ink |

## 8. Worked example — a dropped Common sword from Raw to A

Attack is the sword's own, at a 100% roll.

| Step | Pays, each try | Result |
| --- | --- | --- |
| A kill drops it | nothing | Raw +0 (50 Attack) |
| Fold ×4 | 1 Temper Dust + 42u, always lands | Raw +4 (70) |
| Fold to +10 | 1 Temper Dust + 42u, 90% down to 40%; a failure back one, never below +4 | Raw +10 (100) |
| Quench | 1 Quench Oil + 420u, 40% | F +0 (150, 1 Ink slot) — or Raw +0 with no Salt |
| Fold to +10 | 2 Temper Dust + 126u | F +10 (250) |
| Quench | 2 Quench Oil + 1,260u, 30% | E +0 (350, 2 slots) — or with Coarse Salt, F +4 |
| … | | |
| Fold to +10 at A | 7 Temper Dust + 552u, 22% down to 10% | A +10 (2,500 Attack), 6 Ink slots |

A level 99 character with 99 STR, 30 DEX and 1 LUK swinging that Common A +10 sword has 2,824 Attack. A Thousand
Petals dash strikes for 100% of Attack, so it deals about **2,000** to a level 99 creature in Common A +10 metal
armour (624 Defence from the armour and about 200 from its stats), before crits, elements and held charges.
