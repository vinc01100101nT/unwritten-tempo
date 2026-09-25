---
status:
owner: vince
last-updated: 2026-09-26
tags:
  - ai-learnings
---

# ✂️ One Script Edited Six Files — and You Never Checked Which Ones It Reached

## Reach for this when

You are about to make the same kind of edit to several files in ONE script — a batch of find-and-replace, a
sweep across a folder, a set of doc updates — and you will report them all done when it exits. **Or a command
came back "rejected" or "interrupted" and you are about to say nothing was changed.**

## The rule

**A multi-file edit script is several edits, not one.** Any failure part-way — a missing file, a match that no
longer exists, a typo in the fifth pattern — leaves the earlier files written and the later ones untouched, and
the exception tells you the script failed while saying nothing about which half landed.

So, both halves of the habit:

1. **Write each file as soon as its own edits are done**, not all at the end, so a later failure cannot cost an
   earlier file its work.
2. **Verify per FILE afterwards, never per script.** Grep each target for the new text. The script's exit code
   is evidence about the script; only the file is evidence about the file.

And when one does abort: **do not just fix the pattern and re-run.** Re-running a half-applied script hits an
`assert count == 1` on the files that already succeeded and stops again, which looks like a second, different
failure. Check what landed first, then write only what is missing.

## Why

2026-09-21, this project. One script updated a design doc, a reference doc and a play-test checklist. The
checklist had been renamed into a folder by a parallel session, so the script raised `FileNotFoundError` on it —
and the *implementation plan*, whose edit came after the checklist's in the same script, never got written. The
first two files were already saved, so the failure looked like it was only about the checklist. The plan's update
was reported as done and was missing for **two further turns**, until an unrelated script's assertion failed on
text that should already have been there.

The same session, the same shape, a second time: a later script's `old` pattern did not match, and it aborted
between writing the checklist and writing the plan. Both times the visible error pointed at one file while a
different file was the one left behind.

2026-09-26, the same failure from the outside: a single command (back up a file, edit it, then run three checks)
came back "rejected". The user asked why it was interrupted. The backup and the edit had run; only the checks had
not. Saying "nothing was changed" would have been false — a diff against the backup showed the edit in the file.
A rejection or interrupt stops a command where it is, not before it started.

## How to apply

- One file, one write, immediately:
  ```python
  s = open(p).read(); s = s.replace(old, new); open(p,'w').write(s)   # then the NEXT file
  ```
- After the script, prove each file separately — this is the whole check, and it costs one line:
  ```bash
  for f in a.md b.md c.md; do grep -q "the new sentence" "$f" && echo "ok $f" || echo "MISSING $f"; done
  ```
- Keep `assert s.count(old) == 1` before every replace — it is what makes a silent no-op impossible — but read
  it as "this file was not edited", never as "the script did nothing".
- **After a rejected or interrupted command, diff every file it could have touched** before saying what
  happened. Its first steps may have finished.
- If the files are genuinely independent, prefer one tool call per file. A failed call is then visibly a failed
  call, and nothing else is in doubt.
