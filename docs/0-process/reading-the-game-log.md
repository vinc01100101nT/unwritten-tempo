---
status:
owner: vince
last-updated: 2026-09-04
tags:
  - process
---

# Reading the Game Log

**The standing way to find out what the game actually did.** Every meaningful moment writes one line, each line
belongs to a named category, and you switch categories on and off to decide what you are told. Use it whenever
you are play-testing, hunting a bug, or driving the game through a tester.

It exists because of one measured failure: a play-test's server log held **1205 routine head-count lines against
53 that said anything**, and there was no way to turn the head-count down. Everything that mattered was in there
and nobody could see it.

---

## The categories

Seven of these already existed as hand-typed labels and are **on** by default. Five are new, answer the questions
a play-test actually asks, and are **off** by default — they cost nothing until you ask for them.

| Category | What it writes | Default |
| --- | --- | --- |
| `unwritten` | Start-up and general engine-side notes | on |
| `server` | The machine that owns the world, talking about itself | on |
| `net` | Messages crossing between machines | on |
| `client` | A player's own machine | on |
| `map` | Maps loading, handing over, being held | on |
| `spawn` | Something being created in the world | on |
| `census` | The routine head-count, twice a second — **the one that buries everything** | on |
| `hit` | A blow landing, or being refused, and why | **off** |
| `passive` | A passive skill firing, naming which and which moment | **off** |
| `buff` | A buff arriving or growing, with its stacks, source and duration | **off** |
| `cast` | A cast accepted, or refused with which of six reasons | **off** |
| `command` | Every command run | **off** |

---

## Switching them

There are two logs — **the server's** and **your own machine's** — and they are switched separately, because
they answer different halves of the question. Your machine knows what it drew; only the server knows what really
happened.

**The SERVER's log — the command, in the in-game chat window. GM only.**

```
/log                      show every category and whether it is on, on the server
/log hit on               switch one on
/log census off           silence the head-count
/log all on               everything
/log all off              quiet — warnings and errors still print
/log reset                back to the defaults above
```

This command is declared as the server's, so it is carried to the server and run there, and its answer comes back
into the same chat log. **That is the point of it:** hits, passives and buffs are decided on the server and
announced only in the server's log, so a switch that could not reach the server left the categories that matter
unreachable. A play-test hit exactly that and could not tell whether a passive had fired.

**YOUR OWN machine's log — from code.** A tester does this, and it needs no chat window and cannot mis-type:

```csharp
GameLog.SetAll(true);
GameLog.Set(LogCat.Hit, true);
GameLog.Set(LogCat.Census, false);
```

A tester normally wants **both**: the command for the server, the code for its own machine.

---

## Three rules that make it trustworthy

**A warning or an error ALWAYS prints, whatever the switches say.** Categories decide how much *routine* detail
you get; they never hide a problem. Silencing a real fault to tidy a log is the failure this whole system exists
to prevent — a play-test once reported a clean console while a red error sat at the top of it.

**An off category costs nothing.** The check is one bit test. But a caller that builds a string has already paid
for it, and some of these sit in the per-hit path, so **every caller that builds a string sits behind the check**:

```csharp
if (GameLog.On(LogCat.Hit)) GameLog.Info(LogCat.Hit, string.Concat(name, " hit for ", dmg.ToString()));
```

A bare literal needs no guard.

**The log announces what it is not telling you.** The server prints the categories that are switched off and how
to switch them on — **when the world comes up, and again every time somebody enters it**.

The arrival is the one that matters. A start-up line sits at the *beginning* of the log forever, which is hours
and tens of thousands of routine lines away from wherever anybody is actually reading, and it does not survive a
cleared console. Every tester enters the world, so that is the moment the notice lands at the tail of the log
right as they start reading it.

The line is generated from the category list itself, so a category added later appears in it with nobody updating
anything. It is not itself switchable — `/log all off` still prints it, because it is the thing that tells you
how to switch the others back on.

---

## Reading the server's log file

The server's log is the only window into the machine that owns the world — the live editor link cannot reach it.

Record its length before an action, then read only what was added:

```bash
before=$(wc -c < <logfile>)
# … do the thing …
tail -c +$before <logfile>
```

Without that, the head-count buries whatever you were looking for — which is the original problem, in the file
rather than on the screen.

---

## What the log answers that nothing else can

Three questions that cost real time before this existed, each now one category away:

- **"Did that blow land?"** A player's own machine reads **zero damage for every blow it throws** — always, every
  weapon, by design, because the machine forwards the blow and is told the answer later. Three separate
  play-tests read that zero and reported the game broken when it was working. The `hit` category is the honest
  answer, and it distinguishes a miss from a full absorb from a hit that dealt nothing.
- **"Why did nothing happen when I pressed it?"** There are six different ways a cast is refused and they used to
  look identical from outside. The `cast` category names which one.
- **"Did that passive fire?"** Every check before a passive runs is a reason NOT to run it. The `passive`
  category writes the moment one actually fires.

---

## Adding a category

One entry in the category list, one line in the word table beside it, and the call sites that write it. The
announcement and the `/log` command both read that list, so neither needs touching.

Name it for **one kind of thing that happens** — never an umbrella. `hit`, not `combat`.

---

## Related

- [The Three-Brain Design Process](design-process.md) — the workflow this sits inside.
- [Put the Recorder Inside — Don't Stop the World](../5-ai-learnings/put-the-recorder-inside-dont-stop-the-world.md)
  — why a log written from inside beats pausing the game to read it.
- [Starting the Network](../1-planning/starting-the-network.md) — standing the world up, and where the server's
  log goes.
