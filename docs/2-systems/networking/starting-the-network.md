---
status:
owner: vince
last-updated: 2026-10-02
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

There is an optional fifth thing, the **website** — the game's site, with the player wiki inside it and a live
Server Status page. Nothing in the game needs it; the download server serves it, and it is section 6 below.

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

Starts the database, if that check said it was down. (On Windows it asks for administrator permission once.)

```
tools/platform.sh db-start
```

Starts the sign-in service. Leave that terminal open.

```
cd accounts && npm start
```

Proves the sign-in service is up **and** can reach the database. Want `{"ok":true}`.

```
curl -s http://localhost:8080/health
```

Starts the character server. Leave that terminal open too. It reads the SAME settings file the sign-in service
reads — `settings.env` at the top of the project, including `SERVER_SECRET` — so nothing is typed on the line.

```
charserver/run.sh
```

Proves the character server is up **and** can reach the database. Want `{"ok":true,...}`.

```
curl -s http://localhost:8081/health
```

Runs the game server. It reads its settings from `settings.env` too — the character server's address, the
secret the two servers share, and the address it is reached at — and keeps its log (below).

```
tools/run-map-server.sh
```

**To start with log categories already switched on**, put `LOG_CATEGORIES=` in front of the command with the
categories you want, separated by commas — `LOG_CATEGORIES=hit,passive,buff,cast` — or `LOG_CATEGORIES=all`. It
only switches categories on; the server writes a line saying what it applied, and refuses a word that is not a
category by name. See [Reading the Game Log](../../0-process/reading-the-game-log.md).

**It keeps its log now.** Everything the server decides — a refused cast, a connection turned away, a character it
could not write down, a map that would not come up — goes to `logs/server.log` as well as to your screen, and the run
before it is kept as `logs/server-prev.log`. It used to go only to the terminal, which meant the one account of what
the server actually did could not be read back afterwards by anyone, including Claude.

Opens the game for one player. Run it again for each extra player. It dials the addresses `tools/build-game.sh`
wrote into it from `settings.env` — see step 5.

```
tools/platform.sh open-game
```

Serves the website — the wiki and the **Server Status** page, which reads the live numbers from the character
server — from the update folder. Optional — nothing in the game needs it. The first line copies the site and the
wiki's pages into the update folder; then open `http://127.0.0.1:8090/#status`.

```
tools/publish-update.py --site-only
tools/serve-updates.sh
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

Turns on copying every finished change-log file into the archive folder, at least once a minute. It changes
nothing until Postgres restarts, and prints the restart command. Run it again after the restart: it says whether
files are arriving.

```
tools/backup-start-archiving.sh
```

Takes a full copy of the database, checks every file in it, keeps the newest three, and deletes archived
change-log files older than the oldest copy kept.

```
tools/backup-full-copy.sh
```

Rebuilds a spare database on port 5433 as it was at a moment, checks it, prints how long that took, then stops
the spare and deletes it. It never connects to the live database.

```
tools/backup-rehearse-restore.sh "2026-10-02 14:30:30"
```

Each of the three says what it would do, and changes nothing, with `--dry-run` after it.

### Handing out an update

The full explanation is [Handing out updates](#handing-out-updates), at the end of this page.

Builds the launcher, and the small program Windows players download to fetch it — once, and again only when either
changes or `PATCH_URL` does. A launcher change on its own is then published with
`tools/publish-update.py --launcher-only`: no game build is read and no new version is written.

```
tools/build-launcher.sh
```

Builds the Windows game and the Mac game, then publishes both as one update into `builds/patch/`. Write any news in
`launcher/news.json` first. Add `--dev` for devices on your own Wi-Fi, and `--server` to rebuild the map server too
(stop it first).

```
tools/build-and-publish.sh --dev
```

Serves the update folder — the website with its Download page, the launcher and every update — to this computer and every device
on your Wi-Fi. Leave that terminal open (Ctrl+C stops it). It prints only the addresses that answered when it tried
them from this computer, and a red line when `PATCH_URL` — the address players are given — does not answer. Another
device opens one of the printed addresses in a browser and downloads the launcher from there:

```
tools/serve-updates.sh
```

Then, to try the launcher on this computer too, in a second terminal:

```
tools/platform.sh open-launcher
```

Copies the update to the rented machine, `version.txt` last. `rsync` is not installed on the Windows PC — run this step from the Mac.

```
rsync -a --delete --exclude version.txt builds/patch/ host:/srv/unwritten/patch/
rsync -a builds/patch/version.txt host:/srv/unwritten/patch/
```

### Checking what is running

Asks the operating system whether anything is listening for players.

```
tools/platform.sh port-info 7777
```

Asks whether all four are listening at once.

```
tools/platform.sh port-info 8080 8081 7788 7777
```

Lists what is listening on each of the game's ports, and when each one started.

```
tools/platform.sh port-info 7777 8080 8081
```

This computer's address on your wifi, for players on other machines.

```
tools/platform.sh lan-address
```

### Stopping

Stops the server on its own — the common one, between changes.

```
tools/platform.sh stop-port 7777
```

Stops the game server, the character server and the sign-in service together.

```
tools/platform.sh stop-port 7777 8081 8080
```

Stops the database. You rarely want this.

```
tools/platform.sh db-stop
```

### GM accounts

Lets that account type chat commands. Set `false` to take it away. They must sign out and back in.

```
psql -d unwritten -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

On the Windows PC the database asks for a password, so there the same line reads it from `settings.env` (run it from the top of the project):

