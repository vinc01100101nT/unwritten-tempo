---
status:
owner: vince
last-updated: 2026-09-05
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

There is an optional fifth thing, the **player wiki** — a website with a live Server Status page. Nothing in the
game needs it and it is section 6 below.

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
mkdir -p logs && mv -f logs/server.log logs/server-prev.log 2>/dev/null
CHAR_SERVER_URL=http://127.0.0.1:8081 SERVER_SECRET=change-me MAP_SERVER_ADDRESS=127.0.0.1 \
  builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile - 2>&1 \
  | tee logs/server.log
```

**To start with log categories already switched on**, put `LOG_CATEGORIES=` in front of the command with the
categories you want, separated by commas — `LOG_CATEGORIES=hit,passive,buff,cast` — or `LOG_CATEGORIES=all`. It
only switches categories on; the server writes a line saying what it applied, and refuses a word that is not a
category by name. See [Reading the Game Log](../../0-process/reading-the-game-log.md).

**It keeps its log now.** Everything the server decides — a refused cast, a connection turned away, a character it
could not write down, a map that would not come up — goes to `logs/server.log` as well as to your screen, and the run
before it is kept as `logs/server-prev.log`. It used to go only to the terminal, which meant the one account of what
the server actually did could not be read back afterwards by anyone, including Claude.

Opens the game for one player. Run it again for each extra player. **The setting is not optional** — see below.

```
CHAR_SERVER_URL=http://127.0.0.1:8081 builds/client/unwritten.app/Contents/MacOS/unwritten &
```

Serves the player wiki, whose **Server Status** page reads the live numbers from the character server. Optional
— nothing in the game needs it. Then open `http://127.0.0.1:8099/#status`.

```
cd wiki && python3 -m http.server 8099 --bind 127.0.0.1
```

Proves the public status door answers. Want `playersOnline`, `servers` and `topPlayers`.

```
curl -s http://localhost:8081/stats
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

### Backing up

Saves the whole database to a dated file, proves the file is readable, and keeps the last fourteen. It prints
how many characters it read back **out of the file**, so a backup that saved nothing cannot report success.

```
tools/backup-database.sh
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

### The public status door

One route on this server needs no sign-in and no shared secret: `/stats`. It answers, as plain JSON, how many
players are in the world, which maps are standing on which game-server process, and the twenty characters who
have earned the most experience. It carries counts and public names ONLY — never an account, never a position.

```
curl -s http://localhost:8081/stats
```

**Worked when:** it answers with `playersOnline`, `servers` and `topPlayers`. With no game server running,
`servers` is an empty list and `playersOnline` is 0, which is the honest answer rather than a fault.

It works the answer out at most once every 5 seconds and hands everyone the same copy, so a status page open
on a thousand screens costs this server exactly what one screen costs. It is read by the player wiki's **Server
Status** page — section 6 below.

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
mkdir -p logs && mv -f logs/server.log logs/server-prev.log 2>/dev/null
CHAR_SERVER_URL=http://127.0.0.1:8081 SERVER_SECRET=change-me MAP_SERVER_ADDRESS=127.0.0.1 \
  builds/server/unwritten-server.app/unwritten -batchmode -nographics -logFile - 2>&1 \
  | tee logs/server.log
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
[map] the character server knows this map server holds 1 map(s).
```

The `listening for players` line means it is reachable. The `loaded` line means the world is actually up, not just
requested. **The last line is the one people forget to read** — until the character server knows this server is
there, nobody can be sent here. It says `0 map(s)` first and then the real number; that is correct, the map comes
up after the link does.

**One map is the whole of what it starts with, and that is right.** A game server is no longer told which zones
to hold. It comes up with one map purely so there is somewhere to stand, and everything after that is decided
while people play: the character server tells it to bring a zone up when somebody wants one, and it lets a zone
go again once nobody has been in it for forty-five seconds. So this list is a snapshot, and it changes all
evening. See *Which maps a server holds open* below.

The character server's own terminal says the same thing from its side, with how busy that server is:

```
[char] map server 'YourMac-1a2b3c4d' at 127.0.0.1:7788 holds 1 map(s): town_0 [0 player(s), 15 creature(s)]
```

Those two numbers are what it weighs when it decides which server a new zone should be brought up on.

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
CHAR_SERVER_URL=http://127.0.0.1:8081 builds/client/unwritten.app/Contents/MacOS/unwritten &
```

