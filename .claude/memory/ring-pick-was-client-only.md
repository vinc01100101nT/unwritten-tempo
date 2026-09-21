---
name: ring-pick-was-client-only
description: Ring of Progression picks never reached the server — reset on every login and raised no stats; fixed 2026-09-21 by a server-decided pick request
metadata:
  type: project
---
Symptom (2026-09-21): ring ranks reset on every login, and picks raised no stat.

Root cause, measured: the draft panel called `PassiveRanks.Pick` on the player's machine only — no request to the
server existed. The server's copy (which computes stats and writes the save) never changed. DB row for "Alpha" read
`bankedPoints 33, rankStats [], hasLeader false` after many picks. The wildcard was also rolled on the player's machine.

Fix: `NetPlayer.CmdPickRingStat` (server checks `PassiveRanks.MayPick` — any stat on the opening pick, else leader /
neighbours / the server-rolled `Wildcard`), `Pick`, `RollWildcard`, `SaveSoon`, then `PushMyRing` → `TargetRing` →
`PassiveRanks.TakeFromServer`. `PushMyRing` runs on the server tick, gated by `PassiveRanks.Version`. The panel
(`SkillDraftPanel.OnWalkChanged`) animates only once the server's walk shows the pick landed.

**How to apply:** any "resets on login / does nothing" report on a player choice → first check whether a request to the
server exists at all (grep `Ask`/`Cmd`) and read the character row. Related: [[the-game-is-always-online]].
Still open: the ring rows' and cards' "+N%" text is still worked out on the player's machine from ranks.
