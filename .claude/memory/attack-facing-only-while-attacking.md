---
name: attack-facing-only-while-attacking
description: "The player faces their target ONLY while an attack is under way (a swing, a skill's wind-up + release); walking turns them at once otherwise."
metadata:
  node_type: memory
  type: project
  originSessionId: a1ea220d-c29a-4901-8573-809691c5eaf3
  modified: 2026-10-21T07:34:22.696Z
---

Decided 2026-10-21 by the user. While a swing or a targeted skill is under way, the body faces its target and walking
only moves the legs (a shot released while backing off still goes at the target). The moment the attack is over,
movement owns the facing again. `Player3DController.FocusOn(unit, seconds)`: an attack order refreshes it every frame
for 0.2s; a targeted skill holds it for castTime + one swing (the release window).

**Why:** my first version kept facing the target for 3s after any attack; the user found the body staring backwards
while walking away: "it only face the target when attacking".

**How to apply:** never hold a combat facing past the attack's own window; there is no "in combat" facing state.
