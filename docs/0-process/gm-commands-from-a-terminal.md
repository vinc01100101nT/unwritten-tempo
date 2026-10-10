---
status:
owner: vince
last-updated: 2026-10-11
tags:
  - process
  - networking
---

# GM Commands from a Terminal

`gm` runs GM commands on the live servers with no game client open, and reads what a map server holds. Every
player sees the effect exactly as if a GM had typed the line in chat. It is `tools/gm` (Git Bash, Mac, Linux) and
`tools/gm.cmd` (PowerShell, cmd), both starting `tools/gm.py`; with the `tools` folder on the PATH it is just `gm`.

**A session** — pick the map once, then type commands directly:

```
gm
Maps open right now — choose one with map <number>:
map server DESKTOP-285PTHA-c517fbac — 0 player(s), 32 creature(s), last heard 22s ago
    1  lst_fild01     Lastline Field 1 (field)
gm[no map]> map 1
gm[lst_fild01]> weather rain
gm[lst_fild01]> as Alpha
gm[lst_fild01 as Alpha]> heal
gm[lst_fild01 as Alpha]> status
gm[lst_fild01 as Alpha]> quit
```

**Nobody has to remember a map id.** `maps` lists the maps open right now, numbered, with their names, under the
map server holding each — its id, how many players and creatures it holds, and how long ago it last checked in.
Only an open map can take a command, because a map opens when somebody walks into it. Maps under the same map
server share one weather and one time of day. `maps all` lists every map in the
world layout (`tools/world-creator/worlds/world-regions.json`) and marks the open ones. `map` takes a number from
the last list, an id, a full name (`map old oak`), or any part of an id or name (`map lastline field 1`); words
that match several maps list them all and pick none. `--map` takes the same words. A session started with no map
shows the open list at once.

`map` is taken only once a map server answers for it, so a typo never becomes the map every command goes to.
`as` alone goes back to acting as nobody. `help` lists the session words and the server's GM commands.
`gm --map lst_fild01` opens a session already on that map.

**One command and back:**

```
gm --map lst_fild01 "/weather rain"
gm --map lst_fild01 --as Alpha "/item \"watery ink\" 10"
gm --map lst_fild01 status
```

A map is required for every command: in a session nothing is sent until one is chosen, and a one-shot call
without `--map` is refused. It names which map server the line goes to: the one holding that map.

## The path a line takes

1. The script sends the line to the character server (`POST /server/gm` or `/server/status`) with the server secret
   from `settings.env`. Whoever can run it can do anything a GM can.
2. The character server finds the map server holding that map. A map nobody is in is held by nobody, and the
   answer says so. The map is not brought up.
3. The map server runs the line on its game loop through the same command list the chat uses, and answers with
   every line the command replied, colour tags removed. It waits up to 8 seconds for the first line, then answers
   once 1 second passes with nothing new. If nothing came back, the script says it does not know whether the
   command ran. Every reply line, a late one included, is written to `logs/server.log`.

## What runs

- Only the commands that run on the server, the same rule a player's request meets, plus `/help`.
- With no `--as`, only the commands that act on no person: `/help`, `/log`, `/killall` (in the `--map` map),
  `/whereis`, `/mobinfo`, `/whodrops`, `/iteminfo`, `/time`, `/weather`, `/exprate`. Any other command answers
  "acts on a person — name one with --as".
- `--as <character>` must name somebody in the world, in that map, and not mid-way through a door.
- `/exprate` from the terminal is recorded as started by "the terminal".

## Weather and time are held per map server, not per map

A map server holds several maps at once, and `/weather` and `/time` change all of them together. The reply and
`status` list every map that server holds. A held weather ends when that map server restarts.

## Status

One line each: which map server holds the map and what else it holds; the time of day (0 sunrise, 0.25 noon,
0.5 sunset, 0.75 midnight) and the day number; the weather and whether a GM is holding it; who is in that map.

## Related

- [GMCP — Playing the Game as Text](gmcp-playing-the-game-as-text.md) — a full windowless player, when a command
  needs somebody actually standing in the world.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — the services this talks to.
