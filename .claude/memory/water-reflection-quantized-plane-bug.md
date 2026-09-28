---
name: water-reflection-quantized-plane-bug
description: Rain water system — the doc is docs/2-systems/rain-and-standing-water.md; this note keeps the gotchas that cost rounds (array size lock, DontSave search, never tie water to a player body, mirror hand-out order, 0.25 m ground)
metadata:
  type: project
---

# Rain water: the gotchas behind the system

The design and numbers live in `docs/2-systems/rain-and-standing-water.md` (built 2026-09-27/28). What that doc does
not say, and each cost a round:

- **Never tie water to a player body.** Lakes were briefly capped at "the player's chest" read off the worn rig; the
  user rejected it (bodies differ, every player must see the same water). Depth comes only from ground + shared
  rainfall. The chest read also broke on its own: a body worn in Play is `HideFlags.DontSaveInEditor` and
  `FindAnyObjectByType` skips it — never find a player body by scene search (use `StanceController.Player`).
- **Unity shader array size locks at first set** — `_WaterHeights` first set 16 long cut later 64-long writes; renamed
  `_WaterLevels`. See docs/5-ai-learnings/a-shader-array-keeps-its-first-size.md.
- **An earlier fill rule never closed a lake beside dry land** (level = infinity → whole 0.5 m squares flooded). Replaced
  by the watershed fill; a replay that had printed LEVELS, not counts, would have caught it.
- **Mirrors are handed out in `PlanarReflection.LateUpdate` at execution order 1000**, after the camera moves and the
  lakes refill — handed out in Update, every refill during draining blinked the water dry for a frame.
- **Reflection clip lies exactly on the water surface** (user: nothing beneath water reflects); an older sign bug put
  it 3 cm above and cut every reflected base; a 3 cm push below was removed.
- **A sheet ends where the drawn ground drops away**, so ground drawn at 0.8 m made level edges step 0.8 m however
  round the shape underneath; shaped ground is now drawn at 0.25 m (`GroundBumps.DrawnSpacing`). Only test_grassland
  was rebuilt that way (2026-09-28); the fields and test_grassland_cliffs wait for a rebuild.
- **The weather message changed** (`WeatherHeld` gained since / before / beforeSince): the game server build must match
  the client, or the rain history is wrong for GM weather.
- **Never round a height used as a mirror plane** (older bug: a quantized grouping key used as the plane).
- Unmeasured as of 2026-09-28: the frame cost of the mirrors; "water spots vanish at some angles" still open.
