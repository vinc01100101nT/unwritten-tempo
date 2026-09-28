---
status:
owner: vince
last-updated: 2026-09-28
tags:
  - ai-learnings
---

# A Replay of One Piece Passed, and the Real Flow Still Failed

**Reach for this when:** you proved a fix by building the pieces by hand and calling them directly — and you are
about to report it fixed without running the real sequence a user goes through.

## The rule

A replay you assemble yourself contains only the state you thought to put in it. The real flow also carries state
nobody mentioned: objects switched off while waiting for something, things that arrive a frame later, a screen
that is not up yet. Before reporting a fix, drive the REAL path from its first step to the thing the user
looks at, with a tool that plays it the way a person does, and read what the program itself wrote down.

## Why

2026-09-26: a character's chosen body and colours did not show in the game. I rebuilt the colour logic by hand
in the editor, with two bodies bound to one character, found the colours being erased, fixed that, and watched
the hand-built replay pass three cases. Reported at 80%. The user tested it and saw the default body anyway.

What the replay could not contain: the game signs you in while the whole player layer is switched off, waiting
for the server to let you in, and the body swap ran at that moment, found no player, and did nothing, with nothing
written anywhere to say so. One run through the text client (`game_launch`, `login`, `play`) plus a log line at
the swap showed it in two minutes. It cost a full round and a client build.

The first corrected attempt then showed the second half of this lesson. It loaded the save later, the real run
showed the character server throwing out the connection that had just come in, and that change was reverted
before it reached the user. A hand-built replay would not have caught that either.

2026-09-27, the same failure in arithmetic: rain lakes were checked with each function called on its own and with a
replay over every real map that printed lake COUNTS and AREAS — all plausible. Nobody printed a lake's LEVEL. The game
chains the two functions (hollows found at the deepest depth, then filled to that same depth), and in that chain a
lake beside dry land never stopped rising: its level was infinity, so it flooded whole 0.5 m squares of the map at
any height. The user saw square-edged water and water that "never rose", and it cost a round plus a 15-minute
debugger run; a replay that printed the levels would have shown `inf` on the first map.

## How to apply

- If the project has a way to play the real client without a window (here: the `unwritten-game` MCP,
  `docs/0-process/gmcp-playing-the-game-as-text.md`), use it for the final check. A hand-built replay is only for
  narrowing down the cause.
- Rebuild what that tool runs first, and prove your change is inside it: search the built assembly for a string
  unique to the change.
- If the outcome is not visible in the tool's text, add a permanent log line at the decision (success AND
  failure). Silence is how the original bug hid.
- In a replay, print the VALUES the screen will use (a level, a height, a price), not only counts and sizes — a
  count stays plausible while the value behind it is infinity. Replay the functions chained exactly as the caller
  chains them, with the same arguments.
- Read the whole log of the run, the server's too, not only the line you expected. The self-kick was two lines
  away from the success line.
