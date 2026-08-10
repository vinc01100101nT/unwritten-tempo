# Unified Unit Stats — one stat block for players and mobs

**Status:** implemented 2026-08-10 (roster re-tune pending). Decided 2026-08-10.

**Done — the model is complete in code.** `UnitStatMath` owns every formula, constant and the stat-point economy; `Character` computes nothing itself. `MonsterDef` authors level + six primaries. `UnitStatBlock` carries the live block onto each spawned mob and **re-derives on level-up** — the flat 1.15× mob growth rate is deleted, so nothing scales on a mob-only curve. `UnitEquipment` gives mobs the same worn set the player has, aggregated through the same `EquipBonuses`. `UnitMitigation` gives mobs the Guard cut and Shield absorb; `Health` now also fires a mob's **Reflect** and **ManaLeech** windows, and mobs have a real **MP pool**. Death drops the worn set (`LootDropper.DropWornGear`). **Tier is deleted everywhere** — difficulty is level, and spawn bands are level bands. The Mob Table edits level + primaries with a live pts spent/budget guard that snaps over-budget entries down. `Tools ▸ unwritten ▸ Quick Fixes ▸ Reset Mob Stats to Level + Primaries` resets all 85 assets onto the curve.

**Not done:** nothing in the model, and the roster reset has been run (86 mobs, levels 3–46, all on budget). What's left is content — real equipment for mobs to wear (only two weapons exist) — and re-tuning by playing it.

### The stat-point budget (added 2026-08-10)

