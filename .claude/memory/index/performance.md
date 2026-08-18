# Performance

The hard constraint for this project. The canonical doc is `unity/Assets/Scripts/PERFORMANCE.md`, and an edit gate already forces it to be read before any runtime C# change — these two files hold the WHY behind it.

- [Performance is a hard constraint — pre-bake](../performance-first-prebake.md) — merge static colliders; do the work at author time, not in Play.
- [Runtime hot-path perf patterns](../perf-runtime-hotpath-patterns.md) — canonical=`PERFORMANCE.md`; reuse CameraRig, pooled DamagePopup.