Run that once for each player on this Mac. It starts the game's own program rather than the app, which is what
lets a second copy open AND what carries the setting to it.

**Why not `open -n builds/client/unwritten.app`.** `open` hands the app to the operating system to start, and the
operating system passes it **none** of the settings from the terminal that ran it — so the game comes up with no
character server to ask, cannot be let into the world, and is refused at the door. It now says so and stops
rather than retrying forever, but it still cannot get in. If you prefer `open`, it has to carry the setting
itself:

```
open -n --env CHAR_SERVER_URL=http://127.0.0.1:8081 builds/client/unwritten.app
```

**Worked when:** each player sees the other walking around, and the server prints a line as each arrives —
`[map] expecting <name> — their character arrived ahead of them.` That line is the character server's push
landing, and it appears *before* the player's own connection does. **Watched happening on 2026-09-02** with two
accounts on this Mac.

**If a player connects and instantly drops, over and over,** they were started without `CHAR_SERVER_URL` — see
the troubleshooting entry below. That is the one failure this step has actually produced.

---

## 6. The player wiki

Optional — nothing in the game needs it, and none of the five steps above wait for it.

The wiki is a folder of files with no program behind it, so it needs something to hand those files to a
browser. Python's built-in one is enough and needs nothing installed:

```
cd wiki && python3 -m http.server 8099 --bind 127.0.0.1
```

Leave that terminal open, then open `http://127.0.0.1:8099` in a browser.

**Worked when:** the wiki loads with its left-hand list of pages.

`--bind 127.0.0.1` keeps it on this machine only. Drop it and anyone on your network can read it — which is
what you want on a hosted machine and not on a laptop in a cafe.

### The Server Status page

`http://127.0.0.1:8099/#status` shows how many players are in the world right now, which maps are standing on
which game-server process, and who has earned the most experience. It reads those numbers from the character
server's public door (section 3 above) and asks again every 5 seconds while you have it open.

**Two of those numbers cannot be fresher than the game makes them**, however often the page asks: a level
reaches the database within 15 seconds of being earned, and a game server reports how many players it is
holding every 30 seconds. So a level appears within about 20 seconds of a level-up, and a player count within
about 35 seconds of somebody arriving.

**A browser treats the wiki and the character server as two different sites**, because they answer on two
different ports — so it refuses to hand the page that reply unless the character server names the wiki's
address as allowed. It allows `http://127.0.0.1:8099` unless told otherwise. Serve the wiki from anywhere else
and the character server must be started with that address in `WIKI_ORIGINS`. Setting it REPLACES that default
rather than adding to it, so list every address that needs to read it, separated by commas:

```
WIKI_ORIGINS=http://wiki.example.com SERVER_SECRET=change-me charserver/run.sh
```

**That refusal is SILENT** — the page simply says it cannot reach the character server, and the browser writes
nothing anywhere you would look. So if the status page says that while `curl -s http://localhost:8081/stats` in
a terminal answers fine, this setting is the thing to check.

The experience column reads *not recorded* for a character whose lifetime figure was never written down. That
is filled in for every existing character the next time the sign-in service starts.

## Pointing it somewhere else

A build made once can be aimed anywhere by setting these before you start it — no rebuild. A copy you HAND TO
SOMEBODY ELSE has no terminal to set them in, so it carries the two addresses in a file instead
(`unity/Assets/StreamingAssets/server.txt`); anything set here still wins over that file, so nothing about your
own builds or the servers changes.

