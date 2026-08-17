---
name: existing-function-name-is-not-the-behaviour
description: "A request names a FEEL/RESULT — finding a function whose name matches (SmoothDamp = \"ease-out\") is not proof the result exists; trace what it produces on screen before saying \"already implemented\"."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 04f0333d-1d81-4aa9-8ab5-9eeb56440fe1
---

When the user asks for a **result** — "delayed camera follow", "ease-out", "snappier", "it should fade" — the fastest wrong move is to grep the code, find a call whose NAME means the same word, and conclude the behaviour is already there and only needs a switch/knob. A function name is a label, not a measurement. **Trace the whole path to what actually reaches the screen before claiming the result exists.**

**Why:** the user asked for a result they can SEE; I answered about a mechanism I READ. Those are different claims, and only one of them was checked. When it turns out wrong, the next failure follows automatically: I assume the mechanism is still right and only its number is off, so I hand the user a menu of values to pick — converting my unverified assumption into their decision, and burning a round per guess.

**Scar (2026-08-17, camera follow):** the ask was "smoothing = camera follow has ease-out". `CameraFollow3D` already called `Vector3.SmoothDamp`, so I shipped an on/off checkbox around it and called it done. But the lag was on the camera's POSITION while `transform.LookAt` kept aiming at the player's true position — so a lagging camera has to TURN to keep the player framed. The user saw a swivel, not a delay: *"what you are doing is camera rotating. why cant you follow a simple instruction"*. Two more lines of reading (position → `LookAt`) would have caught it in pass one. The real fix was to lag the FOLLOWED POINT and build both the camera position and its look-at from that one lagged point, so the view direction is constant by construction.

**How to apply:**
- Before "that already exists", answer: *which lines turn this call into what appears on screen?* Read them. For a transform, that means position AND rotation AND anything downstream that overrides either.
- State it as what I verified, not what I inferred: "position is eased at `X:122`, but rotation is recomputed from the true target at `X:124`" — not "it already eases".
- When the user says the feel is wrong, the FIRST suspect is the mechanism, not its magnitude. Don't offer a value menu until the motion itself is confirmed correct — a tuning question built on a broken mechanism pushes my error onto the user.
- Geometry rule worth keeping: to delay a follow without any rotation, lag the TARGET POINT, never the camera's own position.

Related: [[debug-symptom-composition-not-interpretation]] (a guess smuggled in as an observation), [[changes-must-persist-in-repo]].
