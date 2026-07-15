---
name: dormant-kept-systems
description: Unreferenced-but-kept code — MultiHitBurst (Meteor multi-hit revival planned) and DialogueUI/InteractPrompt (future 3D press-to-talk) — do NOT re-flag as dead
metadata: 
  node_type: memory
  type: project
  originSessionId: 03a09163-330e-4068-a637-2b6b0e261990
---

Three scripts have ZERO references but are kept ON PURPOSE (as of 2026-07-14) — a future dead-code scan will re-flag them; don't delete:

- **`MultiHitBurst.cs`** — vince plans to revive RO-style multi-hit for **Meteor**. No live replacement exists: `SkillContext.HitCount` (Flurry) is single-target melee only; the AoE path (SkillRunner repeatCount / multi-tick VFX) re-scans the area per tick and lacks MultiHitBurst's two unique features: **snapshot targeting** (lock the blast's mobs, deliver all ticks to that set) and the **per-mob accumulating combo-total popup**. When rewiring: fix its per-burst `new WaitForSeconds` + per-cast closure, and plug it in as a caller of `SkillEffectApplier`'s sinks (one damage path), not a second engine.
- **`DialogueUI.cs` + `InteractPrompt.cs`** (+ `Resources/DialogueCanvas.prefab`, `InteractPromptCanvas.prefab`, their WorldTools build items) — the world-agnostic UI half of press-to-talk. The 2D finder half (PlayerInteractor/Interactable, BoxCollider2D-based) was deleted; a future 3D dialogue system should rediscover NPCs via a registry scan (the `TownNPC`/`MonsterAI3D.Active` pattern) and call `DialogueUI.Begin/Advance` + `InteractPrompt.ShowAbove`. Nothing instantiates the two prefabs today (`SystemsSceneBuilder` does not pull them in — the "Systems pulls it in" comments were stale).

**Why:** deleting these loses design work vince intends to reuse; re-flagging them wastes a scan round. **How to apply:** before proposing deletions from a dead-code scan, check this list; when implementing NPC dialogue or Meteor multi-hit, start from these files. See [[dead-code-guid-scan]].
