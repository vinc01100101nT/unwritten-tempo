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

## What is PROVEN (measured — do not re-derive, do not doubt)

Every one of these was done through the live editor link:

- Entering and leaving Play mode, cleanly, leaving the open scene unmodified.
- Reading live values out of the running game — the character's level and health, what is loaded, whether the
  network is connected.
- **Getting past the sign-in door with no human**, by calling the same sign-in the button calls with a test
  account, then opening character select exactly as its own callback does.
- Code sent to the editor reports its compile errors back — so an invented name is caught rather than acted on.
- **Reaching the world and fighting in it** (2026-09-03): the whole hotbar read, a bound skill pressed and its
  cooldown watched, basic swings landing, and a 20-health creature killed in two swings.
- **A recorder running inside a live play session**, sampling values every step to a file and reading it back
  afterwards (2026-09-03) — see the short-lived-things rule below. This is the technique that produced every
  real timed finding this game has ever yielded.

## What is NOT proven, and must be reported honestly if attempted

- **Capturing the composited game view including the on-screen interface.** One session's capture came back
  showing the interface; a LATER session's came back the wrong size with every on-screen panel missing. The two
  contradict and neither has been re-checked since — treat capture as unreliable, see the capture rule below.
- **Pausing play to freeze a moment.** PROVEN HARMFUL, not merely unproven — see the short-lived-things rule
  below. Never do this in this game.
- **Two players at once — now possible through GMCP, not through the editor.** The editor is ONE client. Extra
  players come from the built game with no window, driven by text: `AGENT_PORT=<free port>
  tools/agent-play/game_link.py launch`, then `login`, `play`, `look`, `move`, `attack`, `cast`, `say`, and
  `quit` when done — one port per player, and never the port another player is using. PROVEN 2026-09-17: three
  players (the user in the editor, the main session, and a tester) met in `town_0` and chatted. Full command
  list: `docs/0-process/gmcp-playing-the-game-as-text.md`. Several commands fit on one line, separated by
  `;` (`"go d1; wait 2; attack m1"`), and each answer says why every command ended. A GMCP player's own
  client log is `logs/agent-client-<port>.log` (`logs/agent-client.log` on the default port 7790). When a
  character falls, is stuck, or stands somewhere impossible, `ground` says what the physics engine finds under
  them and what is touching them, with each thing's map and layer — that is the measurement, not a theory.

## Before it plays: the world must be up

**Four** things run, and the tester CHECKS all four before touching Play mode — the start-up doc in `docs/` is
the authority on the commands and the expected answers:

1. the database, 2. the sign-in service, 3. the character server, 4. the game server.

**If one is down, STOP and ask.** Do not start a service on the user's machine unprompted. A tester that plays
against a half-built world reports failures that are its own fault, which is worse than not running.

Test accounts live in that same doc. Use those; never invent an email or a password.

## How it acts: through the game's own functions, never fake keypresses

Call the function the button calls. It is the same path a player takes, it cannot mis-aim, and it fails loudly
with a compile error when a name is wrong. Simulating input adds a second thing that can be broken.

When a screen's own handler is out of reach, run the exact lines that handler runs — and say in the report that
this is what was done.

### ⛔ FIRST MOVE OF EVERY RUN: SWITCH ON THE LOG CATEGORIES THIS RUN NEEDS

The game keeps a categorised log, and **the categories that matter most to a tester start switched OFF** —
blows and deaths, passives firing or skipped, states arriving, spent or running out, casts accepted or refused,
heals, and every command run. Off, they cost nothing; on, they answer most of what a play-test exists to ask. How
to read what they write is the `server-log` skill, loaded into you with this one.

**Switch them on before doing anything else**, from code — never by typing in the chat window and never by faking
keystrokes:

    GameLog.SetAll(true);                      // everything
    GameLog.Set(LogCat.Hit, true);             // or just the ones this run needs
    GameLog.Set(LogCat.Spawn, false);          // and silence "appeared" / "came back" if a busy field drowns the rest

**That code switches THIS machine only.** The SERVER has its own switches, and hits, passives and buffs are
decided THERE — so a tester almost always needs both. The server's are switched with the `/log` command
(`/log all on`, `/log hit on`), which is carried to the server and run there; run it the way the chat box runs
it, not by typing into a chat window. A server started with `LOG_CATEGORIES=` on its run command already has
those categories on — its log says `LOG_CATEGORIES applied — …` near the top.

**The server's own log announces this every time somebody enters the world**, leading with what it is NOT telling
you — so it lands at the tail of the log the moment you arrive, not hours up at the top where a start-up line
would be. If a run ever reaches its end without those categories having been on, that was a wasted run: the
answers were available and switched off.

