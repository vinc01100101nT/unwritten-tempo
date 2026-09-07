---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# Your Fix Ran on the Wrong Machine

## Reach for this when

Your change compiled, loaded, and did **nothing** — no error, no warning, nothing in the log. The code is
definitely live and the behaviour is definitely unchanged.

## The rule

Before changing any behaviour in a program that runs in more than one place — a client and a server, a worker
and a scheduler, a browser and an API — **find out which of them actually runs the code you are about to
change**. Not which one *has* it. Which one *runs* it.

The two are different, and a copy that is present but inert looks identical to a copy that works. Search for the
guard that turns it off ("am I the authority", "is this a watcher", "is this the owner") and read what it skips.
A single early return can make a whole feature a picture of itself.

## Why — what it cost

A gap-closing skill fired too late. The moment that triggers it was raised in two places: on the player's own
machine before their swing, and on the server when the blow landed. The obvious fix went to the first one — it
was the one with the timing bug, and the fix was correct.

It changed nothing, because on a machine joined to a server that whole path is inert: a guard three calls deeper
returns early and the client resolves no part of any skill. Every effect it appeared to run was a picture the
server had decided. The comment saying so was sitting in the file the whole time.

That cost a build, a play-test, and a revert — a full round — and the real answer was one guard away from where
the reasoning started.

## How to apply it

1. **Ask which machine owns the behaviour before editing, not after it fails.** One grep for the authority guard
   answers it in seconds.
2. **Read what the guard skips.** "Returns early on a watcher" is not enough — find out whether it skips the
   *display* or the *decision*.
3. **When a fix does nothing, suspect the machine before suspecting the logic.** A correct fix on an inert copy
   and a wrong fix on the live one look exactly the same from the outside.
4. **Say which machine a change lands on** in the report, every time. It is the difference between "rebuild the
   server" and "nothing to do".
