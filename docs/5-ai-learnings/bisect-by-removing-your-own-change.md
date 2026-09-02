---
status:
owner: vince
last-updated: 2026-09-02
tags:
  - ai-learnings
---

# Bisect by Removing Your Own Change

## Reach for this when

**Something worked before your change and is broken after it, and reading the code has not told you why.**

## The rule

Stop reading. First spend five seconds checking whether anything else in the system moved — a service restarted,
a file replaced, a setting changed by somebody's hand — because "right after my change" is a coincidence often
enough to be worth ruling out for free. Then put your change aside, rebuild, and run it. A control run tells you
in one attempt what an afternoon of inference will not: whether the fault is yours at all. Then put the change
back and remove one piece at a time until it works.

You are allowed to do this safely because you keep your own copies: copy every file you are about to touch into
the scratchpad **before** editing, and copy the finished version somewhere else before you set it aside. Then
both directions are a one-line restore and neither needs git.

## Why

2026-08-31. A change made the game server crash on startup. Three explanations were offered to the user, in
order, each stated with confidence and each wrong:

1. "The build overwrote files while the old server was running." Disproved by building into an empty folder.
2. "The multiplayer role setting is stripping server content." Disproved by turning it off and rebuilding.
3. "The data file is malformed." Disproved by the decoder itself being wrong (see
   [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md)).

Each round cost a five-minute build plus a wrong report. The control run — same project, same settings, change
set aside — booted cleanly on the first try and settled the question completely: the fault was mine. The very
next step, putting the change back one piece at a time, found it immediately.

Three rounds of theory, one round of control. The control should have been round one.

## How to apply

0. **First, check whether anything ELSE moved.** Before setting your own change aside, read the START TIME of
   every service and process involved. A control run costs a rebuild; `ps -o lstart=` costs five seconds and
   sometimes settles it outright. **Cost: 2026-09-02.** A game server that had registered happily all afternoon
   suddenly came up refusing every player, right after a code change, and the change was the obvious suspect. It
   was innocent: a companion service had been restarted twenty minutes earlier and come back holding a different
   shared password. Its process start time said so immediately. Probing which password it actually accepted
   settled the rest.
   **And read the project's OWN runbook for whatever refused you, before diagnosing anything.** That same
   afternoon the runbook already carried the exact refusal message, its cause and its fix, written up two hours
   earlier — the whole detour was spent rediscovering a page nobody opened. A learnings index is not the only
   doc worth checking; the operational one usually knows more about its own services than you do.
1. **Before editing, copy every target file to the scratchpad.** This is already a standing rule; this lesson is
   what it buys you.
2. **When your change breaks a build or a run**, save your current versions to a second scratchpad folder, then
   restore the pre-change copies over the working tree.
3. **Rebuild and run.** Clean means the fault is yours; broken means it was there before you arrived, and you
   have just saved yourself from hunting your own diff.
4. **Restore your version and remove ONE piece.** Order the pieces by suspicion — the ones that touch stored
   data, generated files or serialized assets first, because those survive a rebuild and the pure logic does not.
5. **Say which round you are on.** "Control run, change set aside" is a fact the user can trust; "I think it is
   probably X" is the thing this page exists to replace.

A build is five minutes. A wrong theory is five minutes *and* the user's trust in every other thing you said.
