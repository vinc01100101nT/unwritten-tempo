# Portal/warp color convention (by destination)

Every door is the standing, animated warp ring (the user's standard since 2026-10-11 — never the flat disc),
colour-coded by the **kind of map it leads to**. The rings live in `unity/Assets/1_Authored/Prefabs/VFX/Portals/`.

- **Town → yellow** (`Portal yellow.prefab`)
- **Field → green** (`Portal green.prefab`)
- **Dungeon → purple** (`Portal purple.prefab` — made 2026-10-11 as a copy of the blue ring with its three tinted
  particle systems set to 0.6, 0.35, 0.9)
- **Special map → red** (`Portal red.prefab`)

## Where it lives in code

`unity/Assets/Scripts/Editor/RegionBuilder.cs`:
- The kind and its colour are the runtime table `MapKinds` (`Of(type)`, `ColorOf`) — shared with the zone-view door
  marks and the arrival name flash. Kind comes from the DESTINATION map's `type` in the world layout, never the scene
  name: a map's id (`hlt_dun01`, `lastline`, `old_oak`) says nothing reliable about its kind.
- `PortalPrefabFor(kind)` — kind → ring prefab path.
- `DressPortal(portal, destinationType)` — THE one look of a door: strips any old `WarpPad`/`WarpFX` child, adds the
  ring, grounds it and fits the trigger. `MakePortal` calls it; re-dressing existing scenes calls it too.
- A missing ring is an error and the door is left alone — there is NO flat fallback. **Why:** the ring folder moved
  to `1_Authored` while the code still pointed at `0_Raw/Hovl Studio/…`, and the old cylinder fallback silently gave
  almost every generated door a flat disc until 2026-10-11.

To add a new map kind: add a `MapKind` case + its `MapKinds.Of`/`ColorOf` lines + an entry in `PortalPrefabFor`.

## Portal transform facts
- Portal ROOT is always unit scale (`localScale = Vector3.one` pinned in `MakePortal`) so the warp FBX/VFX
  fits at native size.
- **Trigger volume** = a doorway, not a room: `TriggerSize = 1.5×2×1.5`, `TriggerCenter = 0,1,0`
  (single-source consts in `RegionBuilder.cs`), used only when the ring cannot be measured.
- Warp ring child: `WarpFxScale = 0.5`, `WarpFxHeight = 1.0` (stands upright on the ground).
- **`FitTriggerToFx`** (run by the `World ▸ Fit Portal Triggers` menu and `DressPortal`) SIMULATES
  the warp particles, measures only the **ring core** (particles ≥ 40% of max size — ignores the thin
  sparks), grounds that ring's bottom to the portal origin (= terrain), and sizes the BoxCollider 1:1 to it.
  It only works in-editor (can't measure particles from a file edit).
