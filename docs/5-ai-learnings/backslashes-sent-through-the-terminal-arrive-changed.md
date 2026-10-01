---
status:
owner: vince
last-updated: 2026-10-01
tags:
  - ai-learnings
---

# 🔣 Backslashes Sent Through the Terminal Arrive Changed

## Reach for this when

A file you edited by running a script from the terminal now holds an invisible character where `\033` was, a
real line break in the middle of a quoted `'...\n'`, or a line-continuation `\` that vanished — and the file
still passes its syntax check. Or you are ABOUT to edit text that contains backslashes by sending a script
through the terminal.

## The rule

**Text that contains a backslash is written with the editor tools, never by a script typed into the terminal.**
A script's backslashes can be rewritten on the way in — `\033` became the escape character itself, `\n` became a
real line break, `\\0` became the escape character followed by `33` — and the file it writes looks fine to every
check that reads it as code. If a script really must write a backslash, build it from its number (`chr(92)`)
instead of typing it, and scan the result for raw control bytes afterwards.

## Why

2026-10-01, making the project's scripts work on both the Mac and Windows. Three edits made by Python scripts run
from the terminal damaged two shell scripts: every coloured error message lost its `\033`, and two `printf`
lines gained a real line break inside their quotes. Both files still passed `bash -n`, so nothing flagged it — a
count of byte 27 in each file found it. Repairing it took five more commands, one of which (`sed` with `\\033`)
made it worse, and the repeat alarm fired on the session. Exactly which layer rewrites the backslashes was NOT
measured; a quoted heredoc delimiter, which should stop the shell from touching them, did not prevent it.

## How to apply

1. Any edit whose new text contains a `\` goes through the editor's Edit or Write tool.
2. If it must be scripted, write the backslash as `chr(92)` (Python) and keep everything else plain text.
3. After ANY scripted edit, scan every file it touched — not only the one you meant:
   ```bash
   python -c "import sys;[print(f, open(f,'rb').read().count(bytes([27]))) for f in sys.argv[1:]]" <files>
   ```
   Any non-zero count in a file that never held an escape character is damage. Then re-read the diff of each
   edited region, because a real line break inside quotes shows up only there.
4. Repair the damage with targeted edits — never by putting an older copy of the file back over it.
