---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - systems-combat-design
---

# Networking — Implementation Notes

Code-facing companion for the multiplayer layer. Read this before touching anything networked, or before adding a new kind of thing that has to exist for more than one player.

Sibling docs: **[Architecture](README.md)** (the shape on one page — start there) · [Network-Readiness Audit](readiness-audit.md) (the July survey that chose the shape) · [Starting the Network](starting-the-network.md) (how to run it) · [Querying the Database](../../1-planning/querying-the-database.md) (how to read what it persisted) · `charserver/README.md` (the character server).

---

## Traps paid for on 2026-09-05

**A map's place is found, not counted.** Every map a server holds is slid two kilometres from the others, because
they are all authored starting at the same point and would otherwise be stacked. The place was chosen by counting
how many maps were loaded — which was only ever right while nothing left. Unload one in the middle and the count
drops, so the next map to arrive is handed a place another map is still standing on: two worlds' ground, walls
and creatures in one spot, with nothing to say so. It now walks the places in use and takes the first genuinely
free one. **Anything that unloads a map inherits this**, so it was fixed before unloading was built.

**Dropping a connection writes that character down — including when you dropped it on purpose.** The takeover was
built by dropping whoever was already playing a character, and dropping is what a player leaving does, so the
machine being taken over wrote its now-stale copy over the one the new machine was already playing. The exact
duplication the takeover existed to prevent, arriving through the back door. A connection that is being
superseded now does not write on the way out; the write that mattered already happened, before the handover.

**Reading a character before the machine holding them has let go hands over a stale copy.** The first version of
the takeover read the row and then dropped the old machine, so whoever took over started from the last routine
save — up to fifteen seconds old — and everything since was never recorded anywhere. Not overwritten: never
written. The order is now write, wait, drop, then read, which is the order a door already used.

**A scene marking itself modified is a script writing saved data outside Play.** Two depth sorters ran in the
editor and wrote a draw order worked out from the SCENE VIEW CAMERA'S direction, so merely looking around the
world rewrote the scene and every build stopped to ask whether to save a change nobody made. The rule that came
out of it is at the top of the project's own instructions, above everything else.


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

**The table lives in [the architecture overview](README.md)** — one home for it, so a reader can never find two
answers to the same question. In one line: your machine owns how things *look* and when your own body reacts to
your own input; the server owns everything that is *true*.

---

## The files

| File | What it is |
| --- | --- |
| `GameServer.cs` | The single answer to "am I the server?". Read once at start-up and cached. A dedicated build says so itself; anything else reads the `GAME_SERVER` setting. |
| `GameNetwork.cs` | The one place the game joins the wire. Starts listening or dials out, handles a player arriving, validates and resolves an incoming hit, announces the result, and owns the retry-then-give-up loop when a world is lost. |
| `NetPlayer.cs` | One connected person, as everyone else sees them. |
| `NetMob.cs` | One creature the server owns, as everyone else sees it. |
| `ReconnectNotice.cs` | The "Reconnecting…" overlay shown while a connection is being retried. Dims rather than hides — a player being reconnected has not gone anywhere. |
| `PlayerFighter.cs` | The server's copy of one person's character — built per connection **from the character the character server pushed ahead of them**, and taken away when they leave. It fetches nothing; this machine never reads the store. |
| `MapServerLink.cs` | Server-only. Says what maps this server holds, listens on its own port for characters being pushed in, holds the people it has been told to expect, and beats every 30s so the character server keeps believing it. |
| `ServerHttp.cs` | How one SERVER talks to another, carrying the shared secret. Deliberately not `AccountClient`, which speaks as a PERSON — one function taking "some credential" would eventually be handed the wrong one, and neither would look wrong. |
| `BagWire.cs` | A whole bag, purse and worn gear, in the shape they cross the wire in. The same arrays the save uses, on purpose. |
| `BagEntry.cs` | One stack in a bag. Plain data, so the character can hold it without knowing anything about windows. |
| `PlayerUnits.cs` | Who is actually in this world, as fighters. One entry on a player's own machine, one per connection on a server. The thing creatures ask when choosing whom to hunt. |
| `Rewards.cs` | What a kill is worth and who gets it. One door, so the answer cannot differ between the machine that decided the kill and the one that records it. |

