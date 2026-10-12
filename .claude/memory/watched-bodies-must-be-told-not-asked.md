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
- The map server needs the RIG but not the model: the capsule, routing footprint and the declared body height all
  change what a creature IS (body height decides where a projectile aims and therefore when it lands). Strip only the model.
- A person stops REPORTING when they stop walking, so a server-derived speed sticks at its last value — zero it
  after two report intervals of silence.

Related: [[mobs-get-everything-players-get]], [[npc-footsteps-frame-locked]].

## Two more facts that had to be TOLD (2026-09-07) — both found by "a jump does nothing for watchers"

**HOW FAR OFF THE FLOOR.** `Agent3DVisual` seats a body by pinning its model onto the ground raycast every frame.
Right for everything that walks and wrong for the one role that can leave the floor: a jumping player's watched
copy DID rise at the root (the height crosses the wire, and the movement check is measured flat on purpose so a
jump is never refused) and then had its model slammed back down. **It cannot be derived at the far end** — a
watched copy has no CharacterController, so it cannot tell real lift from the height the capsule sits at while
standing, and using the raw reported Y would leave everybody hovering by that offset. `Character3DVisual` already
separates the two (`HeightAboveGround`); that is what travels.

**THE LEG CADENCE — no longer told (2026-10-12).** It used to ride the position report, because a movement-paced Jump
played at 1.5× on the owner and 3.0× on every watcher. Today no stance follows a movement pace: the Jump is Baked, and
walk/run/sprint legs are locked to ground speed, which a watched body already has (`TellTravelSpeed`). So the told
cadence was deleted — see [[gait-speed-sync]].

The cadence rides the POSITION report, and so does the height WHEN GRAVITY IS DOING THE WORK (a fall off a ledge) —
the report gate had to learn that rising counts as moving, or a jump straight up sent nothing at all.

**But a scripted JUMP does not, and putting it there first was the mistake.** A hop's lift is a symmetric arc
decided entirely by when it began, how tall and how long — so it is an ALARM, not continuous state, and it is said
once at the launch and once at the touchdown. Replayed offline against the authored numbers (0.8 units, gravity 12
→ a 730 ms arc), the streamed version reached only 95% of its height, peaked **135 ms late**, hung visibly in the
air **253 ms after the jumper had landed**, and drew the whole rise from **four** reported values. Drawing the
curve from the shared clock instead: full height, **2 ms** late on a local network and 35 ms at 30 ms ping, nothing
left hanging, and 44 frames of curve. None of it was fixable by easing faster — the easing is what makes ordinary
walking smooth. The SIDEWAYS half stays on the position report, because it is steered live by the keys and is not
scripted at all.

The test, and it is the one that was missed: **if the shape is known to both machines, send the SHAPE, not samples
of it.** Only send samples for something neither side can predict.
