---
status: current
owner: vince
last-updated: 2026-10-31
tags:
  - planning-production
---

# Starting the Network

How to get the game running with more than one player. Follow it top to bottom; each step says how to know it
worked before you move on. **What each piece is and why it is shaped this way** is
[Networking — Architecture](README.md); **what is built and the traps already paid for** is
[Implementation Notes](implementation-notes.md).

**Four** things must be running, in this order. Each one needs the one above it.

1. **the database** — where accounts and characters live
2. **the sign-in service** — signs people in, and nothing else
3. **the character server** — owns every character, and is the door into the world. **Without it nobody can
   enter the world at all.**
4. **the game server** — owns the world itself, the one seamless map `world`

Then you open the game, once per player. The **website** (the game's site, with the wiki and a live Server Status
page) is optional — nothing in the game needs it — and is step 6.

**On the Windows PC, `start-servers.sh`** (top of the project, double-click it) starts the sign-in service, the
character server, the game server and the website, each in its own window in a corner of the screen, and leaves alone
any that is already running. `restart-servers.sh` restarts them inside those same windows — all four, or the ones you
name (`restart-servers.sh map`). The steps below are what each window runs, and how to tell it worked.

---

## 0. The settings file

Every secret and every address the programs need lives in ONE file, `settings.env` at the top of the project. It is
never committed. Make it once:

```
cp settings.env.example settings.env
```

Then fill in `JWT_SECRET` and `SERVER_SECRET` (the file says how to make each). Everything else is already right for
your desk, and the template explains every setting. Every command on this page reads it, and **a setting typed in
front of a command still wins** — so a build made once can be aimed anywhere without a rebuild.

## 1. The database

```
pg_isready
```

Expect `accepting connections`. **If it says anything else**, start it (on Windows it asks for administrator
permission once), then check again — it takes a second or two:

```
tools/platform.sh db-start
```

Do not go on until it says `accepting connections`; the sign-in service and the character server will not come up
without it. Postgres 17 is installed to start by itself — Homebrew's **postgresql@17** on the Mac, the Windows
service **postgresql-x64-17** on the PC — so most of the time this step is just the check. The game's data lives in
a database called **unwritten** on port **5432**. To stop it (you rarely want to): `tools/platform.sh db-stop`.

## 2. The sign-in service

```
cd accounts && npm start
```

Leave that terminal open. In a **second** terminal:

```
curl -s http://localhost:8080/health
```

**Worked when:** it answers `{"ok":true}`. The health check asks the database before it answers, so `ok` means the
service is up **and** can reach Postgres. Nothing printed → the service is not running (read its terminal); an
error instead → it cannot reach the database (go back to step 1).

It listens on **port 8080**. At start it prints every address it answered at, and a red line when the address game
copies are built to dial does not answer.

## 3. The character server

A separate program, written in C#, in `charserver/`. It needs the .NET 10 tools — `brew install dotnet` on the Mac,
`winget install Microsoft.DotNet.SDK.10` on Windows. Run it **from the top of this project**:

```
charserver/run.sh
```

It reads the **same** database address and signing secret the sign-in service reads, from `settings.env` — a
different value for either is a bug, not a configuration. The script exists because loading those settings by hand
has a trap: `. .env` makes zsh search your PATH instead of the folder you are in, and reports *no such file or
directory* while the file is sitting right there.

It builds the server and runs it, and **starts it again 5 seconds after it stops**, so a crash needs nobody. It does
not restart after a clean stop, after refusing to start because its port is taken, or after Ctrl+C. On the rented
machine `deploy/unwritten-charserver.service` starts it at boot.

Leave that terminal open. In another:

```
curl -s http://localhost:8081/health
```

**Worked when:** it answers `{"ok":true,...}`. That check asks the database every time, so `ok` means it can
actually serve a character. It listens on **port 8081**.

