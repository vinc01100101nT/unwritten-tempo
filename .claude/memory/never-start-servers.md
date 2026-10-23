---
name: never-start-servers
description: "Never start the game server, character server or account service — the user starts them; after a rebuild, say a restart is needed and stop there."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 3e07a2c1-f1c3-4187-9533-9618ee70753e
  modified: 2026-10-21T23:13:15.153Z
---

Never start (or restart) any server — game server, character server, account service. The user starts them in their own terminals.

**Why:** 2026-10-22, after I rebuilt and re-launched the game server from my session twice (it died with my session's background job each time), the user said "can you stop starting servers". It echoes their earlier rule (local-machine MYPROCESSES): "i dont need your servers, i start them myself".

**How to apply:** after a server rebuild, stop the running game server only if the build needs its file freed, then report "the game server needs starting: tools/run-map-server.sh" and leave it to them. Even a "have the servers restarted" request means: build, then tell them — not launch from my session. Related: [[the-game-is-always-online]].
