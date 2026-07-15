---
name: mob-item-map-data-tables
description: Mobs/items/maps are now data tables keyed by stable kebab id (MonsterDef + MonsterCatalog); the foundation quests will reference. Built 2026-06-27.
metadata: 
  node_type: memory
  type: project
  originSessionId: 9afc9c65-f724-4e6a-9f17-28f323d3e072
---

Built 2026-06-27 (the foundation for the quest system — quests reference mobs/items/maps by stable id). Goal: move mobs out of hardcoded code into editable data assets with stable IDs, matching how Items/MapRecipe already work.

**Mobs — converted from a hardcoded C# array to ScriptableObject assets:**
- `MonsterArchetype` (the stamp DTO struct) MOVED from `Editor/MobRoster.cs` → runtime `Scripts/MonsterArchetype.cs` (so runtime `MonsterDef` can return it; editor types can't be referenced by runtime code).
- `Scripts/MonsterDef.cs` — `[CreateAssetMenu] unwritten/Monster`. Mirrors `MonsterArchetype` field-for-field + adds `id` (kebab, e.g. `mob.slime`), `displayName`, `isBoss`. `ToArchetype()` returns the struct the builders already consume → nothing about how a mob is stamped changed.
- `Scripts/MonsterCatalog.cs` — runtime registry; `Resources.LoadAll<MonsterDef>("Monsters")` ONCE + caches (no per-frame work). `Get(idOrName)` (id first, then displayName/name, case-insensitive), `Boss` (first `isBoss`), `WeightedPick()` (non-boss weight>0), `Count`, `Reload()` (drop cache after editing assets in-editor).
- Assets live in `Assets/Resources/Monsters/*.asset` — 6 migrated: `mob.slime/bat/kappa/cyclope/flam` + `mob.ancient-dragon` (boss). Add a new mob = Create ▸ unwritten ▸ Monster (no code).

**IDs added** to `Item.cs` (`id`, e.g. `item.life-potion`) and `MapRecipe.cs` (`id`, e.g. `map.field-01`). ⚠️ Existing Item/MapRecipe assets on disk deserialize with `id = ""` (Unity gives missing fields the type default, NOT the C# initializer) — they need ids filled in before quests can reference them.

**Consumers rewired** to prefer catalog, fall back to `MobRoster` if `Count==0` (pre-migration safety): `RecipeGenerator.ResolveArchetype` (recipes' `archetypeName` now means a mob id OR name OR "Boss"), `RegionBuilder` Spawn Field Boss.

**`MobRoster` (Editor) kept** as the migration seed + fallback only — delete once assets are confirmed the source of truth (then also the `MobRosterMigration` QUICKFIX tool). Item drops are still name-strings (`itemDropNames` → `Resources.Load<Item>`); a later pass could switch to direct `Item`/id refs.

**Gotchas:** Unity MCP `execute_menu_item` FAILS on the migration's submenu item ("no menu named …", and the `→` arrow char breaks the lookup too) — ran the migration via `execute_code` instead. The QUICKFIX menu is `Tools ▸ unwritten ▸ Quick Fixes ▸ Migrate MobRoster to Assets`.

Relates to [[recipe-generator]], [[hud-progression-windows]] (Item), [[world-creator-pipeline]] (maps). Next: quest system on top of these IDs.
