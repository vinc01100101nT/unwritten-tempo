---
status:
owner: vince
last-updated: 2026-09-02
tags:
  - planning-production
---

# Starting the Network

How to get the game running with more than one player. Follow it top to bottom; each step says how to know it worked before you move on.

**Four** things must be running, in this order. Each one needs the one above it.

1. **the database** — where accounts and characters live
2. **the sign-in service** — signs people in, and nothing else
3. **the character server** — owns every character: the list, the saves, and who is allowed into which world
4. **the game server** — owns the world itself

Then you open the game, once per player.

The character server is the newest of the four and the one most likely to be forgotten. **Without it nobody can
enter the world at all** — a player is no longer told an address by their own build; they ask the character
server to let them in, and it decides where to send them.

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

Starts the sign-in service. Leave that terminal open.

```
cd accounts && npm start
```

Proves the sign-in service is up **and** can reach the database. Want `{"ok":true}`.

```
curl -s http://localhost:8080/health
```

Starts the character server. Leave that terminal open too. It reads the SAME settings the sign-in service
reads, which is why the line begins by loading them.

```
SERVER_SECRET=change-me charserver/run.sh
```

Proves the character server is up **and** can reach the database. Want `{"ok":true,...}`.

```
curl -s http://localhost:8081/health
```

Runs the game server. Run it from the top of this project. The settings are not optional any more — without
`SERVER_SECRET` the character server refuses this map server and nobody can get in.

```
CHAR_SERVER_URL=http://127.0.0.1:8081 SERVER_SECRET=change-me MAP_SERVER_ADDRESS=127.0.0.1 \
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

Asks whether all four are listening at once.

```
lsof -nP -iTCP:8080 -iTCP:8081 -iTCP:7788 -sTCP:LISTEN; lsof -nP -iUDP:7777
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

Stops the game server, the character server and the sign-in service together.

```
pkill -f 'unwritten-server.app/unwritten'; pkill -f CharServer; pkill -f 'node .*src/server.ts'
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

Then check again with `pg_isready` — it takes a second or two. Do not go on until it says `accepting connections`; the sign-in service and the character server will not come up without it.

Postgres is installed here through Homebrew as **postgresql@17**, and it is set to start by itself when you log in — so most of the time this step is just the check. You would only need the start command after stopping it by hand, or if the automatic start failed.

The game's data lives in a database called **unwritten** on the standard port **5432**.

To stop it (you rarely want to):

```
brew services stop postgresql@17
```

## 2. The sign-in service

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

It listens on **port 8080**. It signs people in and does nothing else — the characters moved out of it.

## 3. The character server

Owns every character: the list on the character-select screen, creating and deleting one, and every save. It
is also the door into the world — it decides which game server a player is sent to, and hands their whole
character over before they get there.

It is a separate program, written in C#, living in `charserver/`. It needs the .NET tools:

```
brew install dotnet
```

Start it. It reads the **same** database address and the **same** signing secret the sign-in service reads —
which is why this line loads that file first. A different value for either is a bug, not a configuration: they
would be looking at different databases, or refusing each other's players.

Run it **from the top of this project** — it finds its own settings from there, and works whatever folder you
are standing in.

It exists because loading those settings by hand has a trap in it: `. .env` makes zsh search your PATH instead
of the folder you are in, so it reports *no such file or directory* while the file is sitting right there. That
caught us twice on 2026-09-02. The script is the same work with nothing to mistype.

```
SERVER_SECRET=change-me charserver/run.sh
```

Leave that terminal open. In another:

```
curl -s http://localhost:8081/health
```

**Worked when:** it answers `{"ok":true,"models":52}` or similar. That check asks the database every time, so
`ok` means it can actually serve a character rather than merely being alive.

It listens on **port 8081**.

**`SERVER_SECRET` is how the two servers recognise each other.** It is NOT a player's sign-in token and the two
are never interchangeable: a person signing in must not be able to register a game server or read somebody
else's character, and a game server does not sign in. Set the same value on both, and change it from
`change-me` before this leaves your machine.

If it refuses to start it says why in one line and quits, rather than listening and failing on the first
player. The two reasons it refuses: the port is already taken (another one is running — that is the common
one), or it cannot reach the database.

## 4. The game server

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
CHAR_SERVER_URL=http://127.0.0.1:8081 SERVER_SECRET=change-me MAP_SERVER_ADDRESS=127.0.0.1 \
  builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile -
```

