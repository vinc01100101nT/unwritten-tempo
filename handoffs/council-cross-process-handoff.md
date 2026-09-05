# Council — moving a player between two world processes

## The target

If the world is ever split so that different maps run in different server processes (the way Ragnarok Online
does it), how does a player get handed from one process to another when they walk through a door? Plan it.
Nothing is being built — this is a plan the user has to accept first.

## The roster

Three brains, all covering the SAME ground. The user's instruction, in their own words: *"all. dont split into
angles. you 3 talk to each other. do not plan individually lol"* — so the usual one-angle-each split is OFF for
this run. Each brain plans the whole thing, then round two is each one attacking the other two's plans.

- **Brain A** — spawned, full brief.
- **Brain B** — spawned, full brief.
- **Coordinator** — the main session. Plans first-hand as well, and is the only voice the user hears.

Everything each of them must cover:

1. Losing or duplicating a character — the moment the player exists on both processes at once.
2. What the player sees — this is a real hang-up and re-dial, not a scene load.
3. What blocks a split today — the obstacles already measured.
4. How much of the existing login sequence can be reused, and whether reusing it is genuinely right.

## Agreement rule

Unanimous, or the split goes to the user unresolved. Nothing is edited until the user says go.

## Round 1

### Coordinator — first-hand findings

**My draft plan (the one the other two were told to attack).**

1. The player touches a door. Their own machine asks the server what to do instead of loading the next map by itself.
2. The process they are standing on writes their character back to the character server, with the new map name in it — the save road that already exists, fired once on demand instead of on its fifteen-second beat.
3. The player asks the character server to let them in, naming the target map. That request already accepts a named map; no game code has ever used it.
4. The character server does exactly what it does at login: finds who holds that map, pushes the character there, mints a one-time ticket, hands back an address, a port and the ticket.
5. The player hangs up from the old process and dials the new one with the ticket.

Plus two rules: only a door that crosses a process boundary pays for this (a process knows which maps it holds, so it can decide), and the two world processes must never hand the character to each other directly — it goes through the character server, which is already the only writer.

**What I found against my own plan, measured.**

- **Reusing the reconnect machinery as it stands would throw the player out of the game.** When the character server refuses the request, that path stops the connection and fires "lost the world". That handler signs the player out of their account entirely, tears the world down to the persistent layer, and puts the sign-in screen in front of them saying the connection was lost (`unity/Assets/Scripts/GameRoot.cs:78-84`, `unity/Assets/Scripts/GameNetwork.cs:268-269, 280-282, 288-292`). So if the target process is down, a player who walked through a door does not "stay where they were" — they get logged out. The requirement that a failed handoff leaves the player standing where they are is NOT satisfied by reuse; it needs a separate failure branch.
- **The reconnect path deliberately waits two seconds before re-dialling** (`unity/Assets/Scripts/GameNetwork.cs:59-60` — three attempts, two seconds apart; the wait itself at `:2051`). That delay exists because it is a failure recovery. A handoff is not a failure and must not inherit the wait, or every door across a boundary costs at least two seconds of standing still.
- **The retry and the give-up are already separate, which helps.** There is a guard that distinguishes "a reconnect, the world is already here" from a first arrival (`unity/Assets/Scripts/GameRoot.cs:72`). So the machinery already knows how to re-enter a world without rebuilding it. That is the half worth reusing; the give-up half is the one that must not fire.
- **The character server's push to a world process gives up after five seconds** (`charserver/MapServers.cs`, the shared web client's timeout). That is the worst case for step 4 before the player is told anything.

**What I did NOT measure:** the real length of the gap on screen end to end. The two-second wait and the five-second push timeout are fixed in code and I read them; the network legs and the client's scene load I have not timed, and I will not guess at a number.

### Brain B — findings (round 1)

Measured, with citations, in its own words where it matters.

**The push reads the DATABASE, not the live process.** The character server parses the stored row and hands that to the push (`charserver/Characters.cs:296` → `:316`, `charserver/MapServers.cs:165-174`). The running process's memory is never consulted. On login that row IS the truth. On a handoff it is a copy of unknown age — up to fifteen seconds stale (`unity/Assets/Scripts/NetPlayer.cs:429`, saves every 15s).

**Nothing anywhere knows who is currently in a world.** Grepped all eight character-server files for every word for it; the only "one copy of this character" guard is a set held inside a single world process (`unity/Assets/Scripts/PlayerFighter.cs:141`, checked at `unity/Assets/Scripts/GameNetwork.cs:1868`). So after a move, a second client can ask to enter naming the OLD map, the character server pushes the character there too, and both processes hold a full copy writing whole-document saves. Last write wins. **Items and gold are duplicable by racing two clients.** Ranked the number-one risk.

