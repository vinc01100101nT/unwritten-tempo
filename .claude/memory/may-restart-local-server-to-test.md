---
name: may-restart-local-server-to-test
description: Standing permission — stop and restart the local game server yourself to test a change; a running server is never a reason to be blocked.
metadata:
  type: feedback
---

The user has granted STANDING permission (2026-08-30) to **stop the local game server process and start it
again** whenever a change needs proving. Killing the running `builds/server/unwritten-server.app/unwritten`
process, rebuilding the server, and relaunching it is explicitly allowed WITHOUT asking each time. This is a
named carve-out from the global "destructive / shared-state actions require confirmation" rule — the server is a
local test process, not shared infrastructure.

**Why:** the previous session refused to disturb an already-running server and worked blind around it, which
made a live measurement impossible and turned a measurable bug into a reasoned guess. RULE ZERO needs the
running world; being polite about the process defeats it.

**How to apply:**
- Read the port first — `GAME_SERVER_PORT` (default 7777). A second server for a probe can run on a spare port
  instead of touching the user's (that trick still works, and is preferred when the user is mid-session).
- **Read `logs/server.log` FIRST.** Since 2026-09-07 the blessed run command tees the server's output to that file
  (previous run kept as `logs/server-prev.log`), so the server's own account of what it decided is already on disk
  and usually answers the question without restarting anything. The exact command is in the network runbook.
- A throwaway server you start yourself on a spare port takes its own log file:
  `GAME_SERVER=1 ... -batchmode -nographics -logFile <path>`
- After a C# change the server must be REBUILT (`tools/build-game.sh server`) before restarting — a relaunch
  alone runs the old code. The build drives the open editor and takes minutes.
- Say in the report that the server was restarted, so the user knows their session was interrupted.

Related: [[unity-mcp-bridge]], [[assume-compiled-never-blame-recompile]]