```
psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

Lists who has it.

```
psql -d unwritten -c "select email, is_gm from account order by created_at;"
```

On the Windows PC:

```
psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -c "select email, is_gm from account order by created_at;"
```

## 0. The settings file

Every secret and every address the four programs need lives in ONE file, `settings.env` at the top of the
project. It is never committed. Make it once:

```
cp settings.env.example settings.env
```

Then fill in `JWT_SECRET` and `SERVER_SECRET` (the file says how to make each). Everything else is already right
for your desk. Every command on this page reads it, and a setting typed in front of a command still wins.

## 1. The database

Check whether it is already up:

```
pg_isready
```

Expect `accepting connections`. **If it says anything else**, start it (on Windows it asks for administrator permission once):

```
tools/platform.sh db-start
```

Then check again with `pg_isready` — it takes a second or two. Do not go on until it says `accepting connections`; the sign-in service and the character server will not come up without it.

Postgres 17 is installed to start by itself — through Homebrew as **postgresql@17** on the Mac, as the Windows service **postgresql-x64-17** on the PC — so most of the time this step is just the check. You would only need the start command after stopping it by hand, or if the automatic start failed.

The game's data lives in a database called **unwritten** on the standard port **5432**.

To stop it (you rarely want to):

```
tools/platform.sh db-stop
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

It listens on **port 8080**. It signs people in and does nothing else — the characters moved out of it. At start it
prints every address it answered at when it tried itself, and a red line when the address game copies are built to
dial (`PUBLIC_ACCOUNTS_URL`, or `ACCOUNTS_URL` when that is empty) does not answer — said again whenever this
computer's addresses change.

## 3. The character server

Owns every character: the list on the character-select screen, creating and deleting one, and every save. It
is also the door into the world — it decides which game server a player is sent to, and hands their whole
character over before they get there.

It is a separate program, written in C#, living in `charserver/`. It needs the .NET 10 tools. On the Mac:

```
brew install dotnet
```

On Windows:

```
winget install Microsoft.DotNet.SDK.10
```

Start it. It reads the **same** database address and the **same** signing secret the sign-in service reads —
which is why this line loads that file first. A different value for either is a bug, not a configuration: they
would be looking at different databases, or refusing each other's players.

Run it **from the top of this project** — it finds its own settings from there, and works whatever folder you
are standing in.

It exists because loading those settings by hand has a trap in it: `. .env` makes zsh search your PATH instead
of the folder you are in, so it reports *no such file or directory* while the file is sitting right there. That
caught us twice on 2026-09-02. The script loads `settings.env` with nothing to mistype, and a setting typed in
front of it still wins.

```
charserver/run.sh
```

It builds the server once and runs it, and **starts it again 5 seconds after it stops** — after a crash, nobody has
to notice and start it by hand. Three stops are not restarted: a clean stop, the server refusing to start because
its port is already taken (another one is running), and Ctrl+C, which stops it for good. Because Ctrl+C ends the
script, `restart-servers.sh` runs it again from the top, and that rebuilds the server first. On the rented machine
`deploy/unwritten-charserver.service` starts it at boot.

