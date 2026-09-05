---
status: current
owner: vince
last-updated: 2026-09-05
tags:
  - ai-learnings
---

# A wait-loop never finishes, but the thing it is watching is fine

## Reach for this when

You wrapped a status check in a loop — `until <check>; do sleep; done` — and it never comes back, while the tool
itself answers instantly when you run it by hand.

## The rule

**A wait-loop must exit on EVERY answer the tool can give, not only the ones you were expecting.** Before writing
one, run the check once and read the actual answer. If you cannot list every value it returns, do not loop on it:
run it, wait a fixed time, run it again.

And a loop like this survives its own command being killed, so it keeps asking forever in the background. If one
does run away, kill it by name before doing anything else.

## Why

2026-09-05. A recompile was triggered and then waited on with `until … | grep -qE "completed|idle"`. The editor
compiled in seconds and answered `up_to_date`, because there was nothing to compile — a perfectly healthy answer
that matched neither word. The loop asked forever, the command hit its 300-second timeout and was pushed into the
background, and it was still asking minutes later after the work had moved on.

**Two costs.** Five minutes waiting on a thing that had already finished, and a stray process left polling. It
also looked exactly like the editor hanging, which sent the diagnosis toward a page about dialogs — the tool was
answering the whole time.

## How to apply

1. **Run the check once, alone, and read it.** One call, no loop.

   ```bash
   unity cmd recompile_status
   ```

2. **Match on the FAILURE state instead**, which is usually the one value you can name with confidence:

   ```bash
   until ! unity cmd recompile_status | grep -q '"status":"compiling"'; do sleep 3; done
   ```

3. **Or do not loop at all.** Fire it, wait once, read once — which is what this project's own rule about not
   sitting in a polling loop already asks for.
4. **If one runs away:** `pkill -f "<the command>"`, then check it is gone with `ps aux | grep -c "[t]he command"`.
