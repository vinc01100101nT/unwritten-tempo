# Handoff — the authoritative game server

**Task** — build the C# game server that owns combat and world state. The user's decision, made 2026-08-25:
**server-authoritative**, not peer/co-op. Node for accounts (built, running), C# for the game server (not
started) so it can share the game's own combat and stat code instead of re-implementing every formula.

**Status — NOT STARTED.** No game-server code exists. Everything below is the groundwork it plugs into.
The accounts half is DONE and proven; the game half of this session compiles but **has never been play-tested
— don't trust any "it works" claim about the screens or the world-gate.**

---

## What's done (the foundation the server plugs into)

**The account service** — `accounts/`, Node + TypeScript + Postgres. Register, log in, refresh/revoke a session,
list/create/delete characters, read/write a character's progress, change appearance. Proven: `npm test` runs 20
checks against a real database, all passing. Postgres 17 installed via Homebrew, database `unwritten`, data at
`/opt/homebrew/var/postgresql@17`. Secrets in `accounts/.env` (gitignored); template in `.env.example`.

**One home for character data.** The local JSON save file is DELETED (`SaveSystem`, `SaveTools` — gone from the
repo). `IProfileStore` is now async and server-backed (`ServerProfileStore`), routed through `AccountClient`,
the single place the game talks to the service. `SaveManager` loads asynchronously and waits for a session.

**Appearance vs progress — settled, do not re-merge.** The character RECORD owns name, body and recolour
(identity, true before the character is ever played). The progress document owns level/XP/stats/gear/bag/gold/
hotbar. `SaveData` no longer carries dye fields; a comment in it says so. `SaveData.version == 0` means
"never played" and MUST stay distinguishable.

**The world does not run until a character enters it.** `GameRoot` switches off every root object in the
Systems scene (except itself and whatever carries input handling) and loads no map until
`AccountSession.IsActive`. This is the shape the server will need anyway — a client that isn't in the world
should not be simulating one.

**Sign-in + character select** — `SignInScreen`, `CharacterSelectScreen`, `FrontScreen`, `CharacterPreview`.
Live 3D body preview, free RGB colour mixing wired to the existing `CharacterDye3D`.

**All 52 character bodies load at runtime by name** — Addressable group `Characters`, label `CharacterModel`,
via `CharacterModels` → `AddressablePrefabs` (shared with `MobSpawner`).

---

## Uncommitted working-tree changes

**None at the time of writing** — everything was committed and pushed to `main` at the end of the session
(the commit after `9df5b546`). Re-measure with `git status` before trusting this line.

---

## What's left — the server itself

Read `docs/network-readiness-audit.md` FIRST. It is a measured audit of exactly this job and its refactor
plan (steps A–D) is already DONE on the client: `CombatRng`, caster threading, `IDamageable`,
`CombatAuthority`. Those are the seams the server hooks.

1. **Decide where the server runs the simulation.** Headless Unity (shares the game's C# and its physics/
   pathfinding directly) vs a plain .NET service (lighter, but every formula that depends on Unity types has
   to be lifted out first). This decision gates everything and has NOT been made.
2. **Move the dice.** `CombatRng` is the keystone the audit names — it exists as a choke point but still rolls
   locally. Server-seeded rolls come first, because nothing downstream can be authoritative without it.
3. **Make `CombatAuthority.Submit` send instead of apply.** Today it calls the local apply synchronously. This
   is the one place a net layer swaps in "send to server → validate → roll → broadcast".
4. **Replicate mobs.** `MonsterAI3D` runs client-side today, including its own attack rolls.
5. **Share the token.** The game server verifies the SAME `JWT_SECRET` the account service signs with — that
   is why the accounts half issues a token rather than a cookie. Do not invent a second auth scheme.
6. **Decide who writes progress.** Right now the CLIENT writes its own progress to the account service. Once
   the game server is authoritative, that must move server-side or the whole exercise is decorative. The
   account service is deliberately dumb about gameplay so this move is possible without rewriting it.

---

## Dead ends and traps (proven, not guessed)

- **`Casual3_Male` cannot be a humanoid in Unity.** Its feet are parented to the skeleton ROOT, not to the
  lower legs, and it carries Blender IK helper bones. Unity's humanoid system rejects that by structure — no
  amount of re-mapping fixes it. Its texture WAS fixed this session (it was never bound; a material now exists
  in `1_Authored/Art/Characters/Materials`). It will render but T-pose. It needs re-rigging at source, or
  removing from the roster. It is the only one of 52 like this.
- **"Above the HUD" is not one number.** HUD canvases sit at 5000, 30000 and 32760. Anything that must cover
  the game needs the top slot, not a guessed one.
- **Hiding the world is not stopping the world.** An opaque panel over a running game was the wrong fix and
  was called out as such. Gate the world, don't cover it.
- **Scrypt at 32 MB hits Node's default ceiling** and throws on every password hash. `maxmem` is set
  explicitly in `accounts/src/auth.ts`; don't remove it.

---

## Next single step

Answer question 1 above — headless Unity or plain .NET — because every other step depends on it. Bring the
network-readiness audit and the user's answer to the same conversation; do not pick it unilaterally.

---

## How to resume

Read `handoffs/game-server.md`, then `docs/network-readiness-audit.md`, then continue the authoritative game
server from "Next single step".
