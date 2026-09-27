# Find a Visual Glitch by Casting a Ray Through Its Pixel

**Reach for this when:** a rendered picture shows a line, streak, hole or dark crease, you have already guessed at
its cause once, and your fix left the picture exactly the same.

## The rule

Stop guessing which surface the artifact is. Rebuild the exact camera that drew the picture, cast a ray through the
artifact's pixel (and a row of pixels across it), and list every surface each ray hits, in order, with its height and
facing. The artifact is whatever the ray reports — usually two surfaces fighting for the same spot, or a surface
facing the wrong way.

## Why

On 2026-09-27 a generated cliff showed thin black lines across its grass top. Two fixes went in on reasoning — one
removed a hidden ramp under the grass, the other smoothed where the grass ended — and the re-rendered picture was
**pixel-for-pixel the same** both times. Casting rays through the dark pixels answered it in one run: the rays hit two
grass surfaces within millimetres of each other at slightly different tilts. The grass tops from the two sides of a
narrow ridge overlapped and crossed there; the dark lines were where one dipped under the other. The fix (make both
lie exactly on the same height, with the same shading) took one change, and the next render was clean.

An hour earlier the same method would have caught a sibling: the first rounded cliff showed grass patches in the
middle of the rock face, and they were the underside of ledges wound the wrong way — invisible faces showing the
ground behind them. A ray lists that too (it passes through the backface and hits the ground).

## How to apply

1. Render your own picture with a camera you place in code (never the user's view), so you know its exact position,
   angle and field of view.
2. In the same setup, give that camera the picture's size and turn the artifact's pixel into a ray
   (`Camera.ScreenPointToRay`, with the vertical pixel measured from the BOTTOM).
3. Cast it with a "hit everything" query (`RaycastAll` / `PhysicsScene.Raycast` with an array), sort by distance, and
   print each hit's object name, point and normal. Temporarily add a collider to a drawn-only mesh if it has none.
4. Scan a row of pixels across the artifact, not just one: a jump in which object or triangle is hit is the seam.
5. Fix what the ray named, re-render from the same camera, and compare the same pixels.
