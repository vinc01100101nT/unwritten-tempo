---
name: handoff
description: Use when the user says "do a handoff", "handoff this", "hand off the task", or when stopping work on an unsolved hard bug or an unfinished task that a future cold session must resume. Covers the handoff file's required contents, where it lives, and when to delete it.
---

# Handoff

Treat **"do a handoff"** as a command: WITHOUT further prompting, write a handoff file so the NEXT session can
resume cold. One protocol covers both a general in-progress task and an unsolved hard bug — both live in the
same folder.

1. Derive a short **kebab-case name** for the CURRENT task/bug (what this session has been working on).
2. Write it to `handoffs/<name>.md` (create the `handoffs/` folder if it doesn't exist yet).
3. After writing, tell the user the exact path.

## Required contents, in this order

- **Task/bug** — one line: what it is + any Tempo issue/doc id.
- **Status** — DONE / IN PROGRESS / BLOCKED / NOT FIXED — honest. If a fix or build is unverified (e.g. Unity
  not recompiled) or the bug survived attempts, say so explicitly and add **"don't trust prior 'it works'
  claims"**.
- **What's done** — plain bullets of what was built + the exact files/functions touched.
- **Uncommitted working-tree changes** — what's still unstaged/revertible, so the next session sees the real
  state. Re-measure it; never copy a `git status` from earlier in the session. This is why the file lives in the
  repo rather than in memory.
- **What's left** — the remaining steps, concrete.
- **Dead ends** — for a bug, THE highest-value part: what was tried and did NOT work. Keep **proven facts**
  (traces/measurements) SEPARATE from **hypotheses**.
- **Next single step** — the one thing to do next; for a bug, the single discriminating test that would isolate it.
- **How to resume** — a paste-ready line for a fresh session (points at this file).

## Rules

- One file per task/bug, kebab-case. If a handoff for the same one already exists, UPDATE it in place — never fork.
- When a new task or bug looks related, `ls handoffs/` FIRST for a prior handoff to build on.
- **On resolution:** collapse the durable lesson into a memory first, then DELETE the handoff. No stale handoffs.
