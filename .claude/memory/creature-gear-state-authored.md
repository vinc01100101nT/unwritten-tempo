---
name: creature-gear-state-authored
description: Every creature's gear grade / folds / Ink strength is authored per creature on its MonsterDef (gearQuality, gearFolds, gearInkTier); no kind rule, no random wardrobe. Starting values ramp with level. Built 2026-09-22, unplayed.
metadata:
  type: project
---

**Decided by the user 2026-09-22, built the same day (unplayed).**

- **Owner:** `MonsterDef.gearQuality / gearFolds / gearInkTier` — one set per creature, shared by every worn piece.
  Edited in the Mob Table's equipment panel. `UnitEquipment` carries a MIRROR (baked by `RigBuilder25D`);
  `MobSpawner.Clone` re-reads the def and re-seats when the mirror is stale, so the table works without a re-bake.
- **Built by** `EquipmentInstance.CreateAt(def, quality, folds, inkTier)` — replaced `CreateAtBest`. Ink stat CHOICE
  and the weapon's trademark are re-rolled every life (`MobRespawn.Reactivate` → `ReseatForNewLife`). Trademark only
  above Raw — the user's contract; it kept normals/elites from gaining one they never had.
- **Deleted:** `CreatureWardrobe` (the hidden 40% random piece per empty slot) and `CreatureKinds.WearsBestGear /
  WearsRandomGear`. An empty slot stays empty. User: "one rule for all mobs".
- **Starting values (user's spec):** normals level 1 Raw +0 → level 99 E +15; elites D +0 → B +30 (levels 6–99);
  shadows all A +30. Folds ramp linearly with level; grade = lowest grade ≥ the kind's floor whose fold cap holds
  the folds; Ink tier = grade index clamped 1–5 (my call, labelled to the user). Full table in docs/3-world/mob-roster.md.
- **Mob Table's derived HP/Atk/Def columns** now count grade+folds; they do NOT count Ink stats (rolled per life).

Related: [[kill-loot-on-ground-random-creature-gear]], [[mobs-get-everything-players-get]].
