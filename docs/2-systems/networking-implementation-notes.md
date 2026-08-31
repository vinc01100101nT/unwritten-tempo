---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - systems-combat-design
---

# Networking — Implementation Notes

Code-facing companion for the multiplayer layer. Read this before touching anything networked, or before adding a new kind of thing that has to exist for more than one player.

Sibling docs: [Network-Readiness Audit](network-readiness-audit.md) (the July survey that chose the shape) · [Starting the Network](../1-planning/starting-the-network.md) (how to run it) · [Querying the Database](../1-planning/querying-the-database.md) (how to read what it persisted) · `handoffs/game-server.md` (what is half-finished right now).

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
| Combat outcome | the server — it rolls it, applies it, and announces it | watches, and shows what it was told |

**A player's machine owns nothing and decides nothing.** That is literal now, not aspirational: both damage channels refuse to act anywhere but the server. See *There is no offline game* below.

---

## The files

| File | What it is |
| --- | --- |
| `GameServer.cs` | The single answer to "am I the server?". Read once at start-up and cached. A dedicated build says so itself; anything else reads the `GAME_SERVER` setting. |
| `GameNetwork.cs` | The one place the game joins the wire. Starts listening or dials out, handles a player arriving, validates and resolves an incoming hit, announces the result, and owns the retry-then-give-up loop when a world is lost. |
| `NetPlayer.cs` | One connected person, as everyone else sees them. |
| `NetMob.cs` | One creature the server owns, as everyone else sees it. |
| `ReconnectNotice.cs` | The "Reconnecting…" overlay shown while a connection is being retried. Dims rather than hides — a player being reconnected has not gone anywhere. |
| `PlayerFighter.cs` | The server's copy of one person's character — built per connection, filled in from the account service with that person's own sign-in proof, and taken away when they leave. |
| `PlayerUnits.cs` | Who is actually in this world, as fighters. One entry on a player's own machine, one per connection on a server. The thing creatures ask when choosing whom to hunt. |
| `Rewards.cs` | What a kill is worth and who gets it. One door, so the answer cannot differ between the machine that decided the kill and the one that records it. |

Prefabs `NetPlayer` and `NetMob` live in `Assets/3_Data/Resources` and are found by NAME, so neither half needs scene wiring. Mirror matches them across machines by asset id, so they **must** be real prefab assets — they cannot be built purely at runtime.

---

## Conventions to keep

**One place joins the wire.** `GameNetwork.Begin()` is called from exactly one caller. On a server that is boot; on a player's machine it is the moment a character is chosen. Two callers would let the halves start under different rules.

**The wire comes up BEFORE the world, not with it.** A server may bring its map up immediately — it *is* the world. A player may not: `GameRoot` dials, waits for `GameNetwork.OnJoinedWorld`, and only then wakes the scene and loads a map. Signing in no longer puts anybody anywhere; it earns the right to try.

**Environment settings, never rebuilds.** Where a build points is set by `GAME_SERVER_HOST` / `GAME_SERVER_PORT`/ `ACCOUNTS_URL`, the same rule the account service already followed. One build, aimed anywhere.

**A watched copy must not think — but must not be switched off either.** Silence a mob's brain by setting `MonsterAI3D.ThinksForItself = false`, never by disabling the component. Disabling it drops the mob out of `MonsterAI3D.Active`, and that registry is how the game finds an attack target, picks area-effect targets and draws minimap blips. A mob missing from it is visible but impossible to click or hit, with nothing in any log saying why. The distance-based freeze already solved this the same way — follow it.

**There is no offline game. Do not build one back.** *(Decided 2026-08-27, and it replaced the opposite rule that stood here before — the old one said every networked branch must be gated so nothing changed with no server in the picture. That is void.)*

A player who is not joined is not in the world at all, so there is no state in which local code is the authority, and no fallback branch for anything to fall into. Concretely, and none of these are optional:

- Both damage channels — ordinary blows and raw over-time damage — return without acting unless `GameServer.IsServer`. A blow that reaches that line on a player's machine is aimed at something the server has never heard of, and it is dropped with a one-per-target-kind warning rather than in silence.
- Only the server populates a world with creatures. `FieldMobSpawner` switches itself off anywhere else.
- **The Editor is not exempt.** Pressing Play requires a running server. This was asked for explicitly: an editor-only bypass is the single-player path wearing a different hat, and it is where the two halves drift apart.

**Never gate behaviour on "is the wire up right now".** That is a timing accident, not a fact: dialling takes a moment, and anything asking during that moment gets the wrong answer and acts on it. This exact mistake let a map that finished loading mid-handshake fill itself with private monsters. Ask what job this copy was *given* — `GameServer.IsServer` — which is settled at start-up and cannot change while the process lives.

**A lost world ends the session.** Three attempts, with the reconnect notice up. All spent and the world is torn down: the net layer is forgotten, the session is signed out, the persistent scene reloads, and the sign-in screen returns carrying a line saying why. A frozen world left on screen would be a lie about state.

