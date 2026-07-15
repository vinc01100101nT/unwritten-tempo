---
name: footstep-walk-run-threshold-bug
description: "Walk footsteps/SFX desynced — gait detection must key off the active gait CLIP, never a move-speed/blend threshold."
metadata: 
  node_type: memory
  type: project
  originSessionId: 2d5e8f08-1ac7-40be-b50d-ec7f3a921097
---

**Symptom:** Walk (Shift) footstep SFX + prints out of sync with the visible walk clip, even with correct L/R foot-plant frames authored in the Stance Composer. Run footsteps were fine.

**Root cause:** `FootstepTracker.Tick` kept two authored plant arrays (`walkPhases`/`runPhases`) and picked which to detect against from the animator `Speed` blend value: `bool run = speed01 >= 0.5f`. But `Character3DPose` sets that blend param to exactly **0.5 for the walk pose, 1.0 for run**, so a steady walk sits at `== 0.5` → chose the RUN plants → desync.

**Wrong first fix (rejected):** moving the split to `>= 0.75` (midpoint of 0.5/1.0). Still a magic move-speed guess — breaks whenever blend values or animations change.

**Real fix:** decide the gait from the CLIP actually being played, not any number.
- `Character3DPose` exposes `enum LocoGait { None, Walk, Run }` + `LocoGait Gait`, set in `Play()` from `clip == runClip` / `clip == walkClip` (the same comparison that already drives the blend). Idle/one-shot → None.
- `TryGetLocoPhase(out float frac, out LocoGait gait)` returns it (replaced the old `out float speed01`).
- `FootstepTracker` picks `run = gait == LocoGait.Run`; `gait == None` is the standstill gate (removed the old `MoveSpeed01` numeric gate).

**Lesson:** footstep gait selection must ride the authored L/R plants of the active gait clip and NOTHING else — no move-speed, no blend threshold — because animations/blend values change constantly. The single source of truth is which StanceId/clip the brain requested (`PlayerStanceBrain` → Walk/Run), surfaced through `Character3DPose.Gait`. Detection drives SFX + puddle + snow prints together. Related: [[direction-2p5d-pivot]].
