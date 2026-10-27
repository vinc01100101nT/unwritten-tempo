---
name: no-script-edits-during-users-play
description: "Saving a C# file while the user is in Play mode makes Unity recompile mid-play — check editor_status and stop Play BEFORE the first script edit, not only before a recompile"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 8008d8ec-c710-45e6-996f-6236594b15de
  modified: 2026-10-26T07:08:36.918Z
---

Before the FIRST edit to any C# file, read `editor_status`. If `playMode` is `playing`, stop Play first (stopping is
mine) and say so. Writing the file alone is enough: the editor notices it and recompiles while playing, which breaks
the user's session.

**Why:** 2026-10-26, twice in one session: I edited scripts while the user was play-testing the seamless world; the
editor showed `compiling: true` during Play, their game disconnected and reconnected once, and the console filled.

**How to apply:** one `editor_status` read before starting a batch of script edits; it is not enough to check only
before calling recompile. Related: [[recompile-gate-in-same-command]].

**Again 2026-10-26, three more times:** I wrote a C# file (the builder, a comment-only edit, the carpet script) while
the user was playing the test world, and the editor recompiled under them each time. The `editor_status` check has to
be the tool call immediately BEFORE the Edit/Write of any `.cs`, every time — not once per task, and not only before
recompiling. The user plays between my turns.
