---
name: one-screens-answer-breaks-on-a-server
description: "Any \"the player\" singleton or scene search is a fact about one screen; on a server it silently answers nothing, and the bug is always silent."
metadata: 
  node_type: memory
  type: project
  originSessionId: 655b9438-53c3-49a7-b55a-b2d9183754d1
  modified: 2026-08-29T14:32:17.436Z
---

Every question of the form "where is the player / who is the player / the player's status host" was written when
one person sat at one keyboard. On the game server that answer is **null**, and nothing complains — the code just
quietly does nothing forever. Found three separate instances of this exact shape on 2026-08-29:

- The server refused every swing because it had no character per connection (`Character.Instance` is deliberately
  null there). Fixed by building one `Character` per connection under its `NetPlayer` shell — see `PlayerFighter`.
- A creature latched onto one avatar at `Start` via `FindAnyObjectByType<Player3DController>()` and held it for
  life; a server has no avatar, so every creature it spawned never chased anybody.
- Both spawn-spot pickers kept clear of "the player" the same way, so on a server the clear-radius measured the
  distance from a spawner to itself — zero — and creatures could appear on top of anyone.

**Why:** the failure is always silent. A null "the player" produces no exception and no log line; it produces a
world where nothing happens, which reads as a design problem rather than a bug and costs whole sessions.

**How to apply:** the roll of who is actually here is `PlayerUnits` — `NearestTo` and `AnyoneWithin`. Before
writing or trusting anything that resolves "the player", ask whether the answer has to work when there are
**several** or **none**. If it does, it goes through that registry. Grep `Character.Instance`,
`StatusController.Player`, `PersistentPlayer3D.Instance` and `FindAnyObjectByType<Player3DController>` before
declaring a server-side feature done. Related: [[net-interface-refs-are-not-unity-null]].
