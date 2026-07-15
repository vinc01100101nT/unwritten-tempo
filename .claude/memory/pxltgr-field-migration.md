---
name: pxltgr-field-migration
description: World is 100% Pxltgr now — LowPolySimpleNature pack removed; field rebuilt via PxltgrFieldBuilder/Scatter/Ground; shared utils in WorldBuildUtil
metadata: 
  node_type: memory
  type: project
  originSessionId: 27a9b251-6892-4faa-b2a4-f05c69568fc2
---

> **⚠️ SUPERSEDED 2026-06-22 — see [[recipe-generator]].** `PxltgrFieldBuilder.cs`, `PxltgrScatter.cs`, and `NatureMobs.cs` (the `New Field` / `Scatter Nature` / `Populate Mobs` menu commands) were **DELETED** and replaced by the single content-driven `MapRecipe` + `RecipeGenerator`. Still TRUE below: the world is 100% Pxltgr, `WorldBuildUtil` holds the shared helpers, and `PxltgrGround.LayPlot` is the shared grass-plot builder (the recipe generator calls it). IGNORE the `PxltgrFieldBuilder/Scatter` references — use the recipe flow.

**2026-06-21:** Removed the **LowPolySimpleNature** pack so the whole world (town + field) is one consistent Pxltgr art style. The town was already Pxltgr ([[fantasy-town-pxltgr]]); the field is now too.

**Why:** consistency — the town is built from Pxltgr, so the field shouldn't use a different pack.

**How it's structured now:**
- `WorldBuildUtil.cs` — neutral, pack-agnostic helpers (`EnsureLayer`/`EnsureGroundLayer`/`SetLayerRecursive`/`EnsureMeshColliders`/`SurfacePoint`/`RegisterInBuildSettings`). These used to live on `NatureStageBuilder`; everything (`RegionBuilder`, `GroundFill`, `FantasyTownBuilder`, `SystemsSceneBuilder`, `Build25DSpike`) now calls `WorldBuildUtil.*`.
- `PxltgrGround.LayPlot(...)` — the ONE shared flat grass-plot builder (grass + soil/stone speckle, optional central road, GPU-instanced collider-free tiles over ONE flat floor BoxCollider on the Ground layer, scene-scoped Pathfinder3DConfig). Used by the field AND `RegionBuilder.BuildTownHub`.
- `PxltgrFieldBuilder.cs` — `World ▸ New Field (Pxltgr grass)` bakes the field at `Assets/Scenes/NatureStage.unity` (kept that scene NAME so the existing Town↔field portal wiring resolves with no churn). Also owns the forest day/night ambience (`Add Forest Ambience` menu, moved here).
- `PxltgrScatter.cs` — `World ▸ Scatter Nature (Pxltgr, fill map)` scatters Pxltgr trees/stumps/rocks/bushes/grass/flowers/ferns/mushrooms (from `Assets/Prefabs/Props`) at ×0.3, seated by measured bounds; solids keep mesh colliders, decor stripped, all on Default layer.

**DELETED:** `NatureStageBuilder.cs`, `NatureScatter.cs`, `Assets/Prefabs/LowPolyNature/`, `Assets/Art/LowPolySimpleNature_FBX/`, the old `NatureStage.unity`, and the `Build Low Poly Nature` menu in `KenneyPrefabBaker` (its generic `Build From Folder` baker is kept).

**Rebuild flow after this change:** `New Field (Pxltgr grass)` → `Scatter Nature (Pxltgr…)` → `Populate Mobs (Nature)`. The boot map is still Town; the deleted `NatureStage.unity` must be rebuilt before the Town's field portal works again (its Build-Settings entry re-registers on rebuild). `NatureMobs` is pack-agnostic (billboard mobs) and was kept.
