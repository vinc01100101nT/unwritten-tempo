---
status:
owner: vince
last-updated: 2026-09-12
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

2026-09-12, the same failure from the other end. Five fenced agent roles were added, each carrying its own
guard. The session-start checker was taught to list them — and it then reported all five as "never once
reported running" on the very first turn, because a guard on a role stamps nothing until somebody spawns that
role. On a fresh machine that is the ordinary state, so it would have fired in almost every session forever.

Two rounds were lost to it, and the second one only because the first fix was half a fix: the checker asks the
same question in **two** places — once at the start of a session and once at the end of a turn — and only one
was corrected. The other kept firing. Worse, the checker already had a prover that would have caught both in
one run, and it was never opened before the change was made; the real roles leaked into its fixtures because
it could redirect the settings file but not the new folder.

## How to apply

1. **Split the list by what a repeat MEANS.** Expensive-and-usually-a-retry on it; fast-and-mandated off it.
   Say which is which in a comment at the list, with the numbers, so nobody re-adds one on a tidy-up.
2. **Prove the exclusions.** Write a test that fails if the mandated tools go back on the list — an exclusion
   with no test is one somebody restores next month.
3. **Read the counter before believing the alarm.** These tools keep a tally file; open it. Fifteen sessions of
   history answers "is this guard useful?" in seconds, and no amount of reasoning does.
4. **Test every entry, not just the shapes.** An entry that can never match is silent, permanent and invisible.
   One case per entry catches it.
5. **Adding a new KIND of thing to a checker? Open its prover BEFORE the change.** The prover is the list of
   every question that checker asks, which is exactly what you need to re-ask of the new kind — and it will
   tell you whether the new kind can even be isolated from the real machine, or whether it leaks into every
   fixture. Then answer each question for the new kind: some hold, some are meaningless, and the meaningless
   ones are the false alarms.
6. **Find every place the same question is asked.** One checker asked "has this guard run?" in two functions —
   session start and end of turn. Fixing one and assuming the other looked exactly like a fix that worked, then
   failed again on the next turn.