Prefabs `NetPlayer` and `NetMob` live in `Assets/3_Data/Resources` and are found by NAME, so neither half needs scene wiring. Mirror matches them across machines by asset id, so they **must** be real prefab assets — they cannot be built purely at runtime.

---

## Conventions to keep

**One place joins the wire.** `GameNetwork.Begin()` is called from exactly one caller. On a server that is boot; on a player's machine it is the moment a character is chosen. Two callers would let the halves start under different rules.

**The wire comes up BEFORE the world, not with it.** A server may bring its map up immediately — it *is* the world. A player may not: `GameRoot` dials, waits for `GameNetwork.OnJoinedWorld`, and only then wakes the scene and loads a map. Signing in no longer puts anybody anywhere; it earns the right to try.

**Environment settings, never rebuilds.** Where a build points is set by `ACCOUNTS_URL` / `CHAR_SERVER_URL` / `SERVER_SECRET` / `MAP_SERVER_ADDRESS` / `GAME_SERVER_PORT`, the same rule the sign-in service already followed. One build, aimed anywhere. Anything with no environment to read from — a copy handed to a player who launches it by double-clicking, and the editor when it was started from the Dock rather than a terminal — reads the same two address names out of a file the build carries, and the environment still wins wherever there is one. **A player's build still has to be told where the CHARACTER server is** — that is the one it cannot be sent to, because asking it is how a player is let in at all; what it no longer needs is the address of the world, which the character server hands over. A copy started without it cannot enter, and says so rather than knocking on a door that will refuse it.

**A player never carries their own character onto the wire.** They carry a one-time pass. Their character was pushed to that game server before they were told where to knock, so a connection that cannot produce a pass is refused before it is anybody. This is Ragnarok's order and the reason for it: the thing running the world never talks to the character store, and a client's token is never the key that opens it.

**Two roads, and they never cross.** A PERSON's token reaches their own characters and nothing else. The SERVERS' shared secret registers a game server, receives a pushed character and writes a save back — and cannot read anybody's character list. A player's token on the servers' road is refused, and the other way round.

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

**Forwarding one damage channel down another channel's path corrupts it.** A poison tick briefly went to the server as a plain hit request, where it would have been re-rolled as a full basic swing. A raw tick needs no message at all: the server is already burning its own copy on its own clock, so the watching machine simply does nothing. **The same mistake, one level up:** a SKILL strike went down the basic swing's request too, and was silently served back as a basic attack. Same rule, both times — a channel is never squeezed into a neighbour's. A raw tick still needs no message; a skill has its own message, and it names the CAST rather than a strike, because the whole skill is run there now.

**A request must carry only what the server cannot already know.** The hit request used to name the blow's element and whether it was magic, and the server used both verbatim — which handed the client a damage dial: element multiplies straight against a creature's armour, and physical and magic are two separate defence numbers to choose the lower of. Both are facts about the attacker's WEAPON, which the server is already holding. Before adding a field to any message, ask whether the far end could look it up instead; if it could, that field is not information, it is an invitation.

**A body destroyed before your callback runs takes your bookkeeping with it.** Releasing a departing player's character was done inside the reply to a save request — which arrives long after the connection has been torn down, by which time the object reads as null and the method returns before releasing anything. That character was then held for the life of the server and its owner refused as "already playing" on every attempt to come back. Copy what you need OUT of an object before an async call, and do the bookkeeping that must not be missed synchronously.

**A "retry" that re-sends a single-use thing can never succeed.** The reconnect loop dialled again with the pass it had already spent, so all three attempts were refused for a reason nobody would guess from the message. It now asks the character server again on every attempt, which mints a fresh pass and re-announces the person to the map server.