For **40 seconds after it starts it lets nobody enter the world** ("the world is starting up — try again in N
seconds"). Which game server holds whom lives only in its memory, so a fresh one knows none — and a game server
holding saves it could not send during an outage does not report in until those saves have landed. Letting
somebody in sooner could read their character before their newest save arrived.

Leave that terminal open. In another:

```
curl -s http://localhost:8081/health
```

**Worked when:** it answers `{"ok":true,"models":52}` or similar. That check asks the database every time, so
`ok` means it can actually serve a character rather than merely being alive.

It listens on **port 8081**. At start it prints every address it answered at when it tried itself, and a red line
when the address game copies are built to dial (`PUBLIC_CHAR_SERVER_URL`, or `CHAR_SERVER_URL` when that is empty)
does not answer — said again whenever this computer's addresses change.

**`SERVER_SECRET` is how the two servers recognise each other.** It is NOT a player's sign-in token and the two
are never interchangeable: a person signing in must not be able to register a game server or read somebody
else's character, and a game server does not sign in. It lives in `settings.env` (never committed) and nowhere
else: `charserver/run.sh` loads it from there and refuses to start without it, and `tools/run-map-server.sh`
reads the same line so the two can never disagree. Generate one with `openssl rand -base64 32`.

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
on a thousand screens costs this server exactly what one screen costs. It is read by the website's **Server
Status** page and the status box on its Home page — section 6 below.

## 4. The game server

The server is a separate build of the game with no window. You only rebuild it when the game's code has changed — otherwise skip straight to running it.

### Building it

**Unity must be open on this project**, because the build runs inside the editor you already have running. Then:

```
tools/build-game.sh server
```

Takes a few minutes. Everything it makes lands in `builds/` inside this project, and git is told to ignore that folder. Add `--linux` for a build to deploy to a hosted machine; plain gives you one that runs on this computer — a Mac app on the Mac, an `.exe` on Windows.

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
tools/run-map-server.sh
```

Run it from the top of this project.

**`SERVER_SECRET` is required.** Without it in `settings.env` the script says so and stops: the character
server will not hand players to a game server it cannot recognise.

**Worked when:** it prints these lines and keeps running —

```
[map] listening for arrivals on 7788 as 'YourMac-1a2b3c4d'
[server] listening for players on UDP port 7777 at:
[server]   127.0.0.1:7777      Loopback Pseudo-Interface 1
[server]   192.168.1.12:7777   Ethernet
[server] MAP_SERVER_ADDRESS 192.168.1.12 is one of this computer's addresses — that is the address players are sent to.
[server] world up — loading map 'lastline'
[server] map 'lastline' loaded — 15 root objects, active scene is now 'lastline'
[map] the character server knows this map server holds 1 map(s).
```

The `listening for players` lines list every address this computer holds right now — nothing else is printed as
an address. **A red `MAP_SERVER_ADDRESS … is not one of this computer's addresses` line means players are being
sent somewhere this computer is not** (Tailscale not connected, the Wi-Fi address changed): fix the network or the
setting before anyone tries to join. Both are said again whenever this computer's addresses change, so an adapter
that comes up later is reported when it does. The `loaded` line means the world is actually up, not just
requested. **The last line is the one people forget to read** — until the character server knows this server is
there, nobody can be sent here. It says `0 map(s)` first and then the real number; that is correct, the map comes
up after the link does.

**One map is the whole of what it starts with, and that is right.** A game server is no longer told which zones
to hold. It comes up with one map purely so there is somewhere to stand, and everything after that is decided
while people play: the character server tells it to bring a zone up when somebody wants one, and it lets a zone
go again once nobody has been in it for forty-five seconds. So this list is a snapshot, and it changes all
evening. See *Which maps a server holds open* below.

The character server's own terminal says the same thing from its side, with how busy that server is — but only
after it has tried the address it will hand characters to, and only when something in the line changed (a map, a
count, the address answering or not), so an idle server checking in every 30 seconds writes nothing:

```
[char] map server 'YourMac-1a2b3c4d' answers at 127.0.0.1:7788 and holds 1 map(s): lastline [0 player(s), 15 creature(s)]
```

The player and creature counts are what it weighs when it decides which server a new zone should be brought up on.

**A red `map server '…' does not answer at …` line means nobody can enter that server**: the character server
delivers every arriving character to that address, and nothing answered there. Usually `MAP_SERVER_ADDRESS` names
a network that is not up (Tailscale not connected).

**Two doors, two ports.** Players connect on **7777**. The character server pushes characters in on **7788**.
They are different conversations and neither port does the other's job.

**Read the map lines every time** — they are the only place the server tells you which maps it is holding, and a
map missing from that list has no creatures in it for anyone. A server holds the starting town plus the maps
listed under *Which maps the server holds open* below.

**Double-check it for real** — this asks the operating system, not the game:

```
tools/platform.sh port-info 7777
```

## 5. The players

Open the normal game build, once per player. Each one signs in, picks a character, and is let into the world by
the character server on the way in.

```
tools/platform.sh open-game
```

Run it once for each player on this computer. It starts the game's own program directly, which is what lets a
second copy open.

**Where it dials.** `tools/build-game.sh client` writes two addresses into every build it makes, from
`settings.env`: `PUBLIC_ACCOUNTS_URL` and `PUBLIC_CHAR_SERVER_URL` once the rented server exists, this machine's
`ACCOUNTS_URL` and `CHAR_SERVER_URL` while those two are empty. So while you have no rented server, a fresh build
dials this computer by itself. Once it does, point a build at this computer by typing both in front of it:

```
ACCOUNTS_URL=http://127.0.0.1:8080 CHAR_SERVER_URL=http://127.0.0.1:8081 tools/platform.sh open-game
```

**Worked when:** each player sees the other walking around, and the server prints a line as each arrives —
`[map] expecting <name> — their character arrived ahead of them.` That line is the character server's push
landing, and it appears *before* the player's own connection does. **Watched happening on 2026-09-02** with two
accounts on the Mac.

**If a player connects and instantly drops, over and over,** their copy has no `CHAR_SERVER_URL` — a build made
before `settings.env` had one; rebuild it — see the troubleshooting entry below. That is the one failure this step has actually produced.

---

## 6. The website

Optional — nothing in the game needs it, and none of the five steps above wait for it.

The website is the game's site: Home, News, Game Guide, Wiki, Roadmap, Server Status, Media and Download. It is a
folder of files with no program behind it, served by the same download server that hands out the launcher and the
updates, from the same folder. Its page is `website/index.html`; its wiki pages are the files in `wiki/content/`, and
its news is the launcher's own `launcher/news.json`. Publishing copies all of them into the update folder — a full
game publish does it, and so does this, which copies the site alone and touches nothing else:

```
tools/publish-update.py --site-only
tools/serve-updates.sh
```

Leave that terminal open, then open `http://127.0.0.1:8090` in a browser. Run the first line again after any
wiki edit; the server needs no restart.

**Worked when:** the Home page loads, and the Wiki page shows its left-hand list of pages.

The download server listens on every address of this computer, so the site does too — another device opens one of
the addresses it printed as answering.

### The Server Status page

`http://127.0.0.1:8090/#status` shows how many players are in the world right now, which maps are standing on
which game-server process, and who has earned the most experience. It reads those numbers from the character
server's public door (section 3 above) and asks again every 5 seconds while you have it open. The Home page's
status box reads the same numbers.

**It asks the character server address in `settings.env`** — `PUBLIC_CHAR_SERVER_URL`, or `CHAR_SERVER_URL`
while that is empty, the same address the game is built with. `tools/publish-update.py` writes it into `site.json`
beside the website every time it publishes, so no page carries an address of its own. Another device can read the
numbers only when that address is one the device can reach: on your Wi-Fi or Tailscale, put this computer's address
in `PUBLIC_CHAR_SERVER_URL` and publish the site again (`tools/publish-update.py --site-only`).

**Two of those numbers cannot be fresher than the game makes them**, however often the page asks: a level
reaches the database within 15 seconds of being earned, and a game server reports how many players it is
holding every 30 seconds. So a level appears within about 20 seconds of a level-up, and a player count within
about 35 seconds of somebody arriving.

**A browser treats the website and the character server as two different sites**, because they answer on two
different ports — so it refuses to hand the page that reply unless the character server names the website's
address as allowed. Unless told otherwise it allows the download server's own address: the one in `PATCH_URL`,
plus `http://127.0.0.1` and `http://localhost` on `PATCH_PORT` (none at all when `PATCH_URL` is empty). Serve the site
from anywhere else and the character server must be started with that address in `WIKI_ORIGINS`. Setting it
REPLACES that default rather than adding to it, so list every address that needs to read it, separated by commas:

```
WIKI_ORIGINS=https://play.example.com
```

in `settings.env`, then restart the character server.

**That refusal is SILENT** — the page simply says it cannot reach the character server, and the browser writes
nothing anywhere you would look. So if the status page says that while `curl -s http://localhost:8081/stats` in
a terminal answers fine, this setting is the thing to check.

The experience column reads *not recorded* for a character whose lifetime figure was never written down. That
is filled in for every existing character the next time the sign-in service starts.

## Pointing it somewhere else

Every one of these lives in `settings.env` at the top of the project — the one file for every secret and address
(its template, `settings.env.example`, explains each). Typing one in front of a command still wins over the file,
so a build made once can be aimed anywhere without a rebuild. A copy you HAND TO SOMEBODY ELSE has no terminal, so
it carries the two addresses in its own settings file, written there by `tools/build-game.sh`.

| Setting | What it does | Default |
| --- | --- | --- |
| `GAME_SERVER` | Makes this copy BE the server. Any value except `0` or `false` counts as on. | off |
| `GAME_SERVER_HOST` | Which machine a player's game dials before the character server names one. | none — the character server always names it |
| `GAME_SERVER_PORT` | The port the game server listens on for players. A player's game is told it by the character server. | none — the game server stops without it |
| `ACCOUNTS_URL` | Where the sign-in service lives. A whole address, so `https://…` needs nothing else changed. | none — a game with no address cannot sign in |
| `CHAR_SERVER_URL` | Where the character server lives. **Required on a player's build too:** left unset, the game has nobody to ask to be let in, cannot be announced to the world, and refuses to dial rather than being turned away at the door. Every build carries it, written in by `tools/build-game.sh`. | none on a server — it stops without it; on a player's build, whatever the build carries |
| `PUBLIC_ACCOUNTS_URL`, `PUBLIC_CHAR_SERVER_URL` | The two addresses written into every game build instead of the two above, once the rented server exists — and the names the encrypting front door answers to. | empty — builds carry this machine's addresses |
| `PATCH_URL` | Where the launcher downloads updates from; written into the launcher when it is built, and served by the front door. Also the website's address. It may be an encrypting proxy in front of `PATCH_PORT` rather than that port itself. | none — the launcher build, the website server and `start-servers.sh` stop without it |
| `BIND_HOST` | Which addresses the sign-in service and the character server answer on. Set it to `127.0.0.1` on a hosted machine so the only way in is through the encrypting front door. Set on BOTH. `0.0.0.0` is every address, which a desk needs. | none — both stop without it |
| `SERVER_SECRET` | How the game server and the character server recognise each other. Must match on both. Never a player's token. **Required on the game server.** | none — the game server says so and nobody can enter |
| `MAP_SERVER_ARRIVALS_ADDRESS` | Where the character server delivers characters to this game server — a delivery carries a whole character, an entry ticket and the shared server secret as plain text, so on a rented machine this is a private network address, or `127.0.0.1` when both run on one machine. The game server then listens for deliveries on that address only. Set on the GAME server. | empty — the same as `MAP_SERVER_ADDRESS`, which is right only on a desk or a home network |
| `MAP_SERVER_ADDRESS` | The address this game server is reached at — by the character server, which pushes each arriving character to it, AND by players, whom the character server sends there. On a rented server, its public address. | none — the game server stops without it |
| `WIKI_ORIGINS` | Which website addresses a browser may read the character server's public status from — the website, and nothing else. Comma-separated; replaces the default rather than adding to it. Set on the CHARACTER server. | the download server's address: `PATCH_URL`, plus `http://127.0.0.1` and `http://localhost` on `PATCH_PORT` (none at all when `PATCH_URL` is empty) |
| `MAP_SERVER_ARRIVALS_PORT` | The door the character server pushes characters in on. Not the port players use. | none — the game server stops without it |
| `PORT`, `CHAR_SERVER_PORT` | The ports the sign-in service and the character server listen on. | none — each stops without its own |
| `PATCH_PORT` | The port this computer serves the update folder and the website on. | none — the website server and `start-servers.sh` stop without it |

**There is no setting for which maps a server holds, and there is nothing missing.** There used to be one. A game
server is now told nothing about zones when it starts: the character server decides which server a zone is
brought up on, at the moment somebody wants it, and each server lets a zone go again once nobody has been in it.
Nothing to configure, and nothing to keep in step between machines.

A dedicated server build already knows it is a server; `GAME_SERVER` is for running an ordinary build, or the editor, as one.

**On your own wifi**, players need the host machine's local address rather than `localhost`:

```
tools/platform.sh lan-address
```

Set `MAP_SERVER_ADDRESS` to that address, so the character server sends other devices to it rather than to
`127.0.0.1`. Keep `ACCOUNTS_URL` and `CHAR_SERVER_URL` on `localhost` — this computer's own servers use those — and
put that address in `PUBLIC_ACCOUNTS_URL` and `PUBLIC_CHAR_SERVER_URL`, which only go into game builds. Then build
the copies for other devices with **`tools/build-game.sh client --dev`**: a game refuses plain (unencrypted) web
traffic to any machine but its own, and only a development build is allowed it. Never hand a `--dev` copy to a
player. The character server tells each player which machine and which port to go to, so `GAME_SERVER_HOST`
is not needed at all.

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
there — which is the whole reason empty ones are let go. Measured on the Mac with nobody playing: the starting
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

Every chat command except `/help`, `/whereis`, `/mobinfo`, `/whodrops` and `/iteminfo` is for GMs: nearly all of them change something — a
level, a bag, the creatures standing in the world. So **none of those work unless the account is marked a GM.** An
ordinary player who types one is told *That command is for GMs.* and nothing happens. `/help`, `/whereis`,
`/mobinfo`, `/whodrops` and `/iteminfo` only read, and anybody may type them. `/help` lists every command with
one example line each, in two groups — *Commands for everyone* and *GM commands* — and `/help <command>` shows
that command's full usage.

The mark belongs to the **account**, not to a character — being trusted with commands is a fact about a person,
so it covers every character they make.

### Granting it

There is no screen for this on purpose. Grant it with one line:

```
psql -d unwritten -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

On the Windows PC the database asks for a password, so there the same line reads it from `settings.env` (run it from the top of the project):

```
psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

Take it away again by setting `false`. Either way the person must **sign out and back in** for the change to
reach them — the mark is read when their character is loaded, so a game already running still has the old answer.

Check who has it:

```
psql -d unwritten -c "select email, is_gm from account order by created_at;"
```

On the Windows PC:

```
psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -c "select email, is_gm from account order by created_at;"
```

### Where each command actually runs

**Every command except `/help` and `/where` is carried out by the server** — `/summon`, `/unsummon`, `/dummy`, `/kill`, `/killall`,
`/log`, `/gold`, `/item`, `/weapon`, `/mastery`, `/resetskills`, `/clearbag`, `/lvl`, `/xp`, `/ringxp`, `/statpts`, `/heal`,
`/revive`, `/save`, `/invuln`, `/bind`, `/tp`, `/exprate`, `/speed`, `/model`, `/time`, `/weather`, `/whereis`, `/mobinfo`, `/whodrops`
and `/iteminfo`. Your game
hands the typed line over, the server checks its own copy of the mark, does the work on your
character, sends your game the result, and sends the answer back into your chat a moment later. Each one shows
up in the server's log as *"<name> ran '/…'"*. Summoned creatures are visible to everybody and can actually be
hit, because they are born on the machine that owns the world.

They are here because **everything about a character is the server's**. Run on a player's own machine they
lied — `/item` answered *"Gave 5× Healing Potion"* while the bag went from nothing to nothing, and `/statpts`
showed 61 points while the server held 1, so every point spent past the real ones was silently refused.

`/help` only lists the commands, so it runs in your own game and anybody may type it. `/where` prints your own
position, so it runs in your own game too, but only a GM may type it.

**`/whereis`, `/mobinfo`, `/whodrops` and `/iteminfo` may be typed by anybody**, GM or not, and are the only commands
the server takes from a non-GM. All four only read. `/whereis <creature>` lists every map the world spawn list places that creature in by name,
with how many of it each map keeps alive; a test map that fills from a random mix is left out. `/mobinfo <creature>`
shows its catalog entry as it spawns: level, size, element, health, attack, defence, flee, its six stats, experience
and drops — a kill pays no Unsigned. The server answers them because a creature's combat numbers are worked out on the server, never on a
player's machine — the same numbers the Mob Table shows.

**`/model`, `/time` and `/weather` change what everybody sees.** `/model` changes your body on every screen that
can see you. `/time` and `/weather` change the sky for every player on that game server, and anybody who joins
it later gets the same sky. None of the three is saved: your model goes back to your character's own when you
sign in again or move to a map another game server holds, and the time and weather go back to the real clock and
the calendar when that game server restarts.

**`/exprate` is a world event, and reaches every map server, not only yours.** `/exprate <duration> <multiplier>`
(`/exprate 2h 1.5`; the duration takes `s`, `m`, `h` or `d`) multiplies the experience every creature kill pays,
everywhere, until the time is up; `/exprate off` ends it at once, and running it again replaces the rate. Your map
server asks the character server, which writes it in the `world_event` table and pushes the list to every map server
it knows; each map server also asks for the list every 30 seconds, so one that missed the push, restarted, or started
later catches up within half a minute. Every player online is told in chat when an event starts and when it ends —
*Event started — experience ×1.5 for 2 h.* and *Event ended — experience ×1.5.* — and somebody who signs in while
events run is told *Events running now: experience ×1.5 (1 h 20 min left).* Walking to a map on another map server
says nothing new. A map server that only learns of an event on its 30-second ask says *Event running — …, … left.*
instead of "started". The rate is saved, so restarting either server does not end it. Quest rewards and `/xp` are
not multiplied.

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
Check with `tools/platform.sh port-info 8081`, and remember a character server that is already up does not need
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
character server is down, the world keeps playing and nothing is lost: every save that cannot land is kept — on
disk too, one file per character, in an `unsent-saves-<port>` folder in the game server's data folder — and tried
again every 15 seconds until it lands, even after that player has logged out or the game server has restarted.
Its log says "could not write character … down yet" each time, and "not telling the character server about this
map server until N waiting save(s) have landed" while any are waiting. A save refused for good (the character was
deleted, or the save is too large) is kept in a `refused` folder inside it for a person to look at.

