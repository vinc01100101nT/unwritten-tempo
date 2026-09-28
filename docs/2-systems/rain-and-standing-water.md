---
status: "BUILT 2026-09-27/28, partly play-tested: water sheets and rain-fed lakes with exact reflections; the rain-fed lakes and the downhill spill are not yet played."
owner: vince
last-updated: 2026-09-28
tags:
  - systems
---

# Rain and Standing Water

What rain leaves on the ground, how it is drawn, and why every player sees the same water. Everything here is drawn
on the player's machine; the game server only says what weather it holds and when it began.

## What a player sees

- **A thin sheet of puddles on level ground.** Every flat level of a map (a town's streets, each terrace of a field,
  a cliff top) holds a sheet of water 1 cm deep, patchy: a noise pattern picks where the puddles are, and they gather
  at the foot of any rising ground within 3 m. The sheet ends where the ground beside the level has risen out of it,
  so its edge follows the ground's own curve.
- **Lakes in the hollows.** Rain that falls on sloping ground runs downhill and collects in the hollows. Each hollow
  fills from its lowest point with the rain of all the slope that drains into it; when it is full it spills over its
  lowest rim, and that water runs on down the slope into the next hollow, and so on to the lowest ground, which has
  nowhere to drain (the map's edge holds water in) and floods. Two hollows that fill up to the ridge between them
  become one lake. It rains on every hollow at once, so they all fill together. A small hollow under a big slope fills
  fast and deep; a wide one under a little slope barely wets. Level ground sends no rain anywhere, so a town (level
  everywhere) gets puddles and never a lake.
- **The lakes rise with the rain and drain after it.** Heavy rain adds a step of water every 30 seconds, each step
  filling in over 3 seconds, up to a limit reached after 5 minutes; light rain adds less. When the rain stops, the
  water drains away over 1 minute. The puddle sheets spread and dry on their own timing (the Puddle weather channel).
- **Under a lake, things fade with depth.** Anything under a lake — the ground, a tree's foot, a character's legs —
  shows through less the deeper it is, as muddy rain water does: at 4 cm down it shows at about a third, at 16 cm it is
  gone. The dark outlines fade with it by the same amount, so an outline never shows through water the thing it
  outlines does not.
- **Reflections are exact.** Every water surface is a flat mirror at its own height: a tree on a cliff top meets its
  reflection in the cliff-top puddle, and the low ground's lakes show the cliff. Nothing below a surface is reflected.
  Seen from the game's usual steep view the reflection is as strong as before; as the view flattens toward the water
  it becomes a full mirror.
- **A Swamp always holds water:** puddles at full, and its hollows as if the first 30 seconds of heavy rain had just
  fallen, on every graphics setting.

## How it works

- **Reading the ground** (`WaterSurfaces`, once per map, the first time the ground is wet after a map loads): the
  heights of every exactly-level triangle of the map's ground become the sheets (levels within 1 cm share one sheet —
  a cliff top's grass lies 4 mm over the ground, a road's border strip 3 mm). The ground's top height on a 0.5 m grid
  becomes the hollows: filling the grid from the lowest ground up, every lowest point starts a hollow and every saddle
  joins two into a bigger one (`WaterPooling.BuildWatershed`). Each hollow knows how many sloping squares drain into
  it by the steepest way down, where it spills when full, and which hollow that spill runs into.
- **Filling** (`WaterPooling.Fill`, whenever the standing rain moves by 0.0002): every hollow takes rain × its sloping
  squares; what does not fit runs into the hollow its spill reaches, or rises with its neighbour once that is full too.
- **The rain, the same for everyone** (`WaterPooling.Rainfall`): worked out from the shared world clock and the weather
  history — the weather in force and when it began, and the weather before it. Calendar weather begins at the start
  of its in-game day. A game master's `/weather` is stamped by the server with when it began and what it replaced, and
  sent to every player (and to anyone arriving later) with the weather, so a late arrival sees the same water.
- **Drawing** (`Unwritten/WetGround` shader, `Shaders/WaterBodies.hlsl` shared with the ink outlines): each square of
  the map carries the body of water it belongs to; a pixel is water when its ground lies between that body's floor and
  surface. Reflections come from one mirror camera per distinct water height on screen (at most 8, full screen size),
  handed out after the camera moves and the water refills each frame. A body of water on screen with no mirror left is
  drawn dry rather than with a wrong reflection, and the log says so.

### The numbers

| What | Value |
| --- | --- |
| Sheet depth on level ground | 1 cm |
| Rain per 30 s step, heaviest rain | 0.004 (world units of water) — light rain its share |
| Most rain one wet spell leaves | 0.04 (ten steps, 5 minutes) |
| Drain time after the rain stops | 60 s |
| Clarity (things under water fade by e^(−depth ÷ this)) | 0.04 |
| Mirrors at once | 8 |

Measured on `test_grassland` (ground drawn at 0.25 m) at the most rain: 16 lakes covering 877 m² of the 3,600 m² map,
from a few centimetres to 43 cm deep (a small dip under a big slope); the Swamp field `khn_fild09`: one lake of
2,690 m², 16 cm deep; the town `hilt`: none.

### Ground shape that water needs

A sheet of water ends exactly where the drawn ground drops away, so a level's edge must be drawn round, not in the
steps of the ground's triangles. Shaped ground (a biome with levels or mounds) is therefore drawn with its points
0.25 m apart and its shape worked out on a 0.2 m grid, with the crease where a ramp meets a level rounded over about
half a metre ([Biomes](../3-world/biomes.md)). `test_grassland` has it; the 39 world fields and `test_grassland_cliffs`
were built before it and keep 0.8 m steps along their level edges until they are built again.

## Cost

- Reading a map's ground: about 80 ms, once, when the ground first gets wet after a map loads.
- Refilling the lakes: under 2 ms, a few times during each 3-second rise.
- Each mirror draws the whole world again, every frame. **Not yet measured in the game** — compare the frame time (F3)
  in clear weather and in a storm at the same spot.

## Watching it

The log says, per map: how many sheets and lakes it found, the rain standing, the deepest lake, and — when it happens —
how many bodies of water on screen got no mirror. The rules are pinned in `tools/game-math-tests/Checks.cs`
(`WaterRules`): a bowl holds exactly the rain that fell on it, a level map collects no lake, two bowls join when their
water meets, a hollow under a bigger slope stands deeper, a small hollow below an overflowing one fills from its spill,
and the rain's steps, limit, drain and Swamp hold.

## Not settled

- Water spots were seen to vanish and come back at some view angles (2026-09-28). One cause (mirrors handed out before
  the camera moved) is fixed; the log line above is there to catch the rest.
- Creatures drawn as flat pictures stand on top of the water rather than in it.
- The frame cost of the mirrors.
