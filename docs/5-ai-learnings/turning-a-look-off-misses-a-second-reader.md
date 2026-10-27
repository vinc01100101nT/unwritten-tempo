# Turning a Look Off Leaves It On Somewhere Else

**Reach for this when** a look, effect or feature was reported "off everywhere", and the user still sees it on one screen.

## The rule

Before reporting a feature off, search for every reader of each value the feature owns — not only its master switch.
A second surface that reads a sub-value directly (the strength, the colour) bypasses the switch. Route that reader
through one value the switch also controls, so a single switch truly turns it off everywhere.

## Why

2026-10-26: the hand-drawn look was turned off by setting its master strength to 0, and reported "off everywhere".
The sign-in, character-select and loading screens drew the same paper grain through their own overlay, which read the
grain strength directly and never looked at the master. The user found the grain on the sign-in screen in the next
session; it cost a round and the trust of a "fixed" report.

## How to apply

1. Search the code for every field and constant the feature owns (its strength, colour, texture), not only its name.
2. For each hit outside the feature, ask: does it pass through the master switch? If not, make it read one value
   that does (here: grain times the master strength).
3. Watch it off on every screen that drew it — sign-in, character select, loading, in-world — before reporting.
