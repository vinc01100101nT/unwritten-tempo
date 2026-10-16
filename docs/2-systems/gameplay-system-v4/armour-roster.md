---
status: "BUILT 2026-09-23 — not play-tested. Decided with the user on 2026-09-23; the word amounts are first guesses. Off-hand removed and two shoe words added 2026-10-06; a piece's base by material and slot built 2026-10-14."
owner: vince
last-updated: 2026-10-14
tags:
  - systems-combat-design
---

# Armour Roster, Materials & Words

Armour has **four slots**, every slot comes in **three materials**, and every piece rolls **one word** from its
slot. The material decides what a piece's base buys; the slot decides how much, and which words it can roll.

## 1. The slots

**Head, Body, Garment, Shoes** — the names a player reads (`EquipSlots.Label`). In the code they are `Head`, `Armor`,
`Garment` and `Boots`; the numbers are pinned for saved data, only the words changed. Shoes are shown in the
equipment window. **No armour design is made for the Head** (decided 2026-10-16): the Iron Helm, Leather Cap and
Padded Hood were removed from the game, and the Head slot holds only the fifteen hats that have a 3D model. A hat is
not armour: it adds fixed primary stats and one effect, never rolls a word, and no creature wears one — see
[Hair and Headgear](../hair-and-headgear.md).

**There is no off-hand (built 2026-10-06).** Shields are gone. Blocking and sending damage back belong to the Fist's
defence skills (block chance is a sub-stat only a skill grants), and extra ordinary-attack reach to the Sniper's own
long reach.

## 2. What a piece's base buys — the material decides, the slot scales

A body or a garment is worth one sixth of a 1-second weapon's base (50, a Dagger's), priced with Ink's own exchange rate — 1 defence = 8 HP =
4 MP = 0.5% attack speed, and evasion at double defence, since each point takes 1% off the hits that land — split
between its material's two stats. Head and shoes take half. Sized so a full metal set's defence at equal gear is 25%
of a Dagger's attack (20% of a Sword's). The roll, the rarity, and every fold and quench multiply that base the way they do a weapon's,
rounded half up once (attack speed to one decimal). Full table: [Crafting Tables](crafting-tables.md).

| Base, Common, Raw +0, unrolled | Body, garment | Head, shoes |
| --- | --- | --- |
| **Metal** | 4 defence + 33 HP | 2 defence + 17 HP |
| **Leather** | 2.1% attack speed + 2 evasion | 1% attack speed + 1 evasion |
| **Cloth** | 4 magic defence + 17 MP | 2 magic defence + 8 MP |

**No piece gives movement speed** (decided 2026-10-14). A person moves at the shared pace, changed only by timed
effects such as a Haste or a Slow; a creature moves at that pace times its own movement speed.

**Worn accuracy and evasion reach the hit roll** — Ink's Accuracy and Evasion lines and leather's evasion are added into
HIT and FLEE before the ring's lift (`UnitStatMath.Hit` / `Flee`), for players and creatures alike.

## 3. The roster — nine designs

| Slot | Metal (Smith) | Leather (Tannery) | Cloth (Loom) |
| --- | --- | --- | --- |
| Body | Chainmail | Leather Jerkin | Padded Vest |
| Garment | Chain Mantle | Hide Cloak | Traveller's Cloak |
| Shoes | Iron Sabatons | Leather Boots | Woven Shoes |

No design is made from materials: a piece is dropped by a creature that wears it. Every piece sells to a shop for
5u. Every pair of shoes weighs 30.

## 4. Words — the armour's trademark

- **Rolled once, as the piece comes into the world**, every word of the piece's slot equally likely, through the one random source; never
  changed afterwards. A creature's armour rolls one too, at every grade, every life, the way its weapon rolls a trademark.
- **Each word does exactly one thing, and lives on exactly one slot**, so a character can never wear the same word
  twice — which is also why one word's size never has to allow for stacking with itself.
- **Part of the name**: `+7 Tireless Iron Sabatons [C]`. The card adds one line: the word, then what it does at this
  piece's size.
- **Its size grows with grade** — the finished amount is written by the server; a player's machine only shows it.
- **Saved by name** in the copy's roll text, beside the trademark. A new character's starting Chainmail, Iron
  Sabatons and Chain Mantle are made with none.
- **The three Head words (Ironblood, Steadfast, Wellspring) have no piece to roll on**: no armour is made for the
  Head, and hats roll no word.

| Slot | Word | Its one job | Raw → A |
| --- | --- | --- | --- |
| Head | **Ironblood** | fewer critical hits land on you | −0.9% → −7.5% crit chance (worth 3 → 25 LUK of crit shield) |
| Head | **Steadfast** | stuns, freezes, silences and ailments on you end sooner | 5% → 25% shorter |
| Head | **Wellspring** | mana regenerates faster | +0.5 → +2.5 MP a second |
| Body | **Bulwark** | damage taken is cut | 1% → 8% |
| Body | **Sanguine** | damage dealt heals you | 1% → 5% |
| Body | **Fierce** | all damage dealt is higher, spells included | 1% → 8% |
| Garment | **Emberward / Tideward / Stoneward / Galeward** | less damage from Fire / Water / Earth / Wind | 3% → 15% (never touches an immune or healing hit) |
| Shoes | **Tireless** | sprint drains stamina slower | 5% → 25% |
| Shoes | **Anchored** | knockbacks and pulls move you a shorter distance | 5% → 25% |
| Shoes | **Wayfarer** | HP regenerates faster | +1 → +5 HP a second |

Per grade (Raw, F, E, D, C, B, A): 1 / 2 / 3 / 4 / 5 / 6.5 / 8 for Bulwark and Fierce; 1 / 1.5 / 2 / 2.5 / 3 / 4 / 5
for Sanguine; 5 / 8 / 11 / 14 / 17 / 21 / 25 for Steadfast, Tireless and Anchored; 1 / 1.5 / 2 / 2.5 / 3 / 4 / 5 HP a
second for Wayfarer — Wellspring's mana doubled, since 8 HP is worth 4 MP; 3 / 5 / 7 / 9 / 11 / 13 / 15 for the wards; 0.9 / 1.8 / 2.7 / 3.6 / 4.5 / 6 / 7.5 for
Ironblood; 0.5 / 0.75 / 1 / 1.25 / 1.5 / 2 / 2.5 for Wellspring. **First guesses, to be tuned in play.**

## 5. How it is built

- The numbers: `CraftingMath.PieceNumbers` (what a piece is worth, returning `PieceStats`) and `ArmourWords` (the slots'
  words, their sizes, the card line). Pinned by the game-maths tests.
- **One lift for the words that move an existing stat**: the unit's state store reads what the unit wears
  (`UnitStates.WornChanged`) and `ArmourWords.FoldIntoSubStats` adds each word into the same sub-stat table a timed buff
  moves — so damage dealt, damage taken, life steal, and HP and MP regeneration take worn words wherever they are read, for
  players and creatures. Death clears timed effects, never what is worn.
- The new numbers each have one read site: crit shield (`CritShieldLuk`), ailment length (`StatusController.Apply`),
  the element wards (`Elements.Warded`, in each unit's element lookup) and sprint
  drain (`Player3DController`).
- Rolled in `EquipmentInstance.CreateFresh` — a dropped piece, a grant, a creature's gear — stored as
  `EquipmentInstance.word` / `wordAmount`, named by `ItemTooltip.CopyTitle`.

## Related

- [Crafting Tables](crafting-tables.md) · [Weapon Names & Trademarks](weapon-names-and-trademarks.md) ·
  [Elements & Converters](elements-and-converters.md) · [Gameplay System — V4](README.md)
