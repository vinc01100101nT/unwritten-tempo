---
name: reviewer
description: Use ONLY when the user explicitly says "spawn a reviewer" (or asks in as many words for a reviewer on a change). Spawns one cold sub-agent that reads what changed and hunts for what is broken, citing every claim. NEVER spawn it on your own initiative, never after a change by default, and never offer it unprompted.
---

# Reviewer

One cold sub-agent reads a change and hunts for what is broken in it. It is a **reviewer**, not a tester — it
cannot run the game and has not watched anything happen.

## The trigger, and the default

**Spawn ONLY when the user says "spawn a reviewer"** — those words, or an unmistakable request for one on a
named change.

**The default is OFF, always.** Do not spawn one after making changes. Do not spawn one because a change felt
risky. Do not end a report by offering one. Silence from the user means NO, every time, on every change. If the
user has not said the words this turn, there is no reviewer.

## Why it is not called a tester (do not soften this)

Nothing on this machine can play the game. Play mode is the user's, and no agent has eyes on a screen. So a
reviewer's every finding is REASONING ABOUT CODE, and reasoning dressed as observation is the exact failure this
project keeps paying for — a note once claimed a running total "already works this way" on a client; it had
never worked, and the claim survived because it sounded watched.

So: the word "reviewer" is load-bearing. Never call its output a test result, never write that it "verified"
or "confirmed" anything, and when relaying it to the user, say the findings came from reading the code.

## Spawning it

One `Agent` call, `subagent_type: "general-purpose"`, `model: "opus"`. One reviewer, not several — the council
skill is the thing for several brains.

It starts COLD and inherits nothing. The brief must carry all of this, written out, not summarised:

1. **What changed** — the files touched this session and, in plain terms, what each change was meant to do.
2. **Why it was changed** — the bug or the ask behind it. A reviewer that does not know the intent cannot see
   an intent that was missed.
3. **Everything measured this session** that bears on it: values read, causes ruled out, the user's own words
   about the symptom.
4. **What the main session believes it verified, and HOW** — labelled exactly that way, as belief rather than
   fact. This is material to ATTACK, never the reviewer's scope. The main session's blind spots are inside its
   own checklist: a test aimed at the training dummy could not see a mitigation bug, because that dummy's
   armour is zero. Say what was checked so the reviewer can ask what that check could not see.
5. **The standing rules, verbatim** (they are not inherited):
   - Measure, never guess. Every claim about a cause, a value, or what code DOES carries a `file:line` opened
     THIS run. **No citation ⇒ delete the sentence, or go open the file.** There is no third option.
   - Never invent an identifier, path, menu string, or number. "The source does not say" is a valid answer and
     a preferred one.
   - Never widen a precise fact into a general one. Carry the source's exact scope and qualifiers.
   - An empty search result is not proof of absence — prove the search would have caught a known-present case.
6. **The hunt list** — where this codebase's bugs actually live, so a cold reader knows where to dig:
   - Every state a thing passes through, not just the first: spawn, live, death, pooled, respawn; mount and
     unmount. A change verified at spawn only is the classic miss here.
   - Both sides of any split: the player and a creature, the machine that decides and the machine that watches,
     the author-time path and the runtime one.
   - A number that crosses a boundary: is it the same number at both ends, or does something happen to it twice?
   - A value that is now authored but was not before: what do the assets saved BEFORE it say now?
   - Anything a client states that can move a number.

## What it must hand back

- Findings, worst first. Each one: what breaks, in plain words; the exact `file:line` it read; and the concrete
  case that triggers it — real inputs, real state, real wrong outcome.
- **A separate list of what it could NOT determine**, and why. This is not padding; an honest gap is worth more
  than a confident guess, and the gaps are where the next bug is.
- No fixes applied. A reviewer reports; the user decides what gets changed.

## Relaying it to the user

Findings are claims to check, not verdicts. Re-read the cited line yourself before repeating a finding as
true — a sub-agent can be wrong, and repeating it in your own voice makes it yours. Report in plain language
with no file paths, per the global rules, and say plainly that nothing was played.