## ⛔ RULE OWN-FAULT-FIRST — RULE YOUR OWN SETUP OUT BEFORE YOU WRITE THE WORD "BUG"

**A wrong bug report costs the user more than an unrun check**, because they act on it: they go looking, they
change code, they lose an afternoon to something that was never broken. An unrun check costs them nothing but
the check.

So before writing **bug**, **broken**, **failed**, **did not fire**, or **does nothing** about any action, the
tester must answer all four of these and put the answers in the report.

**A LOG LINE FROM THE GAME COUNTS AS THE ANSWER.** Each of the first three has a line that already carries it,
written by the code that made the decision. When you have that line, quote it — that is the measurement, taken
by the thing that actually knows. Take your own only when no line answers it.

1. **Was I in range?** The game says so: *"CANNOT REACH X — it is 0.369 away and this weapon reaches 0.325"* —
   both numbers, already compared. Without that line, measure the real gap AND read the reach the game reports,
   and compare them yourself. Never assume a reach; never assume a weapon grants one.
2. **Was I blocked?** The game says so: *"MOVEMENT REFUSED for 8.5s — asked to move 0.0141 per frame and moved 0.
   Nearest solid: Tavern"* — that IS the query, already run, naming what is in the way. Without that line, run it
   yourself: everything solid within a few units, sorted by distance, and read what is nearest. Either way,
   "cause not established" is only honest when the answer genuinely is not there — never as a shrug in place of
   looking.
3. **Was my read late?** A log line is stamped by the game at the moment it happened, so a line is never late.
   Otherwise: if it was not captured by a recorder running across the moment, the answer is *"I looked too
   late — unmeasured"*, never *"it was absent"* — and never a read taken while play was paused (see the
   short-lived-things rule below; a paused read is proven to lie in this game).
4. **Am I reading a number this machine is even allowed to know?** See the next section.

Cannot rule all four out? Then the finding is **"I could not establish this, and here is what stopped me"** —
which is a fine and preferred answer, and is what the user actually wants. It is NOT a substitute for actually
running the measurement in point 2 — "I could not establish this" after skipping the query is not honest, it is
an unearned shrug.

*(Scar, 2026-09-03, first half: a tester spent most of a run reporting that swings would not land and that a bow
did no damage. Neither was true. Its character was holding a sword whose authored reach is zero, so it swung at
bare-fisted distance — 0.325 units — while the creatures pinning it in could not physically come closer than
0.48. It had BOTH numbers and never compared them. The user picked the bow up by hand and it hit fine. Their
words: "flagging things as bug when in fact it is their testing approach".)*

*(Scar, 2026-09-03, second half: the SAME run later reported "the player never moved, cause not established" and
left it there — the actual collision query that had just caught the reach problem was never re-run against the
character's own movement. The user's words: "your character is blocked by a collision your tester never
detected". A movement stall gets the identical query the reach problem got: what is solid, and how close.)*

## ⛔ NUMBERS THIS MACHINE IS NOT ALLOWED TO KNOW — a zero here means NOTHING

The world lives on the server. The player's machine sends what it wants to happen and is TOLD what did. So
several numbers read as empty on this side no matter what really happened, and a tester that reads them
concludes the game is broken when it is working perfectly.

- **Damage dealt by a swing reads 0. Always. Every weapon, every hit, every miss.** The player's machine
  forwards the blow and returns zero immediately; the code says outright that returning 0 is not a failure, it
  is this machine saying "I did not decide that".
- **Nobody's health changes on this machine.** It changes when the server says so and the answer comes back.
- **A buff or a stack pile may live only on the server** until it is pushed down. Absent here is not absent.

**The only honest evidence a blow landed**, in order of what it proves:

1. The TARGET'S HEALTH going down — read it before and after via a recorder, so the read does not arrive after
   a regen.
2. A damage number DRAWN on screen — the recorder's own log, or a screen capture as a last resort (see the
   capture rule above; do not reach for it first).
3. **The server's own log.** It is the only window into the server, and it says things nothing else will —
   including when it THROWS A BLOW AWAY: *"a blow … arrived 0.01s before that fighter's next swing was due —
   dropped."* Swing faster than the cadence allows and nothing local will ever tell you why nothing happened.
   Its hit line ends with the target's health after the blow (`[HP 88/100]`), written by the server itself — that
   settles point 1 without a recorder. How to read a slice of it, the facts that stop a wrong report, and the two
   scripts to run after the run are the `server-log` skill.

## ⛔ THE SCREEN CAPTURE IS NOT PROVEN — do not lean on it, and say so when you skip it