**For 40 seconds after it starts it lets nobody enter the world**, and says so. Which game server holds whom lives
only in its memory, so a fresh one must let every game server report in first; letting somebody in sooner could read
their character before their newest save arrived.

**`SERVER_SECRET` is how the two servers recognise each other.** It is NOT a player's sign-in token and the two are
never interchangeable. It lives in `settings.env` and nowhere else; `charserver/run.sh` refuses to start without it,
and `tools/run-map-server.sh` reads the same line so the two can never disagree. Generate one with
`openssl rand -base64 32`.

If it refuses to start it says why in one line and quits: the port is already taken (another one is running — the
common one), or it cannot reach the database.

### The public status door

One route needs no sign-in and no shared secret: `/stats` — how many players are in the world, which maps stand on
which game-server process, and the characters who have earned the most experience. Counts and public names ONLY,
never an account or a position. It is worked out at most once every 5 seconds and everyone gets the same copy, so a
status page open on a thousand screens costs what one does.

```
curl -s http://localhost:8081/stats
```

**Worked when:** it answers with `playersOnline`, `servers` and `topPlayers`. With no game server running,
`servers` is empty and `playersOnline` is 0 — the honest answer, not a fault.

## 4. The game server

A separate build of the game with no window. Rebuild it only when the game's code or data changed.

### Building it

**Unity must be open on this project** — the build runs inside the editor you already have running.

```
tools/build-game.sh server
```

Everything lands in `builds/`, which git ignores. Add `--linux` for a build to deploy to a hosted machine; plain
gives one that runs on this computer. `client` builds the game players run, `both` builds the two. It tells you if
Unity is not running, fails loudly if nothing came out, and prints the exact command to run what it built.

### Running it

From the top of this project:

```
tools/run-map-server.sh
```

It reads the character server's address, the shared secret and the address it is reached at from `settings.env`,
and stops if `SERVER_SECRET` is missing. **To start with log categories already switched on**, put them in front:
`LOG_CATEGORIES=hit,passive,buff,cast tools/run-map-server.sh`, or `LOG_CATEGORIES=all`
([Reading the Game Log](../../0-process/reading-the-game-log.md)). Everything it decides goes to `logs/server.log` as
well as the screen, and the run before is kept as `logs/server-prev.log`.

**Worked when:** it prints these lines and keeps running —

```
[map] listening for arrivals on 7788 as 'YourMac-1a2b3c4d'
[server] listening for players on UDP port 7777 at:
[server]   127.0.0.1:7777      Loopback Pseudo-Interface 1
[server]   192.168.1.12:7777   Ethernet
[server] MAP_SERVER_ADDRESS 192.168.1.12 is one of this computer's addresses — that is the address players are sent to.
[server] world up — loading map 'world'
[server] map 'world' loaded — … root objects, active scene is now 'world'
[map] the character server knows this map server holds 1 map(s).
```

- **A red `MAP_SERVER_ADDRESS … is not one of this computer's addresses` line** means players are being sent
  somewhere this computer is not (Tailscale not connected, the Wi-Fi address changed): fix the network or the
  setting before anyone tries to join. It is said again whenever this computer's addresses change.
- **The `loaded` line** means the world is actually up, not just requested.
- **The last line is the one people forget to read** — until the character server knows this server is there,
  nobody can be sent here. It says `0 map(s)` first and then the real number; the map comes up after the link does.

