---
name: stop-hook-runs-more-checks-than-claude-md-lists
description: The Stop hook runs checks by reading the tools folder, so ls tools/check-* is the authority on which checks exist — not any written list.
metadata:
  type: project
---

**The Stop hook's closing line names every check it ran and every one it could not run** — e.g.
`could not run: math: The checks themselves do not compile · checked and clean: code, perf,
editor-writes, client-math`. That line is printed after a reply is finished, so it reaches the USER
and never reaches the session that caused it. **Read it in their next message; it is the only place a
check that refused to run shows up.**

**`ls tools/check-*` is the authority on which checks exist**, not the list in `CLAUDE.md`. On
2026-09-22 `tools/check-game-math.sh` was in the hook and absent from that list, so a session that
ran everything the list named was still told at the very end that a seventh check had failed. The
list has since been corrected, but the lesson is the folder, not the list.

**And a check that CANNOT COMPILE is not a passing check — it is a check that has not run for as long
as it has been broken.** That one had been dead since 2026-09-21, when the mastery rework renamed its
functions and left the tests calling the old names. Nothing said so until the hook did. When repairing
one, read the DESIGN doc first: four of those calls had been deleted as concepts, not renamed, and
writing them back would have pinned a design that no longer exists. See
[[compile-check-without-unity]].
