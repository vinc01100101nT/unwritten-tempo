---
name: saved-character-filed-in-wrong-map
description: Townsfolk vanished after Select character — the server filed a returning character's body in whatever scene was active, never in their map
metadata:
  type: project
---

**Symptom (2026-10-04):** after "Select character" in Lastline, the townspeople were gone. It only happened for a
character with a save (one already played). A never-played character saw them fine.

**Root cause:** a new player body is created in the server's currently active scene. `SetMapOnArrival` set the map
NAME from the save but never moved the body. The client's later `CmdReportMap` then returned early, because the
name already matched. So the body stayed in the active scene. That scene is Lastline only by luck: once a field was
loaded (it becomes active) and then let go, everyone arriving with a save was filed in another scene. The interest
manager compares scenes first, so they received no townsfolk shells (measured: `NetworkClient.spawned` held only
the own player) and nobody in the town saw them.

**Fix (NetPlayer):** one `FileBodyInto(map)` is now called on arrival and on every map report. A report whose name
matches still re-files a body that is not yet in that map.

**Gotchas:**
- A fresh game-server build is needed; the editor alone cannot prove it against a running server.
- Never-played characters mask the bug, because their map name is empty until the client reports it.
- Driving entry through the live editor too fast after `BackToCharacterSelect` sends two entry requests: "taken over",
  then "Multiple NetworkManagers". That is a test artifact, not this bug.

Related: [[the-game-is-always-online]].