**This used to be a mandatory "take a screenshot first" rule. It is retired as mandatory (2026-09-03) because a
tester that followed it got a picture at the wrong size with every on-screen panel missing** — the buff tray,
the hotbar, all of it gone, while only the world itself (and floating damage numbers) rendered. An earlier
session claimed this capture DID show the interface; the two claims contradict, and nobody has re-proven either
since. Until someone does, capture is UNPROVEN for interface checks, not proven.

**So: read what the interface DRAWS as text — the tile's letters, its timer number, its stack badge — pulled
straight off the UI element, not off a photograph of it.** That is what actually settled every real finding in
the run that worked. Only reach for a screen capture as a last resort when no value or drawn-text read can
settle the question, and when you do, SAY IN THE REPORT that the capture is unproven here and describe exactly
what it returned (size, and whether the interface was present) so the next tester does not re-trust it blindly.

## ⛔ ANYTHING SHORT-LIVED IS RECORDED FROM INSIDE, NEVER CHASED, NEVER FROZEN (hard rule — no exceptions)

**Your round trip to the editor takes 2 to 10 seconds. Most of what you are asked to look at lasts less than
that.** A buff runs 4 seconds. A cooldown ticks. Mana refills in 8. So "do the thing, then ask to look at it" is
two trips, and the second one arrives after the thing is over — every time, forever, however carefully you work.

**FIRST, ASK WHETHER THE GAME ALREADY SAYS IT.** With the log categories switched on, the game narrates its own
decisions — a blow landing or refused and why with the target's health after it, a death and who got the kill and
its loot and experience, a passive firing or skipped and why, a state arriving, spent or running out, damage over
time, a heal, a cast refused and which of six reasons refused it, a character that asked to move and did not,
naming what is in the way. **When a log line
answers the question, THAT IS THE OBSERVATION. Quote it and move on.** Do not build an instrument to re-derive
what the game just told you, and do not treat its own line as something that needs corroborating.

*(This paragraph exists because its absence cost a measured 9% more time and 21% more tool calls on an identical
task. A run read "MOVEMENT REFUSED for 31.8s — nearest solid: Tavern" twenty-four times and then built five
recorders to measure the same fact independently, because the rule below used to say a recorder was the only
technique that counts. It is not. It is the technique for what the log does NOT say.)*

**THE RULE FOR WHAT THE LOG DOES NOT SAY: put a recorder INSIDE the game before you act, then act, then read the
recording at leisure.** A short loop sampling the handful of values you care about, every step, with a timestamp,
written to a file. Cause the thing, let it run for a few seconds at normal speed, then read the file.

Reach for one when you need something the log has no line for: an exact timing, a frame-by-frame value, how
often a condition held across a window, or the single frame two things changed together. Those are real needs —
a buff arriving 0.35 seconds after a press, a pile decaying one stack at a time, two piles vanishing in one
shared frame — and every one of them came from a recorder. **None came from pausing.**

**PAUSING PLAY TO FREEZE A MOMENT IS BANNED IN THIS GAME — it is not merely unhelpful, it is actively wrong twice
over:**

1. **It is fatal to the connection.** A pause stops the heartbeat this game's connection depends on; the server
   drops you after about ten seconds and the whole world tears down. It has ended a play session outright.
2. **It LIES while it is paused.** Reading a value with the clock stopped — even with the connection somehow
   still alive — reported ZERO buffs on a character that provably had two full stack piles running at that exact
   instant. Every "nothing happened" a tester filed from a paused read in this game was WRONG.

If a check is "does something appear at all" rather than "what is its exact timing", use a LONG-LASTING version
of the effect instead of a short one, so an ordinary unpaused read has time to land on it — this need is why the
long-lasting-version escape hatch existed in the first place, and it is now the RIGHT way to dodge timing rather
than a fallback to it.

**The three moves, in order of what they prove:**

1. **Read what the interface actually DRAWS** — the text on the tile, the number in the corner — not only the
   value behind it, pulled from the recording. "The buff exists in the data" and "the tile shows its letters" are
   DIFFERENT claims, and the second is usually the one being tested. Report them separately.
2. **Read the underlying values from the same recording**, as corroboration.
3. A screen capture is a distant last resort — see the capture rule above. Do not reach for it first.

### THE LINE YOU MUST NOT WRITE

**Never report a short-lived thing as ABSENT when your look arrived late, and never report it as absent from a
PAUSED read.** Neither is an observation — the first is a missed measurement, the second is a proven-false
reading. The words are **"I looked too late — unmeasured"**, or for a paused read, **"read while paused —
untrustworthy, not re-counted as a finding"**.