**And "the wire came up" is not "I am in".** The attempt counter was cleared, and the world woken, the moment the connection opened — but a refusal arrives a moment AFTER the connection opens, so the count reset on every attempt and the loop ran forever while the world came up on screen for somebody who had been turned away. **This was fixed once and came back**, because the fix was described in a comment at the top of the method while the reset stayed fifteen lines below it: a comment is not a mechanism. Both now hang off the one thing a refused connection never gets — the server handing this player a body. Reset a counter on the thing you actually wanted, not on the step before it.

**Saving from two places is a rollback waiting to happen.** While a save is in flight, another asked for beside it is a second whole document racing the first, and whichever lands last wins. One at a time, and remember the one you deferred. Give every server-to-server request a deadline, too: without one, a peer that accepts the connection and never answers leaves that player un-saved for the rest of the session with nothing saying so.

**Index-addressed requests need to say what they expected to find.** Naming a position in a list is unambiguous right up until the list moves between the click and the request arriving, which two quick clicks are enough to do. Carry the id alongside and drop the request when it no longer matches.

**Everything watching receives a blow — and no machine is taught by it.** A landed hit teaches its attacker things: a run of consecutive hits, the on-damage-dealt passives, a lifesteal trickle. All of that happens once, on the server, as the blow lands. The announcement still names who swung, but only so each screen can keep one rising total per attacker; a watching machine that credited anybody from it would pay the same lesson a second time.

---

## What is done, and what is not

**Working, watched with two players:** the headless server boots and loads its map; two copies of the built game sign in as different accounts, are let in by the character server, get a body, hold their connection and see each other move — **watched on 2026-09-02**, and the first time two clients have ever been run against this layer. A tester also watched the bag arrive from the server, an item land in it and survive a relog, and reordering hold across a sign-out.

**Watched working, one client:** a fight against server-owned creatures — the hit round-trip in both directions, the damage numbers, the creatures walking, running, swinging and flinching. **Played on 2026-09-02.**

**Written, not yet watched in a fight:** range-based interest at its real distance, and the swing timing now that the server decides when a blow lands rather than being told. **No fight has been watched with two people in it** — treat anything below about several players fighting as a description of what the code says, not of what has been seen.

**The blocker is gone.** The server holds a CHARACTER PER CONNECTION — a real `Character` built under that connection's shell, with that player's own stats — so it can roll their blows, and creatures can hurt them. That character is **pushed here by the character server** before the player is told where to knock, so this machine reads nothing from the store and nothing about a fighter is taken from what the client says about itself. Identity is settled the same way: a connection that cannot produce the one-time pass it was given is refused before it is anybody.

**Creatures choose whom to hunt.** A creature no longer looks for "the" player once at birth and holds whatever it found for life. It picks the nearest living person actually present, re-checked twice a second, and keeps them until they die, leave its leash, or a taunt takes the choice away. Walking past a monster already fighting your friend does not steal it off them.

**A kill pays whoever landed it.** The damage door records who last cost a unit health, and the reward goes to that unit — announced to that player's own machine so they see it, and written down by the server, which is what holds their character.

**Both damage directions cross the wire.** A blow on a person is announced from the same door a blow on a creature is: the person it happened to sees it through the ordinary hit path, and everybody else sees a number over their head. Health for a person travels exactly as a creature's does — a figure the server keeps writing — and their machine stops working it out for itself. Dying and coming back are both the server's word.

**THE WHOLE SKILL RUNS ON THE SERVER** *(2026-09-01)* — not just its damage. A cast crosses as one message naming the skill, what it was aimed at and where; the server runs the **same** skill machinery the player's own machine would have, with one runner per authored skill per connection, and the player's copy only draws it. Before this, only the damage effect ever reached the server — 45 of the 57 effects across the 42 composed skills were applied against a character the server never saw, so a brace armed nothing, buffs protected nobody, and charges were spent on a copy. A skill's damage, a heal's size and a player's own buffs are now all the server's word.

**A cast's randomness is seeded by the server**, so where a scattered shot lands is the same on every screen — and, deliberately, your own spell effect now plays on the server's word rather than at the press. The pose, the cast bar, the ring on the ground and the skill's own call still fire instantly at the keyboard that pressed it; all four reach everybody watching as well, described below. The one wind-up visual that does NOT appear instantly for the caster is the charge-up glow on their own body: that is a cue, and a cue is now always the world's to say — see the entry on cues below.

