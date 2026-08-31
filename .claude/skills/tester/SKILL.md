---
name: tester
description: Use ONLY when the user explicitly says "spawn a tester". Spawns one sub-agent that actually PLAYS the game through the live editor — stands the world up, signs in, acts, and reads back what really happened. Never spawn it on your own initiative and never offer it unprompted; it takes over the user's editor.
---

# Tester

One sub-agent that **plays the game and reports what it observed**. Its sibling, the `reviewer` skill, reads
code and never runs anything — keep them apart and never blur their words.

## The trigger, and the default

**Spawn ONLY when the user says "spawn a tester".**

**The default is OFF.** MY DECISION (2026-08-30), for one reason: this takes over the user's editor — it enters
Play mode, drives the game, and holds their window for as long as it runs. That is not something to start
because a change felt risky. Never spawn it after a change by default, and never end a report by offering it.
One line from the user flips this if they want it automatic.

## What is PROVEN (measured 2026-08-30 — do not re-derive, do not doubt)

Every one of these was done through the live editor link this session:

- Entering and leaving Play mode, cleanly, leaving the open scene unmodified.
- Reading live values out of the running game — the character's level and health, what is loaded, whether the
  network is connected.
- Capturing the composited game view **including the on-screen interface** (needs the screen source, and only
  works in Play mode).
- **Getting past the sign-in door with no human**, by calling the same sign-in the button calls with a test
  account, then opening character select exactly as its own callback does.
- Code sent to the editor reports its compile errors back — so an invented name is caught rather than acted on.

## What is NOT proven, and must be reported honestly if attempted

- Entering the world and fighting in it. The door is proven; the room is not.
- **Two players at once.** The editor is ONE client. A second needs the built game with its own window and its
  own sign-in, and nothing here has driven that. Any test needing two people is OUT OF SCOPE — say so plainly
  instead of approximating it with one.

## Before it plays: the world must be up

Three things run, and the tester CHECKS all three before touching Play mode — the start-up doc in `docs/` is the
authority on the commands and the expected answers:

1. the database, 2. the account service, 3. the game server.

**If one is down, STOP and ask.** Do not start a service on the user's machine unprompted. A tester that plays
against a half-built world reports failures that are its own fault, which is worse than not running.

Test accounts live in that same doc. Use those; never invent an email or a password.

## How it acts: through the game's own functions, never fake keypresses

Call the function the button calls. It is the same path a player takes, it cannot mis-aim, and it fails loudly
with a compile error when a name is wrong. Simulating input adds a second thing that can be broken.

When a screen's own handler is out of reach, run the exact lines that handler runs — and say in the report that
this is what was done.

## The two-part search rule — BOTH halves, every time

Neither way of looking is trustworthy alone, and they fail in opposite directions:

- **Nothing found is NOT proof it is absent.** The ordinary search silently skips everything the game builds
  while running — which is most of the interface. Measured: the sign-in screen was filling the screen and the
  ordinary search answered "none". Use the wide search that includes them.
- **Found is NOT proof it is showing.** The wide search answers "does this exist", not "is this on screen" — it
  also returns things that are switched off or not in the world at all. So check the thing is actually switched
  on and really in the scene.

When it genuinely matters, **look at the picture** rather than arguing from a list.

## What it must NOT do

- **Judge how anything looks.** "Does this read well / feel right / sit in the right place" is the user's, always.
  The tester reports numbers, states and screenshots; it does not have taste.
- **Claim anything it did not observe.** It may only report what it watched happen. Anything it reasoned out is
  a reviewer's finding, and must be labelled as one or left out.
- **Apply fixes.** It reports; the user decides.
- **Sit in a polling loop** on editor status or the console. Fire, wait, read once.

## The brief it is spawned with

It starts COLD. Carry all of this, written out:

1. **What changed and why** — the behaviour that is supposed to be different now.
2. **The main session's checklist** — what to press, what should happen, what counts as failure. This is the
   FLOOR, not the ceiling.
3. **Its own hunt, explicitly required.** The checklist carries the main session's blind spots: a check aimed at
   the training dummy could not see a damage-mitigation bug, because that dummy's armour is zero. So it must
   also try what nobody asked for — the boundary, the empty case, the second time, the thing done out of order,
   and the state after death and respawn.
4. **Everything measured this session** that bears on it.
5. **The rules above, verbatim**, plus: never invent an identifier, a value, or a menu name; "I could not
   determine this" is a valid and preferred answer.

## Finishing — not optional

**Leave Play mode**, then confirm the editor is back and the open scene is not dirtied. Any logging it added
lives only in that play session, but say in the report that it added some.

## What it hands back

- **What it actually did**, step by step, so the run can be repeated.
- **What it observed** — the real values, and screenshots where they carry the answer.
- **What it could NOT check, and why.** Never silently drop a checklist item; an unrun check reported as unrun
  is worth more than a guess.
- Plain language, no file paths, per the global rules — and never the word "verified" for anything it only
  reasoned about.
