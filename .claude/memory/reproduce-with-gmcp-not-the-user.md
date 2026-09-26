---
name: reproduce-with-gmcp-not-the-user
description: "To reproduce a gameplay bug, play it yourself through the text-play client (GMCP) — never ask the user to reproduce it while a recorder watches."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6846ae7b-84f0-4b36-83c5-7b9b5cb27050
  modified: 2026-09-26T03:51:52.673Z
---

When a gameplay bug needs to be seen happening, launch the text-play client and play it myself; do not install a
recorder in the editor and ask the user to go reproduce it.

**Why:** 2026-09-26, chasing the "stops chasing when flinched" bug, I put a probe in the editor and asked the user to
play; their session ended before a chase happened and the round was wasted. The user: "can you instead use gmcp,
rather than the recorder. test it yourself". Playing it through GMCP found the cause in one fight.

**How to apply:** the client build must match the running server — rebuild it first (`tools/build-game.sh client`,
~2–10 min, editor not in Play) if game code changed since the last build. A test character whose saved map no
longer exists cannot enter; move it to a real map's arrival point. Low-level test characters die in seconds in a
spider pack — put stat points in VIT first. Quit the client in the same reply. See [[ai-plays-headless-client]].