**SAYING SOMETHING CROSSES THE WIRE, AND SO DOES CALLING A SKILL'S NAME** *(2026-09-07)*. A typed line goes to the server carrying the words and nothing else — not who said them, not where they were standing — and the server says it back to everybody who can see the speaker, the speaker included. So nothing is drawn at the keyboard that typed it: one line said is one row and one white bubble on every screen, decided in one place, and a person cannot see a different chat from the people listening. A skill's NAME travels the same way and lands in the same chat row and the same bubble, only tinted — sent once per cast from the server, and never back to the caster, whose own machine says it the instant they press. Before this the whole of chat was local: two people standing together could see each other's spells go off and hear neither the words nor the names. A command is refused at that door rather than said out loud, because commands have their own, which asks whether the person is a GM. Both are flood-guarded like every other claim, four lines a second, and a line is cut at 200 characters rather than refused.

**A WIND-UP IS VISIBLE TO EVERYBODY WATCHING** *(2026-09-07)*. The server says when a cast starts, how long it runs against the character IT holds, and when it ended — finished or broken — and each watcher fills the bar in between on its own clock. So somebody else's four-second spell is four seconds of a filling bar with the skill's name over their head, where it used to be four seconds of them standing still followed by an explosion. The bar itself became one per CASTER rather than one in the game: it was a single widget on the reasoning that the player casts one spell at a time, which is true of the player and false of the world. A bar nobody refreshed for a frame puts itself away, so a lost end-message cannot leave one hanging over somebody forever. **The casting POSE crosses with it**, though not on this message: it rides the general channel that carries every held pose (below), which pushes it onto the watched body's own stance choke point exactly as the caster's machine pushes it, fitted to the same length. It deliberately does NOT travel down the channel that carries a swing — that one plays a clip once inside its beat, which would have played the incantation once and dropped the body back to idle for the rest of a long spell. A held pose and a one-shot action are different layers, and a cast is the held one. **The ring on the ground crosses too**: the wind-up message carries where the spell is aimed, and each watcher reads its own ring off the same authored skill.

**Only what is near you is sent to you.** Range-based interest is on at 60 units, comfortably past the 28 at which the game already stops simulating creatures.

**A watching machine reuses creature bodies.** Arriving shells take a spare body of the same kind rather than building a rigged model and throwing it away, which is what a field of respawning monsters used to cost every player.

**EVERY POSE AND EVERY CUE NOW CROSSES, AND BOTH ARE GENERAL** *(2026-09-07)*. Neither names a particular pose or a particular effect anywhere, so one added later travels without being wired up.

*Poses.* Reported from the single place every pose in the game resolves, on the frame it changes — so a jump held in the air, a dodge roll, a flinch, a body lying dead, a seat, a guard and a skill's release swing all reach every screen. Before this, the only thing a watcher ever saw a person's body do was walk and swing. Two poses are deliberately NOT reported, and both because something else already owns the answer: a travel gear (each machine works walking out from the speed and the gear it is told) and a weapon swing (the server announces that itself, from the weapon it holds). Which is which is asked of the stance vocabulary rather than kept as a list at the reporting site, so a new pose is reported by default — the safe way round.

*The sprint gear is told, not measured.* A hasted run and a sprint are both simply fast and no threshold separates them, so the machine that owns the legs says which gear it is in. Walk and run stay on the speed, which genuinely does separate those two.

*Cues* — every visual reaction in the game, from a hit spark to a poison glow — are announced by whichever machine DECIDED the event, which in a shared world is always the server. **A machine that has joined somebody else's world now fires none of its own and draws only what it is told, including cues on its own body.** That one rule is what stops the same puff playing twice on one screen and not at all on another, and it needs no list of which cue belongs to whom. It is the same bargain your own spell effects already make. A lasting glow is NAMED when it starts so the "it is over" can say which one, since two people can be poisoned at once; the name is fenced against the effect pool handing that runner to something else in between.

