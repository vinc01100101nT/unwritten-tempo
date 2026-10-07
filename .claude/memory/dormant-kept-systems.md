---
name: dormant-kept-systems
description: Unreferenced-but-kept code — DialogueUI/InteractPrompt (future 3D press-to-talk) — do NOT re-flag as dead
metadata: 
  node_type: memory
  type: project
  originSessionId: 03a09163-330e-4068-a637-2b6b0e261990
---

Two scripts have ZERO references but are kept ON PURPOSE (as of 2026-07-14) — a future dead-code scan will re-flag them; don't delete. (`MultiHitBurst.cs` was on this list; it has since been revived as the live multi-hit flurry, called from `SkillEffectApplier` with a snapshot of its targets, and its combo total is now summed by `StrikeTotals` on the server and drawn by every screen.)

- **`DialogueUI.cs` + `InteractPrompt.cs`** (+ `Resources/DialogueCanvas.prefab`, `InteractPromptCanvas.prefab`, their WorldTools build items) — the world-agnostic UI half of press-to-talk. The 2D finder half (PlayerInteractor/Interactable, BoxCollider2D-based) was deleted; a future 3D dialogue system should rediscover NPCs via a registry scan (the `TownNPC`/`MonsterAI3D.Active` pattern) and call `DialogueUI.Begin/Advance` + `InteractPrompt.ShowAbove`. Nothing instantiates the two prefabs today (`SystemsSceneBuilder` does not pull them in — the "Systems pulls it in" comments were stale).

**Why:** deleting these loses design work vince intends to reuse; re-flagging them wastes a scan round. **How to apply:** before proposing deletions from a dead-code scan, check this list; when implementing NPC dialogue, start from these files. See [[dead-code-guid-scan]].
