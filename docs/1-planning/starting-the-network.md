---
status:
owner: vince
last-updated: 2026-08-31
tags:
  - planning-production
---

# Starting the Network

How to get the game running with more than one player. Follow it top to bottom; each step says how to know it worked before you move on.

Three things must be running, in this order:

1. **the database** — where accounts and characters live
2. **the account service** — signs people in
3. **the game server** — owns the world

Then you open the game itself, once per player.

---

## Every command on this page

Grouped by what you are doing. Each one is explained in full where it appears further down.

### Starting up, in order

Is the database up? Want `accepting connections`.

```
pg_isready
```

Starts the database, if that check said it was down.

```
brew services start postgresql@17
```

Starts the account service. Leave that terminal open.

```
cd accounts && npm start
```

Proves the account service is up **and** can reach the database. Want `{"ok":true}`.

```
curl -s http://localhost:8080/health
```

Runs the game server. Run it from the top of this project.

```
builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile -
```

Opens the game for one player. Run it again for each extra player.

```
open -n builds/client/unwritten.app
```

### Building

Builds the server. Unity must be open. Add `--linux` for a hosted machine.

```
tools/build-game.sh server
```

Builds the game players run.

```
tools/build-game.sh client
```

Builds both at once.

```
tools/build-game.sh both
```

### Checking what is running

Asks the operating system whether anything is listening for players.

```
lsof -nP -iUDP:7777
```

Lists every server running, and when each one started.

```
ps -o pid=,lstart=,command= -ax | grep '[u]nwritten-server'
```

This Mac's address on your wifi, for players on other machines.

```
ipconfig getifaddr en0
```

### Stopping

Stops the server on its own — the common one, between changes.

```
pkill -f 'unwritten-server.app/unwritten'
```

Stops the server and the account service together.

```
pkill -f 'unwritten-server.app/unwritten'; pkill -f 'node .*src/server.ts'
```

Stops the database. You rarely want this.

```
brew services stop postgresql@17
```

### GM accounts

Lets that account type chat commands. Set `false` to take it away. They must sign out and back in.

```
psql -d unwritten -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

Lists who has it.

```
psql -d unwritten -c "select email, is_gm from account order by created_at;"
```

### Running the server with a different set of maps

Replaces the authored map list for that run only.

```
GAME_MAPS=field_11,field_15 builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile -
```

---

## 1. The database

Check whether it is already up:

```
pg_isready
```

Expect `accepting connections`. **If it says anything else**, start it:

```
brew services start postgresql@17
```

Then check again with `pg_isready` — it takes a second or two. Do not go on until it says `accepting connections`; the account service will not come up without it.

Postgres is installed here through Homebrew as **postgresql@17**, and it is set to start by itself when you log in — so most of the time this step is just the check. You would only need the start command after stopping it by hand, or if the automatic start failed.

The game's data lives in a database called **unwritten** on the standard port **5432**.

To stop it (you rarely want to):

```
brew services stop postgresql@17
```

## 2. The account service

```
cd accounts && npm start
```

Leave that terminal open — it keeps running. In a **second** terminal:

```
curl -s http://localhost:8080/health
```

**Worked when:** it answers `{"ok":true}`.

That one line confirms both of the first two steps at once: the health check asks the database a question before it answers, so `ok` means the service is up **and** it can reach Postgres.

- **Nothing printed** → the service is not running. Look at its terminal for the reason.
- **An error instead** → the service is up but cannot reach the database. Go back to step 1.

It listens on **port 8080**.

## 3. The game server

The server is a separate build of the game with no window. You only rebuild it when the game's code has changed — otherwise skip straight to running it.

### Building it

**Unity must be open on this project**, because the build runs inside the editor you already have running. Then:

```
tools/build-game.sh server
```

Takes a few minutes. Everything it makes lands in `builds/` inside this project, and git is told to ignore that folder. Add `--linux` for a build to deploy to a hosted machine; plain gives you one that runs on this Mac.

The same script builds the game players run:

```
tools/build-game.sh client
```

Or both at once:

```
tools/build-game.sh both
```

It tells you if Unity is not running, and it fails loudly if nothing came out. When it finishes it prints the exact command to run what it built.

### Running it

```
builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile -
```

Run it from the top of this project. (The build script prints the same line when it finishes.)

**Worked when:** it prints these lines and keeps running —

```
[server] listening for players on port 7777
[server] world up — loading map 'town_0'
[server] map 'town_0' loaded — 15 root objects, active scene is now 'town_0'
[server] map 'field_11' loaded — 10 root objects, active scene is now 'field_11'
```

The first line means it is reachable. The `loaded` lines mean the world is actually up, not just requested.

**Read the map lines every time** — they are the only place the server tells you which maps it is holding, and a
map missing from that list has no creatures in it for anyone. A server holds the starting town plus the maps
listed under *Which maps the server holds open* below.

**Double-check it for real** — this asks the operating system, not the game:

```
lsof -nP -iUDP:7777
```

## 4. The players

Open the normal game build, once per player. Each one signs in and picks a character, and joins the world on the way in.

```
open -n builds/client/unwritten.app
```

Run that once for each player on this Mac; `-n` is what lets a second copy open.

**Worked when:** each player sees the other walking around, and the server prints a line as each arrives.

---

## Pointing it somewhere else

Nothing here is baked into a build. A build made once can be aimed anywhere by setting these before you start it — no rebuild.

| Setting | What it does | Default |
| --- | --- | --- |
| `GAME_SERVER` | Makes this copy BE the server. Any value except `0` or `false` counts as on. | off |
| `GAME_SERVER_HOST` | Which machine a player's game dials. | `localhost` |
| `GAME_SERVER_PORT` | The port both sides use. Must match. | `7777` |
| `ACCOUNTS_URL` | Where the account service lives. | `http://localhost:8080` |
| `GAME_MAPS` | Which maps the server holds open besides the starting one, comma-separated. Replaces the authored list rather than adding to it. Server only. | the authored list |

