---
status:
owner: vince
last-updated: 2026-10-11
tags:
  - ai-learnings
---

# 🔁 Two Computers That Share Git Never Copy by Hand

## Reach for this when

A set of steps you are about to hand over says "copy this file from one computer to the other", "bring it over",
"unzip it there" — and both computers already pull the same repository.

## The rule

**If the two computers share git, the file goes through git.** A step that has a person carry a file between machines
is a step that gets skipped, done with a stale copy, or done into the wrong folder, and the user will rightly ask why
it is not push and pull. Commit the thing only one computer can make (large binaries through the repo's large-file
storage), and make the build on every computer start from the committed copy.

## Why

2026-10-11: the small Mac program can only be compiled on a Mac, and the update folder is published from the Windows
PC. The first version zipped the Mac build into an ignored folder and listed "copy the zip to the Windows PC, unzip it
there" in the hand-back. The user, already pulling the same repo on both machines: "why do i need manual copy steps
haha. it should be push and pull step". Fixing it took one more commit: the program is committed through Git LFS
(12 MB), rewritten only when its bytes change, and every computer builds the app from it.

## How to apply

1. Before writing any cross-machine step, ask: do both machines pull this repo? If yes, the artifact belongs in it.
2. Check whether the build is repeatable — build twice and compare the bytes. Commit only when the bytes differ, so a
   rebuild with no code change leaves git clean.
3. Keep whatever differs per machine (an address, a signature) out of the committed copy, and add it at build time
   on each machine.
