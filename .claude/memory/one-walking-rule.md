---
name: one-walking-rule
description: Player, creatures, townsfolk and the route-finder all walk by GroundElevation's one rule — 45° slopes, 0.075 m steps; slope vs step told apart by the ground's tilt.
metadata:
  type: project
---

Built 2026-09-27 at the user's request ("centralize how players walk, so mobs will do the same. no discrepancies").

- **The rule** lives in `GroundElevation`: `MaxClimbStep` 0.075, `MaxWalkSlopeDegrees` 45, `Walkable(rise, run, slopeTanA, slopeTanB)` (both spots ≤45°, and |rise| ≤ step + run × mean slope tangent), `SlopeTan(normal)`, `ApplyWalkRules(cc)`, `StepBound(cc)`. Pinned in `tools/game-math-tests/Checks.cs` `WalkingRule()`.
- **Bodies:** `CharacterSpec.ApplyCapsule` (player, townsfolk) and `PathAgent3D.Awake` (creatures, townsfolk) call `ApplyWalkRules`; `RigBuilder25D` too. `CharacterSpec.capsuleStepOffset/capsuleSlopeLimit` fields were REMOVED (asset lines stripped). Before: creature prefab steps 0.039–0.23; 10 smallest creatures still held at 0.039–0.07 by Unity's capsule limit (open decision MT-47).
- **Routes:** `Pathfinder3D` caches height + slope tangent per cell (`GroundCell`), and the A* neighbour gate, `HasClearWalkableLine` and `LabelAreas` all call `Walkable`. `Pathfinder3D.MaxStep` and `Pathfinder3DConfig.maxStep` were REMOVED (the `maxStep:` line stripped from all 91 scenes; lastline had 0.6 — all 28 door/arrival pairs still route). The old per-cell height-difference gate treated any slope over ~5.4° as a wall.

Doc: `docs/2-systems/walking-and-routing.md`. Related: [[unreachable-goal-search-lag]], [[biome-table]].
