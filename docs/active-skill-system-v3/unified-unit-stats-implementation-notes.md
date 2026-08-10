# Unified Unit Stats — Implementation Notes

> **The MUTABLE, code-facing companion** to [Unified Unit Stats](unified-unit-stats.md) — that doc owns the design intent; this one records how it's actually wired and is expected to churn. If they disagree: **the design doc wins on intent, this one wins on what the code does today.** Sibling of [Implementation Notes](implementation-notes.md) (the skill-system one).
>
> Written 2026-08-10, the session that implemented it.

## Why this is part of V3

V3 says a skill is a list of `SkillEffect`s resolved by one applier, and the ring only *scales* them. That only holds if **every unit an effect can land on has the fields the effect reads**. Guard needs a damage cut on a mob; ManaLeech needs MP on a mob; a target-side Dash needs a mob that can be relocated. So "player === mob" is the data-shaped half of V3's "one applier, no special cases" — not a separate initiative. See also the caster/target split in [Implementation Notes](implementation-notes.md).

## The files

| File | Owns |
| --- | --- |
| `UnitStatMath.cs` | **Every** formula and constant, plus the stat-point economy. Static, no Unity objects. |
| `UnitStatBlock.cs` | A mob's LIVE block: level + primaries + ring ranks + MP. Derives and writes onto `Health`/`MonsterAI3D`. |
| `UnitEquipment.cs` | The worn set (shared shape with the player), aggregated through `EquipBonuses.Aggregate`. |
| `UnitMitigation.cs` | Guard cut + Shield absorb for mobs — the states `Health` stored but ignored. |
| `MonsterDef.cs` | Authoring: level, six primaries, `startingGear`, ring ranks. Derives via `ToArchetype`. |
| `Editor/MobTableWindow.cs` | The full authoring surface (see below). |

## Load-bearing decisions (don't undo these by accident)

**`Character` computes nothing.** Every derived getter delegates to `UnitStatMath`. The formulas were *moved*, not copied — if you add a stat, add it there or the two sides drift.

**Level-up RE-DERIVES, it never scales.** `UnitStatBlock.LevelUp()` raises the level and recomputes. `MonsterAI3D.perLevelGrowth` (a flat 1.15× on attack/HP) is **deleted**. A mob's level-20 numbers are simply what level 20 produces.

**Primaries are a budget, not free numbers.** `PointsEarnedByLevel(L)` = 48 + Σ`⌊L/5⌋+3`; `PointsSpent` charges the same rising `StatCost` curve the player pays. A mob at level L may carry exactly what a player who reached L could have bought — that, not a multiplier, is what keeps the sides comparable. `Allocate` spends a whole budget across a weighted spread (verified on-budget L1→L99; 0–4 points can remain unspendable when the next point costs more than what's left).

**There is no tier and no throttle.** Difficulty is level. The spawn band is a **level** band (`MonsterCatalog.Eligible`, `MapRecipe.minLevel/maxLevel`, `FieldMobSpawner`, `RecipeGenerator`). `MonsterDef.tier` is gone. Deliberately no global dial — retuning means editing a mob's level, spread or gear.

**A ring IS equipment.** There is no separate `ring` field: the ring is the piece of `startingGear` whose `slot == Ring` (`MonsterDef.Ring` derives it). The percentage layer is **gated on wearing it**, same rule as `Character.RingEquipped`, so the thing that grants the power is the thing that can drop. Ranks resolve through `PassiveRanks` → `SkillRing.Percent` — the player's own soft-cap curve (+80% asymptote, k=40), no mob-side copy.

**Worn gear is serialized on the component, not the dictionary.** `UnitEquipment.startingGear` is the serialized half; the live dictionary is runtime-only. Gear is equipped **lazily on first access**, not in `Awake`, so component wake order between `UnitEquipment` and `UnitStatBlock` cannot matter. Getting this wrong makes mob rings silently do nothing.

**A mob drops what it wears.** `LootDropper.DropWornGear` rolls each worn piece; the legacy `itemDropNames`/`itemDropChances` arrays now carry **materials and gold only**. Rings roll on their own constant, `LootDropper.RingDropChance` = **0.00001 (0.001%)**, deliberately not a per-mob field.

**Removed dead fields.** `MonsterDef`'s hand-authored `maxHealth`/`attack`/`defense`/`magicDefense`/`hit`/`flee`/`damageSpread` are deleted — all seven are derived in `ToArchetype`, so storing them was a second copy that could disagree.

## The Mob Table is the authoring surface

`Tools ▸ unwritten ▸ Catalogs ▸ Mob Table`:

- **Inline:** id, name, biome, boss flag, **LVL + STR/AGI/VIT/INT/DEX/LUK**, element/aElement, weight, XP, gold.
- **`pts` column:** `spent/budget` — green on budget, **red over**, amber when a real chunk is unspent. Typing a value the level can't afford **snaps** to the highest that fits (`FitSpreadToBudget`), and only the field you edited is adjusted — untouched stats are never knocked down.
- **Derived readouts** (`=HP/=ATK/=DEF/=FLEE`): read-only, computed live through `UnitStatMath`, so you edit the cause and see the effect. Sorting on them sorts the derived value.
- **`▸` expander:** per-mob **equipment** (one object field per slot; an item is rejected if its slot doesn't match) and, when a ring is worn, its **ranks** with the % each is worth.

**IMGUI gotcha:** the expander toggle is queued in `_pendingToggle` and applied *after* the draw loop. Flipping it mid-draw changes the layout-group count between the Layout and Repaint passes of one frame → "Mismatched LayoutGroup" spam.

## Verifying changes here without Unity

Unity's bundled .NET 8 SDK type-checks all of `Assets/Scripts` offline — see the `compile-check-without-unity` memory. Known-good baseline is **11 environmental errors**; anything above that is yours. Authoring maths (the budget curve, the allocator, the ring percentages) should be **replayed in a throwaway script against the real constants** before being trusted — that's how the "untouched stats get crushed" bug in the budget guard was caught pre-ship.

## Still open

- ~~Reset the roster~~ **DONE** — 86 mobs across levels **3–46**, every one exactly on its point budget (verified by parsing the assets: 0 over-budget, 0 left at level 1). The one-shot migration tool has been deleted; a future bulk reset would mean re-writing it from `UnitStatMath.Allocate`.
- Only two equipment items exist (`Iron Sword`, `Bow Epic`), so mobs are effectively unarmed. Content, not code.
- Mob MP has a pool but nothing spends it — mobs don't pay skill costs yet.
- Roster re-tuning by playing it — the numbers are consistent now, not necessarily *fun* yet.
- Nothing has been run in Play mode.
