---
name: performance-first-prebake
description: Hard rule — unwritten must stay smooth; prefer pre-bake over on-Play work; watch GPU/CPU/RAM
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 10ec24ef-fa97-4d6d-a61e-96a008c5168b
---

For **unwritten** (the Unity 2D game), runtime performance is a first-class constraint from 2026-06-16 onward. The user's words: "a 2D game that lags is a bad game… the performance should not be problematic to the players." Treat GPU, CPU, and RAM as things to actively protect in every design decision.

**Why:** it's "just a 2D game" — players rightly expect it to be perfectly smooth; lag is unacceptable and reflects badly on the whole project.

**How to apply:**
- **Pre-bake over on-Play.** Move work to editor/generate/author time and SAVE it into the scene; avoid `RuntimeInitializeOnLoad` / scene-load generation that creates objects/colliders each load. (The `[[ysort-depth]]` on-Play `DepthSortRuntime.BakeObstacleProps` is being migrated to a generate-time pre-bake; see [[map-generator-tool]].)
- **Merge static colliders.** Static world collision → `CompositeCollider2D` (few merged polygons) on a Static body, not hundreds of per-cell `BoxCollider2D`. Pathfinder-friendly too ([[pathfinding-grid-astar]]).
- **Don't sort what doesn't move.** Static props get a fixed `sortingOrder` computed once from foot-Y at bake; only DYNAMIC units (player/mobs/NPCs) keep a per-frame `YDepthSorter`.
- **Fewer draw calls / renderers.** Favor chunked tilemaps or one-renderer-per-prop (mesh combine) over one SpriteRenderer per cell when it matters; keep one shared material/atlas so URP's SRP batcher merges.
- **Default to the cheaper option and say why.** When proposing an approach, state its GPU/CPU/RAM cost vs alternatives (the user asks).
