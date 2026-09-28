---
name: editor-launched-without-terminal-has-no-server-addresses
description: "Cannot enter the world in Play mode — CHAR_SERVER_URL is not set" means the editor was opened from the Dock/Hub, not that anything broke.
metadata:
  type: project
---

A Unity editor started from the Dock or Unity Hub inherits **none** of the shell settings that aim the game at
its servers, so Play mode signs in and then cannot enter the world, logging `this copy has no character server
to ask — CHAR_SERVER_URL is not set`. An editor started from a terminal that exported it works fine, which is
why the same project plays on Monday and not on Tuesday with no code change between.

**The answer is the project's settings file, not relaunching Unity:** since 2026-09-28 the editor reads
`ACCOUNTS_URL` and `CHAR_SERVER_URL` (those two names only, editor-only code in `ServerAddress`) from
`settings.env` at the top of the project — the one file every server also starts from. Entering Play mode
reloads the domain, so an edit takes effect on the next press with no restart. A build carries the two addresses
in its own StreamingAssets `server.txt`, written there by `tools/build-game.sh`. Anything set in a real terminal
still wins over both.

**Measured 2026-09-09:** the editor's own process had no `CHAR_SERVER_URL` at all (read live through the Unity
CLI), while the day before, a terminal-launched editor had `http://127.0.0.1:8081`. Same code, same project.

Diagnose it by reading the live value rather than guessing:
`unity cmd eval --code 'return AccountSession.CharacterServerUrl + " | env=" + (System.Environment.GetEnvironmentVariable("CHAR_SERVER_URL") ?? "<null>");'`

Related: [[may-restart-local-server-to-test]], [[unity-mcp-bridge]].