| Setting | What it does | Default |
| --- | --- | --- |
| `GAME_SERVER` | Makes this copy BE the server. Any value except `0` or `false` counts as on. | off |
| `GAME_SERVER_HOST` | Which machine a player's game dials. | `localhost` |
| `GAME_SERVER_PORT` | The port both sides use. Must match. | `7777` |
| `ACCOUNTS_URL` | Where the sign-in service lives. A whole address, so `https://…` needs nothing else changed. | `http://localhost:8080` |
| `CHAR_SERVER_URL` | Where the character server lives. **Required on a player's build too:** left unset, the game has nobody to ask to be let in, cannot be announced to the world, and refuses to dial rather than being turned away at the door. On a copy you hand out, put it in the settings file instead. | `http://localhost:8081` on a server; empty on a player's build — which is why you must set it |
| `BIND_HOST` | Which addresses the sign-in service and the character server answer on. Set it to `127.0.0.1` on a hosted machine so the only way in is through the encrypting front door. Set on BOTH. | `0.0.0.0` — every address |
| `SERVER_SECRET` | How the game server and the character server recognise each other. Must match on both. Never a player's token. **Required on the game server.** | none — the game server says so and nobody can enter |
| `MAP_SERVER_ADDRESS` | How the character server reaches this game server back. Its address on the network, not the player's. | `127.0.0.1` |
| `WIKI_ORIGINS` | Which website addresses a browser may read the character server's public status from — the wiki, and nothing else. Comma-separated; replaces the default rather than adding to it. Set on the CHARACTER server. | `http://127.0.0.1:8099` |

**There is no setting for which maps a server holds, and there is nothing missing.** There used to be one. A game
server is now told nothing about zones when it starts: the character server decides which server a zone is
brought up on, at the moment somebody wants it, and each server lets a zone go again once nobody has been in it.
Nothing to configure, and nothing to keep in step between machines.
| `MAP_SERVER_ARRIVALS_PORT` | The door the character server pushes characters in on. Not the port players use. | `7788` |

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

## Which maps a server holds open

**No server is told.** A map server is handed no list of zones when it starts. It comes up holding one map,
purely so there is somewhere to stand, and everything after that is decided while people are playing:

- **A zone is brought up when somebody wants it.** When a player enters a map nobody is holding, the character
  server picks the **quietest** map server — weighing how many players and creatures each already has — tells it
  to bring that zone up, and waits until the zone is genuinely standing before pushing the character across.
- **A zone is let go when nobody is in it.** Each server checks its own maps every few seconds; one that has
  stood empty for forty-five seconds is unloaded, creatures and all. It never lets go of its last map.
- **A zone that comes back is fully populated**, because loading it runs its spawners again. There is nothing to
  remember about what was in it.

So the `map … loaded` and `letting … go` lines in the start-up and running output are the truth about what a
given server is holding at any moment, and that changes through the evening.

Holding a map costs the server real work, because every creature in it is thinking whether or not anyone is
there — which is the whole reason empty ones are let go. Measured on this Mac with nobody playing: the starting
town alone sits near 13% of one processor core; the town plus four fields — 129 creatures — near 16%; every map
in the world at once — 1,809 creatures — near 50%, with the server's own heartbeat starting to slip from 60 to
53–55.

**Treat those numbers as a rough shape, not a budget.** They were all taken with nobody playing, and this game
switches a creature's brain off when no player is near it — so every figure above is the cost of *sleeping*
creatures. What an awake creature costs, and what a player costs, has never been measured.

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

**Every command except `/help` is carried out by the server** — `/summon`, `/dummy`, `/kill`, `/killall`, `/log`,
`/gold`, `/item`, `/weapon`, `/clearbag`, `/lvl`, `/xp`, `/ringxp`, `/statpts`, `/heal`, `/revive`, `/invuln`,
`/bind`, `/tp`, `/speed`, `/model`, `/time` and `/weather`. Your game hands the typed line over, the server checks its own copy of the mark, does the work on your
character, sends your game the result, and sends the answer back into your chat a moment later. Each one shows
up in the server's log as *"<name> ran '/…'"*. Summoned creatures are visible to everybody and can actually be
hit, because they are born on the machine that owns the world.

They are here because **everything about a character is the server's**. Run on a player's own machine they
lied — `/item` answered *"Gave 5× Healing Potion"* while the bag went from nothing to nothing, and `/statpts`
showed 61 points while the server held 1, so every point spent past the real ones was silently refused.

`/help` only lists the commands, so it runs in your own game and anybody may type it.

**`/model`, `/time` and `/weather` change what everybody sees.** `/model` changes your body on every screen that
can see you. `/time` and `/weather` change the sky for every player on that game server, and anybody who joins
it later gets the same sky. None of the three is saved: your model goes back to your character's own when you
sign in again or move to a map another game server holds, and the time and weather go back to the real clock and
the calendar when that game server restarts.

