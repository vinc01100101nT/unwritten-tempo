# Handoff — the authoritative game server

**Task** — build the C# game server that owns combat and world state. Decision (2026-08-25):
**server-authoritative**, not peer/co-op. Node for accounts, **headless Unity** for the game server.

**Status (2026-08-26, end of day):** the server RUNS and two players have played in one world together,
verified by the user. Combat is NOT yet server-owned and is blocked on one named thing (below).

---

## The backend decision, and why

Headless Unity, not a plain .NET service. Measured reasons:

- The combat FORMULAS are nearly engine-free already (accuracy/defence, stat maths, the skill engine) — they
  would port easily either way, so they are not the argument.
- The expensive half is **world geometry**: pathing, ground height and obstacle queries all ask Unity for the
  real shape of the map. A plain .NET service can share none of it and would have to rebuild it.
- Mirror (the networking library) was validated against Unity 6000.4.11f1 by compiling all 228 of its runtime
  source files against this exact editor: **clean**. Mirror only claims support to 6000.1, so this mattered.
- Unity's own Multiplay hosting **shut down 31 March 2026** — do not plan around it. Host on an ordinary
  always-on machine near the players (Singapore for SEA).

---

## What works, verified

- **Headless server build** (Mac + Linux dedicated-server modules installed). Boots with no display, loads
  `town_0`, ticks, and says so in its log.
- **Two players in one world.** Both sign in through the account service, both get a body, both see the other
  move. The user ran this and confirmed it.
- **Server-owned mobs** (written, compiles, NOT yet run). Before this, *every client spawned its own monsters* —
  two players never shared a creature, which is why combat could not be server-owned.

## What is written but has NEVER RUN

Everything committed after `a1ed4db0` is compile-verified only — the working tree at handoff time holds the
mob-ownership and combat-seam work. Unity's remote-control bridge stopped responding before it could be built,
so none of it has executed. **Restart Unity, build, and run it before trusting any of it.**

---

## ⛔ THE BLOCKER — per-connection fighters

**The server cannot resolve combat because it has no fighter for each connected player.**

- A player's stats live on `Character`, which is a singleton, and it finds its avatar with
  `FindAnyObjectByType<Player3DController>()` — *the* one on-screen player.
- On a server holding several people, every `Character` would find the same avatar and they would all share one
  body's stats.
- `Character.Awake` used to DESTROY any second instance. That is now skipped on a server (it would have deleted
  a real person's character the moment they signed in), so `Character.Instance` is **null** on a server.
- Consequence, deliberate and loud: `GameNetwork.OnHitRequest` refuses every swing and logs why, once per
  connection. Mob→player attacks are also inert on the server, because they aim at `Character.Instance`.

**Scale, measured:** 155 uses of `Character.Instance` and 12 of `StatusController.Player`; 44 of those are in
combat files. This is the "deferred, mostly server-side" work the network-readiness audit named.

**Checked and safe:** every per-frame `Character.Instance` use in `MonsterAI3D` / `PathAgent3D` /
`SimulationLod` / `UnitCrowd` / `MobRespawn` is null-guarded, so a null player does not spam or crash the
server. It simply does nothing.

---

## Bugs found and fixed during review (all compile-verified, none run)

1. **Networked mobs would have been untouchable.** Silencing a client's mob brain by disabling the component
   also removed it from `MonsterAI3D.Active` — the registry the game uses to find an attack target, to pick
   area-effect targets and to draw minimap blips. Fixed with a `ThinksForItself` flag checked inside `Update`,
   copying the pattern the LOD freeze already uses for exactly this reason.
2. **Mob shells leaked and left ghosts.** A killed mob is pooled, not destroyed, so a null check alone never
   fired; the shell kept publishing a corpse's frozen position forever and a new shell was made on every
   respawn. Now the shell also goes when its mob is deactivated.
3. **A player who spawned and stood still sat at the world origin** for everyone else — nothing was sent until
   they first moved. The first report now always goes out.
4. **Mobs looked permanently at full health** on other players' screens: the synced health was never applied to
   the watched copy. `Health.SetHealthFromServer` now writes it in, only when the number changes.
5. Dead knob removed (`NetPlayer.Active`, read by nothing).
6. **Perf:** the per-hit target lookup used `GetComponentInParent` and a list walk — both banned on that path by
   `PERFORMANCE.md`. Now a dictionary keyed by the drawn body. Idle mobs also no longer pay for a position and
   rotation blend every frame.

---

## Known limits, stated so nobody mistakes them for done

- **Movement is RELAYED, not validated.** A client is still trusted about where it stands. Written into
  `NetPlayer`'s own documentation so it cannot be quietly forgotten.
- **Only the basic swing** has a server path. Skills do not.
- **Progress is still written by the client** to the account service.

---

## How to run it

```bash
cd accounts && npm start                     # account service on :8080
```
Server and client builds are produced from the editor (`BuildPipeline.BuildPlayer`, subtarget `Server` /
`Player`). Call it DIRECTLY — scheduling it for "shortly" is silently discarded by a domain reload, and the
build then never happens with nothing reporting a failure.

Test accounts already in the database: `alpha@test.local` / `bravo@test.local`, password `testpass123`.

Environment settings, no rebuild needed: `GAME_SERVER=1` (be a server), `GAME_SERVER_HOST`,
`GAME_SERVER_PORT` (default 7777), `ACCOUNTS_URL`.

---

## Next single step

Restart Unity, build, and run the server plus two clients. Confirm mobs appear in both clients at the same
places and that the server logs its refusal to resolve a swing. Then design per-connection fighters — that one
piece unblocks all remaining combat work.
