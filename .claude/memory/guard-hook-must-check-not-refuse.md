---
name: guard-hook-must-check-not-refuse
description: The delete guard refused git-rm outright while allowing a plain delete of the same file — a safety rule that contradicts itself gets worked around instead of respected.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 07e15ed6-d553-4a48-b0f8-d0b4e9b21691
---

`~/.claude/hooks/block-tree-destroyers.sh` lumped git's delete subcommand in with
checkout/restore/reset/clean/revert/switch. Those OVERWRITE a file with an older version, which is
unrecoverable, so they are refused flat. Git's delete only removes a file — and the guard's delete
rules already ask git whether every target is tracked and unmodified, allowing it only then. Result:
deleting a file the ordinary way was allowed, while deleting the SAME file through git — identical
in what it destroys, safer in that it also stages the removal — was refused. Fixed 2026-08-23 by
dropping it from that blanket list; the delete rules match it anyway, so recursive/forced, wildcard,
quoted and untracked-or-modified targets are all still refused.

**Why:** the user's reaction was "why weren't you able to delete a safe delete, fix your bullshit
hook". A guard that refuses provably-safe actions trains everyone to route around it, which costs
more safety than it buys. Refusing outright is only correct when nothing CAN be checked.

**How to apply:** a safety rule should CHECK when a check is possible, and refuse only when it is
not. Before adding a command to a blanket-deny list, ask whether the existing checked path already
covers it. The 58-case suite now lives at `~/.claude/hooks/tests/` — run it after ANY edit to that
guard. Note its trap: a case whose target file does not exist passes vacuously, so a missing fixture
looks like a pass; rebuild fixtures with `fixtures.py` before trusting a run. Known over-reach: prose
that merely mentions a delete command inside a heredoc trips the quoted-target rule — use the file
tools to write such text rather than a shell heredoc.
