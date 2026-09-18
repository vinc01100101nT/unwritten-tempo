---
status:
owner: vince
last-updated: 2026-09-18
tags:
  - process
  - networking
---

# GMCP — Playing the Game as Text

**GMCP** (the game's MCP) lets an AI — Claude in this editor, a tester sub-agent, a debugger — play the built
game the way a person does, with no window: it reads the screen as text and gives the same orders a click or a
key gives. Built and first played on 2026-09-17. That evening three players stood together in `town_0` and talked
in the game's chat: Vince as **Alpha** in the editor, Claude as **Tessa** through GMCP, and a tester sub-agent as
**Tester** through a second GMCP client.

It is a plain MCP: three tools, no special protocol. Everything interesting is inside the game.

## The two pieces

1. **Inside the game — the listener.** The client build starts it only when launched with `-agent-port <port>`.
   It listens on this computer only (127.0.0.1), takes one line at a time and answers with some lines of text
   followed by a line reading `<<END>>`. A line may carry several commands separated by `;` — they run one after
   another and the answer is one line per command saying how it ended, then the screen once. A command that starts
   something — a walk, a fight, a cast — ends when it is over or something interrupts it (arrival, a hit, a death,
   30 s) and says which, so one question is one decision. Code: `unity/Assets/Scripts/AgentLink.cs`.
2. **Outside the game — the MCP server.** `tools/agent-play/mcp_server.py` gives an AI three tools and passes
   each command to the listener. `tools/agent-play/game_link.py` does the same from a terminal. It is registered
   in `.mcp.json` as `unwritten-game`; Claude Code reads that list only when it starts, so the tools appear after
   a restart.

| Tool | What it does |
| --- | --- |
| `game_launch` | Starts the client with no window (`-batchmode -nographics -agent-port 7790`), log in `logs/agent-client.log`. |
| `game` | One line — one command, or several separated by `;`; returns how each ended, the screen as text once, and what happened since the last call. |
| `game_quit` | Stops the client that `game_launch` started. Never any other process. |

## It decides nothing

Every order is a thin translation from a word to the function a click or a key already calls: `move` is the
left-click walk order, `attack` the left-click fight order, `cast` presses the hotbar slot and answers the aiming
ring the way a click would, `say` is the chat box's own send, `equip` / `use` / `stat` / `pickup` / `talk` /
`revive` are the bag, the character sheet, the reach key and the respawn button. The server checks an AI's
orders exactly as it checks a person's, and every number in the screen text is one the machine was already
holding to draw. This is the project's no-client-math rule, unchanged.

## Running it

The four services must already be up — see [Starting the Network](../2-systems/networking/starting-the-network.md).
GMCP never starts or stops them.

```
tools/agent-play/game_link.py launch
tools/agent-play/game_link.py "login alpha@test.local testpass123"
tools/agent-play/game_link.py "play Tessa"
tools/agent-play/game_link.py "look"
tools/agent-play/game_link.py quit
```

A second AI player on the same machine puts `AGENT_PORT=7791` (any free port) in front of every call; each port
has its own log and its own pid file, so one player's `quit` never stops another's game.

**Several commands in one call cost one round trip instead of several** — `"go d1; wait 2; attack m1"` — and a
round trip is the expensive part for an AI, far more than the screen text. A `say` takes the rest of the line, so a
chat message may hold a semicolon (a trailing one is dropped).

Any C# change to the listener reaches the client only after `tools/build-game.sh client` (about ten minutes, and
it runs inside the open editor — never while somebody is in Play mode there).

## The commands

Ids come from the screen: `m` a monster, `p` a player, `n` a villager, `g` a floor item, `d` a door. **An id keeps
its meaning for as long as you are in the map**, whatever happens to the object behind it: a creature, a player
or a floor item is known by the number the server gave it, a door by where it stands and where it leads. (Before
2026-09-18 an id was tied to the object drawn, and the first screen's `d1` had become `d3` by the next command —
measured twice, cause in the game not measured.) An id whose body has since been handed to another creature
answers `unknown id` rather than acting on the wrong one. Every command that waits ends with a line saying why:
`arrived`, `hit`, `new map field_15`, `m3 died`, `you died`, `HP under 30%`, `cast Lunge`, `picked up Grilled
Fish`, `revived`, `5s passed`. A wait may be up to 120 s.

