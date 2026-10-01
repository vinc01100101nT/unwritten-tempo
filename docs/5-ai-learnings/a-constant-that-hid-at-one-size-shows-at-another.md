---
status:
owner: vince
last-updated: 2026-09-30
tags:
  - ai-learnings
---

# 📏 A Constant That Hid at One Size Shows at Another

**Reach for this when:** you reuse a body, a sprite, a collider or any rig at a new SIZE, and something that was fine
before — feet in the ground, a gap, a clipped edge — appears only on the new ones.

## The rule

Before reusing a rig at a new size, list every offset it carries and say which UNIT each is in — a fraction of the
art (grows with the scale) or world units (does not). Where one of each meet, the smaller used to hide the larger;
at a new size the order can flip. Measure both on a live body at the new size before calling it done.

## Why — what it cost

2026-09-30: saved characters reused a mob's body and slicing at about 3.6 times a small mob's scale. The feet looked
sunk into the ground — a round spent on a bug report. Measured live: every body stands 0.020 units above the ground
(world units), and the mobs' sprite pivot sits 1.6 pixels up the frame (art units). On a small mob the pivot's sink is
0.008–0.012 units, inside the lift, so its feet showed; on a character it is 0.035 units, so 0.015 units of feet went
under the ground. Mobs were never right either — the lift was hiding it.

## How to apply

- Probe a live body: its position, the ground under it (a downward ray that skips the body's own collider) and the
  bottom of its drawn picture. `lift = pos − ground`, `sink = pos − picture bottom`; sink > lift means buried.
- Fix with the art's own measurement, not a new constant: the feet line was measured per sheet (the lowest painted
  row of its walk frames) and stored beside the other measured art numbers.
