---
name: dev-settings-panel
description: In-game DevSettings panel (P key) live-tunes gameplay knobs — add new tunables here
metadata: 
  node_type: memory
  type: project
  originSessionId: 5be8e8d3-e529-463f-8c5c-ed5597d49c3c
---

`unity/Assets/Scripts/DevSettings.cs` — a self-booting IMGUI window toggled with **P** that live-tunes
gameplay knobs grouped by category (Player, Player Combat, Player Stats, Mobs, Environment, Camera).
Built 2026-06-18. Pattern mirrors [[perf-runtime-hotpath-patterns]]'s `PerfHud` (F3): zero scene wiring,
`#if UNITY_EDITOR || DEVELOPMENT_BUILD` gates only the bootstrap so release carries no cost.

**To add a tunable:** add one `F(...)`/`I(...)`/`B(...)` line in `BuildKnobs()` with a get/set closure
to the live component. Targets are lazily re-found (survive map swaps). Mob knobs fan out: read the
first `MonsterAI3D.Active`, write to ALL of them (+ the sibling `PathAgent3D` for move speed).

**Gotcha:** IMGUI isn't in the UI EventSystem, so `DevSettings.Blocking` (true while cursor is over the
panel) is checked by `Player3DController.ReadInput` and `CameraFollow3D.HandleInput` to stop slider
clicks leaking into the world. Any NEW input script that reads mouse clicks should check it too.
The snap-radius knob needs `Player3DController.ApplySnapRadius()` because that field has a static mirror.