Run it from the top of this project.

**Those settings are required now.** With no `SERVER_SECRET` it says so and nobody can enter the world: the
character server will not hand players to a game server it cannot recognise.

**Worked when:** it prints these lines and keeps running —

```
[map] listening for arrivals on 7788 as 'YourMac-1a2b3c4d'
[server] listening for players on port 7777
[server] world up — loading map 'town_0'
[server] map 'town_0' loaded — 15 root objects, active scene is now 'town_0'
[server] map 'field_11' loaded — 10 root objects, active scene is now 'field_11'
[map] the character server knows this map server holds 2 map(s).
```

The `listening for players` line means it is reachable. The `loaded` lines mean the world is actually up, not
just requested. **The last line is the one people forget to read** — until the character server knows what this
server holds, nobody can be sent here. It says `0 map(s)` first and then the real number; that is correct, the
maps come up after the link does.

The character server's own terminal says the same thing from its side:

```
[char] map server 'YourMac-1a2b3c4d' at 127.0.0.1:7788 holds 2 map(s): town_0, field_11
```

**Two doors, two ports.** Players connect on **7777**. The character server pushes characters in on **7788**.
They are different conversations and neither port does the other's job.

**Read the map lines every time** — they are the only place the server tells you which maps it is holding, and a
map missing from that list has no creatures in it for anyone. A server holds the starting town plus the maps
listed under *Which maps the server holds open* below.

**Double-check it for real** — this asks the operating system, not the game:

```
lsof -nP -iUDP:7777
```

## 5. The players

Open the normal game build, once per player. Each one signs in, picks a character, and is let into the world by
the character server on the way in.

```
open -n builds/client/unwritten.app
```

Run that once for each player on this Mac; `-n` is what lets a second copy open.

**Worked when:** each player sees the other walking around, and the server prints a line as each arrives —
`[map] expecting <name> — their character arrived ahead of them.` That line is the character server's push
landing, and it appears *before* the player's own connection does.

---

## Pointing it somewhere else

Nothing here is baked into a build. A build made once can be aimed anywhere by setting these before you start it — no rebuild.

| Setting | What it does | Default |
| --- | --- | --- |
| `GAME_SERVER` | Makes this copy BE the server. Any value except `0` or `false` counts as on. | off |
| `GAME_SERVER_HOST` | Which machine a player's game dials. | `localhost` |
| `GAME_SERVER_PORT` | The port both sides use. Must match. | `7777` |
| `ACCOUNTS_URL` | Where the sign-in service lives. | `http://localhost:8080` |
| `CHAR_SERVER_URL` | Where the character server lives. **On a player's build, leaving this unset sends character requests to the sign-in service instead** — which no longer answers them. | `http://localhost:8081` on a server; empty on a player's build |
| `SERVER_SECRET` | How the game server and the character server recognise each other. Must match on both. Never a player's token. **Required on the game server.** | none — the game server says so and nobody can enter |
| `MAP_SERVER_ADDRESS` | How the character server reaches this game server back. Its address on the network, not the player's. | `127.0.0.1` |
| `MAP_SERVER_ARRIVALS_PORT` | The door the character server pushes characters in on. Not the port players use. | `7788` |
| `GAME_MAPS` | Which maps the server holds open besides the starting one, comma-separated. Replaces the authored list rather than adding to it. Server only. | the authored list |

A dedicated server build already knows it is a server; `GAME_SERVER` is for running an ordinary build, or the editor, as one.

**On your own wifi**, players need the host machine's local address rather than `localhost`:

```
ipconfig getifaddr en0
```

