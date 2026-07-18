---
name: map-painter-diagonal-cuts
description: "Diagonal half-cell feature (📏 line tool) — cut = KEPT corner, schema/orientation conventions across painter, importer, and ground builder."
metadata: 
  node_type: memory
  type: project
  originSessionId: a757d62d-6795-442b-9ea0-dcd770218a18
---

Map painter's 📏 Line tool (2026-07-18) draws a 45°-snapped, one-tile-wide ground stroke: full cells on the staircase + a HALF cell flanking each step so both edges are straight 45° lines (RO-plaza look).

**Conventions (the non-derivable part):**
- A cell value is `brushId`/`level` (full) or `{ brush|level, cut, under }`. `cut` = which CORNER's half-triangle is **KEPT** (`nw/ne/se/sw`); `under` = tile/level left on the cut-away half (layered — old paint stays).
- map.json: optional `cut`+`under` on ground/heights entries; absent = full cell, old maps unchanged.
- **Orientation:** the cut is COMPASS-named (`nw`=north-west), so it is NOT flipped on import — same as `yaw`. The importer's z-reflection (`cz→rows-1-cz`) is an index conversion that keeps north at the top of the game's north-up view, so the compass corner is already correct in world space. (An earlier `FlipCutNS` that flipped n↔s SHEARED the band into a zigzag — deleted 2026-07-18.) World-side `DiagonalCuts.dir`: 1=nw 2=ne 3=se 4=sw with n=+Z, e=+X.
- `GroundBlendBuilder.EdgeIsKept(dir, edge)` (edge 0=W 1=E 2=S 3=N): kept corner's two adjacent edges belong to the kept half. Walls beside a cut cell must use the neighbour's PER-EDGE level (`CellEdgeLevel`/`WallB`), not `levels[]` — that's why the old static `WallBottom` was replaced.
- Cut cells: tops = two SOLID one-hot-coloured triangles (`ChannelCol`); `FillCornerNotches` and corner-stone passes SKIP them (painted diagonals are authoritative). Any cut forces the shaped (elevated) builder even on flat maps — the flat builder's ~0.8u grid would jag the diagonal.
- **Surface-based fill** (`fillRegion` returns a `Map k→part` of `whole`/`kept`/`cut`): a cut cell holds TWO tiles, so it joins the region for BOTH — its kept half AND its cut-away (`under`) half. Hover/flood on the prontera OUTSIDE a grass band therefore flows into the grass cells' cut-away halves → clean 45° flood edge, not a staircase. `surfaceAt(wx,wz)` picks the tile+part under the cursor (which triangle). `floodFillGround`/`bumpHeight(...,part)`/`eraseSurface(k,part)` all act on the targeted half; single-cell raise targets the half under the cursor.
- Elevation LABELS drawn once per 4-connected same-read-level group (centroid cell), not per cell — avoids a misleading `+1` on every half-raised diagonal edge (`heightRep`).

**Diagonal step trim (DONE 2026-07-18):** `GroundBlendBuilder.AddDiagonalCurb` gives the 45° step the checker curb face + flat border strip (the diagonal analogue of `AddWall(isCurb)`+`AddBorder`), UVs projected ALONG the edge (absolute) so stones stay square + tile seamlessly across colinear cut cells; `inward = raisedCorner − edgeMid` lays the strip on the raised half. 1-notch drop → curb; taller cliff → plain wall (matches straight edges). Wired in `EmitCutCell`'s step block.

The diagonal strip is lifted `BorderLift*3` (vs straight `BorderLift`) so it wins cleanly where it overlaps a straight strip at a corner instead of z-fighting. (A dedicated corner-stone where a straight border meets the diagonal was considered and deliberately declined — not wanted.) See [[map-painter-pipeline]], [[ground-blend-corner-smear]].