| Command | What it does |
| --- | --- |
| `look` | The whole screen as text. Every other answer leaves out the hotbar, villagers or doors when they read exactly as last time, and says so in one line: `unchanged: hotbar, doors`. |
| `login <email> <password>` · `create <name>` · `play <name\|number>` | Sign in, make a character, enter the world. `play` answers once nothing new has arrived for a second (or after five seconds in a map that never goes quiet), so the first screen has everything on it. |
| `move <x> <z>` · `go <id>` | Walk to a spot, or to a thing. Ends on arrival, a new map (once it has settled), your HP under 30%, death, or 30 s — not on an ordinary hit, because a creature nibbling 1 HP a second would end every walk at once. |
| `attack <m#>` | Fight a monster until it or you die, your HP drops below 30%, or 30 s. |
| `auto on\|off [secs]` | The auto-pilot fights nearby monsters; answers after `secs` (default 10). |
| `stop` | Drop every order. |
| `cast <slot\|R\|name> [m#\|p#\|me \| x z]` | Press a hotbar skill, aimed at a creature, a player, yourself, or a spot. |
| `pickup <g#>` · `talk <n#>` | Reach for a floor item — `picked up …` only when the bag gained it, else `… is gone from the floor` (a kill's drops vanish after 60 s); talk to a villager (returns their lines). |
| `use <bag#\|name>` · `equip <bag#>` · `unequip <slot>` | The bag's own use and equip. |
| `stat <STR\|AGI\|VIT\|INT\|DEX\|LUK>` | Spend one stat point. |
| `bag` · `gear` · `stats` | Detail views. |
| `ground` | What you are standing on and what is touching you, as the physics engine answers it: your height, whether you are grounded or in the air, every solid below you with its map and layer, and everything solid within 1.5 m. The search starts 2 m above your head, so a line with a negative distance is something you are standing INSIDE rather than on. For hunting a fall or a stall from outside the game. |
| `say <text>` | Chat; a `/command` runs like a typed one, waits two seconds to see whether it moved you, and if it did, answers with the new map once it has settled. Lines longer than the server's 200-character cut are split between words. |
| `revive` | Press respawn when dead. |
| `wait <secs>` | Let time pass; answers early on a hit. |
| `log <category\|all> on\|off` | This client's own log categories (written to its log file). |
| `help` | The list above. |

## The screen

One short block: the map; your name, level, HP, MP, XP, gold, position, current order, stat points and target;
each hotbar slot with **ready**, **cd** *N*s or **noMP**; your states with seconds left and stacks; the nearest
monsters (up to 10, within 25 m) with HP and distance — never their states, because the server sends a machine only
its own player's states; players, villagers, floor items and doors within 15 m; an open dialogue; and **since
last** — every chat line, warning, and floating number since the previous command, named after the head it was
drawn over: `me 12`, `m3 46`, `p1 MISS`. The same line straight after itself is counted rather than repeated
(`me 1 x9`); the last 25 are kept, and when more happened the line says `(+N earlier not shown)`. Only what is
already drawn on a player's screen; nothing hidden.

## What playing it has measured about the GAME — open, not fixed

Every one of these is a fault in the game, found by playing it as text. None is about the text layer itself.

- ~~A field's arrival spot is inside the scenery~~ — **FIXED 2026-09-18 at the user's instruction: every map's
  centre arrival point is gone.** What it was: each of the 41 maps carried a point called Start at its exact
  middle, planted automatically by the map generator with nothing checking what stood there — and the generator
  puts the plaza, the tavern and the trees on that same middle. Arriving on it dropped you out of the world
  (measured 22,326 units down in 123 seconds, three fields of three) or left you standing on a roof. Measured with
  the `ground` command before the fix: at a field's Start, in the air with nothing below for 500 units; at either
  of that same map's door arrival points, standing on its floor at 0.02; walking in from a door you were stopped
  1.5 m short of the middle by two taverns and four trees. All 41 centre points were deleted from the maps, and
  both map generators no longer plant one, so a re-import cannot bring it back. The door arrival points are
  untouched — 36 of them across 18 maps.
  **What this changes for anyone arriving without a door:** the game now uses the map's first door point instead
  (a new character starts in town beside the field_11 door rather than in the middle of the plaza). **23 maps have
  no arrival point at all now** — every dungeon but two, four fields and seven of the small towns — and arriving
  in one of those leaves the body wherever it already stood. The user is building the respawn rule that answers
  that case.
- **A creature list that empties a few seconds after arriving is this same fall** — the list only shows what is
  within 25 m, and 25 m down takes about 3 seconds. Nothing is wrong with the list.
- **`field_15`'s Start spawn (0,0) is inside a tree collider**, and `field_10`'s has a tree on it too. Anyone
  teleported or respawned there cannot walk away ("MOVEMENT REFUSED … Nearest solid: Deciduous_tree2 at 0"; also
  Rye and Oak_tree3).
- **A field's own tavern can pin a player standing at its Start**, holding them a metre above the floor and, it
  seems, out of every creature's reach — the same player took no blow in eight minutes with a creature half a
  metre away. Whether the building is what stops the blows is unmeasured. (The line that named `Tavern_LOD0` is
  NOT evidence the town leaked into the field: that probe reports the nearest solid thing in any loaded map, and
  every field carries its own tavern and windmill.)
- **Being hit about once a second keeps you rooted**: every hit restarts the stagger that stops your legs.
- **The Sword item's attack range is 0**, so basic swings never land; skills do.
- **The door from `field_11` back to town** did not fire twice when walked onto; nothing in either log.

## Related

- [Starting the Network](../2-systems/networking/starting-the-network.md) — the four services GMCP plays against.
- [Reading the Game Log](reading-the-game-log.md) — the server's own account of what an AI player's orders did.
- [Networking — Architecture](../2-systems/networking/README.md) — why the client may decide nothing.
