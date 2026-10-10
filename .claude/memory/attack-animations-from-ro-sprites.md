---
name: attack-animations-from-ro-sprites
description: Character animations are cloned from Ragnarok Online sprite sheets onto our chibi rig — the standard since 2026-10-09; the method lives in the clone-ro-sprite-animation skill.
metadata:
  node_type: memory
  type: project
  originSessionId: b8ce1cd0-619e-456d-9766-3499e785620e
  modified: 2026-10-08T16:31:24.072Z
---

Since 2026-10-09 the user's standard for every character animation is: take the matching Ragnarok Online sprite
animation as the reference and clone it onto our 3D characters by posing them frame by frame. The full method and
both scripts are the `clone-ro-sprite-animation` project skill; CLAUDE.md has the standing rule.

**Why:** the hand-posed Katar attack (`AttackKatar.anim`) was judged "much better" than Kimodo text-to-motion, which
never landed a short, exact move, and only knows an adult body so needed refitting to our chibi rig (a big head hides
any straight-overhead arm — see the skill's limits section and [[measure-what-the-user-sees]]).

**How to apply:** for any new motion, invoke the skill first; offer Kimodo only for long, flowing full-body motion.
