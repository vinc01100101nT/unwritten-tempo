# Performance

The hard constraint for this project. The canonical doc is `unity/Assets/Scripts/PERFORMANCE.md`, and an edit gate already forces it to be read before any runtime C# change — these two files hold the WHY behind it.

- [Performance is a hard constraint — pre-bake](../performance-first-prebake.md) — merge static colliders; do the work at author time, not in Play.
- [Runtime hot-path perf patterns](../perf-runtime-hotpath-patterns.md) — canonical=`PERFORMANCE.md`; reuse CameraRig, pooled DamagePopup.
- [Unreachable-goal search lag](../unreachable-goal-search-lag.md) — no route (cliff top → below) burned the 6000-cell A* budget every repath (52–57 ms); connected-area labels per map make it 0.1 ms.