A unit's primaries are an ALLOCATION, not free-typed numbers. `PointsEarnedByLevel(L)` = 48 at level 1 plus `⌊L/5⌋+3` per level after, and `PointsSpent` charges the same rising `StatCost` curve the player pays. A mob at level L may carry exactly the stats a player who reached L could have bought — which is what keeps the two sides comparable without any multiplier. `UnitStatMath.Allocate` spends a level's whole budget across a weighted spread (verified on-budget from level 1 to 99; 0–4 points can remain unspendable when the next point costs more than what's left).

## The principle

Every combat unit — the player, every mob, every future NPC — carries the **same stats and substats**, produced by the **same formulas**. No unit has a stat another lacks, and no system asks "is this a player or a mob?" before reading a number.

This is the data-shaped half of the caster/target work in [Active Skill System V3](README.md): once an effect can land on anybody, the thing it lands on has to *have* the fields the effect reads. Guard needs a damage-cut on a mob, ManaLeech needs MP on a mob, and a shared stat block is what makes "no exceptions" true rather than aspirational.

**1:1, with no scaling layers at all.** No tier, no global multiplier, no mob-only growth rate. A mob is strong for exactly the reasons a player is strong: its level, its primaries, its equipment, its ring. Difficulty is *authored*, not dialled.

## Where we started (measured 2026-08-10, before the change)

**Player** — `Character.cs`, three layers:

1. **Primaries** — STR, AGI, VIT, INT, DEX, LUK. 48 starting points, max 99 each.
2. **Derived** — MaxHP, MaxMP, Attack, MagicAttack, Defense, MagicDefense, Hit, Flee, crit chance, crit multiplier, attack-speed multiplier, cast-speed multiplier, cast-time multiplier, min/max damage spread, hard-def %, hard-magic-def %.
3. **Ring percentages** — the 10 `StatId` nodes, applied by `Character.Scale(...)`.

**Mobs** — `MonsterDef.cs` (**85 assets**), one flat authored layer:

| Mobs already have | Mobs are missing |
| --- | --- |
| maxHealth, attack, defense, magicDefense, flee, hit, luk | the six primaries |
| hardDefPercent, hardMagicDefPercent | MP, MagicAttack (one `attack` + a Physical/Magic switch) |
| element, attackElement, damageSpread | crit chance, crit multiplier |
| — | attack-speed and cast-speed multipliers |

So the gap is roughly nine additions, not a rebuild — mobs already carry most of the defensive half.

**Three competing growth paths exist today** and all collapse into the player's:

- the player's per-level growth (`hpPerLevel`, `attackPerLevel`, …) — **the survivor**,
- `MonsterAI3D.perLevelGrowth` — a flat 1.15× on attack / maxHealth / xpReward at mob level-up (MonsterAI3D.cs:199) — **deleted**,
- `MonsterDef.tier` — **deleted** (it becomes a content label at most, never a multiplier).

## The model

### One block, one set of formulas

A unit is authored as **six primaries + a level**. Every derived stat comes from the pre-renewal formulas already living in `Character` (constants at Character.cs:80-104) — they stop being "the player's formulas" and become the unit formulas:

- `Attack = base + growth + STR + DEX/2 + LUK/4`
- `MaxHP = (base + growth) × (1 + VIT × 0.02)`
- `Hit = level + DEX + LUK/3` · `Flee = level + AGI + LUK/5`
- `CritChance = LUK × 0.003` (capped 0.95) · crit multiplier from a 1.4 base
- `AttackSpeedMult` from AGI · `CastSpeedMult` and cast time from DEX
- damage spread from DEX (physical) / INT (magic) — this replaces `MonsterDef.damageSpread`

Ring percentages stay in the same pipeline for both sides. A mob authors its ring's invested ranks; they resolve through the SAME `SkillRing.Percent` soft-cap curve the player's ring uses (`PassiveRanks`, +80% asymptote, k=40), so a mob's 20-rank ATK ring is worth the same +26.7% a player's is.

### Mobs wear equipment and rings

A mob carries the same `Dictionary<EquipSlotType, EquipmentInstance>` the player does, aggregated through the same `EquipBonuses.Aggregate` (EquipmentInstance.cs:111) — refine levels and cards included. Nothing about the equipment code is player-specific, so this was wiring rather than new systems. Implemented as `UnitEquipment`.

This is what removes the need for a throttle. A mob is dangerous because it is a high level in good gear — the same sentence you'd write about a player.

### Mobs wear rings too

A mob authors a ring ITEM plus that ring's invested ranks. Wearing the item is what switches the percentage layer on — the same gate the player has (`Character.RingEquipped`), so authored ranks with an empty ring slot do nothing. That matters because it makes the thing that grants the power the same thing that can drop.

The ranks resolve through `PassiveRanks` → `SkillRing.Percent`, the player's own curve: 10 ranks = +16%, 20 = +26.7%, 40 = +40%, asymptotic to +80%. No mob-side copy of the maths.

**A ring drops at 0.001%** (1 in 100,000) — its own constant, `LootDropper.RingDropChance`, deliberately separate from the ordinary `wornDropChance`. A ring is the most valuable thing a unit carries, so it's the MVP-card-tier prize, not ordinary loot.

### Mobs drop what they wear

A mob's **equipped set is its gear drop table**: each worn piece rolls independently to drop, and because it's real equipment, the player can equip what drops. The loot table stays, but only for **materials and gold** — things the mob isn't wearing. Implemented as `LootDropper.DropWornGear` (`wornDropChance` per piece).

Single source of truth: the thing it wears is the thing it drops, so a mob can never advertise gear it doesn't actually carry.

### There is no such thing as mob-only gear

**One item catalog, shared.** Anything a mob wears is a real equipment Item the player can wear too — same slot, same bonuses, same 3D model, same drop. No mob-exclusive equipment tier, no stat-bearing "natural weapon" that only exists to give a mob numbers.

The codebase already enforces the line: `ItemType` is DERIVED from `slot` + `category` (ItemType.cs:14-17), so an item is Weapon/Armor *because* it has an equip slot. A stat-carrying item with no slot would be data the equip pipeline can't read — dead by construction.

- **Equipment** — has an equip slot, bonuses, and a model. Worn by anyone. Only two exist today (`Iron Sword`, `Bow Epic`).
- **Materials** — `ItemCategory.Material`: an icon, a name, a crafting input, **no stats and no model**. Fangs, hides, carapace live here and feed [Crafting & Recycling](../crafting-and-recycling.md).

**A mob with no equipment is valid.** Its stats come from level + primaries; `EquipBonuses` aggregates to zeros and every formula still resolves. So mobs start unarmed and get armed as real gear is made — the art debt is incremental content, never a prerequisite for the stat unification.

### No global tuning lever — accepted

With tier and the throttle gone there is no single number to turn when playtesting says the game is too hard. Retuning means editing a mob's level, its primaries, or its gear. Decided deliberately: emergent and honest over quick-to-dial.

### What stays on `MonsterDef`

Behaviour is not stats and does not unify: `moveSpeed`, `aggroRadius`, `leashRadius`, `stopDistance`, wander knobs, `preferredRange`, `fleeHpFraction`, `touchRange`, `touchInterval`, `attackType`, on-hit ailment, loot, sheet/tint/scale.

**One open question:** move speed is arguably a shared stat (Haste already multiplies it on both sides through `UnitStates.MoveMult`), but it's stored on the mover today, not in either stat block. Recommend leaving it where it is for this pass and revisiting once the block is in.

## Migration — reset to a level template

Decided: **do not** preserve the 85 hand-set stat blocks. Each mob gets a level and a primary spread from a template, plus a starting kit of natural gear; hand-tuning happens afterwards on the mobs that feel wrong.

This trades current balance for a clean base. Expect a re-tuning pass across the roster — and with no global dial, that pass is the tuning, so budget for it.

## Build order — all steps done except the re-tune

1. ~~**Extract the stat block.**~~ `UnitStatMath` owns the formulas; `Character` delegates.
2. ~~**Extract the equipment holder.**~~ `UnitEquipment`.
3. ~~**Give mobs both.**~~ `UnitStatBlock` on every spawned mob, wired by `RigBuilder25D`.
4. ~~**Teach `Health` to read the states it already stores.**~~ `UnitMitigation` (Guard/Shield) + `ApplyLeech` (Reflect/ManaLeech).
5. ~~**Drops from the worn set.**~~ `LootDropper.DropWornGear`.
6. ~~**Delete `perLevelGrowth` and tier.**~~ Level-up re-derives; tier is gone from every file.
7. ~~**Migration tool.**~~ `Tools ▸ unwritten ▸ Quick Fixes ▸ Reset Mob Stats to Level + Primaries`.
8. **Re-tune the roster** — the one step left, and it needs play, not code.

Then, as ongoing content: make real equipment (model included), arm the mobs that should carry it, author the materials they drop.

Steps 1-3 are the risky ones: every read of a mob stat moves. Step 4 is what unblocks "every effect works on everyone" and is independently shippable — it needs none of the rest.

## Why this shape

- **Single source of truth** — one set of formulas, so player and mob numbers can't drift apart.
- **No dead ends for effects** — an effect never has to ask what kind of unit it hit.
- **Kills three growth paths** and every scaling layer, leaving one rule: level + primaries + gear.
- **Drops can't lie** — a mob's gear loot is the equipment it is visibly wearing.
- **One item catalog** — no mob-only equipment tier to keep in sync with the real one.

## Consequences worth knowing

- **`CritShieldPerLuk` becomes symmetric.** It already reads the *target's* LUK to lower the attacker's crit chance (Character.cs:89). Mobs have LUK today, but mob attacks don't route through the player's crit path — unifying makes it apply both ways. That is a real balance change, not a refactor.
- **A mob's ring is authored, not drafted.** The player's ring comes from the Ring of Progression's pick-per-10-points draft; a mob just wears a pre-authored one. Same data, no drafting code.
- **Gear defines the skill kit.** Since a weapon's identity is its skill roster, arming a mob with a real weapon hands it that weapon's skills. Powerful, and a reason to keep most mobs on natural gear until that's intentional.