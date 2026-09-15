# Placement / ground / pivot / collision / pathfinding / depth

- [Pivot = ONLY base-anchor source of truth](../pivot-is-anchor-source-of-truth.md) — snap PIVOT to ground; `RegionBuilder.cs:188`.
- [Billboards FEET-pivoted via UnitAnchor](../billboard-feet-pivot-unitanchor.md) — transform=feet; `UnitAnchor.Feet/Center/Head`.
- [Player capsule floated = skinWidth hover](../player-feet-float-skinwidth-fallback.md) — fix = `capsuleSkinWidth` 0.08→0.01.
- [World ground standard (Y0, Ground layer 8)](../world-ground-standard.md) — `Ground_Floor` layer 8; seat via `WorldBuildUtil.SurfacePoint`.
- [Units don't stack/hop](../crowd-no-stacking-3d.md) — `UnitCrowd` → Crowd layer + `PathAgent3D` avoidance.
- [Pathfinding: grid A* + PathAgent](../pathfinding-grid-astar.md) — `Pathfinder.cs` static A* + `PathAgent.cs`; player avoidRadius 0.
- [Dynamic top-down depth / z-index](../ysort-depth.md) — feet-Y sort (`YDepthSorter`) + order BANDS (`DepthSortRuntime`).
- ["Collision bigger than object" = PLAYER capsule](../prop-collision-footprint.md) — fix = short capsule `r0.15/h0.5/center(0,0.1,-0.2)`.
- [Mobs float ~0.08 = CharacterController skinWidth](../mob-float-skinwidth.md) — fix = `PathAgent3D.Awake` `cc.skinWidth=0.02` (skips player).
- [Live-3D NPC feet bury while walking = dual grounder](../npc-walk-feet-bury-dual-grounder.md) — dead billboard-era `TownNPC.LateUpdate` root-ground fought `TownNPC3DVisual`; fix = DELETE it (townsfolk are all live-3D now, no billboards).
- [Overhead world UI anchors on feet-seated visual, not player root](../overhead-world-ui-anchor-feet-visual.md) — player root = WAIST (centered capsule); bubbles ride `Character3DVisual.Pose`; shared `SpeechBubble.ShowOverHead` + 1-bubble-per-owner registry.
- [Endless fall after the server puts a player back](../fall-loop-after-server-correction.md) — the move check restores a mid-air position and the teleport kept the falling speed; `Teleport` now zeroes `vy`. Why the fall started is still unknown.
