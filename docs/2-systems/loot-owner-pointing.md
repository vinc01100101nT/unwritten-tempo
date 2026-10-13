---
status: "BUILT 2026-10-13, not compiled in Unity, not play-tested. Agreed with the user the same day."
owner: vince
last-updated: 2026-10-13
tags:
  - combat
  - rewards
---

# Loot-Owner Pointing System — Who a Kill Belongs To

> **Never append. Always update in place.** This page states only what has been AGREED. Anything still being
> decided lives in **Still open** at the bottom and nowhere else.

## What the player gets

- **Everyone who fights a creature is paid for it.** Experience is split among all of them by how much each did; it no
  longer all goes to whoever landed the last blow.
- **The loot is kept for whoever did the most**, for the first 10 seconds. After that anybody may take it, as before.
- **A healer and whoever holds the creature's attention earn a part too**, without dealing the damage.
- It is one rule for normal creatures, elites and shadows.

## The points

Each person fighting a creature collects points for it, out of 100.

| Part | Points | Shared by |
| --- | --- | --- |
| Damage | 60 | the health each person took off it |
| Holding its attention | 15 | the time it spent chasing each person |
| Healing | 15 | the health each person restored to **other** players that this creature had taken from them |
| First hit | 5 | all to whoever first cost it health |
| Last hit | 5 | all to whoever landed the killing blow |

**Each part is a share of a fixed number of points.** If one person dealt 30% of the damage, they hold 30% of the 60.
Nothing a group does to itself makes more points than the fight had, and a part nobody earned (nobody healed anyone)
hands out nothing.

**Worked example.** Three damage dealers deal 30% of the damage each: 18 points each. A healer deals 10% of the damage
(6 points) and all of the healing (15 points): 21 points. The healer owns the loot, and the experience is split 18,
18, 18 and 21 parts out of the 75 handed out.

## What the points decide

- **The loot:** the most points owns every drop for the 10 seconds a kill's drops are kept. A tie goes to the higher
  damage. A creature that turns back into a character is that person's too.
- **The experience:** split among everyone with points, each by their points over all the points handed out. The parts
  add up to the whole kill, so a solo kill pays all of it; what rounding leaves over goes to the top scorer. Each
  person's part then takes their own event rate and their own run of kills.
- **The kill itself:** everyone with points gets it for their quests and their run of kills, even when their part of
  the experience rounds to nothing.

## How each part is counted

**Damage**
- Only health that really came off counts, never the size of the blow. A blow of 300,000 on a creature with 1,000
  health left counts as 1,000.
- The killing blow is counted before the death pays out.
- When the creature gets health back, every damage claim on it shrinks by the same share. At full health nobody holds
  a damage claim, a first hit or any healing against it.

**Holding its attention**
- Counted from the moment the creature gives chase, with no exception: before anybody has hurt it, and whether or not
  it ever lands a blow. Defence, dodging, parrying and blocking do not enter into it.
- It stays when the creature heals back to full, because the creature is still chasing.
- A creature that gives up on everybody with no damage on it forgets the time it spent chasing.

**Healing**
- Only health actually restored counts, never what the skill offered, and only up to what that creature took from the
  person healed.
- Healing yourself, a potion and your own regeneration earn nothing.
- Health a person got back some other way is no longer owed to a healer.

**First hit and last hit**
- The first hit is the first blow that costs the creature health in this fight. It is forgotten when the creature
  gets back to full health.
- The last hit is the blow that kills.

## Who can be paid

- **A person must be on the creature's map when it dies.** Somebody who left still counts in what the others' shares
  are measured against, so leaving never makes those who stayed look as if they did more.
- **A creature fighting for a player** (a saved ally) earns its points for that player.
- **A person who reconnects keeps their points**: they are kept by character.
- **A new life starts empty.** A creature that comes back owes nothing to whoever fought its last life.
- **A summoned creature pays nothing**, as before.
- **With nobody on the list** (a creature no player hurt or was chased by), the last unit to cost it health takes
  everything, as before.

## What it costs to run

- **Per blow on a creature:** one addition to that person's entry in the creature's list.
- **Per frame:** one comparison and one addition for each creature that is chasing somebody.
- **Per death:** one pass over the list, and one experience message per person paid.
- **Memory:** one small list per creature body, made the first time it chases or is fought and reused for every later
  life. One entry per person. Not measured.
- **Network:** no new message.

## How it is built

- `KillPointsMath` — the points and the split, as plain arithmetic. Pinned in `tools/game-math-tests/Checks.cs`.
- `KillLedger` — the list one creature keeps (`Health.Ledger`). Emptied after a death has paid and when the body comes
  back to life.
- `HurtBy` — which creatures have taken health from one person that nobody has restored yet (`Character.Hurts`); what
  a heal is credited against.
- Damage is written where a creature's health comes off (`Health.SubtractHealth`), chase time in the creature's tick
  (`MonsterAI3D`), healing where a skill heals (`SkillEffectApplier`).
- `Rewards.WhoEarned` names the top scorer; `Rewards.PayKill` splits the experience.

## Still open

- **A stranger who lands only the first hit** on somebody's creature gets 5 of the points handed out, at any level
  gap. A level-gap rule would be its own decision.
- **Damage nobody owns** (a poison no player cast) is credited to whoever hit the creature last.
- **Parties do not exist yet.** When they do, a party's members add their points together.

## Related

- [Creature Materials](../3-world/creature-materials.md) — what a creature drops and what it sells for.
- [Saved Characters](../3-world/saved-characters.md) — the character a beaten creature can turn back into.
- [Auto-Combat](auto-combat.md) — auto-loot takes drops for whoever the loot belongs to.
