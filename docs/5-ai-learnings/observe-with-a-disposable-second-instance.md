---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - ai-learnings
---

# 🔬 Observe With a Disposable Second Instance

**Reach for this when:** something is already running that you need to measure, and you cannot see into it — its output goes somewhere you cannot read, or stopping it would cost someone their session.

> When the thing you need to measure is already running and must not be disturbed, do not reason around it.
> Start a **second, throwaway copy** — its own port, its own output file — measure, then kill it.

## The rule

A running service you cannot see into is not a reason to guess. It is a reason to start another one.

Give the copy its own address so it cannot collide with the live one, send its output to a **file** rather than a
terminal, take the reading, and shut it down in the same turn.

## Why — what it cost, and what it saved

A whole debugging session hung on one question: was the game server populating a map at all, or was the map
genuinely empty? The live server was writing to a terminal nobody could read back, and stopping it would have
ended the user's session.

A throwaway copy on a spare port answered it in about forty seconds. It printed both maps loading and a steady
population count — the map was full. Every explanation built before that measurement had been aimed at the wrong
half of the problem, and one of them was about to be acted on.

The measurement cost one command. The reasoning it replaced had already cost a round.

## How to apply it

- Find how the process is addressed **before** launching — a port, a socket, a data directory. Usually one
  environment variable is enough to move it out of the way.
- Always redirect to a log **file**. A service whose output goes to someone's terminal is a service you cannot
  measure.
- Prefer the copy when the user may be mid-session. Take over the real one only when you need its actual state —
  its data, its connections, the very thing that is wrong with it.
- Kill it in the same turn, and say you did.
- Whenever you quote a number afterwards, say **which instance** it came from.

## The shape of the mistake it prevents

"I can't see it, so I'll work out what it must be doing." That sentence is the failure. The copy exists precisely
so the sentence is never needed.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) — the other half of "look, don't assume".
- [Starting the Network](../1-planning/starting-the-network.md) — the ports and environment settings a second copy needs.
