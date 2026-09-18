# Teach the Harness the Question

## Reach for this when

You can DRIVE a running system from outside — a test client, a CLI, a harness, a REPL — but it cannot answer the
one question the bug turns on, and you are about to theorise instead.

## The rule

**Add the question to the driver you already have, as a general capability, not a one-off print.** You are not
adding scaffolding to be ripped out; you are adding the instrument the next investigation will also need. It is
usually one small command, and it turns "I could not determine this" into an answer the same hour.

## Why

2026-09-18. A player fell out of the world whenever they arrived at a map's centre. Everything readable from
outside was read: the database row (falling), the server's log (a speed cap tripping every 31.5 s), the saved map
files (a floor collider covering the spot, switched on, identical to a map that worked). A whole debugging run
ended honestly with *"what defeats the floor there, I could not determine — the test that would tell you is to ask
the physics engine what is under the player, and I have no way to run code inside the shipped client."*

The system was already driveable: a text client answering commands. Adding one command to it — *what is under my
feet, and what is touching me* — answered the question in three calls: at the map's centre, nothing below for 500
units; at the same map's doorway, the floor right there; walking in, blocked a metre and a half short by the
scenery that occupied the spot. The cause had been invisible to every file on disk because it was a placement
problem, not a data problem.

Cost before: one full debugging run (33 minutes of agent time) that ended without the answer, plus two earlier
rounds of theories. Cost after: one command, one rebuild, three questions.

## How to apply

1. **Name the question as a sentence** — "what is under the player", "which rows did that query actually match",
   "what is in this cache right now".
2. **Put it where the driver already reaches**, next to its existing commands, and make it general: the answer
   should name things with their container and their type, not just the one value you want today.
3. **Keep it.** A probe that earned its place is tooling; say so in the report and document it beside the other
   commands. Delete it only if it decides something (it must READ, never compute or change).
4. **Then re-run the investigation from the top** — the new answer usually kills two theories at once.