The character server's terminal says the same from its side — `[char] map server '…' answers at … and holds 1
map(s): world [N player(s), N creature(s)]` — written only when something in the line changed. **A red
`map server '…' does not answer at …` line means nobody can enter that server**: usually `MAP_SERVER_ADDRESS` names a
network that is not up.

**Two doors, two ports.** Players connect on **7777**; the character server pushes characters in on **7788**. To ask
the operating system rather than the game: `tools/platform.sh port-info 7777`, or all four at once with
`tools/platform.sh port-info 8080 8081 7788 7777`.

## 5. The players

```
tools/platform.sh open-game
```

Run it once per player on this computer — it starts the game's own program directly, which is what lets a second
copy open. Each one signs in, picks a character, and is let in by the character server.

**Where it dials.** `tools/build-game.sh client` writes two addresses into every build, from `settings.env`:
`PUBLIC_ACCOUNTS_URL` and `PUBLIC_CHAR_SERVER_URL` once the rented server exists, this machine's `ACCOUNTS_URL` and
`CHAR_SERVER_URL` while those two are empty. To point a build at this computer once they are filled in:

```
ACCOUNTS_URL=http://127.0.0.1:8080 CHAR_SERVER_URL=http://127.0.0.1:8081 tools/platform.sh open-game
```

**Worked when:** each player sees the other walking around, and the game server prints
`[map] expecting <name> — their character arrived ahead of them.` as each arrives — the character server's push
landing, *before* the player's own connection.

**On your own Wi-Fi**, other devices need this computer's local address (`tools/platform.sh lan-address`). Set
`MAP_SERVER_ADDRESS` to it, so the character server sends other devices there rather than to `127.0.0.1`. Keep
`ACCOUNTS_URL` and `CHAR_SERVER_URL` on `localhost` — this computer's own servers use those — and put the address in
`PUBLIC_ACCOUNTS_URL` and `PUBLIC_CHAR_SERVER_URL`, which only go into game builds. Build those copies with
**`tools/build-game.sh client --dev`**: a game refuses plain (unencrypted) web traffic to any machine but its own,
and only a development build is allowed it. **Never hand a `--dev` copy to a player.** The character server tells
each player which machine and port to go to, so `GAME_SERVER_HOST` is not needed. `GAME_SERVER` exists only to run
an ordinary build, or the editor, as a server; a dedicated server build already knows it is one.

---

## 6. The website

The game's site — Home, News, Game Guide, Wiki, Roadmap, Server Status, Media and Download — is a folder of files with
no program behind it, served by the same download server that hands out the launcher and the updates. Its page is
`website/index.html`, its wiki pages `wiki/content/`, its news `launcher/news.json`. A full publish copies them; this
copies the site alone and touches nothing else:

```
tools/publish-update.py --site-only
tools/serve-updates.sh
```

Leave that terminal open, then open `http://127.0.0.1:8090`. Run the first line again after any wiki edit; the
server needs no restart. **Worked when:** the Home page loads, and the Wiki page shows its left-hand list of pages.
The download server listens on every address of this computer, so another device opens one of the addresses it
printed as answering.

### The Server Status page

`http://127.0.0.1:8090/#status` reads the character server's public door (step 3) every 5 seconds; the Home page's
status box reads the same. **It asks the character server address in `settings.env`** — `PUBLIC_CHAR_SERVER_URL`,
or `CHAR_SERVER_URL` while that is empty — which `tools/publish-update.py` writes into `site.json` beside the website
on every publish, so no page carries an address of its own. For another device to read the numbers, that address
must be one it can reach: put this computer's address in `PUBLIC_CHAR_SERVER_URL` and publish the site again.

A level reaches the database within 15 seconds of being earned, and a game server reports its player count every 30
seconds, so however often the page asks, a level appears within about 20 seconds and a player count within about 35.

**A browser treats the website and the character server as two different sites**, so the character server must name
the website's address as allowed. By default it allows the download server's own: `PATCH_URL`, plus
`http://127.0.0.1` and `http://localhost` on `PATCH_PORT`. Serve the site from anywhere else and set `WIKI_ORIGINS`
in `settings.env` — it REPLACES that default, so list every address that needs it, comma-separated — then restart the
character server. **That refusal is SILENT**: the page only says it cannot reach the character server. If it says
that while `curl -s http://localhost:8081/stats` answers fine, this setting is the thing to check.

---

## GM accounts — who may type commands

