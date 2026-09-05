---
status: current
owner: vince
last-updated: 2026-09-04
tags:
  - ai-learnings
---

# A tool threw away the detail you needed

## Reach for this when

A checker, build or test script printed a summary and deleted the detail behind it — and you are about to copy
the script somewhere writable so you can keep its log.

## The rule

**Do not copy the tool.** A script almost always works out where the repo is from its OWN location, so a copy
placed anywhere else is a different program that cannot find anything. Read the script first — it takes seconds
— and you will usually find the deletion is one line you can neutralise while running the original in place.

And before any of that, ask whether the source answers the question directly. A tool's output is a second-hand
account of code you can simply read.

## Why

2026-09-04. A compile check printed `runtime clean (20 warnings)`, up from 19 after an edit, and gave no way to
see which warning was new. The script writes each pass to a log under a temporary directory and removes it on
exit — one `trap 'rm -rf "$OUT"' EXIT` at line 57.

Rather than read those 57 lines, I copied the script to a scratchpad with the temp directory rewritten to a
path I could keep. It refused to run: it derives the repo root from its own path, so from the scratchpad it
looked for the project one directory up and found nothing. I tried twice more — rewriting the trap as well,
then hunting the filesystem for a log that was never written — before stopping.

**Three attempts, no measurement.** The answer took one `grep` of the file I had just edited: the type that
receives a character from the network is now only ever read, never assigned in our code, so the compiler notes
it. That grep was available before the first attempt.

## How to apply

1. **Ask the source first.** "Which warning is new?" is usually answerable by reading what you just changed.
   A new warning almost always names something you just made unused, unassigned or unreachable.
2. **If you do need the tool's output, read the tool.** Find where it writes and what removes it. Two greps:

   ```bash
   grep -n 'trap\|rm -rf\|mktemp\|OUT=\|LOG=' <the script>
   ```

3. **Run the ORIGINAL in place**, with the cleanup disabled, rather than running a copy elsewhere:

   ```bash
   bash -c 'trap - EXIT; <the script>'     # or copy the log out before the script exits
   ```

4. **A copy of a script is a different program.** Anything it resolves from its own path — the repo root, a
   sibling tool, a config beside it — is wrong the moment you move it. If you must copy one, copy it INTO the
   directory it expects to run from, never outside the repo.