*Two things sit next to this and are deliberately NOT done, so nobody has to rediscover them.* **The floating
WORDS over a head still do not travel** — the stun flash reaches every screen but the word "Stun!" above it does
not. Floating numbers belong to the floating-number system, not to cues: damage numbers cross by their own route,
and so does a heal's green "+12" — told to the person healed, and to everybody watching a creature — so a second
general door for either would draw every number twice. The words still need a small message of their own. **And a release swing is capped at its own
natural length on a watcher**: the caster's machine stretches that animation by one over their attack speed, so a
caster under an attack-speed DEBUFF stretches it past the clip and the watcher's side clamps it back down. The
clamp is right for a creature's bite (it must not still be swinging when the next one lands) and wrong only here,
and only in that one direction — a buffed or normal caster is exact.

**A CHARACTER'S FACING IS THE MODEL'S, NOT THE ROOT'S** *(2026-09-07)*. A live-3D character turns by yawing the MODEL hanging under its root; nothing anywhere rotates that root. The position report read the root's angle, so every player told the world they were facing the direction their root was built at, forever — and everybody else watched them run around permanently facing one way. Read a facing off the pose sink that actually turns. The watched body's root is still what gets turned at the far end, and its own visual then turns its model to match, so the two models end up at the same angle.

**Movement is CHECKED, not simulated.** A player's machine still moves them — that is what keeps the controls answering at this camera distance — but every reported position is measured against how far they could possibly have travelled since the last one accepted, at their real top speed with their buffs. An impossible report is dropped rather than written down, and a run of them puts the person back where the server has them. It is forgiving on purpose: a wrong correction on an honest player is worse than a missed cheat, so a door, a respawn and any shove the server itself ordered are all let off. **This raises the reach checks with it** — they measure from a position that can no longer be arbitrary.

**A player's machine works out no game number** *(2026-09-14)*. It draws what the server sends and sends what the player pressed, and nothing else — movement is the one exception. So: a blow on a creature arrives as the number the server DREW, already past armour, element, parry and barrier, and the watching machine copies it onto the screen; a blow on yourself arrives the same way; no status, poison, regeneration or mana ticks on a player's machine; a heal's green digits are told by the server (a person's to that person, a creature's to everybody watching it); a cooldown starts on the server, which tells the owner when it ends; the character sheet — every derived number, each stat point's cost, the next jump and what worn gear adds to each main stat — is derived on the server and sent to its owner whenever it changes, checked twice a second; and a machine's copy of a creature rolls no gear and derives no stats. Nothing a player's machine sends carries an amount: the heal claim, the strike naming a pile reading, and the strike carrying a client-rolled number were all deleted.

**The bag, the money and the worn gear are the server's.** Equipping, taking off, reordering and using an item are all asked for and decided there; what comes back is the whole bag, and the window redraws from that. Loot is rolled by the server when a creature dies: its Zeny goes straight into the purse of whoever earned the kill, and every item it gave up is put on the ground where it fell. For 10 seconds only that person may pick those items up, then anybody may, and whatever nobody takes vanishes after 60 seconds. Picking up is a request the server checks — the same map, within reach, and not held for somebody else — and the first person it accepts gets the item. A client cannot add an item at all. Requests name a POSITION in the bag **and what the asker expected to find there**, so a second click landing inside the first one's round trip cannot act on whatever slid into that place.

**The server writes the save**, on a 15-second beat, on a map change, and on disconnect — where the write is asked for before the character is let go, which is the guard against the same items existing twice. A save from a player's own machine is refused outright while they are in a world. Spending a stat point is the server's too; arranging the hotbar and choosing passives are asked for and taken as said, because that is a person's own arrangement of their own things and cannot be worth more than somebody else has.

### Deliberate limits, still true

