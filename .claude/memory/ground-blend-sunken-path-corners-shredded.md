---
name: ground-blend-sunken-path-corners-shredded
description: FillCornerNotches raised sunken (-1) path lane turns to base level → zigzag path rendered as broken diagonal; skip cells level<0.
metadata: 
  node_type: memory
  type: project
  originSessionId: 58249bf4-e2d5-49ad-8339-0504995aac02
---

**Symptom:** A path painted in Map Painter as a zigzag/staircase of sunken cells (a road/gutter lane the user sank to level −1) rendered in Unity as a broken DIAGONAL smear instead of the crisp orthogonal steps that were painted. Straight lane runs looked fine; every TURN/joint lost its cell.

**Root cause:** `GroundBlendBuilder.FillCornerNotches` (elevated ground path) raised the lane's turn cells. It was only ever meant to close background GRASS gaps (level 0) sitting in the concave crook of a RAISED feature — but it ran unconditionally on ALL cells. A sunken lane cell at a diagonal turn sits in the crook of two higher (base-level 0) neighbours, so the filler mistook each turn for a gap and lifted it to level 0, then the elevated builder colours a raised cell SOLID from that region (`RaisedRegionCol`) → the mosaic path tile was replaced by the surrounding sidewalk at every corner.

**Ground truth that cracked it** (decoded `tools/map-painter/prontera.json`, replayed FillCornerNotches offline): 16 of 89 painted −1 path cells were being raised away — exactly the diagonal-corner cells. Confirmed the fix (skip level<0) loses 0 path cells while still doing the 4 legit level-0 gap fills.

**Fix:** `GroundBlendBuilder.cs` FillCornerNotches — `if (L < 0) continue;` before computing a fill. Never auto-raise a cell the user deliberately sank; sunken lanes keep crisp corners as painted. Level-0 grass-gap fills between raised arms are unaffected.

**Coupled follow-on (same session):** preserving the sunken turns re-exposed the **missing concave-corner stone** at lane turns. The INWARD corner-stone loop guarded `if (L <= 0) continue;` — only RAISED features got the bridging square stone. A sunken lane's concave corner lives on the BASE plane (L=0) with the lane cell (L−1 = −1) as the diagonal, so it was skipped. It never showed before because the old fill lifted those corners to L=0 (no concave case). Fix: guard `if (L < 0) continue;` — base-plane L=0 now bridges too. Replay on prontera: old guard = 0 stones, fix = 16 (exactly the turns).

**Gotchas / model notes:**
- Channel map is by palette row order: prontera_tile=0 (BASE `_GrassMap`, the mosaic under everything), ground_sidewalk=1 (.r overlay), grass=2, road=3, soil=4. The "path" in prontera is the −1 sunken lane showing the base mosaic, NOT an overlay channel.
- This is a SEPARATE bug from [[ground-blend-corner-smear]] (colour off filled vs painted level) and [[ground-blend-small-cellsize-washed-paths]] (sub floor) — same file, different mechanism. Bilinear/`sub` were NOT the cause here; the geometry-level fill was.
- Re-import to see it: **Tools ▸ unwritten ▸ World ▸ Import Painted Map…**
