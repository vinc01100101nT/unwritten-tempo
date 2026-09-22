---
name: creature-mastery-never-baked
description: Every creature knew none of its weapon's skills — its authored weapon mastery never reached the baked prefab; fixed 2026-09-22.
metadata:
  type: project
---

Symptom (found 2026-09-22 while building the shadows): every baked creature prefab carried `weaponMasteryLevel: 0`,
so `UnitStatBlock.UsableMastery` was 0 and `Health.KitOf` gave no creature a single skill of the weapon it held —
only its random extra skill. The V4 doc already claimed "a level-20 creature has Weapon Mastery 20".

Root cause: `MonsterDef.ToArchetype()` copied every field into `MonsterArchetype` except `weaponMasteryLevel`, and
`RigBuilder25D` stamps the prefab's stat block from the archetype. The value was right on all 84 assets and lost on the
way into the prefab — the same family as [[mob-stats-dropped-by-unserializable-struct]].

Fix: one line in `ToArchetype` (`weaponMasteryLevel = weaponMasteryLevel`) plus a re-bake of all 96 prefabs
(`MobPrefabBaker.BakeAllCore()` through the live link). Verified by reading the prefab YAML: Snake4 99, Tsubame 160.

**Why:** a field added to `MonsterDef` does nothing until `ToArchetype` carries it AND the prefabs are re-baked; both
steps fail silently.
**How to apply:** when adding a creature field, grep `ToArchetype` and `RigBuilder25D.BuildMonster` for it, re-bake,
then read the value back out of one prefab file.
