---
status: implemented 2026-08-10 (roster re-tune pending)
owner: vince
last-updated: 2026-10-03
tags:
  - systems-combat-design
---

# Unified Unit Stats — one stat block for players and mobs

**Status:** implemented 2026-08-10 (roster re-tune pending). Decided 2026-08-10.

**Done — the model is complete in code.** `UnitStatMath` owns every formula, constant and the stat-point economy; `Character` computes nothing itself. `MonsterDef` authors level + six primaries. `UnitStatBlock` carries the live block onto each spawned mob and **re-derives on level-up** — the flat 1.15× mob growth rate is deleted, so nothing scales on a mob-only curve. `UnitEquipment` gives mobs the same worn set the player has, aggregated through the same `EquipBonuses`. `UnitMitigation` gives mobs the damage-reduction cut and Shield absorb; `Health` now also fires a mob's **reflect** and **mana-leech** piles, and mobs have a real **MP pool**. Death drops the worn set (`LootDropper.RollWornGear`). **Tier is deleted everywhere** — difficulty is level, and spawn bands are level bands. The Mob Table edits level + primaries with a live pts spent/budget guard that snaps over-budget entries down. `Tools ▸ unwritten ▸ Quick Fixes ▸ Reset Mob Stats to Level + Primaries` resets the whole roster (**86 mobs** today) onto the curve.

**Not done:** nothing in the model, and the roster reset has been run (98 creatures, levels 1–170, all on budget). What's left is content — real equipment for mobs to wear (two weapons and four low armour pieces exist) — and re-tuning by playing it.

### The stat-point budget (added 2026-08-10)

A unit's primaries are an ALLOCATION, not free-typed numbers. `PointsEarnedByLevel(L)` = 48 at level 1 plus `⌊L/5⌋+3` per level after — and a player's 48 are spent at character creation, on the stat hexagon: STR/INT, AGI/LUK and VIT/DEX are opposite pairs that always add to 10, each stat 1–9, and every such spread costs exactly 48 (`UnitStatMath.IsCreationSpread`, pinned by the maths checks), so a new character starts with none left. `PointsSpent` charges the same rising `StatCost` curve the player pays. A mob at level L may carry exactly the stats a player who reached L could have bought — which is what keeps the two sides comparable without any multiplier. `UnitStatMath.Allocate` spends a level's whole budget across a weighted spread (verified on-budget from level 1 to 99; 0–4 points can remain unspendable when the next point costs more than what's left).

## The principle

Every combat unit — the player, every mob, every future NPC — carries the **same stats and substats**, produced by the **same formulas**. No unit has a stat another lacks, and no system asks "is this a player or a mob?" before reading a number.

This is the data-shaped half of the caster/target work in [Active Skill System V3](README.md): once an effect can land on anybody, the thing it lands on has to *have* the fields the effect reads. A damage-reduction buff needs a damage-cut on a mob, a mana-leech buff needs MP on a mob, and a shared stat block is what makes "no exceptions" true rather than aspirational.

**1:1, with no scaling layers at all.** No tier, no global multiplier, no mob-only growth rate. A mob is strong for exactly the reasons a player is strong: its level, its primaries, its equipment, its ring. Difficulty is *authored*, not dialled.

## Where we started (measured 2026-08-10, before the change)

**Player** — `Character.cs`, three layers:

1. **Primaries** — STR, AGI, VIT, INT, DEX, LUK. 48 starting points, max 99 each.
2. **Derived** — MaxHP, MaxMP, Attack, MagicAttack, Defense, MagicDefense, Hit, Flee, crit chance, crit multiplier, attack-speed multiplier, cast-speed multiplier, cast-time multiplier, min/max damage spread, hard-def %, hard-magic-def %.
3. **Ring percentages** — the 10 `StatId` nodes, applied by `Character.Scale(...)`.

**Mobs** — `MonsterDef.cs` (**86 assets**), one flat authored layer:

| Mobs already have | Mobs are missing |
| --- | --- |
| maxHealth, attack, defense, magicDefense, flee, hit, luk | the six primaries |
| hardDefPercent, hardMagicDefPercent | MP, MagicAttack (one `attack` + a Physical/Magic switch) |
| element (and its level, 1–4), damageSpread | crit chance, crit multiplier |
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

