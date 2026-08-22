---
name: dead-stat-channel-displayed-only
description: A stat can have TWO values where only one is wired to gameplay — the other moves the character sheet and nothing else; trace every reader before trusting a stat exists.
metadata: 
  node_type: memory
  type: project
  originSessionId: 07e15ed6-d553-4a48-b0f8-d0b4e9b21691
---

Cast speed had two separate numbers. The DEX-driven wind-up multiplier was the one that actually
shortened a cast. A second "cast speed" value — fed by DEX again, PLUS the ring's cast-speed node
and the cast-speed buff — was read by the stat panel, the level-up preview and a snapshot struct,
and by **nothing in gameplay**. So the ring node and the buff raised a number on screen and changed
nothing a player could feel. Collapsed 2026-08-23 into one value that keeps instant-at-150-DEX and
lets the ring and the buff divide what is left.

**Why:** the bug was invisible from either end. Reading the buff's code showed it correctly writing
a value; reading the wind-up showed it correctly reading a value. Only listing EVERY reader of the
second number exposed that the two sets never met.

**How to apply:** before believing a stat works, find every reader of it and check that at least one
is gameplay rather than UI. A stat whose only consumers are panels is decorative. Suspect this
whenever a stat has two names for the same idea — that pairing is the tell. See
[[name-knobs-concretely-one-job]].
