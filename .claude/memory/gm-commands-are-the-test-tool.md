---
name: gm-commands-are-the-test-tool
description: The P-key DevSettings panel was deleted 2026-09-13 at the user's instruction — testing goes through GM chat commands, all of which run on the server
metadata:
  type: project
---

On 2026-09-13 the user deleted the in-game DevSettings panel (P key) and said: "we are now using gm commands for
testing starting today." Every GM command now runs on the game server (`changesTheWorld: true`) except `/help`.

**Why:** the panel changed only the player's own machine, so its level / stat-point / ring-XP controls did
nothing the server believed — the same fault `/statpts` had until the same day (the server held 1 point while
the screen showed 61).

**How to apply:** when a test needs a knob, reach for or add a GM command, never a panel or an editor-tool menu
(see [[no-editor-tools-by-default]]). A new command that changes a character or the world is registered with
`changesTheWorld: true` and tells the owner's machine what changed.
