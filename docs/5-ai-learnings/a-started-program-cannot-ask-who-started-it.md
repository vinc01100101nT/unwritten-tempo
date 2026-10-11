---
status:
owner: vince
last-updated: 2026-10-11
tags:
  - ai-learnings
---

# 👶 A Started Program Cannot Ask Who Started It

## Reach for this when

A program needs to know which program (or which app) started it — to replace it, to report back to it, to read
something beside it — and the plan is to look up its parent process. Or the parent the program finds is the system's
own starter (process 1, `launchd` on a Mac, `init` on Linux) instead of the program that ran it.

## The rule

**A parent that starts a program and then closes is gone before the child can ask about it.** Looking up the parent
(`getppid`, `proc_pidpath`, the process list) only works while the parent waits for the child, and nothing in the
child can make it wait. When the parent is code you cannot change — an old version already in people's hands — find
what you need some other way: something the parent left on disk, a place it is registered (on a Mac, the app index
`LSCopyApplicationURLsForBundleIdentifier` lists apps by their bundle name), or an argument the parent passed.

## Why

2026-10-11, handing the first Mac game launcher over to a new program. The old launcher's Start button runs a program
and closes its window at once (`patcher.StartGame(); Close();`). The plan was for the new program to read its parent
first thing and so learn where the player keeps the old app. The test run, with a stand-in old launcher that closed
the moment it had started the child, showed the child's parent was already gone: the hand-over skipped the replacement
silently, and the game still started, so only the log showed it. A purpose-built 50 KB C program that read its parent
on its first line lost the same race 8 times out of 8, already-run or freshly signed. The red team had named this race
before any code was written; the plan treated it as rare, and it cost a round of building on it. The Mac's app index
found both test copies of the old launcher 4 seconds after they were placed, without either being opened, and the
hand-over worked on the next run.

## How to apply

1. Before relying on a parent lookup, ask: does the parent WAIT for the child? If it can close first — and any
   "start it and close" button can — the lookup is a race the child loses.
2. Measure the race the cheap way before building on it: a parent that starts the child and exits immediately, the
   child printing what it sees.
   ```python
   import subprocess, sys, os
   subprocess.Popen([sys.argv[1]]); os._exit(0)
   ```
3. Look for a record that outlives the parent: a file it wrote, a registry or index that lists it (by name or by
   identifier), an argument or environment value it passed when starting the child.
4. Whatever is found that way may match more than one copy. Decide which copies are fair game by something the copy
   itself carries (on that day, the old launchers were the ones with no build number), and exclude the places that
   must never be touched (the bin, the program's own folder, read-only copies).