**Blocker 1 is worse than "newest wins once".** Every process re-registers on a thirty-second beat with a fresh timestamp (`unity/Assets/Scripts/MapServerLink.cs:269, 273-278`, `charserver/MapServers.cs:63, 81-83`). Two processes claiming one map do not settle — ownership FLIPS every thirty seconds, and players entering are dealt to whichever spoke last.

**The old process's parting save clobbers the new one.** On disconnect it saves unconditionally (`unity/Assets/Scripts/GameNetwork.cs:1945-1958`) writing the OLD map name (`unity/Assets/Scripts/NetPlayer.cs:485`), while the new process's first write is fifteen seconds out. The recorded map goes backwards; a relog in that window puts the player back in the map they left.

**A lost save is lost silently.** If the parting write fails, the retry flag is set on an object destroyed on the next lines (`unity/Assets/Scripts/GameNetwork.cs:1953-1966`). Only a log line remains.

**Direct hit on the coordinator's step 5.** A reconnect does NOT load a map — the world-up handler returns early when the world is already up (`unity/Assets/Scripts/GameRoot.cs:72`), and the login path's map load lives in the save-load callback that only runs when the character object is replaced (`unity/Assets/Scripts/SaveManager.cs:63, 105, 156-171`). So the draft as written connects the player to the new process while they are still standing in the old map's scene.

**The arrivals door does not check that it holds the map** (`unity/Assets/Scripts/MapServerLink.cs:170-203`). It checks the secret, the path, the ticket's presence and the build string, and nothing else. A push to a process that does not hold the target succeeds.

**Cooldowns and buffs are not in the save at all.** What gets written is level, xp, HP, MP, stats, ranks, ring, gold, hotbar, passives, equipment, bag, appearance, stamina (`unity/Assets/Scripts/Character.cs:1524-1559`); cooldowns live per-process on a fighter host built fresh and destroyed on dismissal (`unity/Assets/Scripts/SkillRunner.cs:132, 710`, `unity/Assets/Scripts/PlayerFighter.cs:60-73, 168`). **So a handoff is a free cooldown reset for every skill, obtainable by walking through a door.** An exploit, not a loss — and carrying them across is a design decision for the user.

**Party, trade and player chat do not exist yet.** Neither word appears in any game script; chat is a local log and a local speech bubble with nothing crossing the wire. So that worry has nothing to break.

**Mobs mid-chase are clean** — a creature drops its prey when the player is gone (`unity/Assets/Scripts/MonsterAI3D.cs:665`).

**The ticket's thirty-second clock starts before the push**, whose own timeout is five seconds (`charserver/MapServers.cs:134, 155`). The player then still has to hang up, load a scene and dial.

**There is no curtain to hide the gap behind.** A portal today has no fade, no panel, no notice. The only existing screen says the literal words "Reconnecting…" (`unity/Assets/Scripts/ReconnectNotice.cs:18`) — the wrong words for a doorway.

**The client's request to the character server has no deadline** (`unity/Assets/Scripts/AccountClient.cs:50-70`), while the server-to-server one sets ten seconds and says why (`unity/Assets/Scripts/ServerHttp.cs:27`). A hung character server leaves the client waiting forever.

**Two corrections to the brief:** one cited file lives under the Mirror package folder, not the game scripts folder (the line numbers were right); and the docs contain no plan for splitting at all — the words for it appear nowhere in the three named documents.

**B's plan differs from the coordinator's in five places:** the OLD PROCESS drives the whole move, sequenced on its own save callback, rather than the client firing blind; the character document travels WITH the move request so the database is never re-read on this path; a separate client path instead of reusing the reconnect machinery; a residency record on the character server; and a "leaving" latch that makes the old process refuse every command from that player from the moment the move starts. It also dials the new process BEFORE loading the scene, because the ticket's clock is already running and a cold scene load is unbounded.

**B's smallest blocker set:** (1) give the first map an environment override outside the editor-only guard; (2) refuse a NAMED map nobody holds, keeping the arbitrary fallback only for the unnamed case; (3) add the residency record; (4) add the move route; (5) give the client's requests a deadline. Items 1–2 are needed for a split with no handoff at all; 3–5 before a handoff is safe.

**B did NOT measure:** the length of the gap ("anyone stating a number for this is guessing"), whether Mirror tolerates a stop and start in quick succession, the loot claim window, the profile store, and whether any scene besides the persistent one is in a server build's scene list.

**B's confidence: 80%.**

**New words B introduced:** "residency record", "leaving latch" — both B's own, neither in the codebase.

### Brain A — findings (round 1)

**A's framing of the flaw in the draft:** it makes the database the transport for the character, and the write door accepts a whole-document write from ANY registered process with no owner check, no version and no ordering (`charserver/Program.cs:131-168`). Two processes holding a live copy both write, last one wins. A's plan adds exactly one new fact — which process currently owns each character — and makes every write and every entry check it. That turns a race into a refusal.