Start each player's game with `ACCOUNTS_URL` and `CHAR_SERVER_URL` pointed at that address. `GAME_SERVER_HOST`
is now only a fallback — the character server tells each player which machine and which port to go to, so a
build aimed at one host still ends up wherever it is sent.

---

## How somebody gets into the world

Worth knowing, because when entering fails this is the order to check it in. It copies Ragnarok Online's
arrangement almost exactly.

1. The player signs in at the **sign-in service** and is given a token.
2. Their game asks the **character server** to let them in, with that token and which character.
3. The character server looks up **which game server holds the map they logged out in**.
4. It **hands that game server their whole character** and says they are arriving.
5. Only then is the player told **where to knock**, with a one-time pass.
6. Their game connects to the game server and presents the pass. **A connection that cannot produce one is
   refused before it is anybody** — nothing about who a player is travels on their own connection.

Two things follow from this that are easy to trip over:

- **The game server never reads the database.** Everything that happens in the world is written back through
  the character server. Take the character server away and a running world keeps playing but stops being
  written down.
- **A pass works exactly once.** That is deliberate — a captured one is worthless — and it is why a game that
  loses its connection asks for a new one rather than re-using the old.

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

**Commands the server carries out** — `/summon`, `/dummy`, `/kill`, `/killall`, and now `/gold`, `/item`,
`/weapon` and `/clearbag`. Your game hands the typed line over, the server checks its own copy of the mark,
does the work, and sends the answer back into your chat a moment later. Summoned creatures are visible to
everybody and can actually be hit, because they are born on the machine that owns the world.

The four money-and-item ones are here because **what somebody owns is the server's**. Run on a player's own
machine they lied — `/item` answered *"Gave 5× Healing Potion"* while the bag went
from nothing to nothing, and `/gold` moved the number on screen until the server next spoke and put it back.

**Commands that change only you** — level, XP, stat points, heal, revive, invulnerability, model, binding a
skill, travelling, time of day, weather, move speed — still run in your own game, and refuse unless you are a GM.

The server never believes a game's claim about who is playing it. It reads the mark from the character server
itself, the same way it reads a player's real name and body rather than the one they announced.

### An honest limit

The commands that change only your own character are checked **in your own game**. That is a lock on the door,
not a vault: it stops an ordinary player using them, and it would not stop someone who tampered with their own
copy.

**It is a much smaller gap than it was.** Your bag, your money, your worn gear, your stat points, your hotbar
and your saves all belong to the server now, so nothing in that list can be faked into permanence — a tampered
copy would be corrected by the server's next word. What is left in your own game is level, XP, healing and
invulnerability, which is the remaining work.

---

## When it does not work

**Nothing listening on 7777.** Check the server's log for `No Transport on Network Manager`. That means the network layer gave up during start-up and the rest of the log will look normal — it fails quietly, so look for it rather than assuming a silent log is a healthy one.

**Players connect but nobody has a body.** The log will say which prefab is missing from Resources.

**Players see each other but no mobs.** Only the server spawns creatures now. If the server is not running, or a player is connected to a different one, there is nothing to send. If it *is* running, read its `map … loaded` lines — a map the server never opened has no creatures for anyone, and an old server predates any map you added since it started.

**A whole map is empty.** The server is not holding that map open. See *Which maps the server holds open*.

**Connections fail with `OnDeserialize failed` and players drop straight after connecting.** The server and the game are built from different versions of the code, so they disagree about the shape of the player data. Almost always a server left running from before your last change — stop it, rebuild, run it again.

**A second character server refuses to start.** Same as below, and the same answer: one is already running.
Check with `lsof -nP -iTCP:8081 -sTCP:LISTEN`, and remember a character server that is already up does not need
starting again — `curl -s http://localhost:8081/health` tells you.

**A second game server refuses to start.** It says the port is already taken and quits — that is correct, and it is the message doing its job. One server is already up; stop that one first, or give the new one a different `GAME_SERVER_PORT`. A server that cannot take the port never listens to anything, so it exits rather than sitting in the process list looking alive.

