---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - systems-combat-design
---

# Unified Unit Stats — Implementation Notes

> **The MUTABLE, code-facing companion** to [Unified Unit Stats](unified-unit-stats.md) — that doc owns the design intent; this one records how it's actually wired and is expected to churn. If they disagree: **the design doc wins on intent, this one wins on what the code does today.** Sibling of [Implementation Notes](implementation-notes.md) (the skill-system one).
>
> Written 2026-08-10, the session that implemented it.

## Why this is part of V3

V3 says a skill is a list of `SkillEffect`s resolved by one applier, and the ring only *scales* them. That only holds if **every unit an effect can land on has the fields the effect reads**. A damage-reduction buff needs a damage cut on a mob; a mana-leech buff needs MP on a mob; a target-side Dash needs a mob that can be relocated. So "player === mob" is the data-shaped half of V3's "one applier, no special cases" — not a separate initiative. See also the caster/target split in [Implementation Notes](implementation-notes.md).

## The files

| File | Owns |
| --- | --- |
| `UnitStatMath.cs` | **Every** formula and constant, plus the stat-point economy. Static, no Unity objects. |
| `UnitStatBlock.cs` | A mob's LIVE block: level + primaries + ring ranks + MP. Derives and writes onto `Health`/`MonsterAI3D`. |
| `UnitEquipment.cs` | The worn set for EVERY unit — `Character` and mobs both hold one. Aggregated through `EquipBonuses.Aggregate`. |
| `UnitMitigation.cs` | The damage-reduction cut + Shield absorb for mobs — the states `Health` stored but ignored. |
| `MonsterDef.cs` | Authoring: level, six primaries, `startingGear`, ring ranks. Derives via `ToArchetype`. |
| `Editor/MobTableWindow.cs` | The full authoring surface (see below). |

## Load-bearing decisions (don't undo these by accident)

**`Character` computes nothing.** Every derived getter delegates to `UnitStatMath`. The formulas were *moved*, not copied — if you add a stat, add it there or the two sides drift.

**A respawn is a fresh creature.** `MobRespawn.Reactivate` — the one choke both the in-place recycle and the pooled `FieldMobSpawner` reuse go through — calls `UnitStatBlock.ResetToSpawnLevel()` and `MonsterAI3D.ResetCombatXp()` before `Health.Revive()`. Without it a recycled object keeps the levels and banked XP it earned last life, so farming one spot would ratchet a zone permanently upward. The pool is per-type, so the remembered `spawnLevel` always belongs to that mob's own def.

**The training dummy opts OUT of the model.** `TrainingDummy.MakeOne` (run by the `/dummy` GM command) strips the `UnitStatBlock` after building, because the block would re-derive over its hand-set 10M HP / 0 attack and turn the punching bag into an ordinary level-1 mob. It's a measuring instrument, not a unit.

**Tune the CAUSE, never the result.** Anything that changes a mob's numbers — `/summon` with a level included — sets Level + the six primaries and re-derives. Writing a derived stat directly is silently overwritten by the block on the next level-up.

**Level-up RE-DERIVES, it never scales.** `UnitStatBlock.LevelUp()` raises the level and recomputes. `MonsterAI3D.perLevelGrowth` (a flat 1.15× on attack/HP) is **deleted**. A mob's level-20 numbers are simply what level 20 produces.