**Findings A measured that B did not have:**

- **A failure at a door would CLOSE THE PLAYER'S GAME.** All three failure branches of the "ask where to enter" path call a routine that is `Application.Quit(1)` in a build and stops play mode in the editor (`unity/Assets/Scripts/GameNetwork.cs:269, 281, 290` → `:370-376`). Verified independently by the coordinator. Defensible at login; catastrophic at a doorway.
- **Firing the save on demand silently does nothing, some of the time.** The save routine returns false WITHOUT WRITING when another write is already in flight (`unity/Assets/Scripts/NetPlayer.cs:465-470`) — which is roughly whenever the fifteen-second beat is mid-flight. Verified independently by the coordinator.
- **A save from a player whose map is not yet known ERASES their stored map and position.** Those fields are written only when the map name is non-empty (`unity/Assets/Scripts/NetPlayer.cs:484-490`), while the character server replaces the whole document (`charserver/Program.cs:153`). Verified independently by the coordinator.
- **The player's own machine can write the database during the gap.** The client's autosave declines only while a live body exists; during a handoff there is no body, the character object survives, so a dirty autosave on its twenty-second beat — or on quit — writes the client's copy over the world process's (`unity/Assets/Scripts/SaveManager.cs:24, 78, 174-215, 184-189`).
- **A ticket is spent even when it has already expired** (`unity/Assets/Scripts/MapServerLink.cs:79-85` removes the entry, then tests expiry).
- **The arrival crosses a thread boundary before it is usable.** The push is queued on the listening thread and answered 200 immediately, then moved into the expected list on the next game frame (`unity/Assets/Scripts/MapServerLink.cs:190-202, 227-236`). The character server is told "accepted" before that frame runs.
- **A vanished client's body lingers up to ten seconds** (`unity/Assets/Mirror/Transports/KCP/KcpTransport.cs:30`).
- **Eighteen doors a player's actions come through** — nine commands on the shell and nine server-side handlers (`unity/Assets/Scripts/NetPlayer.cs:525, 544, 553, 628, 645, 659, 696, 743, 804`; `unity/Assets/Scripts/GameNetwork.cs:668-676`). Two guards cover all eighteen, because the project's centralisation rule forbids eighteen edits. The loot claim is the one that duplicates items if this is skipped.
- **Positions on the wire are map-relative, so they carry across processes correctly** (`unity/Assets/Scripts/NetPlayer.cs:487-489, 841`; `unity/Assets/Scripts/MapManager.cs:212-215, 223-243`).
- **The freeing of the character name happens immediately on disconnect, on purpose** (`unity/Assets/Scripts/GameNetwork.cs:1978-1981`, `unity/Assets/Scripts/PlayerFighter.cs:150-169`) so a slow save cannot lock somebody out. For a handoff that is wrong — release must wait on the push being confirmed.
- **A portal today has NO fade, NO curtain, NO loading screen.** So the draft's "hide it behind what a portal already looks like" has no basis: a portal currently looks like nothing. The only existing screen is a 55%-opacity dim reading "Reconnecting…" (`unity/Assets/Scripts/ReconnectNotice.cs:18, 52`) — the wrong words for a doorway.

**A's verdict on "the handoff IS the login sequence".** Partly true, and the true part is the cheap part. The character TRANSPORT is genuinely done and reusable unchanged — the named-map parameter, the lookup, the ticket, the push, the arrivals door. But login assumes first entry in four measured places: it can quit; it rebuilds the world; it routes from the SAVE rather than from the door (so a reconnect one second after a portal sends the player back through the door they just walked out of); and it is a player-token route, so the client asks and the old process never learns the answer and has nothing to roll back to. **Reuse the transport, build the decision.** Bending the login route to also mean "hand over" needs three carve-outs inside the only road anyone gets into the world by — the dual-path rot the project bans elsewhere.

**A's confidence: 80%.**

---

## Round 1 — where the three brains stand

### Agreed, unanimously, arrived at independently

