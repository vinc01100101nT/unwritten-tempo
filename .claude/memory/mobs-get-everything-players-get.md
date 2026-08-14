---
name: mobs-get-everything-players-get
description: "HARD LAW — every skill, stat, state and effect that works on the player must work on mobs too. Only STANCES are player-only."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: dc29f1e3-014e-4f28-81af-65199a08afda
---

**Every skill, every stat, every state, every effect that applies to the player MUST also apply to mobs. The ONLY
exception is STANCES.** Stated by the user 2026-08-13 as the single rule to follow, after repeated frustration at
me reporting player-only limits as if they were design.

**Why:** the user's world model is that a mob and the player are the same kind of thing — a combat unit. Any place
the code can only do something to the player is a BUG or unfinished plumbing, never a design boundary. When I
described such a gap as a constraint ("the player has no aggro", "mobs have no dash"), I was inventing design out
of missing code and handing it back as fact. That is the same failure as
[[buff-and-debuff-are-separate-effects]] and the broadened-paraphrase scar: dressing an implementation gap as a
rule.

**How to apply:**
- Never write "X only works on the player" as an explanation. Either make it work on both, or say plainly "this
  isn't built for mobs yet" and build it.
- Before adding ANY unit behaviour, put it on the shared handle (`ICombatUnit`) or the shared store
  (`UnitStates`), never on `Character` alone. See [[player-and-npc-share-one-function]] for the same rule applied
  to player/NPC bodies.
- A per-rig split is allowed ONLY for the mechanism (player mover vs mob pathing agent), never for the capability
  — both sides implement the same named method.
- Stances (`StanceController` / `Character3DPose`) are the one carve-out; do not generalize those.
