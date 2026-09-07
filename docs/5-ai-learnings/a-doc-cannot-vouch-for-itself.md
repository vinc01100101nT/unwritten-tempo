# A doc cannot vouch for its own freshness

**Reach for this when** you are about to repeat, to the user or into another doc, something a document states —
especially under a heading like *"still true"*, *"current"*, *"as of today"* or a `last-updated` date — **or when
a CODE COMMENT tells you the value of a setting that lives in another file.**

## The rule

**A doc's claim about the code is a claim, not a measurement.** Before repeating one, check it against the code.

**A code comment is a document.** It sits inside the source file, so reading it feels like reading the code — and
that is exactly why it is the more dangerous of the two. A comment describing a value that lives somewhere else
(a project setting, a config, another file's field) is the same unverified claim in a costume. Go open the file
that HOLDS the value.
Treat a freshness heading as the strongest possible warning sign, not as reassurance: somebody wrote it on the
day it was true, and nothing has re-checked it since. A `last-updated` line dates the *edit*, never the *claim* —
the two drift the moment anyone touches the file for another reason.

When asked to bring a doc up to date, **the job is every claim in it**, not the claims you happen to know about.
Grepping for the term you changed finds your own edits. It does not find the sentence that quietly stopped being
true a week ago.

## Why

2026-09-02. Asked to update the networking docs, I reconciled every claim touching the work I had just done, ran
a link check, and reported the docs current. I had read a section headed **"Deliberate limits, still true"** in
the same sitting and passed straight over it.

Four of its bullets had been false since the previous day: a skill's damage was the client's word, a heal's size
was the client's word, a player's own buffs did not protect them, and "resolve skills on the server" was the next
piece of work. All four had been settled on 2026-09-01 — the whole skill had moved to the server, and a memory
written that same day said so.

The cost: I repeated all four to the user as the state of their own game, listed the finished work as a
*candidate for what to do next*, and committed the stale section while telling them the docs were up to date.
The user caught it with one question — *"aren't they already in the server?"* — which is the cheapest possible
way to find out and the most expensive place to be found wrong.

2026-09-07, the same rot in a code comment. Asked which settings could be turned down without losing anything
visible, I read a comment on the torch light saying *"Additional-light shadows are OFF in the URP asset, so this
never adds a shadow pass"* and told the user that nothing in the game used lamp shadows, so the whole shadow
buffer could be freed for nothing. The setting said the opposite, and a whole system sat on top of it: after
dark, the few lamps nearest the player are promoted to real shadow casters, and the billboard characters turn
their own shadows to face whichever one is casting.

The user asked me to make the change. I had to stop and retract a recommendation I had by then given twice. The
comment was two lines from the code it described and had been wrong for as long as anyone had been reading it.

## How to apply

- When updating a doc, **list every factual claim it makes about the code**, then check each one. Not the diff;
  the document.
- **A comment that names a setting is a pointer, not an answer.** Open the settings file and read the value. It
  costs one grep, and it is the difference between a recommendation and a retraction.
- Give a **freshness heading no weight at all.** "Still true" is the sentence most likely to be stale, because
  it is written once and never revisited.
- **Search the project's memory for the subject first.** The correction was sitting in a memory file the whole
  time, written by the session that made the change.
- Before repeating a doc's claim to the user, ask: *did I read this in the code, or in a document?* If it was a
  document, say so, or go and read the code.
- When you find one stale claim, **assume there are more in the same section** — they were written in the same
  sitting, by the same person, on the same day.
