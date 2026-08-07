---
name: player-two-gameobjects-one-store
description: The player is TWO GameObjects (persistent Character data + spawned body); they must share ONE UnitStates or player states split and break.
metadata: 
  node_type: memory
  type: project
  originSessionId: 52f1b21a-c216-426e-94d3-10993632fd09
---

The **player is built from two GameObjects**, not one (measured from `Systems.unity`):
- **Character data GO** (`fileID 763514983`) — `Character` (the persistent `Character.Instance` singleton, IDamageable; the player has NO separate `Health`). Holds combat math + the buff-tray reads.
- **Body/rig GO** (`fileID 236044104`) — `Player3DController` + `StatusController` (movement, ailments, Haste, action-lock).

Because both `Character` and `StatusController` resolved their `UnitStates` via `GetComponent ?? AddComponent`, the player ended up with **TWO separate stores**. Symptom (2026-08-06): Haste (written by `StatusController` → body store) sped the player but showed **no tray icon** (tray reads `Character.States` → data-GO store); Guard/DamageBuff/Shield/Regen (written by `Character`) showed fine. Every ailment on the player was invisible to the tray for the same reason.

**Fix:** the player's `StatusController` shares `Character.Instance.States` — one store. Resolved **lazily** (a `states` property, not an Awake field) because the two GOs' Awake order isn't guaranteed; by the first `Update` all Awakes are done and `Character.Instance` is set. Null-guarded for the pre-Character load window. Mobs are a single GO → still own their store.

**Rule:** any player behaviour that reads/writes unit state MUST go through the ONE shared store. Before adding a player state field, check it lands in `Character.Instance.States`, not a body-GO component. See [[unified-state-store]] design. Related: mob move-speed had the sibling dual-path bug — the mover read a private `slowFactor` instead of the store; fixed to read `Health.States.MoveMult` (folds Slow×Haste) so mob Slow AND Haste flow through the one source.
