---
status:
owner: vince
last-updated: 2026-09-28
tags:
  - ai-learnings
---

# A Fake Program Does Not Fake the Script Around It

**Reach for this when:** you are about to test a start script, a build script or any wrapper by swapping the
program it runs for a harmless fake.

## The rule

The fake replaces ONE line of the script. Every other line still runs for real — moving logs, deleting old
output, writing files, killing processes — against the real folders. Before running it, read the script for
anything that touches a file or a process, and run it from a throwaway copy of the folders it touches (or point
those paths at a scratch folder). If you cannot do either, do not run it: check it by reading.

## Why

2026-09-28: a new world-server start script was tested with a fake server program that only printed which
settings it received. The fake worked. But the script's own first line moves `logs/server.log` to
`logs/server-prev.log` before starting — and a real world server was running at the time, writing to
`logs/server.log`. So the test:

- renamed the live server's log (it kept writing, under the other name),
- overwrote the previous run's saved log, which existed nowhere else — lost,
- and left the fake's 225 bytes where the live log should be.

The fake program made the test feel harmless. The harm came from the three lines around it.

## How to apply

1. List every line of the script that writes, moves, deletes or stops something, and where.
2. Check whether anything live uses those paths right now: `lsof <file>`, and
   `ps -o pid=,lstart=,command= -ax | grep <name>` for anything running.
3. Run the test in a copy: `cp -R` the few folders it touches into the scratchpad and run the script from there,
   or change the paths it uses to scratch paths for the test.
4. Afterwards, confirm the real paths are unchanged (file sizes and dates) before saying the test was clean.