A dedicated server build already knows it is a server; `GAME_SERVER` is for running an ordinary build, or the editor, as one.

**On your own wifi**, players need the host machine's local address rather than `localhost`:

```
ipconfig getifaddr en0
```

Start each player's game with `GAME_SERVER_HOST` and `ACCOUNTS_URL` pointed at that address.

---

## Which maps the server holds open

A player is only ever in one map. **A server holds several at once**, and that is what makes a field have
creatures in it before anyone walks in — a map the server never opened has no spawner running, so it makes
nothing, and whoever arrives finds it empty.

Two maps are held by default: the starting town, and **field_11**, the field through the town's western door.

The list is authored on the `GameRoot` object in the `Systems` scene, under **Also Held Open**. To try a
different set without touching the scene or rebuilding, set `GAME_MAPS` when you start the server — it replaces
the authored list for that run:

```
GAME_MAPS=field_11,field_15 builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile -
```

Either way, the `map … loaded` lines in the start-up output are the truth about what came up.

Holding more maps costs the server real work, because every creature in them is thinking whether or not anyone
is there. Measured on this Mac with nobody playing: the starting town alone sits near 13% of one processor core;
the town plus four fields — 129 creatures — near 16%; every map in the world at once — 1,809 creatures — near
50%, with the server's own heartbeat starting to slip from 60 to 53–55. The cost is close to a straight line,
about a fiftieth of one percent of a core per creature, so you can price a change by multiplying.

---

## Test accounts

Already in the database. Password for both: `testpass123`

| Email | Character |
| --- | --- |
| `alpha@test.local` | Alpha — marked as a GM |
| `bravo@test.local` | Bravo |

---

## GM accounts — who may type commands

Every chat command except `/help` changes something: a level, a bag, the creatures standing in the world. So
**none of them work unless the account is marked a GM.** An ordinary player who types one is told *That command
is for GMs.* and nothing happens.

The mark belongs to the **account**, not to a character — being trusted with commands is a fact about a person,
so it covers every character they make.

### Granting it

There is no screen for this on purpose. Grant it with one line:

