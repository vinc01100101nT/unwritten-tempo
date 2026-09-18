---
name: ai-plays-headless-client
description: How Claude plays the built client with no window (AgentLink + tools/agent-play), and what the first run found
metadata:
  type: project
---

Built 2026-09-17 at the user's request: the client build started with `-batchmode -nographics -agent-port 7790`
listens on 127.0.0.1 for one text line at a time (look / login / play / move / go / attack / cast / pickup /
equip / stat / say / revive / wait / log ...) and answers with the screen as text. `tools/agent-play/game_link.py`
launches/sends/quits; `tools/agent-play/mcp_server.py` exposes the same as MCP tools `game_launch`, `game`,
`game_quit` (registered in `.mcp.json` as `unwritten-game`, needs a Claude Code restart to appear).
A C# change to it needs `tools/build-game.sh client` (~10 min) before the client has it.

Reworked 2026-09-18 for round trips and tokens: several commands per line separated by `;` (a `say` takes the
rest of the line), one "why it ended" line per command, the screen once at the end; ids keyed on the server's
network number (doors on target+position) so they never change inside a map; `play` and a door wait until nothing
new has arrived for 1 s; floating numbers carry the unit they were drawn over (`DamagePopup.Shown` is
`Action<Transform,string>`), so `me`/`m3`/`p1` are exact; hotbar/npcs/doors sections are left out when identical to
the last answer (`unchanged: …`; `look` is always full); `since last` keeps 25 and
says how many earlier were dropped; with no window the minimap and water reflection stand down and every camera is
made an overlay (URP skips it) — the client log was 190 error lines a second before. A creature's states are NOT
on its line: the server sends a machine only its own player's states (NetPlayer.TargetStates), so a watched
creature's state store is always empty; showing them needs a wire change and a server rebuild.

Proven 2026-09-17 23:05: three players at once (user Alpha in the editor, Tessa on port 7790, a tester sub-agent's
character on port 7791) met in town_0, saw each other on the players list and talked in chat. Another player is
walked to via `p.DrawnBody` (the body drawn for them), never their network object, which stays at spawn.

Measured, not yet fixed (user not yet asked):
- **A field's Start spot dropped the player out of the world — FIXED 2026-09-18 by deleting every map's centre
  arrival point** (the user's instruction, after being offered the alternatives). What it was: all 41 maps carried
  a SpawnPoint with id "Start" at the map's exact middle, planted by RecipeGenerator and MapPainterImporter with
  no check of what stood there — and those same generators put the plaza/tavern/trees on that middle. Arriving on
  it free-fell (22,326 units in 123 s, measured) in field_11/12/0 or left the body on a roof in field_9/10.
  MEASURED with the new `ground` command: at field_11's Start, in the air with nothing below for 500 units; at
  both of that map's door points, on Ground_Floor (box 42x1x42) at y 0.02; walking in from a door you are BLOCKED
  1.5 m short of the middle, touching Tavern_LOD0 x2, Oak_tree3 x2, Willow_tree2, Oak_tree2, Pine_tree3,
  Sunflower. The fall itself is the client's own avatar (NetPlayer.CmdReportPosition at :1409 feeds the saved
  posY; the server slaves its copy at :1427), and the server's "reported 4 impossible moves in a row" every 31.5 s
  is free fall hitting NeverThisFarInOneStep=40 at gravity 12 with a 0.1 s report interval.
  **What was done:** the 41 centre points deleted from the scenes through the editor (one open+save per scene);
  the "Start" preference removed from PersistentPlayer3D.ResolveSpawn (a Portal's named id, else the map's first
  point, else null = leave the body where it is); the creation removed from RecipeGenerator and
  MapPainterImporter (with that importer's "Spawn point" tick-box and its ClearImported branch); RegionBuilder's
  boss-placement anchor no longer looks for one. 36 door points across 18 maps are untouched; **23 maps now have
  no arrival point at all** (every dungeon but 10 and 13, field_4/5/6/8, town_2/3/5/6/7/8/9). A new character now
  starts beside town_0's field_11 door instead of the plaza centre. Both the SERVER and CLIENT builds embed the
  scenes, so both need rebuilding before the change reaches a running game.
- field_15's "Start" spawn (0,0) is inside a tree collider — "MOVEMENT REFUSED ... Nearest solid: Deciduous_tree2
  at 0" (also Rye, Oak_tree3) — anyone arriving by /tp or respawn there cannot walk away. field_10's Start has a
  tree on it too.
- A field's own tavern can pin a player at its Start a metre above the floor. `Tavern_LOD0` in a field is NOT the
  town leaking in: every field carries its own tavern/windmill under `Landmarks`, and
  `Player3DController.NearestSolidDescription` (Player3DController.cs:480-495) reports the nearest collider in ANY
  loaded scene with no filter — a name from it is not evidence of a foreign map. The server held that player
  correctly in field_11 throughout (`[spawn] field_11 Bravo #67 got up`).
- Being hit about once a second flinches movement every time, so a mob hitting you pins you in place.
- The server and client disagreed on distances (server: Spirit2 2.89 away; client: 0.4).
- A native crash (SIGSEGV on a UnityPlayer worker thread) 1.3s after casting Committed Strike headless, 4/4 on the
  old build — GONE on the build that stops the render pipeline drawing (2026-09-18: cast it, client survived).
- The sword's `attackRange` is 0, so basic attacks never landed; Lunge (a skill) killed fine.

**How to apply:** check the user is not signed into the same test character before playing; always `quit` the client
in the same reply (RULE MYPROCESSES). Related: [[world-split-across-processes]].
