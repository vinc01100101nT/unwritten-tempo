---
name: minimap-is-a-baked-picture
description: The HUD minimap shows each map's baked picture from one central list — no live camera exists any more (user reversed the 2026-09-07 "don't bake" ruling on 2026-09-26).
metadata:
  type: project
---

**Decided 2026-09-26 by the user:** use the static pictures instead of the live camera, as a central, general fix
and not a per-scene one. Delete the live camera and every stale piece of it. The 2026-09-07 ruling ("don't bake,
keep the live camera at 20 per second") is REVERSED.

- **Data:** `Resources/Minimaps/<map>.png` (one per map, 256–1024 px at 6.4 px per unit, compressed, not readable) +
  `Resources/MinimapCatalog.asset` (map → the square the picture covers, in map-local coordinates). No scene holds
  anything; the old per-scene `MinimapData` component was deleted.
- **Game:** `Minimap` swaps the picture when `MapManager.MapVersion` changes (one integer compare a frame), unloads the
  old one, and slides a UV window using `MapManager.InMap`. A map with no picture shows no backdrop and warns once.
- **Editor:** `MinimapBaker` draws from `EditorSceneManager.OpenPreviewScene`, so the open scene is never touched. It
  covers every enabled build scene but Systems. One map is re-drawn when its scene is saved (the user agreed the
  save may write that picture). The menu Bake Minimaps (all maps) draws all of them. The wiki's map preview is a JPEG
  of the same picture.
- The minimap is fixed daytime lighting now. It no longer darkens at night.

**Why:** the live camera re-rendered the world 20 times a second on every client.
**How to apply:** never re-add a camera or a per-scene component for the minimap. A new map gets its picture by being
saved, or by the bake-all menu. See [[wiki-database-generated-pages]].
