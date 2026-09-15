---
name: fall-loop-after-server-correction
description: "A falling player was \"put back\" mid-air by the server's move check with the falling speed kept, so the fall never ended; fixed 2026-09-14 by zeroing fall speed on every teleport. Why the fall STARTED is still unknown."
metadata: 
  node_type: memory
  type: project
  originSessionId: 99f08ae2-01f3-49ab-a776-212cb043a3a2
  modified: 2026-09-14T11:47:20.392Z
---

**Symptom (play-test 2026-09-14):** after `/tp field_15` (no spawn name) Alpha fell to about -17,671 (tester's
numbers); the server logged "reported 4 impossible moves in a row — putting them back" 140 times.

**Proven loop (debugger, cited):** a report may move at most 40 units (`NetPlayer`); gravity 12, reports every 0.1 s,
so free fall first breaks the limit ~33 s in. "Back" is the last accepted position — already mid-air — and the
correction teleports the body (`Player3DController.Teleport`) without touching the falling speed `vy`, which only
resets when grounded and is never capped. So: fall, put back, fall faster, forever; the save wrote the server's height.

**Fix (uncommitted):** `Player3DController.Teleport` sets `vy = 0` — every reposition (server correction, server
teleport, instant blink) passes through it. Side effect: a blink mid-jump now ends the jump's rise at the blink point.

**Still unknown:** why the fall began — field_15's Start spawn is at (0,0,0) over a floor whose top is height 0, and
the arrival raycast should seat the body. The discriminating test: repeat the no-name `/tp field_15` and read height
and `vy` in the first second after arrival. The map's 2000 parking offset was ruled out (sideways only).