**Something is still listening after you stopped the server.** You have a second one running. List them with start times and stop them all:

```
ps -o pid=,lstart=,command= -ax | grep '[u]nwritten-server'
```

**A player is stuck at the sign-in screen.** That is the sign-in service, not the game server — check step 2.

**A player signs in, picks a character, and then cannot get in.** That is the character server. In order:
`curl -s http://localhost:8081/health` — is it up and can it reach the database? Then read the game server's
log for `the character server knows this map server holds N map(s)` — if that line never appeared, or says `0`,
the character server has nowhere to send anybody. The usual cause is `SERVER_SECRET` missing or different
between the two.

**The game server says "the character server would not take this map server — 401 not a server".** The two have
different `SERVER_SECRET` values. That is the guard doing its job: it refuses rather than half-working. Set the
same value on both and restart the game server. **Watched happening on 2026-09-02** while writing this page.

**"the world would not take you just now".** The character server knows about a game server but could not hand
your character to it — that game server is gone, or its arrivals door (7788) is not reachable. It stops
believing a game server that has said nothing for two minutes, so a crashed one clears itself.

**Everything works but nothing is saved.** The game server writes through the character server. If the
character server is down, the world keeps playing and stops being written down — its log says so each time.

**You restarted the character server and now nobody can enter.** Expected, and it fixes itself: a fresh
character server starts knowing about no game servers at all. The game server says hello again on its own
beat — **up to 30 seconds** — and entering works from then on. Watched on 2026-09-02: refused immediately
after the restart, working once the beat landed. Restart the game server too if you would rather not wait.

**Somebody cannot come back after leaving.** They should be able to; if they are refused as already playing,
that is a bug and the game server's log will name the character.

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

Stop everything — game server, character server and sign-in service:

```
pkill -f 'unwritten-server.app/unwritten'; pkill -f CharServer; pkill -f 'node .*src/server.ts'
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
3. **In Unity, force a recompile and wait for it to finish** — the build compiles what the EDITOR has imported,
   not what is on disk, so a file you just added is invisible to it until then. Skipping this is the most
   common cause of a build failing on code that is perfectly fine. See
   [The Offline Checker Is Not the Editor](../5-ai-learnings/the-checker-is-not-the-editor.md).
4. `tools/build-game.sh server` — build the change in (Unity must be open)
5. run the server again — the command under *The game server ▸ Running it*
6. read its start-up lines, check the map list, and check the `character server knows` line appeared

The database, the sign-in service and the character server do **not** need restarting for a game-code change.
Restart the sign-in service only when you changed something inside `accounts/`, and the character server only
when you changed something inside `charserver/`.

**One exception, and it bites.** If a change alters the SHAPE of a save, the two servers must agree about it —
the character server refuses a game server built against a different one, on purpose, so a half-finished deploy
fails loudly instead of storing somebody's life slightly wrong. That agreement is one string, written in both
`charserver/MapServers.cs` and `MapServerLink.cs`; change it in both, in the same edit. You will know you got
it wrong because the game server says the character server would not take it.

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

One ordinary always-on virtual machine, near the players — Singapore for South-East Asia. It runs all four
things above. Not a match-based game-hosting service: those spin a session up and throw it away, while this world runs continuously.

Unity's own hosting service shut down on 31 March 2026; do not plan around it.
---

## Related

- [Networking — Implementation Notes](../2-systems/networking-implementation-notes.md) — what the thing you just started actually does, and what is still unrun.
- `charserver/README.md` — the character server itself: what it owns, its settings, and how to prove it works.
- [The Offline Checker Is Not the Editor](../5-ai-learnings/the-checker-is-not-the-editor.md) — why a build fails on code the checker says is fine.
- [Querying the Database](querying-the-database.md) — reading back the accounts and characters this runbook creates.
- [Network-Readiness — Combat Audit & Refactor Plan](../2-systems/network-readiness-audit.md) — the July survey that chose this shape.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the routine that now needs a running world before it can start.
- [Roadmap to Launch](roadmap-to-launch.md) — Tier 2, where this sits against the plan.
