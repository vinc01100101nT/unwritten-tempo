---
name: routes-hugged-cliff-faces
description: "Routes and auto-combat stalled at cliff edges — the route-finder judged ground height under the body's middle only, and a cliff face is ground, so the obstacle probe never saw it; fixed 2026-10-20 with a ledge-distance grid"
metadata:
  node_type: memory
  type: project
  originSessionId: 92ae9cc7-fc5c-4648-8ad1-efd9aae851a7
  modified: 2026-10-20T05:40:14.951Z
---

Symptom: at a cliff's edge, click-to-walk walked into the face and auto-combat stood still, then "gave up" after 6 s
(8 give-ups in 8 minutes on brm_fild01 in the Player log, 2026-10-20).

Root cause (measured in khn_fild08 at the user's spot -13.20, -12.98): the route-finder checked ground height only
along the body's centre line, and its obstacle probe ignores the ground layer — a cliff face IS ground (RULE
ONEGROUND), so nothing ever said "too close". Routes ran with ground rising 0.75 m within the body's 0.35 m
radius; the capsule hit the face. Separately, auto-combat picked the nearest creature by straight distance even when
it stood in another connected area.

Fix: at map load the area labelling also builds a per-cell distance from the nearest ledge cell (a neighbour no body
can step to); a cell counts as blocked for a body when (steps + ½) × cell size < its radius. After the fix the same
routes have at most 0.17 m of rise beside the body. Measured over all 92 maps: no connected area is cut in two,
98.8% of walkable cells stay usable, labelling 15–32 ms per map. Auto-combat skips creatures in another area unless
already in weapon reach.

**Why:** a route the body cannot follow looks like a working route from outside.
**How to apply:** any new walkability rule must hold for the body's WIDTH, not its middle; replay routes offline
with the side-rise measure before trusting one. Related: [[sandbox-world-no-linear-progression]].
