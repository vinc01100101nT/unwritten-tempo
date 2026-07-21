---
name: assume-compiled-never-blame-recompile
description: "Never diagnose a Unity bug as \"the editor didn't recompile\" — the user ALWAYS recompiles before testing."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7267e5ec-04a8-408b-a4c3-ccb46759fd1f
---

The user ALWAYS recompiles Unity (many times) before checking a result. NEVER attribute a persisting
bug to "stale compile / code never recompiled / Auto Refresh off", never ask "did it compile?", and
never make the recompile the suspect. Treat the compiled code as live and running, and diagnose the
actual runtime behavior instead.

**Why:** Blaming recompilation is a lazy non-answer that wastes the user's time and insults them — they
do the recompile reliably every time. The real cause is always in the logic/runtime, not the build step.

**How to apply:** When a fix "still doesn't work", assume it IS running and go measure what the running
code actually computes (probe values, live state) — see [[changes-must-persist-in-repo]]. Skip any
step that re-verifies compilation.
