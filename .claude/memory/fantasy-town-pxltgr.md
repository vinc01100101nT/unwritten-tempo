---
name: fantasy-town-pxltgr
description: Pxltgr Fantasy Assets pack = town buildings/ground; ×0.3 scale matches units; two editor tools
metadata: 
  node_type: memory
  type: project
  originSessionId: 1cced128-10d8-4d05-bbae-1471cd5538eb
---

> **⚠️ 2026-06-22 — `FantasyTownBuilder.cs` was DELETED. Towns now come from [[recipe-generator]]** (`MapRecipe` + `RecipeGenerator`). Everything else here is STILL TRUE and important: the `FbxPropExtractor` pipeline, the ×0.3 prop / target-size ground scale convention, and the BOUNDS-DRIVEN (not pivot) placement rule — the recipe generator reuses `PropPlacement` which encodes exactly that. Ignore the `FantasyTownBuilder`/`Region ▸ Build Fantasy Town` references below.

The **Pxltgr "Fantasy Assets"** pack (`Assets/Art/Pxltgr/.../Assets.fbx`) is the town art source — houses, taverns, windmill, ground tiles, trees, furniture, food, carts. It shipped as ONE multi-object FBX with per-prop textures (not one shared atlas like the Kenney/LowPolyNature packs — see [[kenney-3d-terrain-overlap]]).

Pipeline (both are one-click `Tools ▸ unwritten`):
- **`FbxPropExtractor.cs`** → `Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props` slices each top-level FBX child into its own prefab under `Assets/Prefabs/Props/`, preserving the imported per-prop material. Foliage meshes (leaf/leaves/canopy/foliage) get NO collider (walk under canopies). Note: the extract also produced `_LOD0/1/2` variants + `UCX_` collision hulls — builders should use the base/`LOD0` meshes and skip `UCX_`.
- **`FantasyTownBuilder.cs`** → `Region ▸ Build Fantasy Town (houses + grounds)` bakes `Assets/Scenes/Town.unity`: tiled `ground_*` plot (collider-free tiles over ONE flat floor BoxCollider on the **Ground** layer), houses lining a central street, Tavern+Windmill, tree ring/carts/scatter, spawns + cyan Portal3D → NatureStage, and auto-links the field back (gold portal). Reuses [[persistent-systems-layer]] travel plumbing via `RegionBuilder.MakeSpawn/MakePortal/LinkFieldBack` (made public).

**Scale convention: PROPS (houses/trees/decor) are placed at ×0.3** — the scale the artist confirmed matches our player/unit size. GROUND tiles are NOT ×0.3 — they're scaled to a target world size (~2.5u) so a few big tiles cover a plot instead of thousands of tiny ones (a ground tile at ×0.3 is ~0.3–0.6u).

**Placement must be BOUNDS-DRIVEN, not pivot-based.** The extracted prefab roots are localScale 1 / localPosition 0 with the mesh on the root, BUT the mesh origin is offset from the geometry — so placing the pivot on the ground floats the prop, and pivot-spacing overlaps neighbours. FantasyTownBuilder measures each instance's world renderer bounds and sets footprint-centre → target XZ, bottom-of-bounds → ground Y; spacing uses measured widths. Reuse this pattern for any future placement of these prefabs.

Pathfinding/collision contract (see [[crowd-no-stacking-3d]]/Pathfinder3DConfig): obstacles = anything NOT on the Ground layer, so houses/trees (Default layer, keep colliders) block; ground tiles (Ground layer) and small decor (colliders stripped) don't. If houses face away from the street, flip `HouseFrontIsPlusZ` in FantasyTownBuilder and re-run.

**2026-06-21 — WHOLE WORLD IS NOW Pxltgr (LowPolySimpleNature pack DELETED).** See [[pxltgr-field-migration]]. The field (`NatureStage.unity`) is rebuilt from Pxltgr too via `PxltgrFieldBuilder` (`World ▸ New Field (Pxltgr grass)`) + `PxltgrScatter` (`World ▸ Scatter Nature (Pxltgr…)`); both town & field share `PxltgrGround.LayPlot` for the flat grass plot. The old `NatureStageBuilder`/`NatureScatter` + `Assets/Prefabs/LowPolyNature` + `Assets/Art/LowPolySimpleNature_FBX` are gone; shared layer/collider/scene-registration helpers moved to `WorldBuildUtil`.
