---
name: world-split-across-processes
description: The world is no longer one server holding a fixed map list — zones are placed on servers by load, unloaded when empty, and players move between servers through a door.
metadata:
  type: project
---

Built 2026-09-05. The multiplayer shape changed fundamentally, and most of what older notes and docs describe is gone.

**No server is told which zones to hold.** The old authored map list and its environment override were deleted. A game server starts holding one map only, so there is somewhere to stand.

**The character server places zones.** Each game server reports how many players and creatures it has; when somebody enters a zone nobody holds, the character server picks the quietest server, tells it to bring that zone up, and waits until it is genuinely standing before pushing the character across.

**A zone empty for 45 seconds is unloaded**, creatures and all. It comes back fully populated because loading runs its spawners again — so nothing about it has to be remembered. A server never releases its last zone.

**A door now asks the server** instead of travelling on the player's own machine. Crossing to a zone another server holds is: controls off, save and wait for it to land, character server places the zone and mints a one-time ticket, then the player hangs up, DIALS FIRST and loads the map second. A failure leaves them where they were.

**Every lasting timer is an absolute moment on the world's clock**, which is the clock of the machine running the world — no asking, no fallback. Cooldowns are saved and keep running while logged out.

**A player's machine can no longer write a character at all** — that door was deleted from both ends.

**One sign-in per account, newest wins.** The sign-in service stamps a marker on the account, carried in the pass and checked at the door into the world. Entering also evicts anyone playing THAT CHARACTER anywhere, after making them save. Deliberately NOT account-level: blocking one account from two characters is defeated by a second account, and the duplication risk is already closed. See [[the-five-anti-invention]].

**None of it has been play-tested.** Everything was compile-verified only.
