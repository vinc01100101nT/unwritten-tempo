---
name: biome-none-agnostic
description: Biome-agnostic mobs = Biome.None (no separate bool); Biome enum is APPEND-ONLY
metadata: 
  node_type: memory
  type: project
  originSessionId: 479e836b-0a24-472a-b5a3-84eda6b112fa
---

2026-06-29: collapsed the mob "spawns anywhere" concept from a separate `MonsterDef.biomeAgnostic` bool into a `Biome.None` enum value. **A mob with `biome == Biome.None` is spawn-eligible in ANY biome's tier band** — that IS "agnostic." The `biomeAgnostic` field is GONE; don't reintroduce it.

- Enum: `public enum Biome { Grassland, Snow, Desert, Volcanic, Corrupted, None }` in `MapRecipe.cs`. **APPEND-ONLY** — Unity serializes enums by integer index, so inserting/reordering silently remaps every saved `.asset`. `None` is deliberately last (=5).
- Spawn filter = `MonsterCatalog.Eligible` → `m.biome == Biome.None ? includeCommons : m.biome == biome`.
- **Two distinct biome fields:** `MapRecipe.biome` (the MAP's theme — never None) vs `MonsterDef.biome` (where a mob spawns — None=anywhere). Switches on the MAP biome (`RecipeGenerator.BiomeOf`, `BiomeRecipes`) never see None; only mob-side readers do.
- `MobBiomeImporter` imports commons as `biome = None`; `BiomeElement(None)` → `_ => Neutral`.
- Mob Table window (`MobTableWindow.cs`): the old `A` column is removed; None is just a dropdown choice. Also fixed that window's header misalignment (header now uses the same manual per-column rects as rows, offset by `-_hscroll.x`).
- Migrated 6 existing agnostic mobs to None via a one-shot QUICKFIX (since deleted).

Builds on [[mob-item-map-data-tables]].
