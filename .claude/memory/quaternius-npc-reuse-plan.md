---
name: quaternius-npc-reuse-plan
description: How much of the 3D player character stack Quaternius NPCs reuse; the shared-vs-per-role split and the combat-NPC plan for later.
metadata: 
  node_type: memory
  type: project
  originSessionId: 0ff3efe9-4828-4512-b1e3-a69f68b53aea
---

Plan for putting Quaternius NPCs on the SAME live-3D character stack as the player (deferred: town/wander NPCs first, combat NPCs "at a later point"). Matches the standing direction [[direction-2p5d-pivot]] (PLAYER+NPC live-3D, MOBS billboard).

**The body is 100% shared, the brain is per-role.** Everything about how a 3D Quaternius body *plays* an animation is already generic and reused verbatim by both player and NPCs:
- `StanceController` — single choke point deciding which pose shows each frame. Already built for both roles via its `selfDrive` flag (`true` = NPC/mob samples its own motion; `false` = player). `BecomePlayer()` only registers the static `Player` so combat/cast can find it — NPCs skip it.
- `Character3DPose` (implements `IStancePose`) — the Animator-driven render sink; fully role-agnostic. Billboard twin `BakedBillboard8` implements the same interface, so billboard⇄3D is a component swap. See [[character-3d-visual-system]], [[stance-system]].
- Authored `StanceCatalog`/`StanceBinding` library, grounding/feet-snap, gait speed-sync ([[gait-speed-sync]]), face-camera, footsteps — all shared.

**NOT shared — the decision layer:**
- `PlayerStanceBrain.Decide` takes `Player3DController`+`Player3DAttacker` and reads player-only concepts (equipped-weapon attack via `Character.Instance`, jump, fall, dash/roll, shift-walk, pickup, hurt). A wandering townsperson has none of these; its brain is tiny (moving→Walk / still→Idle / face travel dir).
- `Character3DVisual` is player glue: rides the controller, hides the player sprite, subscribes to the player's damage + loot-pickup events. NPCs need their own small driver, `TownNPC3DVisual`, that reads `PathAgent3D` velocity instead (~40 lines, no combat). `TownNPC`'s two billboard-specific lines (`visual.VisualWorldHeight` for bark-bubble height, `visual.enabled` for LOD freeze) must talk to `IStancePose` + a height accessor so it doesn't name the concrete visual.

**Combat NPCs (the deferred bit):** guards/hostile humanoids reuse MORE of the player vocabulary — Attack/Hurt/Die stances too — so overlap GROWS. Their brain looks more like `MonsterAI3D` (aggro/target/attack) than `TownNPC` (wander/bark), but still drives the SAME `StanceController`+`Character3DPose`. Speed sources are now only Baked and FitWindow (the window is fed by whoever drives the stance), and walk-cycle legs lock to the NPC's own ground speed, so a combat NPC needs no player-scoped speed source.
