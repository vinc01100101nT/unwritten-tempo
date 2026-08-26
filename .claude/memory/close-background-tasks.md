---
name: close-background-tasks
description: Always kill background tasks once they are no longer needed — never leave wait-loops spinning.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 744e3a9a-ba3c-48c3-99cb-b14c51d55dd3
---

Never leave a background task running once it is no longer needed. Kill it explicitly the moment its
purpose is served, and check for strays before ending a turn.

**Why:** an `until`-loop whose condition never becomes true spins forever. Three of mine sat running for
~49 minutes while I had already moved on; the user had to spot them in the task list and interrupt to tell
me. They cost real machine time and they clutter the user's view of what is actually happening.

**How to apply:**
- Prefer a bounded wait (a loop with a max iteration count) over an open-ended `until` on a condition that
  may never be met — a build that fails produces no artifact, so "wait for the artifact" never returns.
- When a background job's answer arrives another way (I polled the status directly, or the work was
  superseded), kill the job immediately rather than letting it run on.
- Before ending a turn, sweep for strays and either kill them or say plainly what is still running and why.
- This is the same discipline as [[cleanup-when-fix-is-done]] — a spinning process is scaffolding too.
