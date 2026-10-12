---
name: playtest-qol-batch-2026-10-11
description: What the 2026-10-11 play-test QoL batch built and the decisions behind it — map edge walls, standing portals, map-window doors/guide/counts, arrival name flash, hotbar keeps used-up items, server-side equip preview, fold/quench worn gear, far-prop LODs.
metadata:
  type: project
---

Built 2026-10-11 from the user's own play-test list (ranked by value to the player).

- **Map edges:** every map is closed in at load by four invisible boxes standing just outside its DRAWN ground (`MapEdgeWalls.Build`, called in `MapManager.Activate` after parking, every machine). Drawn ground, not collision: a flat map's floor box reaches 1 unit past the drawn ground. Layer Ignore Raycast. No scene stores them.
- **Doors:** the standing animated ring is the only door look; the flat disc was a fallback that fired because `RegionBuilder.PortalsDir` pointed at a moved folder. All 202 flat doors in 92 scenes were re-dressed with `RegionBuilder.DressPortal`. See [[portal-color-convention]].
- **Dungeon walls:** `DungeonRecipe` lost its PerimeterWall group; the 22 dungeons' `Props/Walls` (124 pieces each) were deleted. Lastline's hand-made `Wall` kept.
- **Map kind colours** now live in runtime `MapKinds` (town yellow, field green, dungeon purple, special red) — doors, zone-view door marks and the arrival flash (`ZoneTitle`) all read it. Map display names come from the layout into `WorldAtlas.Entry.name` (`NameOf`).
- **Map window zone view:** door marks + names (click → that map), click elsewhere → `MapGuide` spot that `QuestArrows` leads to ahead of the quest (next door via `WorldRoutes.NextMap`, on the player's machine — where to walk decides nothing), mob list shows "alive/kept" ("12/30") from the server (`MapCreatureCounts` + `WorldSpawns.KeptIn` — kept = the named rows' populations added up; a creature only a random mix places shows alive alone, user asked 2026-10-11), asked every 5 s while shown.
- **Hotbar:** a used-up consumable keeps its slot, drawn red with 0 (`Character.MayKeepOnBar` keeps any usable item). Pressing it does nothing.
- **Gear compare already existed** ("If equipped" in the bag tooltip) but was worked out on the player's machine; now the server sends the finished lines (`EquipPreview`, `NetPlayer.CmdAskEquipPreview`). I wrongly told the user it was missing — my search used "compare", the code says "preview".
- **Every station job works on worn gear** (user, 2026-10-11: "apply to every forging process"): Fold, Quench, Ink and Recycle pages list worn pieces "(equipped)"; server commands by worn slot; a worked piece is re-equipped in place so stats refresh; a recycled worn piece is taken off first (a weapon empties the bar, as unequipping does).
- **Far props:** `PropLodBaker` (40% / 25% triangles, texture seams let go, borders kept) on props ≥ 500 triangles EXCEPT houses (their levels looked broken in play, 2026-10-12; `KeptWhole` strips them on the next bake) + `LodGroupWirer` on the 17 hand-made families; last level never culls (user: never remove far props). 15% was judged too broken from rendered pictures. Generated meshes are 39 MB of text assets in `2_Generated/PropLODs`. Town frame time not yet re-measured (MT-59 on-going).
