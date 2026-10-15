---
status: "BUILT 2026-10-13 and compiled; not play-tested."
owner: vince
last-updated: 2026-10-13
tags:
  - economy
  - items
  - loot
---

# Town Supplies and Hunting Finds — Where Every Consumable Comes From

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

## What the player gets

- **Every consumable has one source.** It is either a **town supply**, bought from the Tool Dealer and never dropped,
  or a **hunting find**, dropped by creatures and never sold in a shop.
- **Town supplies are the reason to go back to town.** Potions, wings and the Antidote run out, and only the Tool
  Dealer has more.
- **A hunting find is worth using or trading.** No shop buys one, so it adds nothing to what a kill pays in Unsigned.

## Town supplies

Flat prices, the same for every character. Each item has one value; the shop adds Tax (a third of the value, rounded
up, per unit) when it sells one and takes it when it buys one back.

| Item | Does | Value | Buy (with Tax) | Sells back for | Weight |
| --- | --- | --- | --- | --- | --- |
| Fly Wing | Sets you down on a random spot of the map you are on | 38 | 51 | 25 | 5 |
| Antidote | Removes 3 debuffs | 75 | 100 | 50 | 5 |
| Butterfly Wing | Returns you to your save point, on whichever map it is | 225 | 300 | 150 | 5 |

The ten Healing Potions and Mana Draughts are town supplies too:
[Healing Potions and Mana Draughts](healing-and-mana-potions.md).

- **Fly Wing** costs what the smallest potion costs: it is used many times an hour.
- **Antidote** sits between the size 1 and size 2 potions.
- **Butterfly Wing** costs 300u at the shop, the same for every character, while the Keeper's teleport charges
  40u × the character's level a door ([The Keeper and the Wings](keeper-and-wings.md)).

## Hunting finds

**Each creature has one find, decided by its element.** A creature of an element that has converters gives up one of
that element's two converters; a creature of any other element gives up that element's food.

| Element | Find | What it does |
| --- | --- | --- |
| Fire | Flame Elemental Converter or Flame Armor Converter | weapon strikes as Fire / armour is Fire, for 5:00 |
| Water | Frost Elemental Converter or Frost Armor Converter | weapon strikes as Water / armour is Water, for 5:00 |
| Earth | Seismic Elemental Converter or Seismic Armor Converter | weapon strikes as Earth / armour is Earth, for 5:00 |
| Wind | Lightning Elemental Converter or Lightning Armor Converter | weapon strikes as Wind / armour is Wind, for 5:00 |
| Neutral | Hard Bread | +15% defence for 5:00 |
| Poison | Spiced Broth | +10% attack speed for 5:00 |
| Dark | Lucky Dumpling | +5% crit chance for 5:00 |
| Undead | Grilled Fish | +15% attack for 5:00 |
| Ghost | Sage Stew | +15% magic attack for 5:00 |
| Holy | Traveller's Tea | +20% movement speed for 5:00 |

**How often: one roll a kill, by the creature's kind.**

| Kind | Chance a kill gives up its find | How many |
| --- | --- | --- |
| Normal | 5% | 1 |
| Elite | 50% | 1 |
| Shadow | 100% | 1 |

- **A creature with two converters gives up one of them, each equally likely** — so a named converter is half its
  kind's chance.
- **A rebel rolls at one kind up**, like the rest of what it carries.
- **No shop sells a find and none buys one.**
- **A food lasts 5 minutes**, the same as a converter. Eating the same food again while it runs starts its 5 minutes
  over and does not double it; different foods run side by side. Nothing eats a food for the player: auto-potion only
  drinks what restores health or mana ([Auto-Combat](auto-combat.md)).
- **Food weights:** Traveller's Tea 6, Lucky Dumpling 8, Spiced Broth 10, Hard Bread 12, Grilled Fish 12, Sage Stew
  12. A converter weighs 8.

## Where it lives

- Which find an element carries, and each kind's chance: `CreatureFinds.cs`.
- The kill's roll: `LootDropper.GiveLoot`. What `/mobinfo`, `/whodrops` and the wiki say a creature drops:
  `CreatureDrops.cs` — both read `CreatureFinds`.
- A converter's element is read off the effect it runs (`ItemCatalog.ConvertersOf`); the item carries no copy.
- Values and weights: the item files in `unity/Assets/3_Data/Resources/Items/`. The Unsellable tag, never the value, is
  what makes a shop refuse an item; an item priced 0 without it sells for 0.
- The Tool Dealer's stock: `unity/Assets/3_Data/Resources/WorldShops.asset`.

## Still open

- **The chances and the food's length are first picks.** At 5% a food takes 20 kills on average and lasts 5 minutes,
  so keeping one running from your own drops needs a kill every 15 seconds on creatures of one element; a named
  converter needs a kill every 7.5 seconds. How fast a player really kills has not been measured.
- **Which food goes with which element is a first pick.**
- **Only Lastline has a Tool Dealer**, so every town supply is bought in one town.
- **Sage Stew's and the eight converters' descriptions are drafts.**
- **Whether the Butterfly Wing's price should follow the level**, as the Keeper's teleport does, is to be decided: at
  300u it is the price of two and a half doors at level 3, and of less than one door from level 8 up.