**A player connects and immediately reconnects, over and over, and the server says "refused a connection nobody announced — no good ticket."** The game had no `CHAR_SERVER_URL`, so it never asked the character server to let it in and dialled the world with no ticket — and a map server refuses every connection it was not told to expect. **Watched happening on 2026-09-02**, from launching a build that carried no address with `open`, which passes it none of the terminal's settings. A build made by `tools/build-game.sh` now carries the address from `settings.env`; rebuild an older one, or start it the way *The players* above says. The game now says this out loud and stops instead of retrying, so if you see the old silent loop you are running a build from before that.

**You restarted the character server and now nobody can enter.** Expected, and it fixes itself: for its first
**40 seconds** a fresh character server lets nobody in and says so, while every game server says hello again on its
own beat (up to 30 seconds, sooner once any waiting saves have landed). Entering works from then on.

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
tools/platform.sh stop-port 7777
```

Stop everything — game server, character server and sign-in service:

```
tools/platform.sh stop-port 7777 8081 8080
```

Each one is stopped by the port it answers on, never by its name — so a second character server you started on a
spare port to test something is left running. Stop that one by its own port:

```
tools/platform.sh stop-port 8091
```

**Stopped when:** the check below prints nothing at all.

```
tools/platform.sh port-info 7777
```

If it still prints a line, something is still holding the port — see the stale-server entry under *When it does
not work*.

### The full round trip after a change

1. `tools/platform.sh stop-port 7777` — stop the old one
2. `tools/platform.sh port-info 7777` — confirm nothing is left listening (it prints nothing)
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

**A copy with no terminal carries its addresses.** Fill in `PUBLIC_ACCOUNTS_URL` and `PUBLIC_CHAR_SERVER_URL`
in `settings.env` on the computer you build on, then build the copies you give people: `tools/build-game.sh client`
writes those two into each build, under the names `ACCOUNTS_URL` and `CHAR_SERVER_URL`, and nothing else from the
file. **Both are needed:** signing in uses the first, and without the second nobody can enter the world at all.
The EDITOR started from the Dock or Unity Hub reads this machine's own `ACCOUNTS_URL` and `CHAR_SERVER_URL`
straight from `settings.env`, so it keeps talking to the servers on your desk.

**The rented machine has its own `settings.env`**, made from `settings.env.example` the same way: its own
secrets, `BIND_HOST=127.0.0.1`, `MAP_SERVER_ADDRESS` set to its public address, `MAP_SERVER_ARRIVALS_ADDRESS` set
to `127.0.0.1` (or the private address, once game servers run on other machines), `PATCH_DIR`, and the same three
public addresses and `CERT_EMAIL` the front door serves. Everything on that machine — both services, the world
server, the backups and the front door — reads it.

**Back the database up on a schedule.** It is the only thing here that exists nowhere else — the code is in git
and every build can be rebuilt, but a lost database is lost players. Two kinds of backup run side by side, and
each works without the other:

- **The daily copy** (`tools/backup-database.sh`): the whole database in one file at 04:10, the last 14 kept. It
  puts the database back as it was at 04:10 that day.
- **The continuous copy**: Postgres copies every finished change-log file into the folder `WAL_ARCHIVE_DIR` names,
  at least once a minute, and `tools/backup-full-copy.sh` takes a full copy into `BASE_BACKUP_DIR` once a week.
  Together they rebuild the database as it was at any second from the oldest kept full copy until about a
  minute ago — at least the last 14 days, with three weekly copies kept.

Turning the continuous copy on, once, in this order:

1. `tools/backup-start-archiving.sh` — writes the settings. Nothing changes yet.
2. In a quiet hour, restart Postgres. Every player is disconnected for a few seconds.

   ```
   tools/platform.sh db-stop
   tools/platform.sh db-start
   ```

3. `tools/backup-start-archiving.sh` again. Want `Archiving is on.`, and once anything has changed in the
   database, a newest file ending `and it is in the folder`. The account Postgres runs as must be able to write
   to the archive folder: on Linux that is usually `postgres`; on the PC it is the service's Windows account.
4. `tools/backup-full-copy.sh` — the first full copy. Nothing before it can be rebuilt from the archive.

On the hosted machine, put the two folders in its `settings.env` (`WAL_ARCHIVE_DIR=/var/backups/unwritten/wal`,
`BASE_BACKUP_DIR=/var/backups/unwritten/base`) and run both copies from cron — the daily one every day at 04:10,
the full one every Sunday at 04:30:

```
10 4 * * * BACKUP_DIR=/var/backups/unwritten /srv/unwritten/tools/backup-database.sh >> /var/log/unwritten-backup.log 2>&1
30 4 * * 0 /srv/unwritten/tools/backup-full-copy.sh >> /var/log/unwritten-backup.log 2>&1
```

**Watch the archive folder's size.** Each archived file is 16 MB, even when Postgres closes it after a minute
nearly empty, so every minute in which anything changed costs 16 MB: up to about 23 GB a day while players are
on, kept until the oldest full copy is deleted — up to three weeks.

To put the daily copy back — this destroys what is in the database now, so read it twice:

```
pg_restore --clean --if-exists -d "$DATABASE_URL" /var/backups/unwritten/unwritten-2026-09-08-0410.dump
```

Rebuilding the live database itself from the continuous copy is not scripted yet. The monthly rehearsal below
proves the full copies and the archive can rebuild it, and how long that takes.

**Rehearse a restore once a month.** It needs a full copy taken before the moment you note, and touches only a
spare database on port 5433 (`RESTORE_REHEARSAL_PORT` to change it).

1. Note the time, T:

   ```
   date '+%Y-%m-%d %H:%M:%S'
   ```

2. Raise one test character's level, and keep playing it for the next 2 minutes. A rebuild can only stop at a
   moment if the database changed again after it, and a minute later that change has reached the archive.
3. Rebuild to 30 seconds after T. In the `last changed` list it prints, the test character has its new level.

   ```
   tools/backup-rehearse-restore.sh "<T plus 30 seconds>"
   ```

4. Rebuild to 30 seconds before T. The test character has its old level.

   ```
   tools/backup-rehearse-restore.sh "<T minus 30 seconds>"
   ```

5. Write down the date and the `took` line it printed, below.

Rehearsals so far: none.

**Off-site storage comes later.** Until it exists, both kinds of backup sit on the same disk as the database,
so a dead disk takes them with it. Copying them to storage at a different company from the host is the next
step once there is a live database; no storage bucket exists yet.

When the world later runs on more than one machine, the character server and the world servers still speak
plain text to each other. That is fine only while they share a private network, which every host offers within
one region — never over the open internet.

### Handing out updates

A player downloads one small program once (about 5 MB on Windows, 12 MB on a Mac). Every time they open it, it
fetches the **launcher** into their own user folder if it is missing or out of date (about 100 MB), and opens it. The
launcher compares their game files with the update
folder on the hosted machine, downloads only the files that changed, and then lets them press **Start**. A copy
of the game whose version differs from the published one is refused by the character server with *"Your game is
out of date. Close it and run the launcher to update."* — so nobody plays a copy that no longer matches the
server.

**Once — build the launcher.** It is pointed at `PATCH_URL` from `settings.env`, so fill that in first:

```
tools/build-launcher.sh
```

It writes these. `builds/launcher/windows/unwritten-launcher.exe` is the small program a Windows player
downloads (`launcher-updater/`, about 5 MB); only a Windows PC with Visual Studio's C++ build tools can build it, and
anywhere else its folder is removed rather than left holding an older build.
`builds/launcher/windows-patcher/unwritten-launcher.exe` is the Windows launcher that small program fetches (about
100 MB). `builds/launcher/mac/unwritten.app` is the small program a Mac player downloads (`launcher-updater-mac/`,
about 12 MB): one app that runs on both kinds of Mac chip. Only a Mac can compile it, so it travels through git
(below).
`builds/launcher/mac-patcher-arm64/unwritten.app` and `mac-patcher-x64/unwritten.app` are the Mac launchers it
fetches, one per kind of chip (about 100 MB each). Every Mac app carries a build number: the time it was built, and for the small Mac
program the time its program last changed. Each
program carries `launcher.txt`, which is how it knows which server to download from; without it the program finds
nothing. It sits beside the Windows program, and in the Mac app's `Contents/Resources` — a non-program file in
`Contents/MacOS` is not covered by the app's signature, and a Mac then refuses the app as "damaged". Everything the
small program and the launcher write goes in the player's own folder, wherever they put the download —
`AppData/Local/unwritten/` on Windows, `~/Library/Application Support/unwritten/` on a Mac: the launcher in
`patcher/`, the game in `game/`. The launcher's **Uninstall** button removes the game and the launcher; the small
program stays where the player put it, and opening it installs everything again.

**How a Windows launcher change reaches players.** The small program asks the update server for
`windows/patcher.json` — the launcher's size and fingerprint, and the update address players should be using — and
downloads the launcher from `windows/files/` when the one on the computer differs. A launcher opened directly (a
taskbar pin) hands back to the small program first, so it is never left behind. The small program itself never
writes into its own folder, which is what lets it run from Program Files; the launcher replaces the copy in the
player's folder with the published one whenever that folder can be written to, and rewrites the address beside it.

**How a Mac launcher change reaches players.** A Mac launcher is a folder of files, so the update server lists it file
by file, one list per kind of chip: `mac/patcher-arm64.json` and `mac/patcher-x64.json`, with the address players
should be using, the contents in `mac/files/` beside the game's. The small Mac program reads the address the server
last gave (kept in `patcher/launcher.txt` in the player's folder), and the one built into it when that stops
answering. It builds the new launcher beside the old one and swaps the two folders in one step, so the player is
never left without one, and hands the address that answered to the launcher. While the launcher is open the small
program says *"The patcher is already open. Close it to let it update, then open this again."*, and a second launcher
says *"The launcher is already open."* — two launchers never update the same game at once. A Mac launcher opened
directly (from the Dock) that is behind the published one says *"A newer launcher is out. Open the unwritten app you
downloaded, or download it again from"* the website's address, and goes no further. The launcher replaces the small
program the player opened with the one in the Mac game list whenever that one has a higher build number. To check the
small Mac program from a terminal without opening a window, run it with `UNWRITTEN_PATCHER_TEXT=1` set: its lines are
printed instead.

**The first launcher is handed over through the game's own file list.** The launcher players downloaded before
2026-10-11 keeps the game beside itself, cannot update itself, and on **Start** runs whatever the Windows list names
as `start`. So the Windows list names the small program as one more game file and as `start`, and the game as `play`,
which every later launcher runs. That player presses Start once: the small program opens instead of the game,
fetches the launcher, the launcher replaces the first launcher's file with the small program and moves the game
into the player's user folder without downloading it again. `tools/publish-update.py --without-hand-over` publishes
the list the old way and withdraws the small program from every first launcher in one publish. A first launcher
that was unzipped into Program Files never gets that far — it cannot create its game folder — so that player
downloads again from the website; the news says so.

**The Mac launchers are handed over the same way, through the Mac list.** Two kinds are in Mac players' hands: the
first, which runs `start`, and the one the Download page handed out on 2026-10-11, which runs `play`. So the Mac list
carries the small Mac program's files under `unwritten-patcher.app/`, names its program as both `start` and `play`,
the game as `game` (which every launcher from now on runs) and the small program's build number as `fetcher`. That
player presses Start once: the small program opens instead of the game, fetches the launcher, and the launcher
replaces every launcher of that kind the Mac knows about — the apps the Mac's own app index lists under the
launcher's name with no build number, wherever the player keeps them, never one in the Trash — with the small
program. The game was already in the player's folder on a Mac, so nothing is moved or downloaded twice.
`--without-hand-over` withdraws it from the Mac list too. *Not yet seen on a real player's Mac:* a launcher kept in
Downloads may make the Mac ask once whether unwritten may use the Downloads folder.

**The website and its Download page.** Publishing also writes the website into the update folder (section 6) —
the page a browser gets at the update server's address — and zips the small Windows program with its `launcher.txt` into
`download/unwritten-windows.zip`, which the Download page's button offers. A player downloads only that zip; its
first run downloads the launcher, and the launcher's first run downloads the whole game, about 850 MB. It also zips the
small Mac program into `download/unwritten-mac-arm64.zip` and `download/unwritten-mac-x64.zip` — the same app in both,
since it runs on either chip — keeping the permission that lets the program start. Each download is written only by a
publish that includes its platform, so it always holds the small program that platform's list carries. The site
reads the version and the news from the same folder the launcher does.

**Everything Mac can be made on the Windows PC** — and the small Windows program only there. The one part only a
Mac can make, the small Mac program itself, travels through git: `tools/build-launcher.sh` on a Mac compiles it into
`launcher-updater-mac/built/` (kept in Git LFS) with its build number, and says so when it changed — commit and push
that folder, pull on the Windows PC, and `tools/build-launcher.sh` there makes the app from it with the Windows PC's
own `PATCH_URL` and signs it. Repeated builds of the same source give the same bytes, so the copy in git and its
build number change only when the program's code does. Publishing refuses the app when it names a different update server
than `PATCH_URL`, lacks either kind of chip, or holds a file its signature does not cover. A publish that leaves Mac out never looks at
it, and `--launcher-only` skips Mac, saying so, when it is not there. A Mac only runs a signed program; off a Mac,
`tools/platform.sh` signs with `rcodesign` (installed to `~/.local/bin`), the same certificate-free signature a Mac
makes. The launcher build signs the Mac apps, and publishing signs the Mac game again after stamping its version. A Mac that has
never seen the game asks once: **System Settings ▸ Privacy & Security ▸ Open Anyway**. The macOS build profile builds
for both Intel and Apple-chip Macs; a Mac game built with no profile active comes out Intel-only, which an Apple-chip
Mac runs through Apple's translation layer.

**Every update:**

1. Build the game: `tools/build-game.sh client` builds this computer's own copy; add `--mac` for the Mac copy on any
   computer, into `builds/mac/`. It switches the editor to the macOS build profile for the build and back afterwards.
   Unity needs its Mac build module installed, and the editor restarted after installing it.
2. Write the news, if any, in `launcher/news.json` — a list of `{ "date", "title", "text" }`, newest first,
   with one patch-notes entry per date: a date that already has one gets the new changes added to it.
3. Publish: `tools/publish-update.py`. With no option it publishes `builds/client` as the platform of the computer
   it runs on; add `--windows <folder>` and `--mac <folder>` to publish both at once. It stamps one version into
   every build it is given, writes `builds/patch/` with the website, and warns if a build still points at
   this machine.
4. Copy `builds/patch/` to `/srv/unwritten/patch/` on the hosted machine, `version.txt` LAST — the moment it
   lands, the server demands the new version, so the files for it must already be there:

   ```
   rsync -a --delete --exclude version.txt builds/patch/ host:/srv/unwritten/patch/
   rsync -a builds/patch/version.txt host:/srv/unwritten/patch/
   ```

   `rsync` is not installed on the Windows PC — run this step from the Mac. `--delete` removes the files the publish no longer needs, so the folder does not grow with every update.

The character server reads the version from that same folder (`PATCH_DIR` in the rented machine's
`settings.env`), and re-reads it when it changes, so step 4 needs no restart. **A platform left out of a publish
keeps its old version**, so its players are told to update until it is published too.

### Trying the launcher on this computer and your Wi-Fi

No rented machine needed: this computer serves the update folder itself, to its own launcher and to every device on
the same Wi-Fi. `PATCH_URL` in `settings.env` is required — the launcher build refuses to run without it.
`http://localhost:8090` works only for this computer; for other devices, set
`PATCH_URL=http://<this computer's address>:8090` (`tools/platform.sh lan-address` prints the address). The folder
is served on `PATCH_PORT` (8090), whatever `PATCH_URL` says.

