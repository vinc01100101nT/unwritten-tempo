---
status:
owner: vince
last-updated: 2026-10-13
tags:
  - ai-learnings
---

# A "How Hard Is It" Measure Must List Every Way Round It

**Reach for this when** you are about to accept, or pass on, a formula that scores how hard something is to beat —
a creature's toughness, a lock's strength, a cost — and the number explodes at an edge, so a cap looks necessary.

## The rule

Before accepting the measure, list every way the thing can be beaten and check the measure sees each one. A score
taken against one kind of attacker is not "how hard it is"; it is "how hard it is for that attacker". An exploding
number is the measure reporting its own blind spot, and a cap hides the blind spot instead of fixing it.

When a sub-agent hands back the formula, this check is still mine. Its table being correct does not make its shape
complete.

## Why

2026-10-13, creature toughness. The planner measured "hits needed to kill" against ordinary physical hits only.
Defence here is a flat subtraction, so heavy armour took the score to 825–1,575%, and to tens of thousands when
over-geared. I relayed the table and argued for a cap across three replies. The user asked two questions — "we do not
include magic defence?" and "same with crit resist?" — and that was the whole fix: a critical hit ignores defence and
magic is stopped by magic defence, so the right measure takes the fastest of three hunters and needs no cap. Their
words: "it is very basic". Cost: three rounds, and one planner pass (about 250,000 tokens) that had to be resumed.

## How to apply

1. Write the list first: for a creature — ordinary hits, critical hits, magic, damage over time, anything that skips
   a defence. Read the damage code for what each one ignores before writing a formula.
2. Score against each and take the cheapest way through. The thing is only as hard as its weakest side.
3. If the number still explodes, find which input sits on a hard floor (minimum damage, minimum hit chance) and ask
   what a real player does there. Only then consider a limit, and call it what it is.
4. Put the list in the brief when a sub-agent designs the measure, so it cannot come back one-sided.