1. **The door asks the server. The client never travels on its own.** A refused move must leave the player exactly where they were standing.
2. **The OLD PROCESS drives the move — not the client.** Both brains rejected the coordinator's step where the client asks. Three measured reasons: the client's ask-to-enter path quits the game on failure; routing through the client lets the client name its own destination; and the old process must learn whether the move was ACCEPTED before it lets go, or there is nothing to roll back to.
3. **A "leaving" latch on the old process** — from the instant the move starts, every request from that player is refused, at two choke points covering all eighteen entry points, and the fighter leaves the target list so no blow, loot or experience can arrive.
4. **Flush the save and WAIT for it to land** before anything is pushed. The existing save routine cannot do this as written.
5. **One new fact: which process owns this character right now.** Recorded on the character server; every write door and every entry checks it. Both brains rank this the number-one fix — without it, one character is live on two processes and items and gold are duplicable by racing two clients.
6. **Kill the silent arbitrary fallback for a NAMED map.** Keep it only for a character that has never played and names none.
7. **Do NOT reuse the reconnect machinery's failure half** — its flat two-second wait, its attempt counter, its sign-out, its quit.
8. **Reuse the transport half unchanged** — the named-map parameter, the lookup, the ticket, the push, the arrivals door. That part is genuinely already built.
9. **Refusal means rollback**: the latch drops, the player stays on their side of the door, and is told in words.
10. **Cooldowns, buffs, debuffs and ailments are in no save**, so a handoff is a free cooldown reset obtainable by walking through a door. Both brains flag it as an exploit and both refuse to decide it — it is the user's design call.
11. **Party, trade and networked chat do not exist yet**, so nothing there breaks.
12. **Nobody measured the length of the gap, and all three refuse to give a number.**

### The one split

**When the client loads the new map, relative to dialling the new process.**

- **Brain A:** both at once — start the local map load and dial the new process together.
- **Brain B:** dial first, wait for the new process to confirm the player is in, THEN load the map. Its reason: the ticket's thirty-second clock is already running before the push even starts, and a cold map load is unbounded, so loading first can burn the ticket.

Not resolved. It goes to the user.

## The user's decisions (2026-09-04, in their own words)

**On cooldowns — decided, no longer open:**

> "let's save the cooldowns to the database. that means, when the user logged out, their cooldowns will pause,
> and will only resume when they are logged in the moment they land on a map. that also means we are saving the
> literal cooldown remaining seconds, not timestamp, we are not matching time stamps upon logging in."

**On the gap on screen — decided, no longer open:**

> "we will add loading screen. for now, let's reuse the background image from the login screen and just put a
> loading progress. make that loading progress real, not aesthetic, meaning, it actually progress with the
> actual handoff/login progress."

**Still open:** the split over when the client loads the new map relative to dialling the new process. The user
has not answered it.

## Round 2

### Brain A — round 2

**A conceded the split: dial first, load second.** Not on B's reasoning — A measured that B's reason was wrong. The character server's ticket list is read by nothing but its own expiry sweep (`charserver/MapServers.cs:142, 149-150`), and the character server deliberately has no "spend this ticket" door (`charserver/Program.cs:170-173`). The only door that can refuse a stale ticket is on the world process, measuring against SIXTY seconds from the frame it dequeued the arrival (`unity/Assets/Scripts/MapServerLink.cs:75, 79-85, 232`). So the real window is 60 seconds from the push landing, not 30 from minting. B's premise did not support B's conclusion.

**A conceded anyway, on asymmetry of failure.** Dial succeeds and the load lags: ugly, logged, and self-correcting the moment the scene finishes. Load succeeds and the dial fails: the client stands in the new map connected to nobody, the retry path runs out, and the game CLOSES (`unity/Assets/Scripts/GameNetwork.cs:370-376`). One recoverable failure against one that quits the application.

**A's correction to B's ordering:** as B stated it, every handoff deliberately passes through the "reports a map the new process is not holding" state, one logged warning per portal per player forever. Fix: the client reports the TARGET map at the moment it dials, not the map it is standing in (`unity/Assets/Scripts/NetPlayer.cs:392`).

**A's attack on B that stands:** carrying the character document with the move request removes the STALENESS but does nothing about the SECOND WRITER. The write door still accepts a whole-document write from any process holding the shared secret (`charserver/Program.cs:131-168`). So the ownership record must GATE THE WRITE DOOR, not merely route entries. Carry-the-document alone loses to the old process's late write; gate-the-door alone leaves a fifteen-second-old row pushed and never corrected. **Both halves are needed; picking one ships a plan that loses characters.**

**Cooldowns — A's finding that changes the shape.** Runners are created LAZILY on first cast, so at restore time they do not exist; the restore must park the numbers on the character and have the one place both runner-creation paths pass through consume them. And **the clock stops by itself** — between the flush and the arrival the character is not a live object anywhere, so the pause is structural, not something to implement.

**Cooldowns — A's warning that needs the user.** The player's own machine keeps ticking its cooldowns through the gap, because the cast routine has no server-only branch and the character object survives on the client. The server pauses; the client does not. So the decision needs a second half: **restore the client's cooldowns from the server's numbers on arrival**, one owner for the value.

**A also confirmed the sign-in screen has NO background image** — an opaque near-black fill plus the game's logo at 10% alpha, 900x900, centred (`unity/Assets/Scripts/SignInScreen.cs:237, 268, 280-296`), the texture being the only file in its folder.

