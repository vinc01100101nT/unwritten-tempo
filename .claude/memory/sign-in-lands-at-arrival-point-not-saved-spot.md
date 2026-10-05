---
name: sign-in-lands-at-arrival-point-not-saved-spot
description: "Characters came back in the right map but at its arrival point after Select character / re-login — two causes, one client, one server (fixed 2026-10-05)"
metadata:
  node_type: memory
  type: project
  originSessionId: 400d8e5b-6aec-4745-a840-d187b7acc2dc
  modified: 2026-10-04T23:36:52.695Z
---

**Symptom:** after Select character (or signing in again without restarting the game) the character comes back in
the saved map but at the map's arrival point, not where they stood. A fresh launch worked.

**Cause 1 (client, the one the user hit):** the save is read the moment a character is chosen, BEFORE the map
loads. After Select character `MapManager.CurrentMap` still names the last map while nothing is loaded under it,
so `SaveManager` "returned them" onto empty space, used up the one-shot resume spot, and the real arrival then
stood them at the arrival point. When the names differed it loaded the saved map a SECOND time beside the one
sign-in was loading — same result. Evidence: the user's Player-prev.log shows "placed at spawn" on every entry
after the first, and a fresh launch (CurrentMap "") had no placed line. Fix: `SaveManager` hands the spot to the
arrival and only acts once `MapIsUp` (current map loaded, nothing arriving).

**Cause 2 (server):** the character server's progress read lets go of the character everywhere, so the player's
OWN save read can kick the connection it just made; that takeover save wrote the right map with (0,0,0), because
`SetMapOnArrival` sets the map at join but no position has been believed yet. Measured 3/3 on headless sign-ins:
(-11.6, 5.7) rewritten to (0,0,0) 150 ms after connecting. Fix: `NetPlayer.SaveNow` writes the stored place it
arrived with until `haveAcceptedAPosition`. The self-kick itself (client reconnects once per sign-in) is still
there — not fixed, asked about.

Related: [[saved-character-filed-in-wrong-map]], [[ai-plays-headless-client]].
