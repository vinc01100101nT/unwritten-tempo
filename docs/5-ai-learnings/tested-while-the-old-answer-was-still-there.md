---
status: current
owner: vince
last-updated: 2026-09-09
tags:
  - ai-learnings
---

# You proved the fallback works — on a machine that still had the old answer

## Reach for this when

**You have added a fallback — a defaults file, a built-in value, a "if nobody said, use this" — and you are
about to report it verified.** Or: you tested it and it passed, and the user hit the exact failure it was
written to prevent within the day.

## The rule

**A fallback is only tested in the state that has NOTHING else to answer with.** Your own machine almost always
still holds the old answer — an exported setting, a config left over, a terminal you started from months ago —
and the code will use it, quietly, and pass. Take the old answer away, or reproduce the environment the user
actually has, and run it again. Then **ship the fallback with a value in it**: a mechanism delivered empty
changes nothing until somebody fills it in, and the person who has to fill it in is the person it was built to
help.

## Why

2026-09-09. The day before, a game's builds gained a settings file so a copy with no terminal could be told
where its servers are. It was verified live: the account address came back from the file, the character address
came back from the environment. Both halves of the priority rule, measured, reported as working.

**But the file was then emptied back to comments — the shipped state — and that state was never run.** The only
reason the test passed at all is that the editor had been started from a terminal that exported the character
address; the file supplied one value and the leftover environment supplied the other, so no single run ever
exercised what a user would actually get.

The next morning the user reopened the editor from the Dock. No terminal, so no exported settings, and a file
holding nothing but comments. Play mode signed in and could not enter the world — the precise failure the file
existed to prevent. The fix was one line in the file that should have been there from the start.

Cost: one round, and a "verified live" report that had not covered the shipped state.

## How to apply

1. **Name the environment the user will be in**, in words, before testing: no terminal, no exported settings, a
   fresh machine, a double-clicked application. Then reproduce it.
2. **Remove the old answer for the test** — unset the variable, rename the leftover config — and confirm the run
   still passes. If you cannot remove it, read it first and say plainly which half of the result came from it.
3. **A test where two sources each answered once has proven neither.** Force every value to come from the new
   source in a single run.
4. **Ship the fallback populated.** Empty templates are for values only the user can know (a domain, a secret).
   Anything you can already answer — this machine, the default port, the local address — goes in as the shipped
   value.
5. **Say which state you tested**, not just that it worked. "Proved with the file filled in" and "proved in the
   state it ships in" are different sentences, and only the second is the one the user is about to live in.
