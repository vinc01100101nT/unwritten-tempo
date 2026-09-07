---
name: check-play-mode-before-recompiling
description: Never trigger a Unity recompile without first asking the editor whether somebody is in Play mode — it has destroyed a live session three times.
metadata:
  type: feedback
---

Before ANY recompile, reload, or restart of the Unity editor, run `unity cmd editor_status` and read
`playMode`. If it says `playing`, do not recompile — say so and wait.

**Why:** a recompile forces a domain reload, which nulls every reference the running game was holding. The
session then throws the same exception every frame from files nobody touched — the classic pair here is a
footstep tracker's plant callback and the unit status panel's tracked target — and the play session is over.
This has now happened three times (2026-09-01, 2026-09-03, and 2026-09-07, when a recompile to pick up a
one-line UI change killed a session the user was mid-test in). The check costs two seconds; the failure costs
their whole run and a round of diagnosis.

**How to apply:** `unity cmd editor_status` immediately before the recompile, every time, not only when it
"feels risky". The reactive version of this lesson is [[never-reload-under-a-live-session]] in the project's
AI-learnings folder — it kept failing to fire because it is indexed by the symptom, which arrives too late.