- **Whether a player really HAD the item they used is still their own machine's word.** The use crosses as a message naming the ITEM, and the server acts on it; the check that it was in the bag lands when the bag check moves server-side.
- **Lifesteal and mana leech only pay out for a player attacker.** A creature having lifesteal is new gameplay nobody has specified, so it was deliberately not invented.
- **One thing on a player's own machine still works numbers out**: the equip-hover comparison ("ATK 220 → 248"), a preview of a loadout the server has not been asked about. A skill's tooltip shows no damage estimate at all.
- **A blow on a creature that is already dead still shows its number** — a flurry beat, a swing or an arrow that was on its way, or a single-target skill landing on a body that just fell — and does nothing else. Damage over time on the dead shows nothing. The killing blow on a person shows too, whichever reaches their machine first, the blow or the health reading zero.
- **A BOW's flight time is still the swinger's to measure.** Every other basic swing is now asked for when it begins and landed by the server part-way through, on its own clock — but an arrow's blow lands when the arrow arrives, which only the machine flying it knows. So a bow asks at that moment instead, the server lands it straight away, and a bow's swing appears on other screens when the arrow lands rather than when it is loosed. See [Action Alarms](action-alarms.md).
- **What a body is DOING now crosses the wire** — which action, when it began on the shared clock, how long it runs — so a watched creature walks, runs and swings, and another person's swing exists on your screen. **Played on 2026-09-02 and it works:** see [Action Alarms](action-alarms.md).
- **A creature cannot yet be fought by two people fairly.** It picks one target at a time and there is no threat model; whoever it picked is who it hits.
- **A phone can sign in and then cannot enter the world.** The address typed on its sign-in screen aims the account service only, and entering needs the CHARACTER server's address, which nothing on a phone can supply — a phone reads no environment settings, and its copy of the shipped settings file lives inside the package where it cannot be opened as a file. Desktop is unaffected either way. Making the typed address serve both is a one-line change nobody has asked for yet; **noticed 2026-09-08 and deliberately not made.**

---

## Order of remaining work, and why this order

1. ~~**Play it with two people.**~~ **Done on 2026-09-02** — two clients, two accounts, both in the world and watching each other. What that run has NOT yet covered is a fight with both of them in it: two people on one creature, and each seeing the other swing.
2. ~~**Say what bodies are DOING**~~ — [Action Alarms](action-alarms.md). **Done**, including the server timing each swing and deciding when its blow lands. The animation half is played and works; the swing-timing half has not yet been watched in a fight.
3. ~~**Resolve SKILLS on the server**~~ — **done on 2026-09-01**, and it is the largest thing in this layer. The server runs the same skill machinery the client does, one runner per authored skill per connection, so a skill's damage, a heal's size and a player's own buffs are all its word now. What is left of it: the bag check behind using an item.
4. **Threat, so several people can fight one creature.** It picks the nearest today, which is honest but shallow.
5. **Server-SIMULATED movement**, with prediction and correction — only if checking turns out not to be enough. It is deliberately not the plan: this camera is close enough that a player must move their own body, and checking covers the whole of what a moving player can lie about.

Mobs had to become server-owned *before* combat could, and that was found the hard way: every client used to spawn its own creatures, so two players never shared a monster and there was nothing for a server to adjudicate. Expect more ordering surprises of that shape — check who owns a thing before writing rules about who may change it.
---

## Related

- [Action Alarms](action-alarms.md) — how a body says what it is DOING across the wire, and who owns the animation, the timing and the rendering.
- [Network-Readiness — Combat Audit & Refactor Plan](readiness-audit.md) — the July survey whose four prep steps this layer was built on top of.
- [Starting the Network](starting-the-network.md) — the runbook: database → sign-in → character server → game server → players.
- [Querying the Database](../../1-planning/querying-the-database.md) — reading back the accounts and characters this layer persists.
- [Unified Unit Stats](../active-skill-system-v3/unified-unit-stats.md) — the stat block the server builds per connection.
- [Active Skill System — V3](../active-skill-system-v3/README.md) — the effects the server now runs, one runner per authored skill per connection.
- [Roadmap to Launch](../../1-planning/roadmap-to-launch.md) — where this sits against the plan (Tier 2).
