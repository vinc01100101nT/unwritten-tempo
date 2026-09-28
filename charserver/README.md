# charserver

Who a character IS, and the only thing that reads or writes one. A small C# server sitting between the
login service and the game.

It deliberately does **not** sign anybody in, and it deliberately does **not** decide anything about
gameplay. Signing in belongs to `accounts/`; deciding what a number should be belongs to the game server.
This owns storage and nothing else — which is exactly the split Ragnarok Online has used since 2002.

## The three servers

| | Owns |
| --- | --- |
| `accounts/` (Node) | Register, sign in, refresh, sign out. Accounts and tokens. |
| `charserver/` (this, C#) | The character list, creation, deletion, appearance, and every save. |
| the game server (Unity) | The world. It never touches this database — it asks here. |

All three point at **one** Postgres, with ownership split by table. `accounts` owns accounts and refresh
tokens; this owns characters. That is the only arrangement in which a value has one writer.

The two servers share the signing secret rather than each having their own. That is why `accounts` issues a
token at all instead of a session nobody else can read: this one verifies it with the same key.

## Running it

It reads the **same** `DATABASE_URL` and `JWT_SECRET` the login service reads, from the same file — `settings.env`
at the top of the project. A different value for either
is a bug, not a configuration — they would be looking at different databases, or refusing each other's
players.

```bash
charserver/run.sh
```

Run it from the top of the project. It loads `settings.env` and starts the server; a setting typed in front of
it wins over the file. It refuses to start without `SERVER_SECRET`.

It refuses to start if it cannot reach the database, rather than listening and failing on the first player,
and `/health` asks the database every time rather than answering from memory — so "alive" keeps meaning
"can actually serve a character".

It runs in the safe mode by default: an unhandled fault answers with a plain message rather than a stack
trace, which anyone could otherwise reach without a token by sending a malformed body. Set
`ASPNETCORE_ENVIRONMENT=Development` deliberately if you want the developer-facing behaviour.

| Setting | Default |
| --- | --- |
| `DATABASE_URL` | required — the same one `accounts` uses |
| `JWT_SECRET` | required — the same one `accounts` uses |
| `CHAR_SERVER_PORT` | 8081 |
| `BIND_HOST` | `0.0.0.0` — every address. Set it to `127.0.0.1` on a hosted machine, where the encrypting proxy in front is the only thing that should be reachable |
| `CHARACTER_MODELS_PATH` | `accounts/data/character-models.json` |
| `SERVER_SECRET` | required — shared with the map servers, never a player token |
| `MAX_PROGRESS_BYTES` | 262144 — the ceiling on ONE save; a larger one is refused in words, not by the wire |
| `PATCH_DIR` | unset — no copy of the game is turned away. Set it to the launcher's update folder (written by `tools/publish-update.py`) and every player route below refuses a copy whose version differs from that folder's `version.txt`, with a message telling the player to update. Re-read when the file changes, so publishing needs no restart |
| `ASPNETCORE_ENVIRONMENT` | Production unless you set it |

## Proving it works

```bash
charserver/test.sh
```

Needs both servers running. It registers a throwaway account through the **Node** service, then does
everything else through **this** one — proving the shared secret works across them — and covers: a forged
token, no token, creating a character, a duplicate name, a bad name, the list, a real save written and read
back field for field, an unversioned save, a save that omits the appearance not wiping it, a recolour merging
rather than replacing, another account being unable to read or delete it, a delete freeing the name, the colour cap on recolouring, colours arriving with stray spaces, whole numbers
too large for an ordinary integer, a character id written in any spelling but the plain one, a malformed body
from a stranger leaking no stack trace, and health actually reaching the database.

## Pointing the game at it

One setting on the game, `CHAR_SERVER_URL`, set in `settings.env` at the top of the project (`http://localhost:8081`
on your desk).

**It is required.** The login service answers no character route at all, so a game with no address for this
server can sign in and then get no further — it cannot list, create or enter a character. The editor reads it
from `settings.env`; a build carries it in its own settings file, written there by `tools/build-game.sh` (the
`PUBLIC_CHAR_SERVER_URL` address when that is set). A value set in a terminal wins over both.

## The endpoints

Every route in this table except `/health` needs a player's sign-in token, and — when `PATCH_DIR` is set — a
copy of the game whose version matches the published one (the `X-Game-Version` header the game sends).

| | |
| --- | --- |
| `GET /character-models` | the models a character may be created with |
| `GET /characters` | this account's characters |
| `POST /characters` | create one |
| `PATCH /characters/:id/appearance` | recolour or change body; merged, never replacing the save |
| `DELETE /characters/:id` | soft delete; frees the name |
| `GET /characters/:id/progress` | the save, plus whether the account is trusted with GM commands |
| `PUT /characters/:id/progress` | refused (405) — a player never writes their own save; the map server writes it through its own door |
| `GET /health` | no token needed; says whether it is alive |

## The handoff — how somebody gets into the world

This is the part copied most directly from Ragnarok, and the order is the whole point.

1. A **map server registers** itself as it starts: who it is, where to reach it, which maps it holds, and what
   it understands a character to be. A map server built against a different character is **refused**, so a
   half-finished deploy fails loudly instead of storing somebody's life slightly wrong.
2. A player asks this server to **enter** with a character. It looks up which map server holds the map they
   logged out in.
3. It **pushes their whole character to that map server** and says they are arriving, then mints a one-time
   ticket.
4. Only then is the player told **where to knock**, with that ticket. Nothing about who they are travels on
   their own connection — their character was already there.
5. The map server **spends the ticket** here. It works exactly once, so a captured one is worthless.
6. Everything that happens in the world is **written back through this server**, with the shared server
   secret. The map server has no database and never will.

A player's token cannot register a map server, spend a ticket, or use the write-back door; the shared server
secret cannot read a player's character list. The two roads are deliberately separate.

```bash
charserver/test-handoff.sh
```

Runs the whole of the above against a stand-in map server: registration, a refused mismatched build, a player
being unable to register, the character arriving BEFORE the player is told anything, the ticket working once
and then being refused, the map server writing back, a player being unable to use that door, and entering
being refused outright when no map server holds the map.

## Settings the map server needs

Set on the **game server**, and `SERVER_SECRET` must match this one exactly:

| Setting | What it is |
| --- | --- |
| `CHAR_SERVER_URL` | where this server is |
| `SERVER_SECRET` | the shared secret between the two servers — never a player's token |
| `MAP_SERVER_ADDRESS` | how this server can reach the map server back |
| `MAP_SERVER_ARRIVALS_PORT` | the map server's door for arrivals (7788), not the port players use |
