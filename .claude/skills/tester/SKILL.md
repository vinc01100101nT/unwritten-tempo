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

- Entering the world and fighting in it. The door is proven; the room is not. As of 2026-09-03 a tester HAS
  reached the town, read the whole hotbar, pressed a bound skill and watched its cooldown start — so the room is
  reachable; fighting in it still is not.
- **The freeze itself.** Pausing play from the outside and capturing the composited screen while paused is what
  the hard rule below is built on, and NOBODY HAS DONE IT YET — the pause control and the screen capture both
  exist, but the two have never been used together. The FIRST tester to need the freeze must say plainly whether
  it worked, so this line can be moved up to the proven list or the rule rewritten around something that does.
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

## ⛔ ANYTHING SHORT-LIVED IS FROZEN, NEVER CHASED (hard rule — no exceptions)

**Your round trip to the editor takes 2 to 10 seconds. Most of what you are asked to look at lasts less than
that.** A buff runs 4 seconds. A cooldown ticks. Mana refills in 8. So "do the thing, then ask to look at it" is
two trips, and the second one arrives after the thing is over — every time, forever, however carefully you work.

**THE RULE: act and freeze in ONE call. Then look as long as you like.**

    one call:  cast the skill  →  immediately pause play
    then:      capture the screen, read the drawn text, read the values — unlimited trips, nothing moving

Paused, the clock stops and so does the thing you are watching. A 4-second window becomes an indefinite one.
Unpause when you are done with that moment.

**The three moves, in order of what they prove:**

1. **Read what the interface actually DRAWS** — the text on the tile, the number in the corner — not only the
   value behind it. "The buff exists in the data" and "the tile shows its letters" are DIFFERENT claims, and the
   second is usually the one being tested. Report them separately.
2. **Capture the composited screen** (the source that includes the on-screen interface) as the backstop.
3. **Read the underlying values** last, as corroboration.

**Make the window bigger when timing is not the question.** If the check is "does the icon appear at all", use a
long-lasting version of the effect rather than a 4-second one. Save the short one for when expiry itself is what
is being tested.

### THE LINE YOU MUST NOT WRITE

**Never report a short-lived thing as ABSENT when your look arrived late.** That is not an observation, it is a
missed measurement, and it reads to everyone else as a bug. The words are **"I looked too late — unmeasured"**.

*(Scar, 2026-09-03: a tester cast a 4-second shielding buff and asked for a picture afterwards. The picture came
back after the buff had gone, so the tile could not be confirmed. The USER, watching the same run in real time,
saw the icon perfectly well. The tester was honest about it — but the whole check was wasted, and the same
lateness had already produced a near-miss on mana: a 12-mana cost read as "mana unchanged" because the refill
takes 8 seconds and the read-back takes up to 10.)*

### The mandatory report line — this is what makes the rule impossible to skip

**Every report ends with a "Timed observations" list: every short-lived thing you looked at, and how you froze
it.** One line each — *what it was, how long it lasts, how it was frozen, what you read*. A timed thing missing
from that list, or listed with no freeze, is a FAILED observation and must be re-done or reported as unmeasured.
Empty list = say "none — nothing short-lived was observed".

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
5. **The rules above, verbatim** — including the freeze rule and its mandatory closing list — plus: never invent
   an identifier, a value, or a menu name; "I could not determine this" is a valid and preferred answer.
6. **Which checks are timed**, named in the brief itself. Whoever writes the brief already knows a buff lasts 4
   seconds and a cooldown 7; saying so turns the freeze rule from something the tester has to notice into
   something it was told.

## Finishing — not optional

**Leave Play mode**, then confirm the editor is back and the open scene is not dirtied. Any logging it added
lives only in that play session, but say in the report that it added some.

## What it hands back

- **What it actually did**, step by step, so the run can be repeated.
- **What it observed** — the real values, and screenshots where they carry the answer. For anything on screen,
  say separately what the interface DREW and what the value behind it was.
- **What it could NOT check, and why.** Never silently drop a checklist item; an unrun check reported as unrun
  is worth more than a guess.
- **The "Timed observations" list** — every short-lived thing looked at and how it was frozen (see the hard rule
  above). This is not optional and an empty list must be stated as empty.
- Plain language, no file paths, per the global rules — and never the word "verified" for anything it only
  reasoned about.
