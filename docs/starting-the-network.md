# Starting the Network

How to get the game running with more than one player. Follow it top to bottom; each step says how to know it worked before you move on.

Three things must be running, in this order:

1. **the database** — where accounts and characters live
2. **the account service** — signs people in
3. **the game server** — owns the world

Then you open the game itself, once per player.

---

## 1. The database

Check whether it is already up:

```bash
pg_isready
```

Expect `accepting connections`. **If it says anything else**, start it:

```bash
brew services start postgresql@17
```

Then check again with `pg_isready` — it takes a second or two. Do not go on until it says `accepting connections`; the account service will not come up without it.

Postgres is installed here through Homebrew as **postgresql@17**, and it is set to start by itself when you log in — so most of the time this step is just the check. You would only need the start command after stopping it by hand, or if the automatic start failed.

The game's data lives in a database called **unwritten** on the standard port **5432**.

To stop it (you rarely want to):

```bash
brew services stop postgresql@17
```

## 2. The account service

```bash
cd accounts && npm start
```

Leave that terminal open — it keeps running. In a **second** terminal:

```bash
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

```bash
tools/build-game.sh server
```

Takes a few minutes and prints where it put the build. Add `--linux` for a build to deploy to a hosted machine; plain gives you one that runs on this Mac.

The same script builds the game players run:

```bash
tools/build-game.sh client
```

Or both at once:

```bash
tools/build-game.sh both
```

It tells you if Unity is not running, and it fails loudly if nothing came out. When it finishes it prints the exact command to run what it built.

### Running it

```bash
./unwritten-server.app/unwritten -batchmode -nographics -logFile -
```

(The build script prints this line with the real path filled in.)

**Worked when:** it prints these three lines and keeps running —

```
[server] world up — loading map 'town_0'
[server] listening for players on port 7777
[server] map 'town_0' loaded — 15 root objects, active scene is now 'town_0'
```

The middle line means it is reachable. The last line means the world is actually up, not just requested.

**Double-check it for real** — this asks the operating system, not the game:

```bash
lsof -nP -iUDP:7777
```

## 4. The players

Open the normal game build, once per player. Each one signs in and picks a character, and joins the world on the way in.

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

A dedicated server build already knows it is a server; `GAME_SERVER` is for running an ordinary build, or the editor, as one.

**On your own wifi**, players need the host machine's local address rather than `localhost`:

```bash
ipconfig getifaddr en0
```

Start each player's game with `GAME_SERVER_HOST` and `ACCOUNTS_URL` pointed at that address.

---

## Test accounts

Already in the database. Password for both: `testpass123`

| Email | Character |
| --- | --- |
| `alpha@test.local` | Alpha |
| `bravo@test.local` | Bravo |

---

## When it does not work

**Nothing listening on 7777.** Check the server's log for `No Transport on Network Manager`. That means the network layer gave up during start-up and the rest of the log will look normal — it fails quietly, so look for it rather than assuming a silent log is a healthy one.

**Players connect but nobody has a body.** The log will say which prefab is missing from Resources.

**Players see each other but no mobs.** Only the server spawns creatures now. If the server is not running, or a player is connected to a different one, there is nothing to send.

**A player is stuck at the sign-in screen.** That is the account service, not the game server — check step 2.

---

## Stopping everything

```bash
pkill -f 'unwritten-server.app/unwritten'; pkill -f 'accounts.*server.ts'
```

---

## Where it goes when it leaves your machine

One ordinary always-on virtual machine, near the players — Singapore for South-East Asia. It runs all three things above. Not a match-based game-hosting service: those spin a session up and throw it away, while this world runs continuously.

Unity's own hosting service shut down on 31 March 2026; do not plan around it.