Nearly every chat command changes something, so **those work only for an account marked a GM**; anybody else is told
*That command is for GMs.* A few only read, and anybody may type them. `/help` lists every command in two groups —
*Commands for everyone* and *GM commands* — and `/help <command>` shows its full usage. Every command except `/help`
and `/where` is carried out by the game server on its own copy of the character, because everything about a
character is the server's; each shows up in its log as *"<name> ran '/…'"*.

**The mark belongs to the account, not a character** — being trusted with commands is a fact about a person. There
is no screen for it, on purpose. Grant it (set `false` to take it away):

```
psql -d unwritten -c "update account set is_gm = true where lower(email) = lower('you@example.com');"
```

Check who has it:

```
psql -d unwritten -c "select email, is_gm from account order by created_at;"
```

On the Windows PC the database asks for a password, so run either line from the top of the project with the address
read from `settings.env` in place of `-d unwritten`:
`psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -c "…"`.

The person must **sign out and back in** — the mark is read when their character is loaded.

## Test accounts

Already in the database. Password for both: `testpass123`

| Email | Character |
| --- | --- |
| `alpha@test.local` | Alpha — marked as a GM |
| `bravo@test.local` | Bravo |

---

## When it does not work

**Nothing listening on 7777.** Look in the server's log for `No Transport on Network Manager`: the network layer gave
up during start-up and the rest of the log looks normal.

**Players connect but nobody has a body.** The log says which prefab is missing from Resources.

**Players see each other but no mobs.** Only the server spawns creatures. If it is running, read its `map … loaded`
line — and a server started before your last change is serving the old world (below).

**Connections fail with `OnDeserialize failed` and players drop straight after connecting.** The server and the game
are built from different code. Almost always a server left running from before your last change — stop it, rebuild,
run it again.

**A second character server or game server refuses to start.** It says the port is already taken and quits — the
message doing its job. One is already up (`curl -s http://localhost:8081/health`, `tools/platform.sh port-info …`).
Stop that one first, or give the new game server a different `GAME_SERVER_PORT`.

**Something is still listening after you stopped the server.** You have a second one running. List them with start
times:

```
ps -o pid=,lstart=,command= -ax | grep '[u]nwritten-server'
```

**A player is stuck at the sign-in screen.** That is the sign-in service — step 2.

