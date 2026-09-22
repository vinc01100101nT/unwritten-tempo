---
name: creature-kinds-and-world-spawns
description: Creatures are placed by the WorldSpawns list read at map load — never by spawners saved in scenes; kind (normal/elite/shadow) drives slots, summons, gear, drops, respawn.
metadata:
  type: project
---

Built 2026-09-22 (design and numbers: docs/3-world/mob-roster.md):

- **Where creatures live = `Resources/WorldSpawns`**, one row per spawn area (map, named creature or biome+level mix,
  population, map-local center, radius). `MapManager.Activate` → `WorldSpawner.Populate` builds runtime
  `FieldMobSpawner`s on the server. No scene holds a spawner any more (all 34 removed) and the generator places none.
  `/where` prints map-local coordinates for a row's center.
- **`MonsterDef.kind`** replaced `isBoss`; the old 18 bosses are elites. `CreatureKinds` owns every per-kind number
  (extra slots 1/2/4, max summons 0/10/5, respawn 5s+3 / 30m–3h / 4–6h). `Health.kind` is copied from the def in
  `MobSpawner.Clone`, the one door every creature is made through.
- **Summoning** = the unpublished `skill.summon` (effect kind `Summon`), a slot of its own; `CreatureSummons` on the
  summoner, `SummonedCreature` on each summon; picks come from `SummonPicks` at runtime (never authored).
- **Drops** = `CreatureLoot` (grade from level, amounts from kind) inside `LootDropper`; no bake needed to change them.

**Why:** the user hates managing map scenes; one list is the single home for creature placement.
**How to apply:** to place or move creatures, edit a WorldSpawns row — never add a spawner to a scene. A new
per-kind rule goes in `CreatureKinds`. See [[auto-pilot-ai-is-the-term]], [[creature-mastery-never-baked]].
