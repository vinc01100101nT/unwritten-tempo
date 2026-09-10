---
name: the-game-is-always-online
description: HARD LAW — this game is always-online; there is no offline/single-machine mode, so never build, fix, test or offer anything for a "no server" game.
metadata:
  type: project
---

Stated by the user on 2026-09-10: **"we do not do offline game, server is necessary."**

A player is ALWAYS signed in and always attached to a game server. There is no supported single-machine game,
so the no-server half of a feature is dead weight, not a case to be covered.

**How to apply:**
- Never offer, plan, or ship a fix "for the offline game too". Fix the networked path, and say so.
- Never use "but with no world…" as a reason to keep a second code path alive.
- The codebase still CONTAINS many no-server branches, written when the game ran on one machine — their
  existence is not evidence that offline is supported. Reading one is not permission to extend it.
- Removing those dead branches is a separate decision that belongs to the user; do not do it unasked.

Related: [[world-split-across-processes]] — the world lives across a character server and several game servers,
which is the shape a player always plays in.
