---
name: copy-the-existing-mechanism
description: "A new progression track (mastery, a new XP bar) copies how the game already earns and shows XP — never a bespoke formula or special case"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4cd5f5ca-a731-47c4-8bf9-65063f15803b
  modified: 2026-09-21T10:10:14.418Z
---

When a new track needs to be earned or shown (weapon mastery, a new level bar), copy the mechanism the game already
has — experience from kills through `Character.AddXP`, the ring's curve, the same "into / needed" display — instead
of designing a bespoke earning rule.

**Why:** Weapon mastery was built on a separate "share of the creature's health × challenge scale" practice system.
It had a bug (the killing blow was recorded after the kill paid out, so one-shot kills taught nothing) and the user
could not see progress. 2026-09-21 the user: "can you just simply copy how exp is gained, why are you over
complicating things … why are you doing special cases … always choose the simple ways". It was replaced by paying
each kill's experience to the held weapon's mastery on the ring's curve.

**How to apply:** before proposing any earning rule, cost curve or progress display, find the nearest existing one
and reuse it verbatim; only propose something new if the user asks for a difference. Related: [[the-five-anti-invention]].