**A's milestone list: steps 4, 5 and 6 — the flush landing, the target being found, the push being accepted — are the whole server-to-server middle and the client can observe NONE of it**, and that is where the real latency is. A client-only bar runs to 3, stalls through the interesting part, then runs 7 to 12 — exactly the failure the user named. Fix: the old process sends those three milestones down the still-open connection as they happen, which it can only do because it drove the move.

### Brain B — round 2

**B conceded the flush hole.** Verified A's finding itself, and went further: the save routine is also called with nobody watching, from the beat and from shutdown, so a handoff could be blocked by a write it cannot wait on. B's fix: when a write is in flight, ADD the new caller to that write's waiters instead of refusing it. **But B says that alone is still wrong** — the in-flight write was captured BEFORE the player touched the door, so joining its waiters moves the player on a document that predates the door. Correct shape: latch, drain the in-flight write, THEN start a fresh one and wait on that. Worst case two sequential writes, each bounded by a ten-second deadline — up to twenty seconds, which is a real cost and a reason the progress screen needs a step for it.

**B WITHDREW its own dial-first ordering** after measuring that its premise was false — it confirmed A's finding that the character server's ticket list is read by nothing, independently.

**B did NOT concede to A's "both at once" either, and proposed a THIRD order nobody had put forward:** load the target scene WHILE STILL CONNECTED to the old process, then hang up, then dial. B's reasons: the scene load is paid inside a live connection so a stalled load leaves the player somewhere recoverable; there is no window where the new process holds a player whose map it does not know; and dialling last makes A's thread race unlikely instead of typical. Safe only because the leaving latch already blocks everything.

**Both brains agree on the fix that mostly dissolves the argument:** the arrivals door must not answer "accepted" until the arrival is actually usable on the next frame (`unity/Assets/Scripts/MapServerLink.cs:202` against `:233`). B's words: "Tuning client ordering around an engine race is a workaround; this is a fix."

**B's attack on A that stands: the lock is on the wrong door.** An owner check on the WRITE door stops the losing copy from saving but not from EXISTING — the character server would still push it, the target would still accept it, and that player fights, loots and levels for up to fifteen seconds before losing all of it silently. **A write-door lock converts silent duplication into silent LOSS.** The lock has to be on the entry door, which is the only place that can refuse before a second live copy exists. B grants A that a write-door check is a good SECOND lock — just never the first.

**B corrected A's count: seventeen doors, not eighteen** — the join request must NOT be latched, because it serves a connection that has no player yet, and latching it would gate a different person arriving. And "two guards" undercounts: six handlers reach the actor one way, but four more reach it directly and would sail past. **The guard must not go inside the shell lookup itself**, or a server shutdown mid-handoff silently skips that player's write.

**B found A's client-write hole is worse than A said** — the guard exists in TWO places and both test the same expression, so both open at once during the gap. Two independent triggers: a twenty-second beat and application quit.

**Cooldowns — B agrees with A's shape independently** (lazy restore at the moment a runner is built, the pause being structural). B adds: store only the skill id and the seconds, never the duration, because the duration is an authored number and a stored copy is the second-owner-for-one-value the project bans. And **the latch and the cooldown save are one change, not two** — without the latch a player casts one more skill after the flush and arrives with it ready.

**B's rule to write down:** an absent cooldown entry always means READY, and the arrays must never be repurposed to positively assert readiness — the moment they mean "available", a missing value becomes ambiguous.

**B on the loading screen:** confirmed there is no background image, and found the sign-in look is already exposed through a public helper that builds the camera, the opaque veil and the emblem and hands the veil back — so a loading screen can have the sign-in look with no copy. **B's honest recommendation, labelled as B's:** a named STEP LIST that changes when something actually happened, plus a real bar over the ONE stretch that has a real number (the scene load) — because two ten-second writes with no inside guarantee the stall the user wanted to avoid. B also found the scene load currently throws away the progress value it would need.

## Round 2 — where the three brains stand

**Converged, both brains, after attacking each other:** the flush must drain-then-write-fresh; the latch covers seventeen doors at the actor accessor and not the shell lookup; the client's own writer must be silenced during the gap; a save with no map name must be refused outright; the arrivals door must not answer until the arrival is usable; cooldowns captured at the flush and restored lazily; the latch and the cooldown save ship together; the loading screen reuses the sign-in look through its existing helper; a step list beats a percentage.

**Converged on the lock, from opposite directions:** it goes on the ENTRY door first (B's point, which A did not have) AND on the write door second (A's point, which B did not have). Both are needed. Neither alone is safe.

**The remaining split — the client's move order.** A now says dial first, then load. B withdrew dial-first and now says load while still connected, then hang up, then dial. They swapped positions and landed apart. Both agree the arrivals-door fix removes the race that motivates most of the disagreement. **Unresolved; goes to the user.**

## Round 3 — idle zones

### Brain A

