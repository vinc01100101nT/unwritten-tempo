---
name: crowd-no-stacking-3d
description: "How the 2.5D game stops units stacking / hopping on each other's heads (Crowd layer + PathAgent3D avoidance)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0ef68d6e-0ec8-4ede-9860-5ef602f27d9c
---

In the 2.5D game, mobs used to pile into one spot and the player could stand on top of them. Two causes:
CharacterControllers don't keep each other apart (mobs converge), and a 0.3 `stepOffset` lets a CC climb onto
another CC's capsule (hopping). Fixed 2026-06-18 by porting the 2D recipe ([[pathfinding-grid-astar]]):

1. **Units pass through each other** — `UnitCrowd.cs` (runtime): puts player + mobs on a shared **"Crowd"
   layer** and once calls `Physics.IgnoreLayerCollision(Crowd, Crowd, true)`. Unit↔unit collision off; unit↔
   terrain/props still on. `MonsterAI3D.Awake` + `Player3DController.Awake` call `UnitCrowd.Register(go)`. The
   layer is created at bake by `NatureStageBuilder.EnsureLayer(name)` and assigned in `Build25DSpike.BuildPlayer`/
   `BuildMonster`. **The player now walks THROUGH mobs** (RO-style) — that's the guarantee against hopping.
2. **Mobs fan out** — `PathAgent3D` got the 2D `PathAgent`'s local avoidance (`avoidRadius`/`avoidStrength` +
   `Steer()`: radial push-off + tangential slide). `MonsterAI3D.Start` sets `agent.avoidMask = UnitCrowd.Mask`
   and `agent.avoidIgnore = player` (spread from other mobs, NOT from the chase target). One
   `OverlapSphereNonAlloc` per agent/frame (reused buffer; alloc-free per PERFORMANCE.md).

⚠️ Requires a re-bake to create/assign the Crowd layer: **Setup Systems Scene** (player) + **Populate Mobs
(Nature)** (mobs). Without it, the layer is missing and both fixes safely no-op (old solid-collision behavior).
Tunables: `PathAgent3D.avoidRadius/avoidStrength`. If a unit shouldn't separate (e.g. training dummy), set its
`avoidStrength`/`avoidMask` to 0.
