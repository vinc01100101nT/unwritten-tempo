---
name: npc-footsteps-frame-locked
description: "NPC step SFX/ripple centralized onto the player's frame-locked plant detector (was a stale odometer)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3e39a0b1-5766-4f78-bfce-7c8628f7b80d
---

Town NPC footstep SFX + puddle ripple were **misaligned to the visible legs** because `TownNPC.Footsteps()` fired steps off a DISTANCE odometer (`sStride = NpcStepCadence 0.42s × runSpeed`) — a leftover from the billboard era. Townsfolk are now live-3D (`AmbientLife` adds a self-animating `Character3DPose`, `AmbientLife.cs:113`), so they have the SAME `TryGetLocoPhase(frac, gait)` foot-plant signal the player uses; the odometer cadence was unrelated to the walk clip's authored plant frames → foot-slide.

**Fix (2026-07-21):** extracted the player's frame-locked detection into one shared method `FootstepTracker.DetectPlants(Character3DPose, pos, Gait, onPlant)` + a per-character `FootstepTracker.Gait` state class (single source of truth for "animation crossed an authored plant frame → step under this foot"). Player (`FootstepTracker.Tick` → `OnPlayerPlant` → `Stamp`, player ring + `PlayerStepped` SFX) and each NPC (`TownNPC.OnFootPlant` → `EmitStep` NPC ring + positional `Sfx.PlayAt`) both route through it. `onPlant` delegates are cached fields (zero per-frame closure alloc). Seed `Gait` on stop/freeze/re-enable (`Gait.Seed`) to avoid a phantom step from a stale pos.

Plant phases/offsets are OWNED by `FootstepTracker` (mirrored from Walk/Run `StanceBinding.footPlants`), shared by all walkers. Gotcha: `DetectPlants`'s `ResolvePhases` reads the tracker's PLAYER `pose` for clip frame counts — fine since NPCs borrow the same walk/run clips; self-heals if player pose not yet found. Related: [[footstep-walk-run-threshold-bug]].
