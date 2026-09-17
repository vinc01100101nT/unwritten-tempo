---
name: shadow-body-frozen-after-revive
description: "A watched 3D creature that respawned or came back from the body pool stood static with no pose — its distance switch, looking for players in the Systems scene, switched its animator off. Fixed 2026-09-16 with SimulationLod.Release."
metadata:
  type: project
---

**Symptom (play-test 2026-09-16):** 3D (shadow) creatures animate on first spawn, but after dying and coming back they
stand static — no idle, walk, run, attack or skill pose. Billboard creatures unaffected.

**Root cause:** a watched body is moved to the always-loaded Systems scene (`MapManager.KeepAcrossMaps`). Its
`SimulationLod` asks `PlayerUnits.AnyoneWithin(pos, radius, gameObject.scene.name)` — "Systems" never matches the
player's map, so the answer is always "nobody near". `MobRespawn.Reactivate` (both the in-place revive on a watcher
and a body handed back from `NetMob`'s spare-body pool) ends with `lod.Resync()`, which un-suspends and forces that
answer, switching the frozen parts OFF: the mover, the body's Animator and the `MobShadow3DVisual` driver.
`NetMob.SilenceTheBrain` then only called `Suspend()`, which keeps the parts as they are — off. A creature's FIRST
body escaped because its model loads asynchronously and registers the parts after the switch is already suspended.

**Fix:** `SimulationLod.Release()` (suspend + every part ON + Simulating true), called first in
`NetMob.SilenceTheBrain`, before the mover is switched off again. Compiles; not play-tested.

**Gotcha — the class:** any code asking "who is near" by `gameObject.scene.name` on an object parked in Systems gets
nobody. The server's own creatures stand in their real map scene, so they are unaffected.

Related: [[creatures-cast-weapon-kit]].
