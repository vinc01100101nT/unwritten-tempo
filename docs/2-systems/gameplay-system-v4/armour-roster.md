---
status: "BUILT 2026-09-23 — not play-tested. Decided with the user on 2026-09-23; the word amounts are first guesses."
owner: vince
last-updated: 2026-09-23
tags:
  - systems-combat-design
---

# Armour Roster, Materials & Words

Armour has **five slots**, every slot comes in **three materials**, and every forged piece rolls **one word** from its
slot. The material decides what a piece's folds buy; the slot decides how much, and which words it can roll.

## 1. The slots

**Head, Body, Off-hand, Garment, Shoes** — the names a player reads (`EquipSlots.Label`). In the code they are
`Head`, `Armor`, `Shield`, `Garment` and `Boots`; the numbers are pinned for saved data, only the words changed. The
off-hand is still where a block comes from. Shoes are shown in the equipment window.

## 2. What a fold buys — the material decides, the slot scales

Priced with Ink's own exchange rate — 1 defence = 8 HP = 4 MP = 0.5% attack speed, and evasion at double defence,
since each point takes 1% off the hits that land — so a fold is worth the one defence it bought before, split between
two stats. Body, off-hand and garment take the full amount; head and shoes half. The grade's lift applies on top,
rounded half up (attack speed to one decimal). Full table: [Crafting Tables](crafting-tables.md).

| Each fold buys | Body, off-hand, garment | Head, shoes |
| --- | --- | --- |
| **Metal** | 0.5 defence + 4 HP | 0.25 defence + 2 HP |
| **Leather** | 0.25% attack speed + 0.25 evasion | 0.125% attack speed + 0.125 evasion |
| **Cloth** | 0.5 magic defence + 2 MP | 0.25 magic defence + 1 MP |

**Worn accuracy and evasion reach the hit roll** — Ink's Accuracy and Evasion lines and leather's evasion are added into
HIT and FLEE before the ring's lift (`UnitStatMath.Hit` / `Flee`), for players and creatures alike.

## 3. The roster — fifteen designs

| Slot | Metal (Smith) | Leather (Tannery) | Cloth (Loom) |
| --- | --- | --- | --- |
| Head | Iron Helm | Leather Cap | Padded Hood |
| Body | Chainmail | Leather Jerkin | Padded Vest |
| Off-hand | Iron Kite Shield | Leather Buckler | Padded Shield |
| Garment | Chain Mantle | Hide Cloak | Traveller's Cloak |
| Shoes | Iron Sabatons | Leather Boots | Woven Shoes |

Every design forges for 5 band-1 units of its material and 400 gold. Weight and sell value copy an existing design
(shoes copy the Leather Cap's); each off-hand blocks 5% of physical blows. The **Leather Buckler** was the Wooden Buckler and keeps its id
(`item.wooden-buckler`), so a save holding one still loads it.

## 4. Words — the armour's trademark

- **Rolled once, at the forge**, every word of the piece's slot equally likely, through the one random source; never
  changed afterwards. A creature's armour above Raw rolls one too, every life, the way its weapon rolls a trademark.
- **Each word does exactly one thing, and lives on exactly one slot**, so a character can never wear the same word
  twice — which is also why one word's size never has to allow for stacking with itself.
- **Part of the name**: `+15 Fleet Iron Sabatons [C]`. The card adds one line: the word, then what it does at this
  piece's size.
- **Its size grows with grade** — the finished amount is written by the server; a player's machine only shows it.
- **Saved by name** in the copy's roll text, beside the trademark. A piece forged before words existed has none and
  keeps its old name.

| Slot | Word | Its one job | Raw → A |
| --- | --- | --- | --- |
| Head | **Ironblood** | fewer critical hits land on you | −0.9% → −7.5% crit chance (worth 3 → 25 LUK of crit shield) |
| Head | **Steadfast** | stuns, freezes, silences and ailments on you end sooner | 5% → 25% shorter |
| Head | **Wellspring** | mana regenerates faster | +0.5 → +2.5 MP a second |
| Body | **Bulwark** | damage taken is cut | 1% → 8% |
| Body | **Sanguine** | damage dealt heals you | 1% → 5% |
| Body | **Fierce** | all damage dealt is higher, spells included | 1% → 8% |
| Off-hand | **Thornclad** | damage taken is sent back | 2% → 10% |
| Off-hand | **Guarding** | blocks more often | +1% → +8% (block chance held under 95%) |
| Off-hand | **Longreach** | ordinary attacks reach further | +0.1 → +0.5 units |
| Garment | **Emberward / Tideward / Stoneward / Galeward** | less damage from Fire / Water / Earth / Wind | 3% → 15% (never touches an immune or healing hit) |
| Shoes | **Fleet** | you move faster | 2% → 10% |
| Shoes | **Tireless** | sprint drains stamina slower | 5% → 25% |

Per grade (Raw, F, E, D, C, B, A): 1 / 2 / 3 / 4 / 5 / 6.5 / 8 for Bulwark, Fierce and Guarding; 2 / 3 / 4 / 5 / 6 /
8 / 10 for Thornclad and Fleet; 1 / 1.5 / 2 / 2.5 / 3 / 4 / 5 for Sanguine; 5 / 8 / 11 / 14 / 17 / 21 / 25 for
Steadfast and Tireless; 3 / 5 / 7 / 9 / 11 / 13 / 15 for the wards; 0.9 / 1.8 / 2.7 / 3.6 / 4.5 / 6 / 7.5 for
Ironblood; 0.5 / 0.75 / 1 / 1.25 / 1.5 / 2 / 2.5 for Wellspring; 0.1 / 0.15 / 0.2 / 0.25 / 0.3 / 0.4 / 0.5 for
Longreach. **First guesses, to be tuned in play.**

## 5. How it is built

- The numbers: `CraftingMath.PieceNumbers` (what a fold buys, returning `PieceStats`) and `ArmourWords` (the slots'
  words, their sizes, the card line). Pinned by the game-maths tests.
- **One lift for the words that move an existing stat**: the unit's state store reads what the unit wears
  (`UnitStates.WornChanged`) and `ArmourWords.FoldIntoSubStats` adds each word into the same sub-stat table a timed buff
  moves — so damage dealt, damage taken, reflect, life steal, MP regeneration, movement speed and reach take worn words
  wherever they are read, for players and creatures. Death clears timed effects, never what is worn.
- The new numbers each have one read site: crit shield (`CritShieldLuk`), ailment length (`StatusController.Apply`),
  block chance (`Combat.Blocked`), the element wards (`Elements.Warded`, in each unit's element lookup) and sprint
  drain (`Player3DController`).
- Rolled at `NetPlayer.CmdForge` (players) and `EquipmentInstance.CreateAt` (creatures), stored as
  `EquipmentInstance.word` / `wordAmount`, named by `ItemTooltip.CopyTitle`.

## Related

- [Crafting Tables](crafting-tables.md) · [Weapon Names & Trademarks](weapon-names-and-trademarks.md) ·
  [Elements & Converters](elements-and-converters.md) · [Gameplay System — V4](README.md)
