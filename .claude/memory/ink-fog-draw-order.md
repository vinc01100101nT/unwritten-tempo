---
name: ink-fog-draw-order
description: "Hand-drawn INK outlines not fogging in fog → fix = FogScreen Queue Overlay+100 so fog draws OVER ink"
metadata:
  type: project
---

**Symptom:** In foggy weather the hand-drawn INK outlines + paper grain (the `Unwritten/InkScreen`
screen-space filter) stayed crisp/black while the rest of the world dissolved into haze — the ink "floated"
un-fogged over the fogged fills.

**Root cause = DRAW ORDER, not missing fog math.** `InkScreen` and `FogScreen` were BOTH `Queue "Overlay"`
(4000). Same queue + both quads parked at the camera (tie distance) → order undefined, and the ink ended up
painting OVER the fog veil, so the fog never touched it.

**Fix (one line):** `FogScreen.shader` SubShader tag `Queue "Overlay"` → **`"Overlay+100"`** (4100) so the fog
veil draws AFTER the ink quad. The same depth-based haze that washes out the world then blends over the ink
lines/paper by their true distance — near ink untouched, far ink dissolves into the mist — automatically
matched, no per-line fog code. Side effect (accepted): fog now also layers over rain/snow (also `Overlay`).

**Dead-ends (cost ~4 rounds — don't repeat):**
- Adding a hand-tuned fog fade INSIDE `InkScreen` (publish `FogOverlay.CurrentFog`/start/end → fade `lineA` by
  `1 - fog*fdist`) does NOT work: a separate linear ramp can't match the fog fill's real opacity (`floor` 0.8 +
  `coverage` + billow noise), so the lines stay too visible. It's also a duplicate mechanism (two things doing
  the fog). Reordering makes the ONE real fog apply to the ink for free.
- **NEVER disable the ink's `Stencil { Ref 32 ... Comp NotEqual }` ground-skip to chase a side-bug** — that
  stencil bit-32 test IS the FLOOR's immunity to the ink (see [[ink-ground-black-band-stencil]]). Commenting it
  out inks the entire ground = immediate visible regression.

**Discriminating test to find which layer an outline lives on:** DevSettings (P) → Hand-Drawn → intensity 0.
If the outlines VANISH they're the `InkScreen` screen filter; if they STAY they're baked into the prop art
textures. (Confirmed live: they vanish → it's InkScreen.)

**Also fixed by the SAME change:** the ink quad (paper + lines) "flashing to clear" during a right-drag camera
orbit is GONE too — confirmed by the user. Root cause was the same tie: InkScreen and FogScreen shared the
`Overlay` (4000) slot at identical camera distance, so their sort order was undefined and thrashed during
motion, intermittently dropping the ink. Splitting fog to `Overlay+100` removed the ambiguous tie → stable.
(So an ambiguous same-queue + same-distance sort between two camera-locked overlays is a flicker trap — give
each its own queue.)