**It is a slow half-minute, not a quick six seconds — and the wait is not ours.** The gap between attempts is 2s, but that is only the gap. Nothing here bounds how long one attempt TAKES: a dial into silence is failed by the transport, whose own limit is 10s, so all three attempts run to roughly half a minute of "Reconnecting…" before somebody is put back at the door. Shortening it means moving the transport's timeout, and that same number is what decides how long a LIVE connection may go quiet before it counts as dropped — so it is one decision about two things, not a knob to turn casually.

**Per-hit and per-frame paths obey** `PERFORMANCE.md`**.** The shell-for-a-body lookup is a dictionary, and it is the *only* lookup — the component-search fallback was removed, because a fallback runs on every MISS, which is the common case of a creature hitting a player. Anything networked registers itself in that table; there is deliberately no second, slower way to be found. Idle bodies — players **and** creatures — skip their position and rotation blend entirely.

---

## Traps already paid for

**Order of construction.** Adding Mirror's manager component runs its start-up that instant, and the first thing it looks for is a transport. Finding none it logs one line and gives up **half-built** — singleton claimed, everything after skipped, and the rest of the log looks perfectly normal. Attach the transport first. This cost a session.

**A killed mob is not a destroyed mob.** It is switched off and kept for reuse, so a null check never fires. A shell must also let go when its body goes inactive, or every player watches a corpse frozen in place forever and a new shell piles up on each respawn.

**Something that never moves never sends.** A first update has to go out unconditionally, or a player who spawns and stands still is reported at the world origin and everyone sees them in the far corner of the map.

**Synced numbers are not applied numbers.** Health crossing the wire does nothing until something writes it into the body. Until that was added, mobs looked permanently at full health while dying.

**Scheduling a build for "shortly" silently loses it.** A domain reload discards pending callbacks, so the build never happens and nothing reports a failure. Call the build directly and accept that the request times out — it still completes.

**"Am I the one steering this?" has THREE possible askers, not two.** Mirror's ownership flag is only ever true on the machine that owns the thing, so on a dedicated server it is false for *everybody* — and code written as `if (owned) report; else watch;` puts the server in the watcher branch. `NetPlayer` did exactly that and the server was building a rigged, animated character body for every person who connected, on a machine with no screen. Split it three ways: owner reports, a **client** watches, the server does neither. `NetMob` was always right because it asks `isServer` first — copy that shape.

**A watched copy must not do its own arithmetic.** Let it subtract health locally and it can reach zero a moment before the server says so, count itself dead, get corrected back to alive, and then play no death at all when the real one arrives. A copy presents a blow in full — number, flash, impact — and writes no health: that arrives already correct. Its death is driven from the server's number, and its `Die()` is suppressed outright so it never announces a kill that every other machine also announces.

**Do not reuse the sign-in veil for an in-world message.** That front canvas owns a camera that clears the frame, so it blacks the world out — correct for a door onto the game, wrong for a moment inside it. The reconnect notice is its own thin overlay for that reason.

**Forwarding one damage channel down another channel's path corrupts it.** A poison tick briefly went to the server as a plain hit request, where it would have been re-rolled as a full basic swing. A raw tick needs no message at all: the server is already burning its own copy on its own clock, so the watching machine simply does nothing. **The same mistake, one level up:** a SKILL strike went down the basic swing's request too, and was silently served back as a basic attack. Same rule, both times — a channel is never squeezed into a neighbour's. A raw tick still needs no message; a skill now has a request of its own, which is what that rule was waiting for.

**A request must carry only what the server cannot already know.** The hit request used to name the blow's element and whether it was magic, and the server used both verbatim — which handed the client a damage dial: element multiplies straight against a creature's armour, and physical and magic are two separate defence numbers to choose the lower of. Both are facts about the attacker's WEAPON, which the server is already holding. Before adding a field to any message, ask whether the far end could look it up instead; if it could, that field is not information, it is an invitation.

**Everything watching receives a blow — only ONE machine may be taught by it.** A landed hit teaches its attacker things: a run of consecutive hits, the on-damage-dealt passives, a lifesteal trickle. The announcement goes to everybody, so it has to name who swung, and each machine credits the person at its own keyboard only when the id matches. Send it with no attacker and the lesson is lost everywhere; hand it to whoever is watching and every spectator is credited for a stranger's swing.

---

## What is done, and what is not

**Working, watched with two players:** the headless server boots and loads its map; players sign in, connect, get a body, and see each other move.

**Written, never run:** everything else — server-owned creatures, the whole hit round-trip in both directions, per-connection fighters, skills over the wire, range-based interest, and the client-side pool of watched bodies. It builds, it loads, and the editor compiles it clean. **Nobody has watched any of it behave.** Treat every sentence below about combat as a description of what the code says, not of what has been seen.

**The blocker is gone.** The server now holds a CHARACTER PER CONNECTION — a real `Character` built under that connection's shell, with that player's own stats — so it can roll their blows, and creatures can hurt them. Their numbers are read from the account service using the sign-in proof the client hands over on joining, so nothing about a fighter is taken from what the client says about itself. That proof also settles identity: the service only answers for characters on the account it was issued to, so typing somebody else's name no longer gets their character.

