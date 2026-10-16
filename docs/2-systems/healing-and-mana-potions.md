---
status: "BUILT 2026-10-13 as saved game data; not play-tested."
owner: vince
last-updated: 2026-10-16
tags:
  - economy
  - items
---

# Healing Potions and Mana Draughts — Sizes, Prices and Weights

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

## What the player gets

- **Five sizes of Healing Potion and five sizes of Mana Draught**, all sold by the Tool Dealer.
- **Every potion has a fixed price, and what it restores never follows the drinker's level,** the map, or the
  creature it came from.
- **A Healing Potion restores a rolled amount of health at once**, like an RO potion: each drink is a whole number
  from the lowest to the highest in its range (a Minor Healing Potion restores 130 to 170). The server rolls it.
- **A Mana Draught restores a fixed amount of mana** in five equal parts, one a second for 5 seconds.
- **A Mana Draught costs the same as the Healing Potion of its size.**

## The ten potions

| Size | Healing Potion | Health restored | Mana Draught | Mana restored | Value | Buy (with Tax) | Sells back for | Weight |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Minor Healing Potion | 130–170 | Minor Mana Draught | 25 (5 a second) | 38 | 51 | 25 | 7 |
| 2 | Healing Potion | 240–320 | Mana Draught | 75 (15 a second) | 168 | 224 | 112 | 8 |
| 3 | Greater Healing Potion | 520–680 | Greater Mana Draught | 200 (40 a second) | 600 | 800 | 400 | 10 |
| 4 | Superior Healing Potion | 1,150–1,450 | Superior Mana Draught | 500 (100 a second) | 1,875 | 2,500 | 1,250 | 12 |
| 5 | Grand Healing Potion | 2,800–3,600 | Grand Mana Draught | 1,250 (250 a second) | 5,625 | 7,500 | 3,750 | 15 |

## The rules behind the numbers

- **The sizes follow how big a health bar can be,** on the starting health of 160 (decided 2026-10-16). Maximum
  health runs from a new character's to its most on a level 99 character with every stat point in VIT, a full set
  of grade A +10 metal armour, a top health Ink and a top VIT Ink on each piece, and a level 70 weapon mastery that
  raises health. Size 1 is for a new character's bar (163 to 218 health over the first ten levels); size 2 a level
  25 bar with nothing invested (310); size 3 a level 25 to 50 bar with the points in VIT (547 to 1,135); size 4 a
  level 50 to 75 bar with the points in VIT (1,135 to 1,800); size 5 is for geared characters (1,800 and up).
- **The prices were not raised when starting health went from 20 to 160** (decided 2026-10-16). The extra health was
  given for a new character's survival, not to change what hunting costs, so each potion kept its price and now
  restores more for it.
- **A bigger potion costs more for each point:** at the shop price and the middle of each range, about 0.3u a health point at size 1,
  then 0.8, 1.3, 1.9 and 2.3. A big bar can still be filled with small potions, at the cost of more drinks and more
  weight carried.
- **Mana Draughts kept their amounts,** because the mana bar did not change (10 mana at level 1, gaining 3 a level).
- **Each potion has one value; the shop adds Tax when it sells one and takes Tax when it buys one back** — a third of
  the value, rounded up, on each unit ([The Shop Cart, the Tax, and the Station Window](shop-cart-and-station-window.md)).
  Selling one back therefore pays about half of what buying it cost.
- **Weight rises with size** — 7, 8, 10, 12, 15 — and a Mana Draught weighs what the Healing Potion of its size
  weighs. A character carries 2,000 weight plus 30 for each point of STR.

## Where they come from

- **The Tool Dealer stocks all ten, and is the only source.** Going back to town to restock is part of the loop.
- **No creature drops one** (decided 2026-10-13). A kill gives up only the creature's own find, a food or an element
  converter ([Town Supplies and Hunting Finds](town-supplies-and-hunting-finds.md)).
- **Torvald's side quest pays 5 Minor Healing Potions** — the one potion not bought
  ([Quests and Tutorials](quests-and-tutorials.md)).
- **A new character starts with 100 Newbie Potions** — not one of the ten: "You just woke up holding these", the
  Minor Healing Potion's picture, heals 80 to 100 at once (a Minor heals 130 to 170), weighs nothing, and can never be dropped, sold,
  stored or traded, so a throwaway character's potions are worth nothing to anybody else.

## Where it lives

- The ten items: `unity/Assets/3_Data/Resources/Items/` — `value`, `weight`.
- What each restores: the Heal effect its item runs — its HP and its "HP up to", `unity/Assets/3_Data/Resources/Skills/skill.item.<name>.asset`.
- The Tool Dealer's stock: `unity/Assets/3_Data/Resources/WorldShops.asset`.

## Still open

- **The amounts and the price for each point are first picks.** How much health a player loses for each kill has
  not been measured, so nothing says yet how many potions an hour of hunting uses.
- **The five new potions' descriptions are drafts.**
- **The top of the health range is not measured since gear gained a base.** A full Common A +10 metal set alone gives
  5,000 health, so size 5's reach, and whether a sixth size is needed, is checked again.
