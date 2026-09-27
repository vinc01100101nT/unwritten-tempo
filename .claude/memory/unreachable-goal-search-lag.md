---
name: unreachable-goal-search-lag
description: Chasing a target the map gives no route to (cliff top → creature below) ran the full 6000-cell A* budget every 0.3 s — 52–57 ms stutters; fixed with once-per-load connected-area labels.
metadata:
  type: project
---

**Symptom (2026-09-27, test_grassland_cliffs):** standing on a cliff top and targeting a creature below, the chase stuttered ~3×/s.

**Measured cause:** `Pathfinder3D.FindPath` has no way to know a goal is unreachable, so it expanded every reachable cell up to `MaxExpansions` (6000) and returned "partial" — 52–57 ms per search in the editor; the player's chase re-searches every 0.3 s (`Player3DController.PursueAttackTarget`), creatures every `repathInterval` 0.35 s.

**Fix:** `Pathfinder3D.LabelAreas(bounds)` flood-fills a map's cells once (same ground/step/solid rules as the search, but thinnest body 0.01 and corner-cutting diagonals — strictly MORE permissive, so "different areas" is only said when certain); `Pathfinder3DConfig.Start` calls it per map (Start, not Awake — a server parks maps after load) and `OnDestroy` forgets it. `FindPath` checks `KnownApart` after the goal-cell adjust; if apart it walks the straight line and stops at the last point in its own area (`partial-other-area`, returns true — the old "partial" return shape, so no caller changed). Measured: 13.4 ms + 28.8 KB per 60×60 map at load; 28 ns per check; cliff-top→below search 0.1 ms, pass-to-pass unchanged 10.9 ms. `areaGrids` also cleared in `ResetElevationStatics` for no-domain-reload play.

Related: [[biome-table]] (cliffs), [[perf-runtime-hotpath-patterns]].
