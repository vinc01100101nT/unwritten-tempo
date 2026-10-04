---
status:
owner: vince
last-updated: 2026-10-04
tags:
  - ai-learnings
---

# A Shared Diary Loses Entries When Writers Overlap

## Reach for this when

**A check says something never ran, and it runs fine when you start it by hand.** That includes a heartbeat,
a "last seen" file, a status file, or a tally that several processes each update. Entries that were there
yesterday are now missing, and a stray `.tmp` file sits beside the real one.

## The rule

A file that several processes **read, add to, and write back** needs one writer at a time. Take a lock before
the read and release it after the write. **An unreadable file is not an empty one.** If it exists but cannot be
read, skip this write: writing back "everything I could read, plus me" erases every other writer. On Windows,
retry a refused read or replace for a moment before believing it. A process holding the file open for one
instant is enough to make the refusal happen.

## Why

2026-10-04. The end-of-turn check blocked a reply and said the session-start guard "has never once reported
running". Started by hand, the guard ran and stamped itself in under a second. The diary held three guards. The
session-start guard and the write-scope gate had been erased, together with their eight-session histories. A
`.tmp` file left over from the night before still held them, which shows a replace on Windows had been refused
and nothing had cleaned up after it.

Two separate faults were behind this:
- Every hook treated a refused read as an empty diary, and then wrote back only itself.
- Hooks that fire back to back each read the diary, added themselves and wrote it back, so the slower one
  dropped whatever the faster one had added.

The atomic move already in place stopped half-written files. It did nothing about lost updates.

## How to apply

- Find every writer: search for the file's name, and read each hit that writes to it.
- Lock with a file created exclusively (`os.open(path + ".lock", O_CREAT | O_EXCL)`). Wait up to about two
  seconds for it. Treat a lock older than the longest hook timeout as left behind by a killed process.
- Read: a missing file means `{}`; a file that exists but can't be read means skip the write. Retry
  `PermissionError` 20 times, 50 ms apart.
- Write beside the real file, replace it in one step, retry the replace the same way, and always delete your
  `.tmp` in a `finally`.
- Prove it under load: eight processes writing ten times each, all at once. Every writer's entry must be present
  afterwards, with no leftover files.