**The finding that changes the question: the creatures in those measurements were ALREADY ASLEEP.** The runbook says the numbers were taken with nobody playing (`docs/1-planning/starting-the-network.md:448`), and the project already stops a creature's brain when no player is within range on its own map (`unity/Assets/Scripts/SimulationLod.cs:77, 89-94`; the brain returns on its first line at `unity/Assets/Scripts/MonsterAI3D.cs:460`). **So a fiftieth of one percent of a core per creature is the price of an ALREADY-FROZEN creature.** Any "pause" built by gating brains and movers saves nothing — that saving has already been taken.

What is left is the engine still calling into every frozen creature every frame to do nothing: about 217,000 calls a second at the full world. That figure is A's arithmetic on measured values, and **nobody has measured what those calls cost**, so the size of the prize is unknown.

**A's correction to the coordinator's arithmetic.** The fit holds and A could not destroy it, but: the rate is fitted from the same three points it is checked against, so it is not independent confirmation; the two intervals disagree by 15% and the whole-percent rounding hides it, so the rate is really established only by the top pair; and **the ~13% intercept is NOT the cost of a bare process** — it is a process holding the starting town, which carries twelve townsfolk each with their own update (`unity/Assets/Scripts/AmbientLife.cs:29`, `unity/Assets/Scripts/TownNPC.cs:177`). So the coordinator's "per-zone processes pay 13% each" OVERSTATES the penalty. And the measurement that would settle it cannot be taken today, because the first map is fixed in code and in the scene with an editor-only override — **the same blocker found in round 1 is blocking the measurement that would answer the process-count question.**

**The cheap sleep already has a working precedent in this codebase** — the persistent layer is already quieted and woken by walking a scene's root objects, recording exactly which were on, switching them off, and restoring exactly those (`unity/Assets/Scripts/GameRoot.cs:104-123`). A bonus falls out: a disabled creature removes itself from the list every targeting scan walks linearly, so sleeping one map speeds up the maps still awake.

**The registry answer: sleep by disabling roots and NOTHING about the handoff changes.** A scene slept that way is still loaded, so the process still announces it and the character server still routes to it. No third state, no wake-in-the-middle-of-a-move, no protocol change. Unloading forces the opposite and buys a large amount of new surface for nothing the cheap version does not already give.

**Sleeping this way saves NO MEMORY.** The objects, meshes and textures stay resident; only their update calls stop. Only unloading frees memory — and unloading carries the registry problem AND a real coordinate-collision bug already sitting in the code: the map parking allocator hands out a slot by counting current maps and shrinks that count on unload, so unloading a middle map and loading another drops the new one directly on top of an existing one (`unity/Assets/Scripts/MapManager.cs:126, 227`). The author anticipated unloading and fixed only half of it, and says so in a comment.

**The one genuine breakage: respawn timers die and do not come back.** Both waits are coroutines (`unity/Assets/Scripts/FieldMobSpawner.cs:157-163`, `unity/Assets/Scripts/MobRespawn.cs:53-63`), and a coroutine stops when its object is disabled and does not resume. A map slept with dead creatures mid-respawn wakes with those creatures permanently missing — **the population silently shrinks every time it sleeps.** Fix: store a time to wake up at, checked on wake, rather than a running timer.

**A slept map wakes fully populated; a never-opened map is empty.** That difference is the whole value of sleeping over never loading.

**A's honest verdict:** the processor saving is real and probably large but its size rests on one unmeasured cost; there is no memory saving with the cheap version; and "save processes" is the wrong expectation, because sleeping happens inside one process and does not reduce process count. **The user believed this decision would change the handoff design. On the evidence it does not, provided sleeping is done by disabling roots.**

**The one experiment that turns the whole question into a number:** run the server holding every map, sleep them all, and read the same processor figure. One afternoon.

**A's confidence: 82%.**

### Brain B — round 3 (contradicts A on the central recommendation)

**B agrees with A that the brains are ALREADY frozen** — same measurement, reached independently (`unity/Assets/Scripts/SimulationLod.cs:77, 89-94`; `unity/Assets/Scripts/MonsterAI3D.cs:460`).

**But B found where the measured cost per creature actually goes, and it is NOT the brains.** Two per-frame network costs the freeze never touches:
1. The interest manager walks EVERY spawned network object every frame (`unity/Assets/Mirror/Components/InterestManagement/SceneDistance/SceneDistanceInterestManagement.cs:129, 131, 134`), marked to run on the server whether or not anyone is connected (`:119`), and walks them ALL again every second reading positions (`:20, 188-199`).
2. Every creature's network shell runs its update on the server every frame with **no freeze check anywhere in it** (`unity/Assets/Scripts/NetMob.cs:171-173, 185, 194-212`).

And every creature is TWO objects — the creature plus a separate network shell (`unity/Assets/Scripts/GameNetwork.cs:82-84`).

