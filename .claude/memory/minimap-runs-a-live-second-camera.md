---
name: minimap-runs-a-live-second-camera
description: The HUD minimap renders the world a second time on every map, capped at 20 per second; the user ruled out baking charts, so that second camera is permanent.
metadata: 
  node_type: memory
  type: project
  originSessionId: 0464e031-7687-4db6-b520-6569f7a756d2
  modified: 2026-09-07T15:14:43.577Z
---

Measured 2026-09-07: **none of the 42 map scenes carry a baked minimap chart** (searched every scene for the
component that holds one — zero hits). The HUD minimap has two backdrop modes, a baked image that costs nothing
and a live orthographic camera as the fallback, and with nothing baked it takes the fallback **on every map,
always**. So each client renders the world an extra time, into a 256×256 picture, alongside the main view.

Its re-render rate was capped at **20 per second** on 2026-09-07 (both in the code default and in the saved HUD
prefab, which is the copy that actually runs). Before that it followed the game's frame cap, so it was
re-rendering the world up to 120 times a second per client. **That cap is the settled answer.**

**Why:** the other route is baking a chart for each map, which removes the second camera instead of throttling
it — a baker tool exists and nobody has run it. **The user ruled that out on 2026-09-07: do not bake, and do not
offer it again.** So the live camera is permanent, and its cost is a standing per-client fact rather than a
problem waiting to be solved.

**How to apply:** when a performance question touches per-client render cost, count this as a second full world
render per client that is here to stay. Do not propose baking.
