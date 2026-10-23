---
status:
owner: vince
last-updated: 2026-10-11
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

2026-10-03, the same thing from the other side: edit scripts piped to `py - <<'PYEOF'` that searched for C# text
containing `\n` (inside string literals) failed to find it about six times in one session, because the `\n` in the
script arrived as a real line break. Nothing was damaged only because every script asserted that its search text
occurred exactly once before replacing — each failure cost a re-run. Writing the script to a file with the Write
tool and then running `py <file>` worked first time, every time.

2026-10-11, the rule above ignored, twice in one session. A Python edit piped through `<<'EOF'` wrote
`re.compile(r"^[^/\\\0]+$")` into a publish script: the `\0` arrived as a real zero byte, and the file would not
even load ("source code string cannot contain null bytes"). Later a piped edit script that itself contained a shell
heredoc (an `Info.plist` written with `<<EOF`) was cut off at that inner `EOF` line — the outer heredoc ended there
and the shell refused the whole command with "parse error near `else'". Nothing was damaged only because the shell
parses a whole command before running any of it; each cost a round. Both edits went through first time once the
script was a file written with the Write tool.

2026-10-22, again with this page on disk and unread. Adding two test cases to a checker's self-test by a piped
`<<'PYEOF'` script, the `\\n` typed for a literal `\n` arrived as a real line break, so the self-test itself would
not load ("unterminated string literal"). The same night a search for a line of that self-test containing `\n`
failed three times before the fix was written as a file using `chr(92)`. Each cost a round.

## How to apply

1. Any edit whose new text contains a `\` goes through the editor's Edit or Write tool.
2. If it must be scripted, write the SCRIPT itself with the Write tool and run it as a file (`py path/to/edit.py`) —
   never pipe it through a heredoc. This goes double when the text being written holds a heredoc of its own: its
   closing word ends the outer one. Make every replacement assert its search text occurs exactly once, so a mangled
   search string fails loudly instead of writing nothing or the wrong thing. Inside a terminal-typed script, write a
   backslash as `chr(92)`.
3. After ANY scripted edit, scan every file it touched — not only the one you meant:
   ```bash
   python -c "import sys;[print(f, open(f,'rb').read().count(bytes([27]))) for f in sys.argv[1:]]" <files>
   ```
   Any non-zero count in a file that never held an escape character is damage. Then re-read the diff of each
   edited region, because a real line break inside quotes shows up only there.
4. Repair the damage with targeted edits — never by putting an older copy of the file back over it.