**Creatures choose whom to hunt.** A creature no longer looks for "the" player once at birth and holds whatever it found for life. It picks the nearest living person actually present, re-checked twice a second, and keeps them until they die, leave its leash, or a taunt takes the choice away. Walking past a monster already fighting your friend does not steal it off them.

**A kill pays whoever landed it.** The damage door records who last cost a unit health, and the reward goes to that unit — announced to that player's own machine, because progress is still written there.

**Both damage directions cross the wire.** A blow on a person is announced from the same door a blow on a creature is: the person it happened to sees it through the ordinary hit path, and everybody else sees a number over their head. Health for a person travels exactly as a creature's does — a figure the server keeps writing — and their machine stops working it out for itself. Dying and coming back are both the server's word.

**Skills cross the wire, carrying the strike they resolved.** Until now a skill strike was refused outright, so skills did nothing in a shared world. It is now forwarded with its own damage, element and flags; the server checks that the target is real, is not absurdly far away, and that nobody is sending thousands a second — and applies it through the same door as everything else.

**Only what is near you is sent to you.** Range-based interest is on at 60 units, comfortably past the 28 at which the game already stops simulating creatures.

**A watching machine reuses creature bodies.** Arriving shells take a spare body of the same kind rather than building a rigged model and throwing it away, which is what a field of respawning monsters used to cost every player.

### Deliberate limits, still true

- **Movement is relayed, not validated.** A player is still trusted about where they stand. Server-owned movement additionally needs prediction and correction on the client, or it feels laggy — its own phase, and the largest piece left. **Read this together with the reach checks:** the server measures whether a blow could reach from the position that player reported, so those checks are exactly as trustworthy as the position is, which today is not at all. They stop an honest mistake, not a determined one.
- **The training dummy takes no damage from a client.** It is a hand-built scene fixture with no shared identity, so a swing at it finds nothing the server has heard of and is refused out loud. It was only ever hittable because a player's machine used to decide damage. Making it work again means giving it a networked identity — and first answering whether a dummy is one fixture everybody shares or one each, which nobody has decided.
- **A skill's DAMAGE is the client's word.** The server does not hold the skill system, so it cannot roll a skill itself; it checks what it can and applies what it is told. This is the trusted half of the layer, and it is trusted on purpose rather than by accident — refusing instead is what made skills useless. Resolving skills server-side is the fix, and it is a whole piece of work.
- **A heal is the client's word too, for the same reason,** and so is the size of it. Health belongs to the server, so a potion drunk only on somebody's own machine would be undone by the server's next word about them; it crosses as a claim instead. It ends the same day skills do.
- **A player's own buffs do not protect them.** The character the server holds carries no buffs, barriers or parry charges, because everything that grants one still runs on the player's machine. So a shield shows on your screen and soaks nothing that the server decided. Same root cause as the two above.
- **Progress is still written by the client** to the account service.
- **Lifesteal and mana leech only pay out for a player attacker.** A creature having lifesteal is new gameplay nobody has specified, so it was deliberately not invented.
- **A watching machine re-runs the defender's mitigation** on a hit the server announces. Defence and element are data and agree; **parry is a state**, and a copy holds none, so it may briefly show a number the server parried. The authoritative health corrects it within a tenth of a second.
- **The hit rate cap is a flood guard, not a swing cadence** — set far above any real attack speed so it can never gate legitimate play.
- **A creature cannot yet be fought by two people fairly.** It picks one target at a time and there is no threat model; whoever it picked is who it hits.

---

## Order of remaining work, and why this order

1. **Play it.** Everything above is unrun. Two clients against one server, hitting the same creature, being hit back. Nothing further should be built on top of code nobody has watched work.
2. **Resolve SKILLS on the server**, which closes three holes at once — a skill's damage, a heal's size, and a player's own buffs mattering — because all three are the same missing thing: the character the server holds does not run the game's effects. This is the next real piece.
3. **Server owns player movement**, with prediction and correction. Hardest piece; needs the ones above settled first.
4. **Threat, so several people can fight one creature.** It picks the nearest today, which is honest but shallow.
5. **Progress written by the server** rather than by the player's own machine.

Mobs had to become server-owned *before* combat could, and that was found the hard way: every client used to spawn its own creatures, so two players never shared a monster and there was nothing for a server to adjudicate. Expect more ordering surprises of that shape — check who owns a thing before writing rules about who may change it.
---

## Related

- [Network-Readiness — Combat Audit & Refactor Plan](network-readiness-audit.md) — the July survey whose four prep steps this layer was built on top of.
- [Starting the Network](../1-planning/starting-the-network.md) — the runbook: database → account service → server → players.
- [Querying the Database](../1-planning/querying-the-database.md) — reading back the accounts and characters this layer persists.
- [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — the stat block the server builds per connection.
- [Active Skill System — V3](active-skill-system-v3/README.md) — the effects the server cannot yet resolve, and why that is the next piece.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — where this sits against the plan (Tier 2).
