---
name: authoring-toolkit-prop-palette
description: "Maps are HAND-AUTHORED via the Prop Palette toolkit, not per-map hardcoded generators"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1cced128-10d8-4d05-bbae-1471cd5538eb
---

**Decision (2026-06-20): maps in the 3D world are HAND-AUTHORED**, not procedurally generated per-map. The user (a Unity beginner) places buildings/props by hand for full control; editor tools handle the fiddly parts (scale, ground-snap, no-overlap). Do NOT write more per-map hardcoded scene bakers like [[fantasy-town-pxltgr]]'s `FantasyTownBuilder` — that stays as a one-off rough-in. Extend the toolkit instead.

**Why:** crafted maps suit an RO-style game; matches the existing generate-then-hand-finish philosophy ([[hand-authored-world-content]]).

The toolkit (`unity/Assets/Scripts/Editor/`):
- **`PropPlacement.cs`** — shared, reusable placement ENGINE (the thing to build on). Bounds-driven: `TryWorldBounds`, `GroundY`, `PlaceOnGround` (footprint-centre→XZ, bottom→ground), `DropToGround`, `SnapToGrid`, `GroundMask()`. Reuse for any new authoring/placement feature.
- **`PropPalette.cs`** — `Tools ▸ unwritten ▸ Prop Palette`: thumbnail grid of `Assets/Prefabs/Props`; pick one, CLICK in Scene view to place it (auto-scale, ground-drop, optional grid snap, Q/E rotate, Esc/right-click stop). Parents under a "Handmade" root. Hides `UCX_`/`_LOD1`/`_LOD2` by default.
- **`PlacementTools.cs`** — `Tools ▸ unwritten ▸ Authoring ▸ …` on selection: Drop to Ground (Ctrl+Shift+G), Scale ×0.3, Snap to Grid (0.5), Rotate 90° (Ctrl+Shift+R).

**How to apply:** new placement/authoring needs → add to `PropPlacement` (engine) + surface via the palette or an Authoring menu command. Keep placement bounds-driven (see [[fantasy-town-pxltgr]] for why pivot-based floats props).
