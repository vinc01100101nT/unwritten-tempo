# Portal/warp color convention (by destination)

Warp portals are colour-coded by the **kind of map they lead to**, using Hovl Studio's upright
"Portal" ring prefabs (`unity/Assets/Art/Hovl Studio/Magic effects pack/Prefabs/Portals/`).

- **Town → yellow** (`Portal yellow.prefab`) ✅ available
- **Field → green** (`Portal green.prefab`) ✅ available
- **Dungeon → purple** ⚠️ no `Portal purple.prefab` in the pack yet → currently falls back to blue + logs a
  warning. Drop a purple portal prefab into `Prefabs/Portals/` to enable it.
- **Special map → red** (`Portal red.prefab`) ✅ available

## Where it lives in code

`unity/Assets/Scripts/Editor/RegionBuilder.cs`:
- `enum WarpKind { Town, Field, Dungeon, Special }`
- `KindOfType(mapType)` — maps the DESTINATION map's `type` from the world layout (town | field | dungeon |
  special) → `WarpKind`. Never the scene name: a map's id is its name in snake_case (`mines_1`, `lastline`), which says
  nothing about its kind. (Until 2026-09-26 it matched substrings of the name, so `dg_*` doors came out green.)
- `PortalPrefabFor(kind)` / `PortalTintFor(kind)` — kind → prefab path / cylinder-fallback tint.
- `MakePortal(..., destinationType)` → `AddWarpFx(portal, destinationType)`; World Creator passes the target map's
  `type` on every door it builds.

To add a new map kind: add an enum case + a case in `KindOfType` + entries in the two `...For(kind)` maps, and the
same type in the browser World Creator.

## Portal transform facts
- Portal ROOT is always unit scale (`localScale = Vector3.one` pinned in `MakePortal`) so the warp FBX/VFX
  fits at native size. (An old hand-edited Town portal sat at 0.92929 — fix stray ones to 1,1,1.)
- **Trigger volume** = a doorway, not a room: `TriggerSize = 1.5×2×1.5`, `TriggerCenter = 0,1,0`
  (single-source consts in `RegionBuilder.cs`). The old `3×2.4×3` cube was way too big.
- Warp ring child: `WarpFxScale = 0.5`, `WarpFxHeight = 1.0` (stands upright on the ground) — both
  eyeball-tunable in `RegionBuilder.cs`.
- **Relink Region** (`RefreshWarpFxInScene`) now ALSO normalises existing portals: resets root scale to 1
  and re-fits the trigger — so re-running it fixes stale giant-cube triggers in baked scenes.
- **`FitTriggerToFx`** (run by the `World ▸ Fit Portal Triggers` menu, Relink, and `MakePortal`) SIMULATES
  the warp particles, measures only the **ring core** (particles ≥ 40% of max size — ignores the thin
  sparks), grounds that ring's bottom to the portal origin (= terrain), and sizes the BoxCollider 1:1 to it.
  ⚠️ Earlier bug: measuring the *whole* particle cloud grounded the spark cloud while the ring kept floating
  — fixed by the size-filter. The fit is idempotent (re-running converges). It only works in-editor (can't
  measure particles from a file edit), so applying it to a live scene = run the menu once, then save.
- Built 2026-06-22, swapping the old flat `Magic circle.prefab` ground decal for upright portals.