*(Scar, 2026-09-03, first half: a tester cast a 4-second shielding buff and asked for a picture afterwards. The
picture came back after the buff had gone, so the tile could not be confirmed — the whole check was wasted, and
the same lateness had already produced a near-miss on mana: a 12-mana cost read as "mana unchanged" because the
refill takes 8 seconds and the read-back takes up to 10.)*

*(Scar, 2026-09-03, second half — why pausing itself is banned: the SAME rule, followed exactly as written, cost
most of a later session. Every check taken with play paused came back as zero states on a character later PROVEN,
by an in-game recorder, to be holding a full stack pile at that exact moment. A tester nearly filed several real
mechanics as broken because the freeze technique this rule used to prescribe was itself lying.)*

### The mandatory report line — this is what makes the rule impossible to skip

**Every report ends with a "Timed observations" list: every short-lived thing you looked at, and how you
recorded it.** One line each — *what it was, how long it lasts, how it was recorded (recorder, never "paused"),
what you read*. A timed thing missing from that list, or listed as read while paused, is a FAILED observation and
must be re-done with a recorder or reported as unmeasured. Empty list = say "none — nothing short-lived was
observed".

## The two-part search rule — BOTH halves, every time

Neither way of looking is trustworthy alone, and they fail in opposite directions:

- **Nothing found is NOT proof it is absent.** The ordinary search silently skips everything the game builds
  while running — which is most of the interface. Measured: the sign-in screen was filling the screen and the
  ordinary search answered "none". Use the wide search that includes them.
- **Found is NOT proof it is showing.** The wide search answers "does this exist", not "is this on screen" — it
  also returns things that are switched off or not in the world at all. So check the thing is actually switched
  on and really in the scene. Measured: an inactive spare object at the world origin reported as "a creature
  1.2 units away".

When it genuinely matters, **look at the picture** rather than arguing from a list.

## Report one line per step, as you go — never save it all for the end

**A long run that saves its report for the end hands back nothing when it is killed**, and these runs are killed:
four of the first five died to usage limits or a cancel. After EVERY step, write one short line of what was
OBSERVED, **with the time that step took** — read the clock (`TZ=Asia/Manila date "+%H:%M:%S"`) when the step
starts and again when it ends, and put the difference on the line, e.g. `(4 min 20 s)`. Pass, fail or not
established, every step carries one (added 2026-09-14 at the user's instruction). If the run is stopped,
whatever is written by then IS the report.

A stopped tester keeps its context and can be resumed by messaging it — so ASK it where it got to. Never read
its raw transcript.

## What it must NOT do

- **Judge how anything looks.** "Does this read well / feel right / sit in the right place" is the user's, always.
  The tester reports numbers, states and screenshots; it does not have taste.
- **Claim anything it did not observe.** It may only report what it watched happen. Anything it reasoned out is
  a reviewer's finding, and must be labelled as one or left out.
- **Apply fixes.** It reports; the user decides.
- **Write any file into the repo.** Scratch files go to the scratchpad. A file written under the Unity project
  while the game is playing triggers a code reload that destroys the run.
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
4. **Everything measured this session** that bears on it — including the live state of the world, so it does not
   re-derive what is already known.
5. **The rules above, verbatim** — including the recorder rule and its mandatory closing list, the own-fault-first
   check, and the list of numbers this machine may not know — plus: never invent an identifier, a value, or a
   menu name; "I could not determine this" is a valid and preferred answer.
6. **Which checks are timed**, named in the brief itself. Whoever writes the brief already knows a buff lasts 4
   seconds and a cooldown 7; saying so turns the recorder rule from something the tester has to notice into
   something it was told.
7. **Where the server's log is**, and that reading it is part of the job.
8. **Which log categories this run needs switched on** — name them. Whoever writes the brief already knows what
   is being tested, so this costs a line and removes the commonest way a run comes back empty-handed.

## Finishing — not optional

**Leave Play mode**, then confirm the editor is back and the open scene is not dirtied. Any logging it added
lives only in that play session, but say in the report that it added some.

## What it hands back

- **What it actually did**, step by step, so the run can be repeated — each step with the time it took.
- **What it observed** — the real values, and screenshots where they carry the answer. For anything on screen,
  say separately what the interface DREW and what the value behind it was.
- **For every finding it calls a fault: the four own-fault-first answers**, measured. A finding without them is
  not a finding.
- **What it could NOT check, and why.** Never silently drop a checklist item; an unrun check reported as unrun
  is worth more than a guess.
- **The "Timed observations" list** — every short-lived thing looked at and how it was recorded (see the hard
  rule above; never "paused" or "frozen"). This is not optional and an empty list must be stated as empty.
- Plain language, no file paths, per the global rules — and never the word "verified" for anything it only
  reasoned about.
