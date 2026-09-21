# Server Guide — unwritten

**Read this before editing** `accounts/` **(the sign-in service, Node) or** `charserver/` **(the character
server, C#), or a migration in** `accounts/sql/`**.**

Two services, one database. The sign-in service issues tokens and does nothing else. The character server owns
every character: the list, the save, the models one may be created with, and the only door into the world. The
game servers (the headless Unity build) hold the world and write a character's progress back through a
server-to-server door. **A player's machine never writes progress** — it never learns what really happened in a
fight, so anything it sent would be older than the truth and would land on top of it.

Everything below was measured on 2026-09-20, before it was fixed. The rules are what stop it coming back; the
checker at the bottom is what makes them stick.

---

## The golden rules

1. **A secret never has a default.** The servers' shared secret — the only thing between a stranger and a door
   that writes any save to any character — defaulted to the literal word `change-me`, and the real server ran with
   it. Both services refuse to start without one; it lives in `accounts/.env` and nowhere else, and the start
   commands read it from there. `openssl rand -base64 32` makes one.
2. **The sign-in service serves no character route.** A leftover door on it let any signed-in player overwrite
   their own save — level, gold, gear, everything — with anything carrying a whole number called `version`. The
   character server had refused that write for weeks; the sign-in service had not. There is no character path on
   port 8080; the checker refuses one.
3. **Every pool is bounded.** With no query timeout, one query that hangs — a lock, a slow read on a grown table —
   holds its connection until the socket dies, and enough of those wedge every door. The sign-in pool sets
   `max`, `statement_timeout`, `query_timeout`, `idleTimeoutMillis`, `connectionTimeoutMillis`; the character
   server sets `CommandTimeout` and auto-prepares its handful of repeated statements.
4. **Every ordered read names the index that serves it** — `/* index: <name> */` beside the `order by`, and that
   index exists in a migration. The leaderboard read every character row and sorted them all, twelve times a
   minute, with no index that could ever help; it now reads twenty index entries. A read that grows with the table
   is silent until the table grows.
5. **The password routes are rate-limited per address.** Each password check costs 32 MB of memory and one of
   Node's four worker threads for as long as the hash takes; a burst of sign-ups from one address pinned the whole
   process. Ten a minute per address on sign-up and sign-in; 120 a minute per address on everything.
6. **A lockout is keyed by address AND email.** Keyed by email alone, five wrong guesses from anywhere locked the
   real owner out for a minute, repeatable forever.
7. **Trust the forwarded address only behind the proxy.** Bound to `127.0.0.1` the service sits behind the
   encrypting proxy and every request arrives from it — without trusting the forwarded header every player
   shares one rate-limit bucket. Bound anywhere else there is no proxy, and trusting that header lets a caller
   name any address they like. `trustProxy` follows `BIND_HOST`.
8. **Entering the world asks the server that holds you, not every server.** The character server remembers
   which map server it handed each character to and asks that one to let go; every server is asked only when none
   is written down. The old fan-out was players × map servers, all but one of them wasted.
9. **A door that hiccups logs and stays open.** The loop that accepts arrivals on a map server used to end
   itself on any exception, silently — nobody could enter that server again until a restart, and nothing said why.
10. **Nothing grows without a sweep.** 262 refresh tokens for 20 accounts, 89% already revoked, and soft-deleted
    characters never purged. Anything a row is written to needs the line that deletes it, or the table is a leak.
    *(Open: this one has no checker yet and no sweep yet — it is the next thing to build here.)*

---

## Established patterns in THIS project — reuse them, don't reinvent

| Need | Use | Why |
| --- | --- | --- |
| A setting both servers must agree on | **`accounts/.env`**, read by `charserver/run.sh` and by the map server's start command | One file, one value; a different value on either side is a bug, not a configuration. |
| Writing a character's progress | **`PUT /server/characters/{id}/progress` on the character server, with `x-server-secret`** | The only write. The player-facing `PUT` returns 405 on the character server and 404 on the sign-in service. |
| Reading a character in the world | **The announcement** (`/arriving` on the map server, sent BY the character server) | The character travels with the ticket; the map server never fetches it. |
| Every user value in SQL | **`$1`, `$2` …** — never pasted | Both services do this without exception; keep it that way. |
| A schema change | **A numbered file in `accounts/sql/`**, applied by `npm run migrate` in `accounts/` | Each runs once, in its own transaction, recorded by filename. The checker warns while one is unapplied. |
| Proving a server change | **`charserver/test.sh`** (end to end, both services) and **`accounts/test/smoke.ts`** | Writes go through the server door in the test, exactly as in the game. |

---

## Anti-patterns (these regressed us — don't do them)

- ❌ `: "${ANYTHING_SECRET:=something}"` in a shell script, or `?? "something"` after an environment read of a secret in code.
- ❌ A route on the sign-in service whose path starts with `/characters` or `/character-models`.
- ❌ `new Pool({ connectionString })` with nothing else.
- ❌ An `order by` in a query string with no `/* index: … */` beside it.
- ❌ Keying a lockout or a limit by something the attacker chooses (an email) instead of where they come from.
- ❌ `trustProxy: true` unconditionally.
- ❌ Asking every server a question one server can answer.
- ❌ `catch { return; }` on an accept loop.
- ❌ A table that only ever gains rows.
- ❌ A test that writes to the live database and cleans nothing up (six of the nine names on the public leaderboard were test debris).

---

## The automatic check

```bash
tools/check-server-rules.py           # rules 1–5 and 8's naming half, on the whole tree; --all lists unapplied migrations
tools/prove-server-rules-check.py     # re-run after ANY edit to the checker — it plants 8 faults and expects each named
```

It runs by itself at the end of any turn that edited a file under `accounts/src`, `accounts/sql` or `charserver/`,
and refuses to end the turn while a rule is broken in a file that turn touched. Rules 6, 7, 9 and 10 are not yet
mechanical: they are here so a reader knows them, and a checker for each is the honest next step.

---

## Checklist before committing a server change

- [ ] No new secret has a default; a new one is in `accounts/.env.example` with how to generate it.
- [ ] No new route on the sign-in service touches characters.
- [ ] A new query with `order by` names its index, and the index is in a migration.
- [ ] A new table has the line that deletes from it.
- [ ] `charserver/test.sh` and `accounts/test/smoke.ts` still pass against a scratch database.
- [ ] Both services restarted — a change to either does not reach a running process.