**A player signs in, picks a character, and cannot get in.** That is the character server. `curl -s
http://localhost:8081/health` — is it up and reaching the database? Then read the game server's log for `the character
server knows this map server holds N map(s)` — never appeared, or `0`, means the character server has nowhere to send
anybody. The usual cause is `SERVER_SECRET` missing or different between the two.

**"the character server would not take this map server — 401 not a server".** The two have different
`SERVER_SECRET` values. Set the same value on both and restart the game server.

**"the world would not take you just now".** The character server could not hand your character to the game server
it knows — that server is gone, or its arrivals door (7788) is not reachable. A game server silent for two minutes is
forgotten, so a crashed one clears itself.

**Everything works but nothing is saved.** The game server writes through the character server; while that is down
the world keeps playing and every save waits — on disk too, in an `unsent-saves-<port>` folder in the game server's
data folder — and is retried until it lands. Its log says "could not write character … down yet". A save refused for
good is kept in a `refused` folder inside it for a person to look at.

**A player connects and immediately reconnects, or is told the game has no character server address.** Their copy has
no `CHAR_SERVER_URL` — a build made before `settings.env` had one, or one started with `open`, which passes none of
the terminal's settings. Rebuild it with `tools/build-game.sh client`, or start it the way step 5 says.

**You restarted the character server and now nobody can enter.** Expected: for its first 40 seconds it lets nobody in
and says so, while every game server says hello again (within 30 seconds). Entering works from then on.

**Somebody cannot come back after leaving.** They should be able to; if they are refused as already playing, that is
a bug and the game server's log names the character.

---

## Stopping and restarting

**A running server never picks up your changes.** It holds a copy of the game as it was when you started it, so the
order after any change is always **stop, build, run again**.

| Stop | Command |
| --- | --- |
| The game server on its own — the common one, between changes | `tools/platform.sh stop-port 7777` |
| The game server, the character server and the sign-in service | `tools/platform.sh stop-port 7777 8081 8080` |
| A spare one you started on another port | `tools/platform.sh stop-port <its port>` |
| The database (rarely) | `tools/platform.sh db-stop` |

Each is stopped by the port it answers on, never by its name. **Stopped when** `tools/platform.sh port-info 7777`
prints nothing at all.

### The full round trip after a change

1. `tools/platform.sh stop-port 7777`, then `tools/platform.sh port-info 7777` — it prints nothing.
2. **Let Unity finish compiling your change** — the build compiles what the EDITOR has imported, not what is on disk,
   so a file you just added is invisible to it until then
   ([The Offline Checker Is Not the Editor](../../5-ai-learnings/the-checker-is-not-the-editor.md)).
3. `tools/build-game.sh server` (Unity open).
4. `tools/run-map-server.sh`, and read its start-up lines down to `the character server knows`.

The database, the sign-in service and the character server do **not** need restarting for a game-code change.
Restart the sign-in service only for a change inside `accounts/`, and the character server only for a change inside
`charserver/`. **One exception, and it bites:** a change to the SHAPE of a save. The character server refuses a game
server built against a different one, on purpose, so a half-finished deploy fails loudly instead of storing
somebody's life slightly wrong. That agreement is one string, in both `charserver/MapServers.cs` and
`unity/Assets/Scripts/MapServerLink.cs` — change both in the same edit, and restart both.

**Leaving one running by accident is the classic trap.** A server from hours ago looks exactly like a healthy one:
the port answers, players sign in, the world appears — the **old** world. Check its start time with the `ps` line
above; older than your last change means it is the problem.

---

## Where it goes when it leaves your machine

One ordinary always-on virtual machine, near the players, running all four. Not a match-based game-hosting service:
those throw a session away, while this world runs continuously. **Which machine, from whom, and what it costs is
[Where the Game Is Hosted](../../1-planning/where-the-game-is-hosted.md)** — one place to argue with it.

**The two web services must stop being readable.** The sign-in service and the character server speak plain text;
over the internet a password and a save can be read by anything on the path. So on a hosted machine one program
stands in front of both, holds the certificate and renews it — `deploy/Caddyfile` is the whole configuration. Start
both services with `BIND_HOST=127.0.0.1` in the same change: a certificate on the front door does nothing while the
plain door beside it is still open. **The world servers are deliberately not behind it** — players reach those on a
connection certificates do not cover, and its protection is the one-time entry ticket.

**A copy with no terminal carries its addresses.** Fill in `PUBLIC_ACCOUNTS_URL` and `PUBLIC_CHAR_SERVER_URL` on the
computer you build on, then build: `tools/build-game.sh client` writes those two into each build, and nothing else
from the file. **Both are needed:** signing in uses the first, and without the second nobody can enter. The editor
started from the Dock or Unity Hub reads this machine's own `ACCOUNTS_URL` and `CHAR_SERVER_URL`, so it keeps talking
to your desk.

**The rented machine has its own `settings.env`**, made from `settings.env.example`: its own secrets,
`BIND_HOST=127.0.0.1`, `MAP_SERVER_ADDRESS` set to its public address, `MAP_SERVER_ARRIVALS_ADDRESS` set to
`127.0.0.1` (or a private address, once game servers run on other machines — a delivery carries a whole character
and the shared secret as plain text, so it never crosses the open internet), `PATCH_DIR`, the public addresses and
`CERT_EMAIL` the front door serves. Everything on that machine reads it.

### Backing up the database

It is the only thing here that exists nowhere else — the code is in git and every build can be rebuilt, but a lost
database is lost players. Two kinds of backup run side by side, each working without the other. Each script says what
it would do, and changes nothing, with `--dry-run` after it.

- **The daily copy** — `tools/backup-database.sh`: the whole database in one dated file, the last 14 kept. It proves
  the file is readable and prints how many characters it read back **out of the file**, so a backup that saved
  nothing cannot report success.
- **The continuous copy** — Postgres copies every finished change-log file into `WAL_ARCHIVE_DIR` at least once a
  minute, and `tools/backup-full-copy.sh` takes a full copy into `BASE_BACKUP_DIR` once a week, keeping three.
  Together they rebuild the database as it was at any second from the oldest full copy kept until about a minute ago.

Turning the continuous copy on, once, in this order:

1. `tools/backup-start-archiving.sh` — writes the settings. Nothing changes yet.
2. In a quiet hour, restart Postgres (`tools/platform.sh db-stop`, then `tools/platform.sh db-start`). Every player is
   disconnected for a few seconds.
3. `tools/backup-start-archiving.sh` again. Want `Archiving is on.`, and once anything has changed, a newest file
   ending `and it is in the folder`. The account Postgres runs as must be able to write to the archive folder.
4. `tools/backup-full-copy.sh` — the first full copy. Nothing before it can be rebuilt from the archive.

On the hosted machine, put the two folders in its `settings.env` and run both from cron:

```
10 4 * * * BACKUP_DIR=/var/backups/unwritten /srv/unwritten/tools/backup-database.sh >> /var/log/unwritten-backup.log 2>&1
30 4 * * 0 /srv/unwritten/tools/backup-full-copy.sh >> /var/log/unwritten-backup.log 2>&1
```

**Watch the archive folder's size.** Each archived file is 16 MB even when nearly empty, so every minute in which
anything changed costs 16 MB, kept until the oldest full copy is deleted.

To put a daily copy back — this destroys what is in the database now, so read it twice:

```
pg_restore --clean --if-exists -d "$DATABASE_URL" /var/backups/unwritten/<the dump file>
```

Rebuilding the live database from the continuous copy is not scripted. **Rehearse a restore once a month** with
`tools/backup-rehearse-restore.sh "<moment>"`, which rebuilds a spare database on port 5433
(`RESTORE_REHEARSAL_PORT`) as it was at that moment, checks it, prints how long it took, then deletes the spare. It
never connects to the live database, and needs a full copy taken before the moment.

1. Note the time, T: `date '+%Y-%m-%d %H:%M:%S'`.
2. Raise one test character's level, and keep playing it for the next 2 minutes — a rebuild can only stop at a
   moment if the database changed again after it.
3. Rebuild to 30 seconds after T: the test character has its new level in the `last changed` list.
4. Rebuild to 30 seconds before T: it has its old level.

**Off-site storage comes later.** Until it exists both kinds of backup sit on the same disk as the database, so a
dead disk takes them with it.

---

## Handing out updates

A player downloads one small program once. Every time they open it, it fetches the **launcher** into their own user
folder if it is missing or out of date, and opens it; the launcher downloads only the game files that changed, then
lets them press **Start**. A copy whose version differs from the published one is refused by the character server
with *"Your game is out of date. Close it and run the launcher to update."* How the small programs and the launcher
update themselves is in their code: `launcher-updater/` (Windows), `launcher-updater-mac/` (Mac) and `launcher/`.

**Once — build the launcher.** It is pointed at `PATCH_URL` from `settings.env`, so fill that in first. Run it again
only when the launcher, the small programs or `PATCH_URL` change; a launcher change on its own is then published with
`tools/publish-update.py --launcher-only`.

```
tools/build-launcher.sh
```

- Only a Windows PC with Visual Studio's C++ build tools can build the small Windows program.
- Only a Mac can compile the small Mac program, so it travels through git: `tools/build-launcher.sh` on a Mac compiles
  it into `launcher-updater-mac/built/` (Git LFS) — commit and push that folder, pull on the Windows PC, and
  `tools/build-launcher.sh` there makes and signs the app with that PC's own `PATCH_URL`. Publishing refuses a Mac app
  that names a different update server, lacks either kind of chip, or holds a file its signature does not cover.
- A Mac that has never seen the game asks once: **System Settings ▸ Privacy & Security ▸ Open Anyway**.

**Every update.** Build and publish both games in one go — write any news in `launcher/news.json` first, add `--dev`
for devices on your own Wi-Fi and `--server` to rebuild the game server too (stop it first):

```
tools/build-and-publish.sh
```

Or the same by hand:

1. `tools/build-game.sh client` builds this computer's own copy; add `--mac` for the Mac copy on any computer, into
   `builds/mac/` (Unity needs its Mac build module installed).
2. Write the news, if any, in `launcher/news.json` — one patch-notes entry per date.
3. `tools/publish-update.py` — publishes `builds/client` as this computer's platform; add `--windows <folder>` and
   `--mac <folder>` for both. It stamps one version into every build it is given, writes `builds/patch/` with the
   website, and warns if a build still points at this machine. **A platform left out keeps its old version**, so its
   players are told to update until it is published too. `tools/publish.sh` publishes what is already built —
   both games, the website, the wiki and the news — without building anything.
4. Copy `builds/patch/` to the hosted machine, `version.txt` LAST — the moment it lands, the server demands the new
   version, so the files must already be there. `rsync` is not installed on the Windows PC; run this from the Mac.

   ```
   rsync -a --delete --exclude version.txt builds/patch/ host:/srv/unwritten/patch/
   rsync -a builds/patch/version.txt host:/srv/unwritten/patch/
   ```

The character server reads the version from that folder (`PATCH_DIR`) and re-reads it when it changes, so step 4
needs no restart.

**Players who still hold the first launcher** (from before the self-updating one) are handed over through the game's
own file list on their next Start; `tools/publish-update.py --without-hand-over` publishes the list the old way and
withdraws that hand-over.

### Trying the launcher on this computer and your Wi-Fi

No rented machine needed: this computer serves the update folder itself, on `PATCH_PORT` (8090). `PATCH_URL` is
required; `http://localhost:8090` reaches only this computer, so for other devices set
`PATCH_URL=http://<this computer's address>:8090`.