A creature authors its ring's invested ranks. Since V4 the ring is PART OF THE UNIT — there is no ring item and no ring slot, for a creature or for a player — so authored ranks always count.

The ranks resolve through `PassiveRanks` → `SkillRing.Percent`, the player's own curve: 10 ranks = +16%, 20 = +26.7%, 40 = +40%, asymptotic to +80%. No mob-side copy of the maths.

**A ring drops at 0.001%** (1 in 100,000) — its own constant, `LootDropper.RingDropChance`, deliberately separate from the chance every other worn piece drops at. A ring is the most valuable thing a unit carries, so it's the MVP-card-tier prize, not ordinary loot.

### Mobs never drop what they wear

**A creature gives up NOTHING it wears.** Since V4 gear is made, not taken off a corpse: a kill pays materials and consumables, and the creature's own gear dies with it (no death pays gold — see [Saved Characters](../../3-world/saved-characters.md)). Its loot table carries those; each kill also has a 30% chance of one random item that is not equipment. Everything lands on the ground where it fell (see [Networking — Implementation Notes](../networking/implementation-notes.md)). What it wears still shows on the hover and target panel — its grade and folds beside each piece — because what a creature is holding is what its mastery works with.

### What state a mob's gear is in

A creature wears exactly the pieces authored on its catalog entry and nothing else; an empty slot stays empty. The same entry carries three settings every one of those pieces comes to life at — `gearQuality` (the grade), `gearFolds` (clamped to the grade's cap) and `gearInkTier` (1 Watery to 5 Sharp) — built through `EquipmentInstance.CreateAt`, the same station rolls a forge uses. Which stats the Ink lands on, and a weapon's trademark (only above Raw), are re-rolled every life. No creature kind overrides them. The starting values per level are in [Mob Roster](../../3-world/mob-roster.md) (Gear state). The server decides it; a watching machine is told what each creature wears.

### There is no such thing as mob-only gear

**One item catalog, shared.** Anything a mob wears is a real equipment Item the player can wear too — same slot, same bonuses, same 3D model, same drop. No mob-exclusive equipment tier, no stat-bearing "natural weapon" that only exists to give a mob numbers.

The codebase already enforces the line: `ItemType` is DERIVED from `slot` + `category` (ItemType.cs:14-17), so an item is Weapon/Armor *because* it has an equip slot. A stat-carrying item with no slot would be data the equip pipeline can't read — dead by construction.

- **Equipment** — has an equip slot and bonuses; a weapon also has a model. Worn by anyone. Six exist today: the weapons `item.sword` and `item.bow`, and the armour `item.leather-cap`, `item.padded-vest`, `item.travellers-cloak` and `item.wooden-buckler`.
- **Materials** — `ItemCategory.Material`: an icon, a name, a crafting input, **no stats and no model**. Fangs, hides, carapace live here and feed [Crafting & Recycling](../crafting-and-recycling.md).

**A mob with no equipment is valid.** Its stats come from level + primaries; `EquipBonuses` aggregates to zeros and every formula still resolves. So mobs start unarmed and get armed as real gear is made — the art debt is incremental content, never a prerequisite for the stat unification.

### No global tuning lever — accepted

With tier and the throttle gone there is no single number to turn when playtesting says the game is too hard. Retuning means editing a mob's level, its primaries, or its gear. Decided deliberately: emergent and honest over quick-to-dial.

### What stays on `MonsterDef`

Only behaviour and inputs: level, the six primaries, worn gear and its state, ring ranks, weapon mastery, `aggroRadius`, `leashRadius`, wander knobs, `preferredRange`, `fleeHpFraction`, element, on-hit ailment, loot, sheet/tint/size.

**No sub-stat is ever typed onto a unit** — not on a creature, not on the player. Movement speed, bare-handed reach and the base swing interval are one shared value each on `CharacterSpec`, read by players and creatures alike: a creature chases on the run gear and wanders on the walk gear exactly as a person moves, reaches `unarmedReach` (0.5) bare-handed or its weapon's `attackRange`, and swings every `swingInterval` (0.6 s) before attack speed. The combat numbers on a creature's body (`Health`, `MonsterAI3D`) are written only by `UnitStatBlock` and are never saved.

## Migration — reset to a level template

Decided: **do not** preserve the hand-set stat blocks the roster used to carry. Each mob gets a level and a primary spread from a template, plus a starting kit of natural gear; hand-tuning happens afterwards on the mobs that feel wrong. The roster is **86 mobs** today.

This trades current balance for a clean base. Expect a re-tuning pass across the roster — and with no global dial, that pass is the tuning, so budget for it.

## Build order — all steps done except the re-tune

1. ~~**Extract the stat block.**~~ `UnitStatMath` owns the formulas; `Character` delegates.
2. ~~**Extract the equipment holder.**~~ `UnitEquipment`.
3. ~~**Give mobs both.**~~ `UnitStatBlock` on every spawned mob, wired by `RigBuilder25D`.
4. ~~**Teach `Health` to read the states it already stores.**~~ `UnitMitigation` (the damage-reduction pile + Shield) + `ApplyLeech` (the reflect and mana-leech piles).
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

---

## The stat reference (added 2026-08-23)

Three tables, read off `UnitStatMath` and its callers. Every row is what the code does today, not intent.

### Primary stats — the only six you allocate

STR, AGI, VIT, INT, DEX, LUK. Each starts at 1, caps at 99, and has exactly **two** sources: the points you spend and what your gear grants. **No skill, buff, debuff, passive, ailment or state may move a primary** — that rule is the reason the sub-stat table below exists.

### What the primaries drive

| Sub-stat | Scaled by |
| --- | --- |
| Melee attack | STR ×1/pt + STR's jump, DEX÷2, LUK÷4, base + 1/level, gear, the Atk ring node, a timed Attack change. Used by every weapon that is not ranged, and bare hands |
| Ranged attack | DEX ×1/pt + DEX's jump, STR÷2, LUK÷4, base + 1/level, gear, the **same** Atk ring node, a timed Attack change. Used while holding a Bow or Firearm (`WeaponTypes.IsRanged`) |
| Magic attack | INT ×1/pt + INT's jump, LUK÷4, base + 1/level, gear, the MAtk ring node, a timed MagicAttack change |
| Defence | VIT÷3 + VIT's jump, base + 1/level, gear, the Def ring node, a timed Defense change |
| Magic defence | INT÷3, base + 1/level, gear, the **same** Def ring node, a timed MagicDefense change |
| Max health | level curve × (1 + VIT×0.02), gear, the HP ring node, a timed MaxHP change |
| Max mana | level curve × (1 + INT×0.015), gear, the MP ring node, a timed MaxMP change |
| Accuracy | level + DEX×1 + LUK÷3 + worn accuracy (Ink), the Accuracy ring node, a timed Accuracy change |
| Evasion | level + AGI×1 + AGI's jump + LUK÷5 + worn evasion (Ink, leather folds), the Evasion ring node, a timed Evasion change |
| Attack speed | AGI 0.5%/pt + DEX at 0.25× that, gear, the AttackSpeed ring node, a timed AttackSpeed change; capped ×3 |
| Cast speed | `1 − DEX/150` (instant at 150 DEX), divided by the CastSpeed ring node and a timed CastSpeed change |
| Crit chance | LUK 0.3%/pt, gear, the Critical ring node, a timed CritChance change; capped 95% |
| Crit damage | 1.4 base + **LUK 0.2%/pt** + LUK's jump as percentage points, the same Critical ring node, a timed CritDamage change |
| Crit resistance | the DEFENDER's LUK, 0.3%/pt off the attacker's crit chance |
| HP regen | **VIT 0.02% of MaxHP per second per point**, plus a timed HPRegen change |
| Mana regen | 1.5/s base + **INT 0.02% of MaxMP per second per point**, plus a timed MPRegen change |
| Minimum damage floor | 55% of max, closed toward 1.0 by DEX (physical) / INT (magic) at 1.2%/pt |

**The three bold lines were added 2026-08-23.** Before that, crit damage read the ring alone, HP regen had no source at all (it existed only while a skill granted it), and mana regen was a flat 1.5/s.

### Misc — nothing here reads a primary

| Stat | Where it comes from |
| --- | --- |
| Movement speed | `CharacterSpec` gear speeds (run gear 1.5, walk gear 0.6 world units/s), the same for every person and creature, × `UnitStates.MoveMult` |
| Attack reach | `CharacterSpec.unarmedReach` (0.5) bare-handed, or the held weapon's `attackRange`; blended by the pair's body sizes |
| Base swing interval | `CharacterSpec.swingInterval` (0.6 s) for every unit, divided by attack speed |
| Mana leech | 0; a timed sub-stat change only |
| Life steal | worn words + a timed LifeSteal change; nothing typed |
| Damage reflect | worn words + a timed Reflect change; nothing typed |
| Damage dealt | **not a character stat** — a per-hit multiplier applied while the hit is built (`UnitOffense.Dealt`) |
| Damage reduction | **not a character stat** — the DamageReduction sub-stat (the worn Bulwark word + timed states such as Guard), read per incoming hit by `UnitMitigation.Landed`. It is the ONLY percentage cut on a hit; defence itself subtracts flat |
| Max stamina | flat 100 on `Character`; drain/refill rates on `Player3DController`, player-only |
| Block chance | the equipped off-hand `Item.blockChance` |
| Element (what the unit is hit as) | a person: Neutral level 1, or an Armor Converter's element at level 1 while one runs; a mob: its authored element and level (`MonsterDef.element` / `elementLevel`), or a converter's |

### Regeneration is one ticker for everyone (2026-08-23)

Both pools refill in `StatusController` — the one per-unit ticker — on the 0.8s cadence, each carrying its sub-1-point remainder so a tiny rate is paid exactly rather than rounded up. Rate = the unit's own natural rate + the timed sub-stat change; **a rate at or below zero pays nothing**, so a regen debuff can stop healing but never drain, and never heals what it is meant to weaken.

This closed a player-only gap: mobs had an MP pool that nothing ever refilled. `Character.Update` no longer trickles either pool.

### Every HP gain announces itself the same way (2026-08-23)

`CombatAuthority.NoteHPAdded` is the ONE door: it draws the green digits, fires the cue and raises the `OnHPAdded` event, which carries only WHO and HOW MUCH. Any healing source added later is covered for free. The cue was renamed from `OnReceiveHeal` (same int, so authored libraries keep their binding) because a cue must name the moment, not the source.

**One deliberate exception left:** `Character.LeechHeal` still adds HP silently, so multi-hit life steal doesn't spam numbers.
- **Gear defines the skill kit.** A weapon's identity is its skill roster, so a mob holding a weapon casts every skill of that weapon type its weapon mastery has reached (capped by its weapon's grade), each at level 1, since a creature spends no mastery points: an active or the ultimate in place of a swing when one is off cooldown, affordable and in reach (the ultimate first, otherwise one at random), and its passives on their own triggers. It pays MP and waits out cooldowns exactly as a player does, and its skills land on people, never on other mobs. Every cooldown starts ready on a new life.
- **One extra skill slot, apart from the weapon.** Every mob also carries one extra skill, cast by exactly the same rules as its weapon's skills. Each life it is drawn at random from every published skill — any weapon's, ultimates and passives included — skipping any skill its weapon already carries. `/summon` can name a skill id instead (any skill, published or not), and that mob keeps it for every life. Only Sword skills are published today, so the extra slot is how a mob holding a Bow or nothing casts anything. Per-biome hand-written extra skills are planned, not built.
---

## Related

- [Unified Unit Stats — Implementation Notes](unified-unit-stats-implementation-notes.md) — the code-facing companion.
- [Active Skill System — V3](README.md) — why one stat model is the data-shaped half of "one applier, no special cases".
- [Pre-Renewal RO Port — Phase Plan & Formulas](../pre-renewal-ro-port.md) — where these formulas came from.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the percentage curve mobs share with the player.
- [State System — Buffs, Debuffs & Combo Marks](state-system-buffs-debuffs-combo-marks.md) — the sub-stat piles the table above is read through.
- [Creatures and Where They Live](../../3-world/biome-mob-families.md) — the creatures this model was reset onto.
- [Crafting & Recycling](../crafting-and-recycling.md) — the materials a mob drops that it isn't wearing.
- [Networking — Implementation Notes](../networking/implementation-notes.md) — the per-connection fighter the server builds from this block.
