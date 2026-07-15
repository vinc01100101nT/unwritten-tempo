---
name: ysort-depth
description: Dynamic top-down depth (z-index) = per-object feet-Y sorting (YDepthSorter) + fixed order BANDS for tilemaps/FX; tall props are OBJECTS not tiles
metadata: 
  node_type: memory
  type: project
  originSessionId: 7e46c3f0-a8b6-4991-952c-5c03f2890bdc
---

On **unwritten** (Unity 6, URP 17 / 2D Renderer), dynamic top-down depth ("z-index": lower on screen = drawn in front) uses **per-object integer sorting + fixed order bands**, decided **2026-06-14** after the pure transparency-axis approach flickered on same-row ties. Source of truth: `DepthSortRuntime.cs`.

## Why NOT the transparency axis alone (history)
First attempt set Camera `transparencySortMode = CustomAxis (0,1,0)` so all sprites/tiles sort by Y. It works ONLY when everything shares one sorting order, and then **same-Y ties flicker** (two units on the same row have equal sort keys → unstable each frame). That was the "sometimes right, sometimes wrong" bug. The axis is now kept only as a harmless tie-fallback.

## The model now: order BANDS (sortingOrder is 16-bit → keep within ±32767)
```
Ground tilemap      −32000   behind everyone (Chunk)
Decor tilemap       −30000   under-foot decals, NO collider (Chunk)
(Obstacles stopgap) −28000   un-converted obstacle tiles park here until converted
ENTITIES         −25000..+25000   per-object by feet-Y (YDepthSorter): player/mobs/NPCs/props
Overhead tilemap    +30000   walk-under canopies/roofs, NO collider (Chunk)
FX               +31000..+32000  slash 31000 / click 31100 / target 31200 / dmg popup 32000
```
Bands are far apart so per-object entity orders swing with world-Y without ever colliding with ground/overhead/FX. All constants live in `DepthSortRuntime` (FxSlash etc.); FX scripts reference them — don't hardcode.

## Pieces
- **`YDepthSorter.cs`** ([ExecuteAlways], on every entity sprite): each LateUpdate sets `sortingOrder = OrderForY(sr.bounds.min.y + sortOffset)`, i.e. sort by the **bottom of the sprite (feet/base)** — pivot-independent, deterministic, no flicker. `OrderForY(y) = clamp(round(-y*50), ±25000)`. ExecuteAlways → previews in the Scene view too.
- **`DepthSortRuntime.cs`** (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + sceneLoaded): on every scene load, sets the camera axis (tie-fallback), `BandTilemap()`s each tilemap by NAME, and `EnsureUnitSorter()`s every in-scene unit. So Play "just works" with no menu. Runtime-spawned monsters get their sorter via `CharacterAnimator2D.Awake → DepthSortRuntime.EnsureUnitSorter`.
- **Obstacle tilemaps → per-object props is now PRE-BAKED (2026-06-16, perf — see [[performance-first-prebake]]).** The bake (flood-fill 4-connected tiles into CLUSTERS → one grouped prop per cluster, child SpriteRenderer per cell, raw tilemap renderer disabled) is the SAME, but it's run at **author/generate time** via `DepthSortRuntime.BakeObstacles()` (the Map Generator calls it; hand-painted maps run **Tools ▸ unwritten ▸ Bake Map (props + collision)** = `MapBaker`). On Play, `BakeObstacleProps` is just a **guarded fallback** (skips tilemaps whose renderer is already disabled). **Static props now get a FIXED `sortingOrder` computed once from foot-Y — NO per-frame `YDepthSorter`** (only moving units keep one). That removes per-frame sort cost on every tree/house.
- **Collision is SEPARATE, pre-baked, and HALF-CELL resolution (2026-06-16).** The bake makes NO colliders. One invisible **`Collision` GameObject** (`CollisionLayerTools`, child of the Grid) holds a **Static `Rigidbody2D` + plain run-merged `BoxCollider2D`** (greedy horizontal run-merge → one box per contiguous row span; each box blocks on its own; green gizmo outlines, Pathfinder wall). ⚠️ **NOT a `CompositeCollider2D`** — a composite generated in the editor shows outlines but loads with EMPTY geometry at Play → player passed through (the 2026-06-16 bug). Plain static boxes are the reliable pattern (same as the old per-cell prop colliders). Collision is stored in **half-cells** (½ a world cell/axis; full cell (X,Y)→half-cells (2X,2Y),(2X+1,2Y),(2X,2Y+1),(2X+1,2Y+1)) so thin objects can block part of a cell. ⚠️ Tilemap colliders are full-cell only — that's why it's boxes, not a Collision tilemap. Built from `TownLayout.collision` (half-cell coords; generator) or name-derived (MapBaker — **hand-painted maps only**, can't see per-prop Custom modes). ⚠️ On-Play per-cell BoxCollider generation was REMOVED — old scenes need re-generating; **Custom props authored before the half-cell switch need re-painting** (old full-cell `collisionCells` now read as half-cells).
- **Per-prop collision is AUTHORABLE** on `PropTemplate` (`collisionMode` Auto/Custom/None + half-cell `collisionCells`): **Auto** = buildings block full footprint, trees block only their bottom row (trunk), each full cell expanded to its 4 half-cells; **Custom** = half-cells clicked in the `PropTemplateEditor` 2×-resolution grid (or a "Collision" capture layer, painted full cells → 4 half-cells each); **None** = off but keeps the cells. `PropTemplate.CollisionFootprint()` (half-cells) + `PropTemplate.HalfCellsOf()` resolve it.
- ⚠️ **GROUP sorting is essential for multi-tile props (round 3).** Per-cell sorting made each canopy cell sort by its own bottom → walking under the leaves popped the player in front. Fix: sort the WHOLE prop by the LOWEST child's base (one foot/trunk line) — now baked as a fixed order; live `YDepthSorter` GROUP mode (no own SpriteRenderer) is still used for dynamic groups.
- ⚠️ **Editor preview caveat:** in edit mode the raw Obstacles tilemap shows flat at order −28000 (behind entities); the real per-prop depth only appears at Play (baking is runtime-only). Acceptable — author in editor, test in Play.
- The old editor **Convert Obstacles to Objects** / **Group Selection** menus were REMOVED (runtime does it). Editor menus now: only **Create Decor + Overhead Layers** + **Refresh Depth Sorting**.
- **Two NON-colliding decoration tilemaps** (`DepthSortTools.CreateDecorLayers`, menu **Create Decor + Overhead Layers**): "Decor" (under-foot, behind chars) and "Overhead" (walk-under, in front). They get NO `TilemapCollider2D` — that component (not the layer name) is what creates tile collision; omit it and tiles never block. (Alt: a Tile asset's `Collider Type = None`.)
- Builders add YDepthSorter: `PlayerBuilder`, `MonsterBuilder.BuildMonster`. Menu **Refresh Depth Sorting** persists banding + sorters into the scene/prefabs for the editor preview (optional; runtime does it anyway).

## Gotchas
- sortingOrder is effectively **16-bit (±32767)** — never exceed; that's why bands stop at ±32000 and entities clamp at ±25000 (≈ ±500 world-units of Y at precision 50).
- Tilemap collision = a **TilemapCollider2D** on the tilemap, NOT the layer's name.
- The OLD "everyone at order 0 + transparency axis" approach is retired (flickered). Don't reintroduce shared-order-0 sorting for entities.