**So B's conclusion is the opposite of A's: unloading beats pausing in place, decisively.** Pausing in place cannot reach either cost — the objects stay in the engine's spawned set and the shells keep ticking. **"Pausing saves the part that is already free; unloading saves the part that actually costs."**

**B found the SAME parking-slot collision A found**, independently, and calls it a hard blocker on the unload route.

**B's refinement of the arithmetic.** Both brains call the coordinator's verification CIRCULAR — the rate was derived from the same two points it was checked against, so it had to fit. The conclusion survives via the OTHER point: the line predicts 13.39% at zero creatures and 13% was measured on the town alone. B's further refinement: an extra EMPTY map costs somewhere between nothing and a few tenths of a percent — **below the precision of the inputs, so it should not be reported as a number at all.** Roughly 13% is the cost of the PROCESS existing, paid once; the creatures are the entire variable cost.

**B's sharpest point for the user: sleeping maps and per-zone processes PULL AGAINST EACH OTHER.** The 13% is per process, so splitting the world into more processes pays it again per process, while sleeping maps does nothing to that. They are not complementary — the second undoes what the first saves, several times over, before a single creature is counted.

**B's two cheap fixes that get most of the win with none of the architecture** (B names them as the user's call, not a recommendation):
1. Give the creature's network shell the same freeze check the brain already has — one condition in one file.
2. Raise the interest manager's rebuild interval, or skip empty scenes in its per-frame walk.

Neither touches the handoff, the registry, or anything planned. **B's recommendation, labelled as B's: measure what the cheap version alone recovers before paying for the expensive one.**

**B says the idle-zone decision DOES change the handoff** (A said it does not). Under unloading, the wake must happen on the target BEFORE the push, adding a milestone the client cannot see and needing its own deadline with the same rollback every other failure gets. B also argues waking makes A's thread race substantially MORE likely, because it puts a synchronous frame spike immediately before the moment that race is decided.

**B on the wake cost:** the spawner builds its whole population synchronously inside one frame (`unity/Assets/Scripts/FieldMobSpawner.cs:76`), each creature costing a ground raycast, an asset load and two spawns. **That spike is paid by everyone on that process, not by the player walking in.**

**B on what breaks:** respawn timers do NOT break under unloading (nothing dies in an empty map, and the spawner rebuilds the full population on wake) — which contradicts A, whose breakage was specific to disabling roots. **Dropped loot DOES break** — B searched and found no lifetime on it, so a drop sits until claimed and an unload destroys it.

**B's confidence: 78%, deliberately lower.** The code reading is solid; the ATTRIBUTION of the measured cost to those two walks is reasoned from code shape, not profiled, and the whole "unload beats pause" recommendation rests on it. B's own words: "If a profiler says the cost is somewhere else, that recommendation should change and I would want to be told."

## Round 3 — the verdict

**Agreed by both:** the creature brains in an empty map are already frozen, so the existing measurements are of already-frozen creatures; the coordinator's arithmetic check was circular though its conclusion survives by another route; roughly 13% is the cost of a process existing and an extra empty map is nearly free; the map parking allocator will drop one map on top of another the moment anything unloads; memory is unmeasured and only unloading could free any; sleeping does not reduce process count.

**SPLIT — the method, and it is the biggest one yet.**
- **A: sleep by disabling the map's objects in place.** Cheap, has a working precedent in the codebase, keeps the map announced so the registry and the whole handoff plan are untouched. Saves no memory. A's breakage: respawn timers die and never resume.
- **B: unload the scene.** The only version that reaches the costs B measured, and the only version that frees memory. Requires a second registry list, a wake step inside the move with its own deadline, and a fix to the parking allocator. B's breakage: dropped loot is destroyed.

**The split turns on ONE unmeasured question:** whether the measured cost per creature is the empty update calls (A's premise) or the two network walks (B's premise). **A profiler run settles it.** Neither brain profiled anything and both say so.

**Unresolved. Goes to the user.**

## The user's decision on cooldowns (2026-09-04, in their own words)

> "ah if there are discrepancies on the cooldown, let's make it server-timed instead of skill's own time."

**The coordinator's reading, labelled as the coordinator's:** the server's number is the only truth. The server tells the player's machine the remaining seconds at the moments it CHANGES — when a skill is cast, and when a player lands in a world — and the player's machine counts down from that number for display only. It does NOT mean the server pushes the value continuously; that would be per-frame traffic per skill and the project's performance rules forbid it. The server stays the only thing that decides whether a cast is allowed.

**Measured by the coordinator this run:** the cast routine has no server/client branch (`unity/Assets/Scripts/SkillRunner.cs:572-589`), gating on its own clock at `:574` and setting it at `:586-587` — both machines run that same clock independently. The only consumers of the number are the hotbar's ring and its seconds text (`unity/Assets/Scripts/HotbarPanel.cs:410, 423`, through `unity/Assets/Scripts/IHotbarSkill.cs:30, 32`); no other reader found.

