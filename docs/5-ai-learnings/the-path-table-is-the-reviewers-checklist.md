---
status: current
owner: vince
last-updated: 2026-09-13
tags:
  - ai-learnings
---

# Every fix round produces a new bug, and the user is exhausted

## Reach for this when

A fix has been reviewed, the review found new breaks, you fixed those, the next review found more — and the
person paying for it says something like "we will endlessly loop on this". Or you are about to hand over a
play-test while a reviewer's findings are still open.

## The rule

**Before touching anything, write the path table: every situation the changed code runs in, one row each, with
what it does TODAY and what it will do AFTER.** Then make the edit, walk every row again against the new code,
and hand the SAME table to the reviewer as its checklist, asking for a verdict per row. A reviewer briefed with
"here is what changed" hunts wherever it likes; one briefed with fifteen rows has to answer all fifteen, and the
row it cannot answer is the one you did not know you were missing.

**And no play-test until the table is closed** — no known bug, no unanswered reviewer finding, no pending
decision. A play-test proves the code as it stands; anything changed afterwards can un-prove it, so a session
run halfway through the work is a session that has to be run again.

## Why

2026-09-13. A hit-effect change went through four reviewer rounds. Round one found the effect firing twice;
the fix for that fired it on the wrong side; the fix for that missed the multi-hit path; and the "slow, haste,
blind" cue that came with it was judged on a summed total and missed a slow landing on a hasted unit. Each fix
was correct for the case in front of it and wrong for a case nobody had listed. The user's words: "it is
exhausting… make sure your next return to me will have no bugs at all."

The round that ended it was the one that started with a fifteen-row table (player and creature, normal hit and
skill hit, crit and miss, single hit and flurry, alive and dead, the machine that decides and the machines that
watch, refresh vs first application). The reviewer answered every row with a citation; two rows broke; both
were fixed the same turn; the next review found nothing in play.

A play-test checklist had also been handed over twice while findings were still open. Both would have been
wasted — a rule was written into the project file so it does not happen again.

## How to apply

1. List the rows BEFORE the edit. The axes that keep catching this codebase: both sides of every split (player /
   creature, deciding machine / watching machine, editor / runtime); every lifecycle state (spawn, live, dead,
   pooled, respawn); single vs repeated (one hit / a flurry, first application / refresh / extra stack); and the
   nothing-happened cases (miss, block, parry, immunity, full absorb).
2. Fill "today" from the code, with a line number per row. A row you cannot fill is a row you do not
   understand yet — read until you can.
3. Edit. Diff against a copy taken before. Run every offline check in the same turn.
4. Walk the table again against the new code, row by row, and write the result beside each.
5. Brief the reviewer with the table verbatim and ask for holds / breaks / could-not-determine per row, cited.
6. Fix what breaks, re-run the checks, review the delta. Only when the table is clean and nothing else is
   pending does the play-test checklist go out — one checklist for the whole batch.
