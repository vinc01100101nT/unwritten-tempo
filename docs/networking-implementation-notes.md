# Networking — Implementation Notes

Code-facing companion for the multiplayer layer. Read this before touching anything networked, or before adding a new kind of thing that has to exist for more than one player.

Sibling docs: [Network-Readiness Audit](network-readiness-audit.md) (the July survey that chose the shape) · [Starting the Network](starting-the-network.md) (how to run it) · `handoffs/game-server.md` (what is half-finished right now).

---

## The decision, and why it is not up for re-litigation

**Server-authoritative, on headless Unity.** Not peer-to-peer, not a plain .NET service.

The reason a plain service was rejected is worth knowing, because it looks attractive every time someone re-reads the hosting bill: the combat FORMULAS are nearly engine-free and would port in a day. The expensive half is **world geometry** — pathing, ground height, obstacle checks — which all ask the engine what shape the map is. A non-engine service can share none of that and would have to rebuild it, including a movement solver that agrees with Unity's closely enough that the server and the player's screen never drift apart.

Networking library: **Mirror 96.11.2**, vendored at `Assets/Mirror`. Chosen over Unity's own because Unity documents theirs as suited to ≤64 players in casual co-op, and their scalable alternative would mean rewriting the game in a different programming model. Mirror ships interest management, which this game needs.

Mirror only claims support up to Unity 6000.1 and this project is on 6000.4.11f1, so all 228 of its runtime source files were compiled against this exact editor before adopting it. Clean. **Re-run that check when either version moves.**

---

## The one idea the whole layer is built on: shells

A networked thing is **two objects**, not one.

- **The shell** — a tiny object carrying only what must cross the wire: an id, a position, a facing, health. The server owns it. Mirror replicates it.
- **The body** — the real thing you see, built locally on each machine from the shell's id, using the spawn path the game already had.

**Why, and do not undo this:** bodies are heavy. A character is a rigged model fetched on demand; a mob is a baked asset. Sending those over the wire would mean shipping models per player. Sending an **id** and letting each machine build its own copy costs a few dozen bytes and reuses the spawn code that already exists rather than creating a second one.

Both live things follow this exactly: `NetPlayer` for a person, `NetMob` for a creature. **A new networked kind should follow it too.**

### Who decides what

|  | Owned by | Everyone else |
| --- | --- | --- |
| A player's position | the player's own machine (for now — see limits) | watches |
| A mob's brain, path, health | the server | watches |
| Combat outcome | the server (seam built, not yet reachable) | watches |

---

## The files

| File | What it is |
| --- | --- |
| `GameServer.cs` | The single answer to "am I the server?". Read once at start-up and cached. A dedicated build says so itself; anything else reads the `GAME_SERVER` setting. |
| `GameNetwork.cs` | The one place the game joins the wire. Starts listening or dials out, handles a player arriving, and holds the seam where a hit becomes a request to the server. |
| `NetPlayer.cs` | One connected person, as everyone else sees them. |
| `NetMob.cs` | One creature the server owns, as everyone else sees it. |

Prefabs `NetPlayer` and `NetMob` live in `Assets/3_Data/Resources` and are found by NAME, so neither half needs scene wiring. Mirror matches them across machines by asset id, so they **must** be real prefab assets — they cannot be built purely at runtime.

---

## Conventions to keep

**One place joins the wire.** `GameNetwork.Begin()` is called from exactly one caller, at the moment the world starts existing. On a server that is boot; on a player's machine it is entering the world after sign-in. Two callers would let the halves start under different rules.

**Environment settings, never rebuilds.** Where a build points is set by `GAME_SERVER_HOST` / `GAME_SERVER_PORT`/ `ACCOUNTS_URL`, the same rule the account service already followed. One build, aimed anywhere.

**A watched copy must not think — but must not be switched off either.** Silence a mob's brain by setting `MonsterAI3D.ThinksForItself = false`, never by disabling the component. Disabling it drops the mob out of `MonsterAI3D.Active`, and that registry is how the game finds an attack target, picks area-effect targets and draws minimap blips. A mob missing from it is visible but impossible to click or hit, with nothing in any log saying why. The distance-based freeze already solved this the same way — follow it.

**The offline game must stay untouched.** Every networked branch is gated so that with no server in the picture nothing changes. `GameNetwork.IsConnectedToAServer` is false in single-player, and the local machine remains the only authority, exactly as before.

**Per-hit and per-frame paths obey** `PERFORMANCE.md`**.** The shell-for-a-body lookup is a dictionary, not a list walk and not a component search, because every swing asks it. Idle bodies skip their position and rotation blend entirely — most things are idle most of the time.

---

## Traps already paid for

**Order of construction.** Adding Mirror's manager component runs its start-up that instant, and the first thing it looks for is a transport. Finding none it logs one line and gives up **half-built** — singleton claimed, everything after skipped, and the rest of the log looks perfectly normal. Attach the transport first. This cost a session.

**A killed mob is not a destroyed mob.** It is switched off and kept for reuse, so a null check never fires. A shell must also let go when its body goes inactive, or every player watches a corpse frozen in place forever and a new shell piles up on each respawn.

**Something that never moves never sends.** A first update has to go out unconditionally, or a player who spawns and stands still is reported at the world origin and everyone sees them in the far corner of the map.

**Synced numbers are not applied numbers.** Health crossing the wire does nothing until something writes it into the body. Until that was added, mobs looked permanently at full health while dying.

**Scheduling a build for "shortly" silently loses it.** A domain reload discards pending callbacks, so the build never happens and nothing reports a failure. Call the build directly and accept that the request times out — it still completes.

---

## What is done, and what is not

**Working, watched with two players:** the headless server boots and loads its map; players sign in, connect, get a body, and see each other move.

**Written, never run:** server-owned mobs, and the seam where a hit becomes a request. Everything after commit `a1ed4db0` is compile-verified only.

**The blocker — read this before planning combat.** The server cannot resolve a fight because it has no fighter per connected player. `Character` is a singleton that finds its avatar by searching the scene for the one on-screen player; on a server holding several people they would all share one body's stats. It is deliberately left null on a server, so the hit seam refuses every swing and says so. Measured scale: **155 uses of** `Character.Instance`**, 12 of** `StatusController.Player`**, 44 of them in combat files.** Per-connection fighters is the one piece that unblocks all remaining combat work.

**Deliberate limits, not oversights:**

- **Movement is relayed, not validated.** A player is still trusted about where they stand. Server-owned movement additionally needs prediction and correction on the client, or it feels laggy — its own phase.
- **Only the basic swing** has a server path. Skills do not.
- **Progress is still written by the client** to the account service.

---

## Order of remaining work, and why this order

1. **Per-connection fighters.** Everything below is blocked on it.
2. **Server owns combat outcomes** — the dice, then the damage. Combat is where cheating pays, so it comes before movement.
3. **Server owns player movement**, with prediction and correction. Hardest piece; needs the two above to be settled first.
4. **Interest management** — stop telling every player about every entity. Mirror provides it; it is configuration rather than new code.

Mobs had to become server-owned *before* combat could, and that was found the hard way: every client used to spawn its own creatures, so two players never shared a monster and there was nothing for a server to adjudicate. Expect more ordering surprises of that shape — check who owns a thing before writing rules about who may change it.