Both brains are working round 4 on whether that reading is right and what it changes.

## DECISION SUPERSEDED — all timers are server-timestamped (2026-09-04)

The user's earlier cooldown decision (pause on logout, store remaining seconds) is **withdrawn by them**. Their words: *"no. disregard my earlier suggestion. focus now on server time sync"*, clarifying *"for all the timer problems you think when we shut down a map entirely such as mob spawns, boss spawns, cooldowns, etc. the solution is just to make those timer server synced. we save server timestamp instead of plain duration."*

**So: every timer that must survive anything — a save, a sleeping map, a move between processes — stores an absolute server moment, not a remaining duration.** Cooldowns included, which means a cooldown keeps burning while a player is logged out. The coordinator raised the collision with the earlier decision and the user overrode it deliberately.

### The finding that changes the work: THIS ALREADY EXISTS

`unity/Assets/Scripts/WorldClock.cs` is already the shared server clock. It asks the account service `/time` once (`accounts/src/server.ts:23`), turns the answer into an offset from the local steady clock, and exposes `UtcNow` (`:29`) and `SecondsSince(epoch)` (`:32`). Its own comment states the user's exact reasoning: the day/night cycle used to run off the player's own machine, which is wrong the moment there are two players.

**It is used by exactly two lines in the whole project:** the sign-in calls `Sync()` (`unity/Assets/Scripts/AccountSession.cs:87`), and the day/night cycle reads `SecondsSince` (`unity/Assets/Scripts/TimeOfDay.cs:94`). Nothing else.

Every timer the user named still uses a per-process counter that restarts at zero when that process starts — cooldowns at `unity/Assets/Scripts/SkillRunner.cs:574, 586-587, 710`, field respawn at `unity/Assets/Scripts/FieldMobSpawner.cs:157-163`, mob respawn at `unity/Assets/Scripts/MobRespawn.cs:53-63`.

### Two gaps to close before any timer can rely on it

1. **A map server never syncs it.** `Sync()` is called only from a PLAYER signing in (`unity/Assets/Scripts/AccountSession.cs:78-89`). A dedicated server never signs in, so its clock falls back to its own machine's UTC. With several server machines their clocks must agree, or a timestamp written by one is wrong on another.
2. **It falls back to the local clock silently-ish when it cannot reach the server** (`unity/Assets/Scripts/WorldClock.cs:41-46` warns but carries on). Acceptable for a sky; NOT acceptable for a durable timestamp, because a timer written while unsynced records the wrong moment permanently.

### The scope line — do NOT convert everything

Only timers that must survive a save, a sleeping map, or a process hop move onto the shared clock. Per-frame timing that lives and dies inside one action — a wind-up, an animation blend, a cast bar — stays on the fast local counter. Converting frame-level timing into absolute-time subtraction would be a performance mistake the project's performance rules forbid.

## BUILT (2026-09-04) — the world clock and the timers anchored to it

The user said go, adding "make day and night follow server timer".

**Done, compiled clean, perf scan clean, editor recompiled with no errors:**

- The world's clock is now the clock of the machine running the world. A server reads its own machine's clock directly, with no sync step, no offset and no fallback branch. A player's machine is TOLD that time in a new message sent with its body on joining, and counts forward from it. Nothing asks the account service for the time any more — that was a different machine from the one running the world, so the sun and every lasting timer were being read off two different clocks.
- **Day and night now follows the server's clock, and holds still until it has one.** It already read the shared clock, so pointing that clock at the server was the whole change — plus a refusal to advance on the local clock before the server has spoken, because a stand-in is exactly the substitution being removed.
- **Cooldowns are anchored to an absolute moment.** The moment is the owner and the thing that survives being saved, slept or moved; the existing per-frame float beside it is its mirror, kept because the gate and the hotbar ring ask several times a frame and a date is too expensive there. A method exists to resume a cooldown from a moment handed over by a save or another server.
- **Both respawn timers are anchored to absolute moments AND recover on being switched back on.** This fixes the breakage the council found: a coroutine stops dead when its object is disabled and never restarts, so a sleeping map would have woken permanently short of creatures, silently, losing more population every time. Anything whose moment passed while the map was off now comes back at once; the rest resume with only the time genuinely left. The pooled revival path clears any recorded return so a creature that is already back cannot be brought back twice.

**NOT done, and deliberately left:**
- The cooldown moment is not yet written into the save document or read back. The plumbing is there and nothing calls it. That needs the save version and the two-sided character handshake string bumped together, which is a separate change.
- The player's machine still runs its own cooldown countdown rather than being sent the server's numbers. On an ordinary session the two agree; they only diverge across a handoff, which does not exist yet.
