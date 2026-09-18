---
name: sprint-manager-ticks-never-adds
description: The sprint manager only ticks existing tasks and moves existing roadmap entries; a NEW task or roadmap item is written by the main session first, then it is sent to tick
metadata:
  type: feedback
---

Sending the sprint manager a job that includes WRITING a new task or a new Roadmap item costs a round: it
reads both files and declines, correctly, because adding wording is planning and planning stays with the main
session (project CLAUDE.md, RULE TRACKING).

**Why:** on 2026-09-17 a "give GMCP a roadmap spot" request went to it first; it returned the next free ID and
the file shapes but wrote nothing.

**How to apply:** write the `MT-N` task (next free number) and the timeline item yourself — inserting a line,
never re-serialising `roadmap.json` (json.dump reformats every line of that hand-formatted file) — then spawn
the sprint manager to tick and keep "Now" consistent. Related: [[ai-plays-headless-client]].
