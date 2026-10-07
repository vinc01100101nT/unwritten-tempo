---
status: current
owner: vince
last-updated: 2026-10-07
tags:
  - ai-learnings
---

# The next step found a file missing, though the step before it said "done"

## Reach for this when

A step that reads another program's output fails with "missing" or "incomplete", the file is there when you look
afterwards, and it worked on earlier runs. Or you are writing a wait that decides another program is finished
because its output has appeared.

## The rule

**An output appearing is not its writer finishing.** Most programs create the folder or file first and fill it in
over the next seconds or minutes. Wait for the writer's OWN "finished" signal — its exit, its returned result, or a
note it writes as its very last act — never for the output to exist. A fixed pause after the output appears is a
race that passes until the day the writer is slower.

## Why

2026-10-07. The build-and-publish command built the Windows and Mac games, then the publish step stopped: the Mac
game's start program was "not among the files". It was there a minute later. The file times told the whole story:
Unity created the Mac app's folder at 14:41:40, the build script saw it, waited its fixed 5 seconds and declared
the build done; the publish step read the folder until 14:42:13; Unity wrote the start program and its signature
at 14:42:14 — 34 seconds after the folder appeared. Every earlier run had happened to finish inside the 5 seconds.

**The cost:** a 15-minute build-and-publish thrown away, and a round to find it. Worse, the script had re-signed
the app and written into it while Unity was still writing, so even a lucky publish could have shipped an app whose
signature Unity then overwrote.

## How to apply

1. **Find the writer's own end.** For work run inside Unity, that is `BuildPipeline.BuildPlayer` returning: write
   its result to a note as the very next line, and wait for the note.
2. **Give the note a name only this run knows** (derive it from a `mktemp` name), so a later run can never read an
   earlier one's, and delete it once read.
3. **Read the result, not just the note's presence** — "Failed" finishes too.
4. **To diagnose a suspected race, compare times to the millisecond:**

   ```bash
   ls -la --time-style=full-iso <the output> <what the next step wrote>
   ```

   A file written AFTER the reader finished is the race, proven.
