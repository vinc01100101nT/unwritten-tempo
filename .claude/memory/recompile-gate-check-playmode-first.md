---
name: recompile-gate-check-playmode-first
description: "Never chain editor_status and recompile in one command — read playMode, and only if \"stopped\" send the recompile in a SEPARATE call"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 12c40a91-cb06-4493-91b0-543f17fad2f5
  modified: 2026-10-18T08:42:18.551Z
---

Read `editor_status` on its own and act on its answer before sending `recompile`. Never put both in one command
(`editor_status; recompile`) — the status is printed but nothing stops the recompile.

**Why:** 2026-10-18, while the user slept, I ran `editor_status; recompile` in one Bash call; status said
`"playMode":"playing"` (the user had left Play running) and the recompile went ahead mid-play, which the project rule
forbids (a recompile mid-play reloads every script and ends the session as a wall of errors).

**How to apply:** two calls: (1) editor_status; (2) only if playMode is "stopped", recompile. If playing, wait for the
user or use `tools/check-unity-code.sh` (offline) instead. Related: [[assume-compiled-never-blame-recompile]].
