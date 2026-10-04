---
name: quest-guide-is-mandatory-and-separate-from-path
description: Tutorials play via Play/Stop (guide + path together); hints only on tutorials; one path across all quests; Play gated by requirements
metadata:
  type: project
---

The user's quest design, as of 2026-10-04:
- **Only tutorials have hints.** The guide (dark screen with one lit spot) teaches only the tutorial that is playing.
- **A tutorial has Play, which turns to Stop while it plays.** Playing a tutorial follows it, so its guide AND its path
  turn on together. Stop follows nothing. Tutorials have no Show path button.
- **There is one path across every kind of quest.** A side or main quest's Show path moves the path to it, which stops
  the playing tutorial.
- **Play is checked by the server, at the press only.** It needs what the rest of the tutorial still requires: the
  forging and folding bill worked out from the item costs (never typed), plus the level the quest names. The Quests
  window shows the list with the server's met or unmet flags. When the check fails, the player sees "Requirements are
  not met".
- **A new character's first tutorial plays on its own** when its requirements are met.

**Why:** while a tutorial plays, it is mandatory. The user can still stop it on purpose. A player is "trapped" only
when the lit spot cannot be pressed, or pressing it cannot finish the step. Being made to do the required step is the
design, never a trap.

**How to apply:** never call the guide a trap because it limits the clicks. Never let a tutorial be followed except
through Play. Related: [[the-five-anti-invention]].
