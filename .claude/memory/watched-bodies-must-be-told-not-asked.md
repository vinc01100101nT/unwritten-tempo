---
name: watched-bodies-must-be-told-not-asked
description: "A body the server places has a switched-off mover and a silenced brain, so anything its animation ASKS answers \"no\" forever — it must be TOLD what it is doing."
metadata: 
  node_type: memory
  type: project
  originSessionId: 7c69e1a3-4830-490f-a8d2-3f1fccafeb0a
  modified: 2026-09-02T14:16:48.948Z
---

Fixed 2026-09-02, play-tested and working. Symptom: shadow monsters slid around in a standing pose and never
swung, while damage numbers climbed over them; another player's body never swung at all.

**Root cause, two of them, same shape.** A creature arriving from the server has its mover disabled and its
brain silenced (`ThinksForItself = false`) so the server's positions are the only thing moving it. The shared
animation driver asked that mover "am I walking?" — a disabled component never runs its Update, so `Moving`
stays false forever. And the swing was read off the brain's `LastAttackTime`, which a silenced brain never
stamps.

**The rule this leaves behind:** a body the server PLACES must be **told** what it is doing, never asked, and
never measured. Measuring is not a fallback here — positions arrive 10×/s and `WatchedBody` closes 12/second of
the gap, so within each 100 ms window the apparent speed decays to ~30% and jumps back: a 3× swing ten times a
second, straight across the walk/run threshold, and a body that stops reads as walking for ~0.3 s.

**The shape that replaced it** (see `docs/2-systems/networking/action-alarms.md`): the server publishes a ground speed with
the position, and an action beat held as a value — which action, the moment it began on Mirror's shared clock,
and the beat it must finish inside. The clip plays at its own natural length capped by the beat, so the server
owns the cadence and the art owns the animation. A body that starts watching mid-action JOINS it at the right
frame (the user's explicit call — never skip, however little is left).

**Gotchas paid for:**
- `agent != null` is the wrong question; `agent.isActiveAndEnabled` is the right one.
- The beat must be a number the SERVER holds. Never a clip length — the map server is headless.
- The map server needs the RIG but not the model: the capsule, routing footprint, `UsesCharacterGears` and the
  declared body height all change what a creature IS (gears decide melee reach; body height decides where a
  projectile aims and therefore when it lands). Strip only the model.
- A person stops REPORTING when they stop walking, so a server-derived speed sticks at its last value — zero it
  after two report intervals of silence.

Related: [[mobs-get-everything-players-get]], [[npc-footsteps-frame-locked]].