1. `tools/build-launcher.sh`
2. `tools/build-game.sh client --dev` — a development build, for other devices on the Wi-Fi (step 5).
3. `tools/publish-update.py`
4. `tools/serve-updates.sh` — leave it open (Ctrl+C stops it). It prints only the addresses that answered, and a red
   line when `PATCH_URL` does not.
5. On another device, open one of the printed addresses, press **Download for Windows**, unzip it, and run
   `unwritten-launcher.exe` from the `unwritten` folder. On this computer: `tools/platform.sh open-launcher`.

**Worked when:** a small window says "Downloading the patcher", the launcher opens by itself, the News box fills, the
bar downloads the whole game the first time, and **Start** lights up and opens it. Open it again and it downloads
nothing; publish again and it downloads only what changed. **"Could not reach the update server"** means step 4 is
not running, or the launcher was built with a `PATCH_URL` the device cannot reach. The launcher keeps everything in
`AppData/Local/unwritten/` on Windows and `~/Library/Application Support/unwritten/` on a Mac; its **Uninstall** button
lets you try a first install again.

---

## Related

- [Networking — Architecture](README.md) — the four processes, how a person gets in, and which server holds which map.
- [Implementation Notes](implementation-notes.md) — what the thing you just started actually does, and the traps already paid for.
- `charserver/README.md` — the character server itself: what it owns, its settings, and how to prove it works.
- [Querying the Database](../../1-planning/querying-the-database.md) — reading back the accounts and characters this runbook creates.
- [Reading the Game Log](../../0-process/reading-the-game-log.md) — the log categories and how to read a slice.
- [Performance Checkup — My Routine](../../1-planning/performance-checkup-routine.md) — the routine that needs a running world before it can start.
- [Roadmap to Launch](../../1-planning/roadmap-to-launch.md) — where this sits against the plan.
