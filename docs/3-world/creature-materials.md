---
status: "BUILT, not play-tested. Every creature names its one material; every material's tier and price are worked out from its creature by the editor and proven by tools/check-creature-materials.py."
owner: vince
last-updated: 2026-10-31
tags:
  - world-content
  - economy
---

# Creature Materials — What Each Creature Drops and What It Sells For

> **Never append. Always update in place.** This page states the rules. Which creature drops what, at what chance and
> price, is never typed here: it is read from the game's own data (see **Where to read the numbers**).

## What the player gets

- **No kill drops Unsigned.** A player earns Unsigned by selling what creatures drop to a shop.
- **Every creature drops exactly one material of its own, and only it drops that material** — normals, elites and
  shadows alike. A Tainted or Blighted version of a creature is a creature of its own and has its own material.
- **A creature's multiplier decides its material's tier.** The multiplier is the one difficulty dial typed on each
  creature ([What a Kill Pays](what-a-kill-pays.md)): the harder the creature, the rarer and more valuable its material.
- **A material sells for more the higher its creature's level and the rarer its tier.** No price is typed on a
  material: the editor writes each one from its creature whenever an item is saved.
- **The materials are what hat quests and material quests ask for** (planned).
- **Each material weighs 10**, so a hunter fills up and walks back to town to sell.

## The rule

- **A ×1 kill is worth 10u × the creature's level, on average, whatever its tier.** Each tier's price is its ×1 drop
  chance turned around, so every tier averages the same 10 × level a kill.
- **The multiplier multiplies how many fall, never the tier's price.** One kill gives up the tier's ×1 chance × the
  multiplier pieces on average: the whole pieces every time, and one more on a roll of what is left over. So a kill is
  worth 10u × level × multiplier on average.
- **What a shop pays is the price itself.** The material's value is written so that, once a shop takes its Tax, it pays
  exactly the tier's price at the creature's level.
- **Drops never grow with the number of attackers.** A death is rolled once and paid to the one person who earned it
  ([Loot-Owner Pointing System](../2-systems/loot-owner-pointing.md)). A summoned creature drops nothing.
- **Nothing an elite drops is ever auto-looted.** It all falls on the ground, so an elite has to be hunted by hand.

## The tiers

| Tier | Opens from | Chance at ×1 | Kills per drop at ×1 | Sells for | Why this chance |
| --- | --- | --- | --- | --- | --- |
| Common | ×1 | 50% | 2 | 20 × level | Seen in every fight. At 500 kills an hour a hunter picks up 250, 2,500 weight; the carry limit is 2,000 + 30 per point of STR, so the bag fills inside an hour at low STR. |
| Uncommon | ×3 | 10% | 10 | 100 × level | About one a minute, so a recipe can ask for dozens in one sitting. |
| Rare | ×10 | 2% | 50 | 500 × level | One every few minutes: a goal for one evening when a recipe asks for ten. |
| Epic | ×30 | 0.5% | 200 | 2,000 × level | A few an hour. A recipe asks for a few, and each one is worth stopping for. |
| Legendary | ×90 | 0.1% | 1,000 | 10,000 × level | Hours apart, so a recipe asks for one. |

- **A creature's tier is the highest its multiplier opens.** Its chance is the tier's ×1 chance × its multiplier: a ×5
  creature drops its Uncommon material at 50% a kill, a ×30 elite its Epic one at 15%.
- **Each tier is 4 or 5 times rarer than the one before**, so each is a different length of hunt: seconds, a minute,
  minutes, half an hour, hours. The rarer the tier, the fewer pieces a hunter carries for the same Unsigned.
- **The yardstick is 500 kills an hour**, one kill every 7.2 seconds, from a fight simulation of fighting without
  resting. It is a first guess.

**Accepted:** a high-level player soloing elites on low-level ground can earn more than farming normal creatures at their
own level. That is the reward for hunting by hand.

## How often a creature comes back

- **A normal creature comes back 5 to 8 seconds after it dies**, so a creature few of which live does not make its
  material rare for one hunter who looks for it. It limits how many hunters can farm it at once.
- **An elite comes back 30 minutes to 3 hours after it dies**, and pays one kill's drop to everybody hunting it together.
- **A shadow comes back 4 to 6 hours after it dies.**

## Where to read the numbers

- **Every material, its creature, tier and price:** the wiki's Materials table (`materialTable`, generated from the
  game's items).
- **Who drops an item and at what chance, and what a creature drops:** the GM commands `/whodrops <item>` and
  `/mobinfo <creature>` ([GM Commands from a Terminal](../0-process/gm-commands-from-a-terminal.md)).
- **Where each creature lives:** `tools/world-spawns/build_world_spawns.py` places every creature of the seamless world
  `world` into `WorldSpawns.asset`; its `--preview` draws the patches.
- **How many creatures there are, by kind and version:** `python3 tools/project-status.py`.
- **Proof the saved files agree with the rule:** `tools/check-creature-materials.py` — which also fails when a
  Blighted creature is kept on a world patch inside the spawn builder's ring (`RING` in
  `tools/world-spawns/build_world_spawns.py`) of any town of `data/world-layout.json`.

## Still open

- **Every material name is a draft**, written from each creature's portrait.
- **First guesses:** 500 kills an hour as the yardstick, the five drop chances, the multiplier each tier opens from, and
  weight 10.
- **What a shop pays for the grade materials, Inks, Salts and consumables** waits until income is settled, with every
  other price.

## Related

- [Mob Roster](mob-roster.md) — the three kinds of creature and what they drop.
- [Creature Gear](creature-gear.md) — what each creature wears.
- [What a Kill Pays](what-a-kill-pays.md) — the multiplier, and everything else a kill pays.
- [Saved Characters](saved-characters.md) — the Unsigned and material a saved character pays.
- [Hair and Headgear](../2-systems/hair-and-headgear.md) — the hats the hat quests will hand out.
