# Two Runs That Took Different Routes Are Not a Before and After

**Reach for this when:** something vanished right after your change, and you are about to say your change broke it.

## The rule

A comparison of "the run before my change" and "the run after it" only blames the change if both runs reached the
screen the same way. Before owning the break, read the live state of the thing that vanished — is it switched on,
is it running — and check how each run got there. The route (fresh start, returning from somewhere, reopened
window) is an input as real as the code.

## Why

2026-09-29: the character-creation preview's ink outline was too thick. The fix made the preview's picture follow
the box's real size. On the next run the outline was gone entirely, and the reply said "my fix made it worse" —
the change was the only code difference, so it looked proven. It was not. A live read showed the ink component's
object was switched OFF: the front screen turns off every world object while it is up, and the ink is one of
them. The first run had reached the creation screen by leaving the world; the second came straight from a fresh
start. The change had nothing to do with it. Cost: one full round, a debugger run, and a wrong statement to the
user.

## How to apply

1. Read the vanished thing's live state first: is its object active, is its component enabled, is it running.
   One read would have ended this at once.
2. Write down how each run reached the screen, from the log (the lines just before the screen opened).
3. Only when the runs took the same route and the thing is running but the result differs, bisect by setting
   your change aside ([Bisect by Removing Your Own Change](bisect-by-removing-your-own-change.md)).
