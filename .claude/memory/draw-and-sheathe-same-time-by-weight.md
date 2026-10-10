---
name: draw-and-sheathe-same-time-by-weight
description: "Taking a weapon out and putting it away always take the SAME time, from its weight only; a weapon that needs longer gets more weight — never a per-weapon time setting or an animation-length rule"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 605f945c-3281-4842-813b-03aa221e090f
  modified: 2026-10-10T13:48:35.572Z
---

A weapon's take-out and put-away last the same time, worked out from its weight alone (0.2 s + 0.006 s × weight).
When a weapon's animation needs a different time, change the number the formula reads — its weight (the Greatsword
weighs 133 for 0.998 s), or the formula's multiplier for everyone — never add a per-weapon duration setting and never
make the put-away follow its animation's length.

**Why:** 2026-10-10. The user said "change the sheathe duration formula if needed" for a greatsword flourish. I first
added a per-weapon put-away time to the stance settings (corrected: "what i mean was to change the formula, not create
a new field"), then made a put-away last the longer of the weight's time and its animation (corrected again: "the
sheathe and unsheathe should have the same duration. you can just scale the formula … adjust their weights, the
multiplier"). Two rounds lost on one sentence. My reading of "formula" was mine, not theirs.

**How to apply:** when asked to change a duration "formula", say back in one line which input I will change and that
both directions stay equal, before building. Also from the same day: put-aways play on the ARMS ONLY in game, so a big
flourish with a heavy weapon looks static ("only arms is moving while the great sword is too heavy") — keep put-aways
simple. Related: [[locked-attack-animations]].
