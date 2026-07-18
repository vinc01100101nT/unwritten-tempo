# Handoff — elevated-path inside-corner border/grass gap

**Task/bug:** On elevated ground (raised paths/plazas from the map painter, `GroundBlendBuilder.LayPlotSampledElevated`), the checkered **border ring breaks at an inside corner** where two path regions / two border lines meet — leaving a bare cell (grass shows through). No Tempo issue id. This is the last open piece of the curb+border feature.

**Status: NOT FIXED — code REVERTED to the pre-attempt baseline.** Every fix attempt so far has been backed out. `GroundBlendBuilder.cs` is back to: curb + flat border ring (works, user-confirmed) + the prior-session `FillCornerNotches` whole-cell pass (present but a known dead-end, see below). No corner-gap fix is currently active. Don't trust any earlier "it works"/confidence line — the gap is still there.

**User's stated spec (take literally):** "detect when two borders are meeting at one point, then extend a border 1 cell to fill that gap." The user does NOT want a diagonal miter, a diagonal border, or any diagonal curb. They want the existing **checker border line continued by one cell** through the inside corner so it doesn't break. NO new decoration, NO black curb, NO grass wedge.

---

## What's DONE and working (don't touch)

- **Curb + flat border ring** on 1-notch raised edges — `LayPlotSampledElevated` + `AddWall` + `AddBorder` + `MakeCurbMaterial`. User-confirmed good.
- **Curb/border textures** + a `GroundTiles/` Default/Repeat import carve-out (`PixelArtImportPostprocessor.cs`). Memory: `ground-tiles-need-repeat-import`.
- `SampleCol`**/**`AddWall`**/the** `GroundSamplerLeveled` **delegate now take a** `refLevel` (added on disk between sessions, NOT mine) — sampling the blend field with `refLevel = someLevel` masks out cells at other levels, so it returns that level's (e.g. path) weights. Useful hook for any future fill that must read as path.

## Key measured facts (ground truth, not reasoning)

- Baked test mesh `unity/Assets/2_Generated/Meshes/Ground_SampleElevation_ground.asset` (120×64, 4u cells, LevelHeight 0.2). Its shape: raised **strips (level 1)** separated by 4-wide level-0 grass channels, with **45° diagonal staircase edges** on the strips.
- **Top surface has NO voids** (decoded a full top-down coverage map at 1u and 0.5u — every interior cell has a flat top). So the in-game black line the user saw was NOT a missing-ground hole — it was my own added diagonal curb geometry (now removed).
- **Border strip is THIN:** `ground_curb.png` = 2172×359 (aspect ≈ 6.05). `borderWidth = BorderRows(1) * CurbWorldPerTexture(2.0) / 6.05 ≈ 0.33u`, checker squares ≈ 0.33u. Cells are 4u. So at an inside corner the two \~0.33u border strips run on the two raised neighbour cells and the \~4u low corner cell between them is left bare → that's the visible gap.

## Dead-end ledger (do NOT retry as-is)

1. `FillDiagonalElbows` (prior session) — filled a cell only if BOTH crook cells were lower; the real crook often holds a level-2 tile, so it skipped exactly the real joints. Removed.
2. `FillCornerNotches` (prior session, CURRENTLY IN THE CODE) — whole-cell raise a low cell to `min(perpendicular higher arms)`. Proven by decoding the baked mesh: it only **RELOCATES** the notch one ring outward (13 notches remained after the pass); iterating it would cascade-fill whole regions. It's a structural fact that whole-cell raising a diagonal staircase edge can't close it. Left in place only because it's the pre-attempt baseline; **recommend removing it** once a real fix lands.
3. **Sub-cell corner BEVEL / miter** (this session, REVERTED) — `AddCornerBevel`: for a low cell at a concave corner, raise only the half-cell 45° triangle hugging the corner to the arm level, path-coloured via `refLevel`, with a curb + border on the exposed diagonal. Offline coverage prototype proved it closed 100% of concave-corner grass (14→0 real, 16→0 synthetic) WITHOUT changing cell levels (so no cascade). **BUT in-engine it looked wrong:** it drew an unwanted **diagonal checker border** and a **black diagonal curb** down the staircase edge. User rejected it hard. Removed entirely.
4. **Whole-cell BORDER TILE** (this session, REVERTED) — `AddBorderTile`: detect a low cell with two perpendicular higher neighbours, lay ONE flat border-material quad over the whole 4u cell at the arms' height, tiled to the checker square size. Simpler, no diagonal/curb. Not eyeballed before the user asked to revert. Potential issue: on an OPEN 45° edge (region vs grass, not region vs region) it would still tile a border cell jutting into grass. Removed.

## What's LEFT / next single step

Implement the user's literal spec cleanly: **continue the thin border LINE by one cell through the inside corner**, not fill a whole 4u cell, not raise terrain, not draw anything diagonal.

- Likely the right shape: at an inside corner, emit one more `AddBorder`-style **strip segment** (width `borderWidth`, length \~1 cell) that bridges the two perpendicular strips' endpoints so the checker line turns the corner continuously. The bare interior of the low cell behind the line can stay path/grass as designed — the user only wants the LINE unbroken.
- **Get clean ground truth first:** the current baked mesh still has the prior `FillCornerNotches` applied, so decode is muddied. Before coding, have the user rebake (or reason from the raw painted `levels`) to see exactly where the border line breaks, then reproduce that break offline and confirm the one-segment bridge closes it with no over-extension into open-grass edges.
- Open question to confirm with the user before building: at the corner should the bridging segment run **straight across the corner mouth** (diagonal of the corner) or as an **L (two short axis-aligned segments)**? Their words "extend a border 1 cell" suggest one short segment; confirm the direction.

## Uncommitted working-tree changes

- `M GroundBlendBuilder.cs` — REVERTED to baseline this session (all my adds removed, `FillCornerNotches` restored). The remaining diff vs HEAD is the prior-session curb/border feature + the `refLevel` param, both pre-existing and working.
- `M PixelArtImportPostprocessor.cs` — GroundTiles Default/Repeat carve-out (prior session).
- `?? unity/Assets/1_Authored/Art/GroundTiles/` — border PNGs + meta; `ground_curb.mat` generated into `2_Generated/Materials`.
- Debug scaffolding from this session (bevel/coverage prototype scripts, throwaway decoders) has been DELETED from scratch — nothing left in the repo.

## How to resume (paste into a fresh session)

> Read `handoffs/elevated-path-corner-notch-gaps.md`. The elevated-path inside-corner border gap is NOT fixed; code is at baseline. User's spec: extend the thin checker border LINE by one cell to bridge where two border strips meet at an inside corner — NO diagonal, NO curb, NO whole-cell fill, NO terrain raise (all four have been tried and rejected — see dead-end ledger). Get clean ground truth (rebake, or read raw painted `levels`) BEFORE coding, then bridge the corner with one short `AddBorder`-style strip segment.