**Primaries are a budget, not free numbers.** `PointsEarnedByLevel(L)` = 48 + Σ`⌊L/5⌋+3`; `PointsSpent` charges the same rising `StatCost` curve the player pays. A mob at level L may carry exactly what a player who reached L could have bought — that, not a multiplier, is what keeps the sides comparable. `Allocate` spends a whole budget across a weighted spread (verified on-budget L1→L99; 0–4 points can remain unspendable when the next point costs more than what's left).

**There is no tier and no throttle.** Difficulty is level. The spawn band is a **level** band (`MonsterCatalog.Eligible`, a world spawn list row's `minLevel/maxLevel`, `FieldMobSpawner`). `MonsterDef.tier` is gone. Deliberately no global dial — retuning means editing a mob's level, spread or gear.

**A ring IS equipment.** There is no separate `ring` field: the ring is the piece of `startingGear` whose `slot == Ring` (`MonsterDef.Ring` derives it). The percentage layer is **gated on wearing it**, same rule as `Character.RingEquipped`, so the thing that grants the power is the thing that can drop. Ranks resolve through `PassiveRanks` → `SkillRing.Percent` — the player's own soft-cap curve (+80% asymptote, k=40), no mob-side copy.

**One equipment holder, player included.** `Character` has no equip dictionary of its own — it lazily attaches a `UnitEquipment` (`Character.Gear`) and routes get/equip/save/load through it. The equip-hover preview no longer mutates a cached field: `Gear.BonusesWith(slot, hypothetical)` returns a pure aggregate and `Character.previewBonuses` overrides the getter for the duration of the snapshot. That snapshot runs on the server: hovering a piece asks for it, and the server sends back the finished "If equipped" lines (`EquipPreview`).

**The mob builder does NOT stamp derived stats.** `RigBuilder25D` sets only armour percentages and presentation; `UnitStatBlock` is the single writer of `maxHealth`/`defense`/`magicDefense`/`flee`/`luk` and `attack`/`hit`/`damageSpread`. `MonsterArchetype` carries the stat block, not a snapshot of its outputs — there is no second copy to drift. The legacy `MobRoster` fallback entries carry a level + `UnitStatMath.Allocate` spread for the same reason.

**What a kill pays is not authored on the creature.** `CreatureKinds.KillExperience` works it out from the creature's kind, its level at death and the grade and folds of its gear ([What a Kill Pays](../../3-world/what-a-kill-pays.md)) (see [the curve](../pre-renewal-ro-port.md)), so a creature that levels up mid-fight simply pays its new level; there is no reward growth rate, and no per-creature pay number is saved.

**Worn gear is serialized on the component, not the dictionary.** `UnitEquipment.startingGear` is the serialized half; the live dictionary is runtime-only. Gear is equipped **lazily on first access**, not in `Awake`, so component wake order between `UnitEquipment` and `UnitStatBlock` cannot matter. Getting this wrong makes mob rings silently do nothing.

**A creature keeps what it wears; on `CreatureLoot.GearDropChance` (10%) of kills `LootDropper` drops a NEW copy of one random piece it wears** (`EquipmentInstance.CreateDropped`: Raw, unfolded, its rarity from `CreatureLoot.DropRarity`). The ring has no drop chance — a ring is not an item any more. `itemDropNames`/`itemDropChances` carry **materials, consumables and gold**, and `Rewards.PayLoot` puts what they pay on the ground, held for the killer.

**Removed dead fields.** `MonsterDef`'s hand-authored `maxHealth`/`attack`/`defense`/`magicDefense`/`hit`/`flee`/`damageSpread` are deleted — all seven are derived in `ToArchetype`, so storing them was a second copy that could disagree.

## The Mob Table is the authoring surface

`Tools ▸ unwritten ▸ Catalogs ▸ Mob Table`:

- **Inline:** id, name, biome, kind, size (a number, its Small / Medium / Large group shown beside it; a shadow's is locked — it is always 1), **LVL + STR/AGI/VIT/INT/DEX/LUK**, element, Element Lv, weight, XP, gold.
- **`pts` column:** `spent/budget` — green on budget, **red over**, amber when a real chunk is unspent. Typing a value the level can't afford **snaps** to the highest that fits (`FitSpreadToBudget`), and only the field you edited is adjusted — untouched stats are never knocked down.
- **Derived readouts** (`=HP/=ATK/=DEF/=FLEE`): read-only, computed live through `UnitStatMath`, so you edit the cause and see the effect. Sorting on them sorts the derived value.
- **`▸` expander:** per-mob **equipment** (one object field per slot; an item is rejected if its slot doesn't match), when a ring is worn its **ranks** with the % each is worth, and on a shadow its **appearance** — a turning 3D preview, a body picker over every body in the roster, and one colour swatch per part of the chosen body, saved by Save All.

**IMGUI gotcha:** the expander toggle is queued in `_pendingToggle` and applied *after* the draw loop. Flipping it mid-draw changes the layout-group count between the Layout and Repaint passes of one frame → "Mismatched LayoutGroup" spam.

## Verifying changes here without Unity

Run `tools/check-unity-code.sh` — it type-checks three passes (the game, the editor tools, and the game as a shipped PC build sees it) in about eight seconds without opening Unity, and reports only real errors. For authored skill data, `tools/check-skill-data.py` proves the assets are sane. Authoring maths (the budget curve, the allocator, the ring percentages) should be **replayed in a throwaway script against the real constants** before being trusted — that's how the "untouched stats get crushed" bug in the budget guard was caught pre-ship.

## Still open

- ~~Reset the roster~~ **DONE** — every creature exactly on its point budget. Re-levelled on 2026-09-22 onto the [Mob Roster](../../3-world/mob-roster.md) ladder (normals 1–99, elites 6–99, shadows 105–170) by re-spending each one's budget through `UnitStatMath.Allocate` in its existing proportions.
- Twenty-six equipment items exist — one weapon per weapon type (fourteen; all but the Sword and the Bow are stand-ins borrowing one of their two looks) and twelve armour pieces (four slots × three materials). Content, not code.
- Roster re-tuning by playing it — the numbers are consistent now, not necessarily *fun* yet.
- Nothing has been run in Play mode.

---

## Related

- [Unified Unit Stats](unified-unit-stats.md) — the design intent this tracks.
- [Implementation Notes](implementation-notes.md) — the skill-side sibling.
- [Active Skill System — V3](README.md) — the design this is the unit half of.
- [Creatures and Where They Live](../../3-world/biome-mob-families.md) — where the creatures came from.
- [Ring System — Current Facts (Trio-Ring)](../ring-system-trio-ring.md) — the curve a mob's ring ranks resolve through.
- [Networking — Implementation Notes](../networking/implementation-notes.md) — server-owned creatures, built from these blocks.
