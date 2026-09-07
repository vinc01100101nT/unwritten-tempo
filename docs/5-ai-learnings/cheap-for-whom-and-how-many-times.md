---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# Cheap for Whom, and How Many Times

## Reach for this when

You timed something, it came out fast, and you are about to tell the user it is free — or you are about to
explain WHY it is affordable.

## The rule

A timing is one number, and on its own it settles nothing. Before calling anything cheap, answer two more
questions and MEASURE both: **which machine pays it**, and **how many times per second, multiplied by what**.
A cost paid once per player on that player's own machine and a cost paid per player on one shared server are
the same nanoseconds and completely different decisions. And never volunteer a REASON the cost is affordable
unless that reason is itself measured — an unmeasured justification travels attached to a measured number and
inherits its credibility.

## Why

A 150-nanosecond, zero-allocation check was measured properly: 200,000 real runs, timed in the live editor,
with a control loop proving the timing came from compiled code. All of that was true. Then it was handed to
the user with an invented explanation — that it was affordable "because you and the server are on one
machine" — which had never been measured and was wrong. The check runs entirely on the player's own client,
for their own character, and never runs on the server at all. The user had to stop and ask whether the
recommendation only held on the developer's own machine. The measurement was sound; the sentence next to it
was not, and it put the whole recommendation in doubt.

## How to apply

Before writing the words "cheap", "free", "negligible", or any sentence explaining why a cost is acceptable:

1. **Find the machine.** Open the thing that owns the code and check which side runs it — a client-only rig,
   a server-authoritative component, or both. Do not infer it from the class name.
2. **Find the multiplier.** Per frame? Per player? Per creature? Per player × per creature? Write the product
   out, with the real rates, not "per tick".
3. **Compare against the real budget** — one frame at the real frame rate, not a round number.
4. **State all three together**: the timing, the machine, the multiplier. If any is unmeasured, say which.
