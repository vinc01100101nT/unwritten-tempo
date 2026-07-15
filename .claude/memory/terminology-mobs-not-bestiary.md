---
name: terminology-mobs-not-bestiary
description: "Game-wide term for enemies is \"mobs\", never \"bestiary\""
metadata: 
  node_type: memory
  type: feedback
  originSessionId: f81eae59-e40a-4631-b8a9-e23e5c066677
---

Across the entire game (code, issues, docs, conversation) call enemies/monsters **"mobs"** — do NOT use the term "bestiary".

**Why:** user's chosen naming convention for the project (stated 2026-06-19).
**How to apply:** use "mobs" in new code identifiers, issue/doc copy, and replies. The old enemy-variety system was named `Editor/Bestiary.cs` ([[direction-2p5d-pivot]] M1 work) — rename references to "mobs" when touched. Related: [[crowd-no-stacking-3d]].
