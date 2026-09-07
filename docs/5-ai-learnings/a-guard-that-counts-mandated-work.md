---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# A Guard That Counts the Work Your Own Rules Demand

## Reach for this when

**An alarm keeps firing at you for doing the thing you were told to do** — a "you have run that several times"
notice about a command a rule requires after every edit, a lint-on-save counted as a retry, a health check that
trips on the healthy path.

## The rule

A guard that counts repeats must **exclude whatever the project's own rules require you to repeat**. Before
adding anything to such a list, ask of each entry: *is repeating this a mistake, or is it compliance?* If the
answer is compliance, it does not belong on the list at any threshold — because it will fire in almost every
session, and an alarm that is usually wrong trains everyone to skim past the one time it is right.

And when a guard does fire, **check whether it is right before acting on it**. Read what it counted. A wrong
alarm is a bug in the guard, not a reason to change what you were doing.

## Why

2026-09-07. A repeat-watcher was built to catch the real second attempt — nobody runs a five-minute build twice
for fun. Three fast offline checkers were put on its list alongside the build, because they were "project tools".
But this project's rules say to run the compile checker **after every single edit, mid-task**. So the guard was
counting compliance.

The saved tally settled it, and the numbers are not close: it flagged the compile checker in **13 of the 16
sessions** recorded — 73 runs in one, 61 in another, 29, 28, 17, 15. Not one of those was a retry; each was an
edit verified exactly as required. Every firing blocked the end of a turn and demanded a lesson index be read,
and each cost a round. Worse, it buried the two entries that genuinely meant something — a five-minute build and
a server restart — inside the same message.

Fixing it turned up a second fault in the same file, invisible for the same reason: two entries on the list were
two words long (`npm run`, `npm start`), and the matching only ever compared single arguments — so those two had
**never matched anything, in any session**. A dead entry reports no error. It was found only because a test case
written for the first fix failed.

## How to apply

1. **Split the list by what a repeat MEANS.** Expensive-and-usually-a-retry on it; fast-and-mandated off it.
   Say which is which in a comment at the list, with the numbers, so nobody re-adds one on a tidy-up.
2. **Prove the exclusions.** Write a test that fails if the mandated tools go back on the list — an exclusion
   with no test is one somebody restores next month.
3. **Read the counter before believing the alarm.** These tools keep a tally file; open it. Fifteen sessions of
   history answers "is this guard useful?" in seconds, and no amount of reasoning does.
4. **Test every entry, not just the shapes.** An entry that can never match is silent, permanent and invisible.
   One case per entry catches it.
