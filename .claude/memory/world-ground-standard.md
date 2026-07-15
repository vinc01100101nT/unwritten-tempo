# World ground standard (universal across all maps)

The 2.5D RPG's coordinate convention — **every map obeys this, no exceptions**:

- **Ground height is the Y axis.** (X/Z = the horizontal ground plane; Z is depth, NOT height.)
- **The walkable ground surface sits at Y = 0** in every map. Implemented as a `Ground_Floor` object with a
  BoxCollider whose TOP = `center.y (−0.5) + halfHeight (0.5) = 0`.
- **`Ground_Floor` is on the "Ground" layer (user layer 8).** Movement, pathfinding, and placement rays hit
  this layer. `WorldBuildUtil.EnsureGroundLayer()` owns the layer; `SurfacePoint(p, 1<<groundLayer)` is the
  one true ground-snap (raycasts the Ground layer ONLY — never props).
- **All ground-placed pivots (portals, spawns, props, player) rest on the ground surface (Y = 0).**

## Verified (2026-06-22)
- Town `Ground_Floor`: layer 8, collider top Y0. NatureStage `Ground_Floor`: layer 8, collider top Y0. ✅ same.

## The gotcha that caused a long bug hunt
Hand-dragging an object in the Scene view can snap it onto a **prop/roof collider** instead of the floor —
Town's `Portal_ToField` drifted to **Y = 0.79** (floating above the real Y0 ground). The ground was never
wrong; the object's pivot was. Symptom: the portal/ring looked "elevated" and grounding-to-its-own-origin
couldn't fix it (the origin was itself in the air).

### Standard fix / prevention (AUTOMATIC — no manual step)
- **`Editor/PortalGroundGuard.cs`** (`[InitializeOnLoad]` + `EditorSceneManager.sceneSaving`) snaps EVERY
  `Portal3D` onto the Ground layer right before any scene is saved. A floating portal can no longer be
  written to disk — just press Ctrl+S as normal. Only moves portals that are actually off-ground (1mm
  tolerance), Y only, Ground-layer raycast (ignores props/triggers). Logs how many it corrected. **This is
  the permanent fix; the menu/fit tools are now just backups.**
- `RegionBuilder.FitTriggerToFx` also raycast-snaps the portal pivot to ground before grounding the ring
  (so fit/relink/bake all comply too).
- When seating anything on the ground in NEW tools, snap via `WorldBuildUtil.SurfacePoint(p, 1<<groundLayer)`
  against the Ground-layer mask; never trust a hand-placed Y.
- To extend the guard to other ground entities (NPCs, props, spawns), generalize the type it scans in
  `PortalGroundGuard.OnSceneSaving`.
