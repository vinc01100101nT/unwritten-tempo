---
name: unified-unit-stats-implemented
description: "Players and mobs share ONE stat model — same formulas, same point budget, no tier and no mob-only multiplier. Implemented 2026-08-10."
metadata: 
  node_type: memory
  type: project
  originSessionId: 036aac99-29a9-45db-bff5-8157919fb418
---

Mobs and players are **1:1**. Same stats, same formulas, same stat-point economy, no scaling layers anywhere. Docs (both under V3, since this is V3's unit-side half): `docs/active-skill-system-v3/unified-unit-stats.md` (design) + `unified-unit-stats-implementation-notes.md` (code-facing).

**The pieces (all in `unity/Assets/Scripts/`):**
- `UnitStatMath.cs` — THE formulas + constants + the point economy (`StatCost`, `PointsEarnedByLevel`, `PointsSpent`, `MaxAffordableStat`, `Allocate`). `Character` delegates to it and computes nothing itself.
- `UnitStatBlock.cs` — a mob's LIVE block (level + primaries). `LevelUp()` raises the level and **re-derives**; there is no growth multiplier. Also owns the mob's MP pool.
- `UnitEquipment.cs` — the worn set, shared shape with the player, aggregated via `EquipBonuses.Aggregate`.
- `UnitMitigation.cs` — Guard cut + Shield absorb for mobs; `Health.ApplyLeech` fires a mob's Reflect + ManaLeech.
- `LootDropper.DropWornGear` — a unit drops what it WEARS (`wornDropChance` per piece); the loot table now only carries materials/gold.
- The roster reset RAN 2026-08-10: 86 mobs, levels 3–46, every one exactly on budget (verified by parsing the assets). Its one-shot tool (`Editor/MobStatMigration.cs`) was deleted afterwards — a future bulk reset means re-writing it from `UnitStatMath.Allocate`.

**Deleted, do not reintroduce:** `MonsterDef.tier` (difficulty is LEVEL; spawn bands are level bands in `MonsterCatalog`/`MapRecipe`/`FieldMobSpawner`), `MonsterAI3D.perLevelGrowth` (the flat 1.15×), and the hand-authored `maxHealth/attack/defense/magicDefense/hit/flee/damageSpread` on `MonsterDef` (all derived now). There is **no global mob multiplier** — that was considered and deliberately rejected in favour of authored difficulty.

**The rule that keeps it balanced:** a unit's primaries are an ALLOCATION, not free numbers. A mob at level L may carry exactly what a player who reached L could have bought. The Mob Table shows `pts spent/budget` and SNAPS an over-budget entry down to what fits (only the edited field moves — untouched stats are never reduced).

**Rings:** mobs wear real rings. `MonsterDef.ring` (the item) + `ringRankStats`/`ringRankValues` (its invested ranks); `UnitStatBlock` resolves them through `PassiveRanks` → `SkillRing.Percent` — the player's own soft-cap curve (+80% asymptote, k=40), no copy. The % layer is GATED on the ring item actually being worn (same rule as `Character.RingEquipped`), so the thing granting the power is the thing that can drop. Ring drop = `LootDropper.RingDropChance` = **0.001%** (1 in 100,000), its own constant, deliberately separate from `wornDropChance`.

Gear a unit spawns wearing is serialized on `UnitEquipment.startingGear` (the live dictionary is runtime-only) and equipped lazily on first access, so Awake order between it and `UnitStatBlock` can't matter.

**Process scar (2026-08-10):** after being told "make stats player === mobs", I implemented the core and then *listed* the leftovers (level-up still scaling, gear, drops, migration) as future work for the user to approve. That was wrong — a stated architectural goal means finishing every path that violates it, not stopping at the first shippable slice and asking. When the user names an invariant, hunt every violation of it and fix them all. See [[apply-review-fixes-dont-gatekeep]].
