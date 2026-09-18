# A Name Is Not an Address

## Reach for this when

A log line, an error or a probe names an object — and that name makes you believe something about WHERE it came
from: another module, another world, another repo, somebody else's code.

## The rule

**A bare name tells you what a thing is called, never which container it lives in.** Before you build a theory on
"that belongs to X", find out where the naming tool actually looked. Most of them look everywhere and print only
the name. And when you write a diagnostic yourself, print the container beside the name — the scene, the module,
the file, the database — because the reader cannot add it back afterwards.

## Why

2026-09-18. A tester playing a map called `field_11` could not walk, and the game's own stall message said
*"Nearest solid: Tavern_LOD0"*. A tavern is a TOWN building. That single name produced an entire wrong theory —
the town's collision had leaked into the field — which reached the bug list, then a debugger's brief, then a round
of investigation. It was wrong twice over: every field in that game carries its own tavern, and the probe behind
the message searched every loaded map with no filter at all, so the name could never have meant what it was read
to mean. The server's log had the player in the right map the whole time.

Cost: one wrong entry in a bug list, one wrong hypothesis in a debugger brief, and most of a debugging round to
undo both.

## How to apply

1. **Read the tool that printed the name** before believing where it came from. One look at the query behind the
   message settles it: what did it search, and did it filter?
2. **Ask whether the name is unique.** "A tavern" in one place is evidence; a tavern in all seventeen maps is not.
   Grep the name across the whole project and count the hits before it goes in a sentence.
3. **When you write the diagnostic, print the address.** Name plus container plus the one attribute that decides
   the question — `Ground_Floor [field_11, layer Ground, box 42x1x42]` answers in one line what a bare
   `Ground_Floor` cannot answer at all.
4. **In the report, quote the line and say what it does NOT prove.** "The message names a tavern; that probe
   reports the nearest solid thing in any loaded map, so it is not evidence about which map."
