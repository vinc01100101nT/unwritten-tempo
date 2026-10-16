---
name: game-server-is-the-map-server
description: "Game server" and "map server" are ONE thing — the Unity server build; always say "the game server", never list them as two
metadata:
  type: feedback
---

The game server and the map server are the same thing: the Unity server build (`builds/server/unwritten-server.app`)
that runs the maps. Call it **"the game server"**, always, and treat rebuild + restart as one item about it.

**Why:** a play-test checklist on 2026-10-16 listed "Game server build: rebuild" and "Map server: restart" as two
separate items; the user: "why do you keep saying two different names for map server and game server, they are the
same. fix your self." The two-item shape came from the project rule's own list, now merged into one item.

**How to apply:** in every checklist and report, the servers are three: the game server (rebuild and restart, or
not), the character server, the account service. Never write "map server" for the game server. Related:
[[restart-servers-keeps-old-settings]].
