# Handoff: wall TOWER facing is wrong (map-painter bake)

**Task/bug** — Baked wall **towers face the wrong direction**. Two specific complaints: (1) an END tower with only ONE wall attached, sitting BESIDE another tower, faces slightly angled instead of straight along its wall; (2) a CORNER tower (two walls) doesn't read as a clean 45°. No Tempo issue id. Code: `unity/Assets/Scripts/Editor/MapPainterImporter.cs` → `TowerYaw()` + `SeatWall()`.

**Status — NOT FIXED. All attempted fixes were REVERTED.** `TowerYaw` is back to its ORIGINAL vector-sum form. **Do NOT trust any prior "it works / fix confidence 90%" claims in the chat** — three fixes were shipped, each "verified" only by offline replay, and the user reported the in-scene result still wrong every time. The root cause is genuinely not yet understood.

## What's done (this session, kept — these are NOT the bug)
- `tools/map-painter/index.html` — eraser object-first, Fill/Line mutually exclusive, "Flatten all" button. (Working, confirmed.)
- `MapPainterImporter.cs` — wall no-squash **crop tiling** (`LayRun`+`SliceMeshMinSide`), **run→tower gap** fix (`startExtra`/`endExtra` reach tower centre), **adjacent-tower LINK** (pass 3). All confirmed working by the user. **Leave these alone** — the facing bug is independent of them.

## Uncommitted working-tree changes (nothing committed/pushed)
- `MapPainterImporter.cs` — `TowerYaw` REVERTED to original (vector sum over all 8 present neighbours, face away). The `AttachedWall` helper and the corner snap-to-45 were REMOVED. All the crop/link/gap work above remains.
- `.claude/memory/map-painter-wall-pipeline.md` — facing "fix" notes replaced with a "still broken" note pointing here.
- New generated dir `unity/Assets/2_Generated/Meshes/WallSlices/` (regenerable crop meshes) — unrelated to this bug.
- Plus pre-existing unstaged changes from before the session (tree thumbnails, town_0.unity, other .cs, etc.).

## Proven facts (measured, trustworthy)
- **The importer z-FLIPS the grid**: `wcz = floor((sizeZ - o.z)/cellSize)` — reconstruct cells from json with `cz = floor(sizeZ - o.z)` or every cell is mirrored in z. (Confirmed: baked tower world positions match the flipped grid, NOT the raw one. `wz=(cz+0.5)*cs - sizeZ/2`.)
- **The bake was NOT stale for the `AttachedWall` version**: parsing `town_0.unity` `PrefabInstance` `m_LocalRotation` (yaw = `2*atan2(y,w)`) for all 22 towers == the formula's output on the flipped grid, exactly. So the C# that produced town_0 == the `AttachedWall` code. (I did NOT re-measure after the snap-to-45 edit — unknown whether the user recompiled+rebaked that one before reporting "still wrong".)
- town_0.unity (22 towers, 28 straights) was baked from **prontera.json** (49 wall cells, octagonal wall → has straight+diagonal corners). map.json is a different unrelated diagonal test line. The user's screenshots are ZOOMED crops of the bigger baked map.
- Under the original (now-restored) formula, prontera corners come out: ortho+ortho → ±45/±135 (looks right); straight-meets-diagonal bends → 67.5°/112.5°/157.5° (looks off).

## Dead ends (tried, user said STILL WRONG — all reverted)
1. **Exclude adjacent towers** from the facing sum (`WallKind(nb)!="tower"`). Still wrong — also mis-counted a parallel run's diagonally-touching cell as a wall.
2. **`AttachedWall` rule** (a wall counts only if the orthogonal neighbour's run axis points at the tower, or a matching diagonal run). Offline replay looked perfect (1-wall→aligned, corners→45-class). User: still wrong.
3. **Snap 2-wall corners to nearest diagonal** (`Mathf.Round((yaw-45)/90)*90+45`). Offline replay: all 9 prontera corners → clean ±45/±135. User: still wrong. **Not independently re-measured against a fresh bake.**

## Hypotheses (NOT yet tested — start here)
- **H1 (most likely): a z-flip-vs-yaw convention mismatch.** `TowerYaw` builds its direction sum in the FLIPPED grid but the yaw sign convention may not match how world z is oriented after the flip → towers face MIRRORED in z. This would be invisible to the offline replay (which just reproduces the same formula) and would explain why numerically-"correct" formulas still look wrong in the scene.
- **H2: tower yaw vs straight-wall yaw use DIFFERENT conventions.** `SeatStraight`/`SeatWallSegment` yaw = `runAxisIsX ? atan2(-dcz,dcx) : atan2(dcx,dcz)`; `TowerYaw` returns `atan2(-sx,-sz)`. If these two frames disagree, a tower won't line up with the very wall feeding it even when each looks "right" alone.
- **H3: the tower PREFAB's own front axis isn't local +Z.** `WallTowerFacingOffset` is 0. If Wall_tower1/2's roof gable faces, say, +X, every tower needs a constant offset. (Alone this can't explain DIFFERENT wrongness for 1-wall vs corner, but could compound with H1/H2.)

## Next single step (THE discriminating test)
Bake ONE minimal known config and read BOTH yaws from the scene: a 3-cell straight horizontal wall `tower–straight–tower` (e.g. paint cells in a row), plus a 3-cell vertical one. Then:
1. Parse the baked scene for the straight `part2` piece yaw AND each end `tower` yaw.
2. Check by hand: does the tower end-cap's front point OUTWARD along the SAME world axis the straight piece runs? Compare horizontal-run vs vertical-run towers — if only ONE axis is wrong, it's a z-sign bug (H1); if the tower disagrees with its own wall on BOTH, it's H2; if uniformly rotated, it's H3.
This isolates convention (H1/H2) vs prefab-front (H3) in one bake, which the offline replay fundamentally cannot do (it never models the prefab's front or the world-z sign).

## How to resume
Read this file: `handoffs/tower-facing.md`. The facing code is at `MapPainterImporter.cs` `TowerYaw()` (~line 731) and the wall/tower yaw at `SeatStraight`-equivalent `SeatWallSegment()` + `SeatWall()`. Reproduce with prontera.json (default map the importer bakes) or paint the minimal 3-cell test. Get GROUND TRUTH from the baked scene BEFORE editing — and this time verify a FRESH bake, not the offline replay.
