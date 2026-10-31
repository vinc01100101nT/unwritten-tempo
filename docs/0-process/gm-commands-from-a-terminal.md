---
status:
owner: vince
last-updated: 2026-10-31
tags:
  - process
  - networking
---

# GM Commands from a Terminal

`gm` runs GM commands on the live game server with no game client open, and reads what it holds. Every player sees
the effect exactly as if a GM had typed the line in chat. It is `tools/gm` (Git Bash, Mac, Linux) and `tools/gm.cmd`
(PowerShell, cmd), both starting `tools/gm.py`; with the `tools` folder on the PATH it is just `gm`.

The game is one seamless map, `world`, so every command goes to `world`.

**A session** — pick the map once, then type commands directly:

```
gm
Maps open right now — choose one with map <number>:
map server <id> — 1 player(s), … creature(s), last heard 3s ago
    1  world
gm[no map]> map 1
gm[world]> weather rain
gm[world]> as Alpha
gm[world as Alpha]> tp parley
gm[world as Alpha]> heal
gm[world as Alpha]> status
gm[world as Alpha]> quit
```

- `maps` lists the maps open right now, numbered, under the game server holding each — its id, how many players and
  creatures it holds, and how long ago it last checked in. Only a map a game server holds takes a command; the
  character server never brings one up for a command.
- `maps all` lists every map of the game — the one seamless map, its id read from `SeamlessWorldBuilder.MapId` —
  marking the ones open now.
- `map <which>` picks the map: a number from the last list, the id (`world`), or a place in the world by its whole id or
  name in any case, spaces and underscores optional (`old oak`, `lastline`), which means the map that place is in. The
  places are read from `unity/Assets/3_Data/Resources/WorldPlaces.json`; part of a place's name is not enough ("No map
  matches 'oak'"). The choice is taken only once a game server answers for it, so a typo never becomes the map every
  command goes to.
- `as <character>` acts as that person; `as` alone goes back to acting as nobody. `help` lists the session words and
  the server's GM commands. `status` and `quit` do what they say.

**One command and back** takes the same words as a session line:

```
gm --map <map> "/weather rain"
gm --map <map> --as Alpha "/item \"watery ink\" 10"
gm --map <map> status
```

`--map` takes the same words as `map` — `--map world`, `--map "old oak"`. A one-shot call without `--map` is refused,
and in a session nothing is sent until a map is chosen.

## Moving somebody: `/tp`

`/tp` needs `--as`. It takes:

- **`/tp <place>`** — a town, landmark, cave, pond or dungeon door of the world, by its name in any case, spaces
  optional, or any part of it only one place has. The names come from `unity/Assets/3_Data/Resources/WorldPlaces.json`,
  which the world layout tool writes. Several matches are listed and none is picked.
- **`/tp <x> <z>`** — a spot of the world by the numbers `/where` prints; the arrival finds the ground there.
- **`/tp`** alone — lists every place, the built ones first and the ones not built yet after.

A place or spot on a square that is not built is refused ("… is not built yet."). The code still accepts
`/tp <map id> [arrival point]` for another map, but the build ships no map other than `world`.

## The path a line takes

1. The script sends the line to the character server (`POST /server/gm` or `/server/status`) with the server secret
   from `settings.env` (`CHAR_SERVER_URL`, `SERVER_SECRET`). Whoever can run it can do anything a GM can.
2. The character server finds the game server holding that map. If none holds it, the answer says so.
3. The game server runs the line on its game loop through the same command list the chat uses, and answers with every
   line the command replied, colour tags removed. It waits up to 8 seconds for the first line, answers once 1 second
   passes with nothing new, and stops at 12 seconds. If nothing came back, the script says it does not know whether
   the command ran. Every reply line, a late one included, is written to the game server's log.

## What runs

- Only the commands that run on the server, the same rule a player's request meets, plus `/help`.
- With no `--as`, only the commands that act on no person: `/help`, `/log`, `/killall`, `/whereis`, `/mobinfo`,
  `/whodrops`, `/iteminfo`, `/time`, `/weather`, `/exprate`. Any other command answers "acts on a person — name one
  with --as <character>".
- `--as <character>` must name somebody online, in that map, and not part-way through a map change.
- `/exprate` from the terminal is recorded as started by "the terminal".

## Weather and time

`/weather` and `/time` are held by the game server for every map it holds. A held weather ends when that server
restarts.

## Status

One line each: which game server holds the map and what else it holds; the time of day (0 sunrise, 0.25 noon,
0.5 sunset, 0.75 midnight) and the date; the weather and whether a GM is holding it; who is in that map.

## Related

- [GMCP — Playing the Game as Text](gmcp-playing-the-game-as-text.md) — a full windowless player, when a command
  needs somebody actually standing in the world.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — the services this talks to.
