---
name: unsolved-bug-handoff-protocol
description: "When a hard bug eats real debug time and you stop WITHOUT a fix, write a repo-root DEBUGGING_<bug>_HANDOFF.md ledger."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 62f5b254-0bc9-4a90-bbf7-c475e096b1cc
---

When a bug ate real debugging time (multiple hypotheses tried) and you're stopping WITHOUT a fix, write a repo-root `DEBUGGING_<bug>_HANDOFF.md` before ending. Open-bug sibling of [[bug-fix-memory-habit]] (that = closed bug → memory; this = open bug → repo file) .

**Why:** the next agent (or me next session) otherwise re-walks the dead-ends the last session already eliminated. The proven value in the FACING bug was NOT the answer — it was the ledger of ruled-out hypotheses + the single discriminating test, which collapsed the whole problem from one user observation.

**How to apply:** file must contain — (1) status = NOT FIXED + "don't trust prior 'it works' claims"; (2) changes still in the working tree (revertible); (3) **what was tried and did NOT work** — the dead-end ledger (highest-value part); (4) proven facts (traces/measurements) kept SEPARATE from hypotheses; (5) the single discriminating test to run next. Lives in the REPO, not memory — it references uncommitted working-tree state. On a related new bug, `ls DEBUGGING_*_HANDOFF.md` FIRST. On resolution: collapse the lesson into a memory (per [[bug-fix-memory-habit]]) and DELETE the handoff — no stale handoffs at root. Standing rule lives in project `CLAUDE.md`.
