---
name: mob-stats-dropped-by-unserializable-struct
description: Every creature loaded at level 1 with all six primaries at 1 because the baked stat block (a struct) lacked [System.Serializable], so Unity silently dropped it from all 84 mob prefabs on save.
metadata:
  type: project
---

**Symptom (found by the 2026-09-20 repository audit, never watched in play):** all 84 baked creature prefabs carried the stat-block component but none held its `inputs:` entry — every creature spawned at level 1, health 3, every primary 1, while the creature data files span levels 3–46. A level-46 boss and a level-3 rat fought identically. The 2026-09-14 "one-hit kills" play-test complaint is the likely visible face of it.

**Root cause (measured):** `UnitStatInputs` in `unity/Assets/Scripts/UnitStatMath.cs` is a plain `struct` with no `[System.Serializable]`. `UnitStatBlock.inputs` (a public, tooltipped field, filled by the baker in `Editor/RigBuilder25D.cs`) is therefore not serializable, so `PrefabUtility.SavePrefabAsset` wrote the two list fields beside it and silently dropped the struct. On load the field keeps its C# initializer, `UnitStatInputs.Default(1)`. The only runtime re-stamp, `UnitStatBlock.SetSpawnInputs`, is called from one GM command and nothing else.

**Fix:** `[System.Serializable]` on the struct (2026-09-20), then **re-bake every creature** — Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs. Adding the marker alone changes nothing: the prefabs on disk still lack the entry until they are written again.

**How to apply / gotchas:** any struct that a baker writes into a prefab or asset field needs the marker, and the proof is to grep the saved file for the field name — a component whose OTHER fields are written while one is missing is this exact bug. The baked values' presence is now checked by play-test checklist batch 20, without-play step 3. Related: [[find-or-create-hides-duplicates]] (same "silent success" family).