```
psql -d unwritten -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

Take it away again by setting `false`. Either way the person must **sign out and back in** for the change to
reach them — the mark is read when their character is loaded, so a game already running still has the old answer.

Check who has it:

```
psql -d unwritten -c "select email, is_gm from account order by created_at;"
```

### Where each command actually runs

Two kinds, and the difference is who else can see the result.

**Commands that change only you** — level, XP, stat points, heal, revive, invulnerability, model, Zeny, items,
weapons, clearing the bag, binding a skill, travelling, time of day, weather, move speed — run in your own game,
exactly as before. Now they simply refuse unless you are a GM.

**Commands that change the world** — `/summon`, `/dummy`, `/kill`, `/killall` — are carried out by the server.
Your game hands the typed line over, the server checks its own copy of the mark, does the work, and sends the
answer back into your chat a moment later. This is why summoned creatures are visible to everybody and can
actually be hit: they are born on the machine that owns the world, like every other creature.

The server never believes a game's claim about who is playing it. It reads the mark from the account service
itself, the same way it already reads a player's real name and body rather than the one they announced.

### An honest limit

The commands that change only your own character are checked **in your own game**, because your game is still
what writes your save. That is a lock on the door, not a vault: it stops an ordinary player using them, and it
would not stop someone who tampered with their own copy. Anything touching the shared world is checked on the
server, where tampering does not reach. Closing the first gap means moving save ownership to the server, which
is its own piece of work.

---

## When it does not work

**Nothing listening on 7777.** Check the server's log for `No Transport on Network Manager`. That means the network layer gave up during start-up and the rest of the log will look normal — it fails quietly, so look for it rather than assuming a silent log is a healthy one.

**Players connect but nobody has a body.** The log will say which prefab is missing from Resources.

**Players see each other but no mobs.** Only the server spawns creatures now. If the server is not running, or a player is connected to a different one, there is nothing to send. If it *is* running, read its `map … loaded` lines — a map the server never opened has no creatures for anyone, and an old server predates any map you added since it started.

**A whole map is empty.** The server is not holding that map open. See *Which maps the server holds open*.

**Connections fail with `OnDeserialize failed` and players drop straight after connecting.** The server and the game are built from different versions of the code, so they disagree about the shape of the player data. Almost always a server left running from before your last change — stop it, rebuild, run it again.

**A second server refuses to start.** It says the port is already taken and quits — that is correct, and it is the message doing its job. One server is already up; stop that one first, or give the new one a different `GAME_SERVER_PORT`. A server that cannot take the port never listens to anything, so it exits rather than sitting in the process list looking alive.

**Something is still listening after you stopped the server.** You have a second one running. List them with start times and stop them all:

```
ps -o pid=,lstart=,command= -ax | grep '[u]nwritten-server'
```

**A player is stuck at the sign-in screen.** That is the account service, not the game server — check step 2.

---

## Stopping and restarting

### A running server never picks up your changes

The server holds a **copy** of the game as it was when you started it. Editing code, rebuilding, or changing a
map does nothing to a server that is already running — it keeps serving the old world until you stop it.

So after any change you want to see, the order is always: **stop the server, build it, run it again.** Rebuilding
without stopping leaves the old one serving; stopping without rebuilding runs the same code over.

### Stopping

Stop the server on its own — this is the common one, between changes:

```
pkill -f 'unwritten-server.app/unwritten'
```

Stop everything, server and account service together:

```
pkill -f 'unwritten-server.app/unwritten'; pkill -f 'node .*src/server.ts'
```

**Stopped when:** the check below prints nothing at all.

```
lsof -nP -iUDP:7777
```

If it still prints a line, something is still holding the port — see the stale-server entry under *When it does
not work*.

### The full round trip after a change

1. `pkill -f 'unwritten-server.app/unwritten'` — stop the old one
2. `lsof -nP -iUDP:7777` — confirm nothing is left listening
3. `tools/build-game.sh server` — build the change in (Unity must be open)
4. run the server again — the command under *The game server ▸ Running it*
5. read its start-up lines and check the map list is what you expect

The database and the account service do **not** need restarting for a game-code change. Restart the account
service only when you changed something inside `accounts/`.

### Leaving one running by accident is the classic trap

A server left over from hours ago looks exactly like a healthy one from the outside: the port answers, players
sign in, the world appears. What you get is the **old** world — missing whatever you have added since — and if
the player data changed in the meantime, every connection will fail to decode and drop. Check what is actually
running, and when it started, before believing anything else:

```
ps -o pid=,lstart=,command= -ax | grep '[u]nwritten-server'
```

If that start time is older than your last change, that server is the problem.

---

## Where it goes when it leaves your machine

One ordinary always-on virtual machine, near the players — Singapore for South-East Asia. It runs all three things above. Not a match-based game-hosting service: those spin a session up and throw it away, while this world runs continuously.

Unity's own hosting service shut down on 31 March 2026; do not plan around it.
---

## Related

- [Networking — Implementation Notes](../2-systems/networking-implementation-notes.md) — what the thing you just started actually does, and what is still unrun.
- [Querying the Database](querying-the-database.md) — reading back the accounts and characters this runbook creates.
- [Network-Readiness — Combat Audit & Refactor Plan](../2-systems/network-readiness-audit.md) — the July survey that chose this shape.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the routine that now needs a running world before it can start.
- [Roadmap to Launch](roadmap-to-launch.md) — Tier 2, where this sits against the plan.