1. Build the launcher:

   ```
   tools/build-launcher.sh
   ```

2. Build the game. For other devices on the Wi-Fi it must be a development build — see *On your own wifi* above:

   ```
   tools/build-game.sh client --dev
   ```

3. Publish it as an update. This stamps a version into `builds/client` itself and writes the website:

   ```
   tools/publish-update.py
   ```

4. Serve the update folder, and leave this terminal open:

   ```
   tools/serve-updates.sh
   ```

5. On another device, open one of the addresses step 4 printed, press **Download for Windows**, unzip it, and
   run `unwritten-launcher.exe` from the `unwritten` folder. On this computer: `tools/platform.sh open-launcher`.

**Worked when:** a small window says "Downloading the patcher" and then the launcher opens by itself; the
News box fills, the bar downloads the whole game (about 850 MB) the first time, and **Start**
lights up and opens the game. Open it again and it downloads nothing; publish again and it downloads only what
changed. **"Could not reach the update server"** means step 4 is not running, or the launcher was built with a
PATCH_URL the device cannot reach.

**Stop the folder server** with Ctrl+C in its terminal. The launcher keeps the game it downloaded in
`AppData/Local/unwritten/` in your user folder on Windows, and in `~/Library/Application Support/unwritten/` on a Mac;
press **Uninstall** in the launcher to try a first install again.

---

## Related

- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — what the thing you just started actually does, and what is still unrun.
- `charserver/README.md` — the character server itself: what it owns, its settings, and how to prove it works.
- [The Offline Checker Is Not the Editor](../5-ai-learnings/the-checker-is-not-the-editor.md) — why a build fails on code the checker says is fine.
- [Querying the Database](querying-the-database.md) — reading back the accounts and characters this runbook creates.
- [Network-Readiness — Combat Audit & Refactor Plan](../2-systems/networking/readiness-audit.md) — the July survey that chose this shape.
- [Performance Checkup — My Routine](performance-checkup-routine.md) — the routine that now needs a running world before it can start.
- [Roadmap to Launch](roadmap-to-launch.md) — Tier 2, where this sits against the plan.