The server never believes a game's claim about who is playing it. It reads the mark from the character server
itself, the same way it reads a player's real name and body rather than the one they announced.

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

**A player connects and immediately reconnects, over and over, and the server says "refused a connection nobody announced — no good ticket."** The game was started without `CHAR_SERVER_URL`, so it never asked the character server to let it in and dialled the world with no ticket — and a map server refuses every connection it was not told to expect. **Watched happening on 2026-09-02**, from launching the built game with `open`, which passes it none of the terminal's settings. Start it the way *The players* above says. The game now says this out loud and stops instead of retrying, so if you see the old silent loop you are running a build from before that.

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

**That middle one stops EVERY character server on this machine**, including one you started on a spare port to
test something. There is no way to stop only one of them by name, so if you are running two, stop the one you
mean by its port:

```
lsof -nP -iTCP:8091 -sTCP:LISTEN     # the port yours is on — read the PID from the second column
kill <that PID>
```

And the wiki, if you started it (section 6):

```
pkill -f 'http.server 8099'
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

One ordinary always-on virtual machine, near the players. It runs all four things above. Not a match-based
game-hosting service: those spin a session up and throw it away, while this world runs continuously.

**Which machine, from whom, and what it costs is [Where the Game Is Hosted](../../1-planning/where-the-game-is-hosted.md)** — the choice lives there so
there is one place to argue with it. Unity's own hosting service shut down on 31 March 2026; do not plan
around it.

### Three things that are only true once it leaves your machine

**The two web services must stop being readable.** The sign-in service and the character server speak plain
text. On your desk nothing can read it; over the internet a password and a save can be read by anything on the
path. So on a hosted machine one program stands in front of both, holds the certificate and renews it by
itself — `deploy/Caddyfile` is the whole configuration, with the domain names and the checks to prove it.
Start both services with `BIND_HOST=127.0.0.1` in the same change: a certificate on the front door does nothing
while the plain door beside it is still open to the internet. **The world servers are deliberately not behind
it** — players reach those on a different kind of connection that certificates do not cover, and its protection
is the one-time entry ticket the character server hands out.

**A copy with no terminal carries its addresses.** `unity/Assets/StreamingAssets/server.txt` holds
`ACCOUNTS_URL` and `CHAR_SERVER_URL` — the same two names you would set in a terminal, and a terminal still
wins where there is one. Fill it in, then build the copies you give people. **Both are needed:** signing in
uses the first, and without the second nobody can enter the world at all. **This is also what the EDITOR
reads** when it was started from the Dock or Unity Hub instead of a terminal, which is why the file ships
pointing at this machine rather than empty — an editor with no addresses can sign in and then cannot enter.

**Back the database up on a schedule.** It is the only thing here that exists nowhere else — the code is in git
and every build can be rebuilt, but a lost database is lost players. On the hosted machine, once a day:

```
10 4 * * * BACKUP_DIR=/var/backups/unwritten /srv/unwritten/tools/backup-database.sh >> /var/log/unwritten-backup.log 2>&1
```

To put one back — this destroys what is in the database now, so read it twice:

```
pg_restore --clean --if-exists -d "$DATABASE_URL" /var/backups/unwritten/unwritten-2026-09-08-0410.dump
```

When the world later runs on more than one machine, the character server and the world servers still speak
plain text to each other. That is fine only while they share a private network, which every host offers within
one region — never over the open internet.
---

## Related

- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — what the thing you just started actually does, and what is still unrun.
- `charserver/README.md` — the character server itself: what it owns, its settings, and how to prove it works.
- [The Offline Checker Is Not the Editor](../5-ai-learnings/the-checker-is-not-the-editor.md) — why a build fails on code the checker says is fine.
- [Querying the Database](querying-the-database.md) — reading back the accounts and characters this runbook creates.
- [Network-Readiness — Combat Audit & Refactor Plan](../2-systems/networking/readiness-audit.md) — the July survey that chose this shape.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the routine that now needs a running world before it can start.
- [Roadmap to Launch](roadmap-to-launch.md) — Tier 2, where this sits against the plan.
