# Council — the networking layer / game server

**Opened 2026-08-27. Status: Round 1 running.**

## The target

The multiplayer layer that was just built, plus today's removal of the offline game. Find bugs, waste, stale leftovers, and anything that will make the game feel slow or broken for a player. Nothing is edited until all four brains agree and the user says go.

The user's bar, in their words: no bugs · optimized networking · best user experience · no lag · smooth network · no broken · no stale codes and features · clean · best practice.

## What is already known going in

- The layer is server-authoritative on headless Unity, using Mirror 96.11.2 vendored in the project.
- A networked thing is TWO objects: a small "shell" that crosses the wire, and a heavy local "body" built from the shell's id on each machine.
- As of today there is NO offline game. Both damage channels refuse to act off the server, only the server spawns creatures, and the Editor is not exempt — pressing Play needs a live server.
- Everything after the commit before last is **compile-verified only**. It builds and loads. Nobody has watched it behave. Two-player movement is the only part actually observed working.
- Known and deliberate, NOT bugs to re-report: monsters cannot currently hurt players; a creature on a server never swings because its brain latches onto one on-screen avatar at birth; there is no per-connection fighter yet; movement is relayed rather than validated; progress is still written by the player's machine; interest management is not turned on yet.

## The roster

| Brain | Angle | Who |
| --- | --- | --- |
| A | **Did offline really die?** Surviving fallbacks, dead single-player code, stranded features, editor bypasses. | The coordinator (main session) |
| B | **What crosses the wire, and how often.** Send rates, redundant data, per-frame cost per networked thing, the everyone-hears-everything problem. | Spawned |
| C | **Every state a thing passes through.** Join, spawn, hit, die, respawn, out of range, disconnect, reconnect, teardown. | Spawned |
| D | **Who is allowed to decide, and the hit round-trip.** What the request carries, what is re-checked, who is credited, what is refused, the flood guard, double mitigation. | Spawned |

Four angles were chosen, so four brains — one more than the default three.

## Round 1

### Brain A — "Did offline really die?" (the coordinator)

**Verified good first:** the main seam holds. A blow on a player's machine that finds no server-known target
falls to an explicit refusal rather than landing locally (`CombatAuthority.cs:32-36`), the over-time channel
refuses the same way (`CombatAuthority.cs:129`), the map spawner switches itself off anywhere but a server
(`FieldMobSpawner.cs:69`), and a player can no longer reach a world without joining one (`GameRoot.cs:48-61`).
The old "am I connected right now" gate that used to guard the spawner is gone — the name it used has zero
references left anywhere in the scripts. I checked; the removal was done properly at those four places.

**A1 — HIGH. Two chat commands still make private monsters.**
`/spawn` clones the nearest creature with a plain `Object.Instantiate` (`GmCommands.cs:327`) and `/summon`
builds fresh ones through `MobSpawner.Spawn` (`GmCommands.cs:383`). Neither asks `GameServer.IsServer`, and
neither registers what it made with the net layer — compare the map spawner, which does both
(`FieldMobSpawner.cs:69` for the gate, `FieldMobSpawner.cs:116` for the registration: *"Born on the server →
every connected player gets to see it."*). Typed on a player's machine these produce creatures the server has
never heard of, standing in a world nobody else shares. The notes call this exact shape *"the thing that makes
shared combat impossible"* and record that a version of it already cost a session. Today's change closed it for
the map spawner and left these two open.
*What a player sees:* monsters only they can see, that no one else can hit, that the server will never resolve
a blow against.

**A2 — HIGH. The GM kill command goes around the one damage door, and now silently fails.**
`GmCommands.cs:301` and `GmCommands.cs:310` call `Health.TakeTrueDamage` directly. The seam that carries the
server gate is `CombatAuthority.ApplyTrue` (`CombatAuthority.cs:121-131`), and these two skip it. Traced
through: on a watched copy the subtract is a no-op (the `IsWatchedCopy` guard added today), but the damage
number is popped AFTER the subtract, so the command pops a `2147483647` over the creature, takes nothing off
it, and the creature lives. No warning is printed — the refusal message on the real door never runs, because
the door was never opened.
*What a player sees:* a giant number over a monster that shrugs it off, with nothing in the log.

**A3 — MEDIUM. The combat seam's own description still says the game is single-player.**
`CombatAuthority.cs:9-13`: *"**Today** it applies locally and synchronously — zero behavior change, still
singleplayer. **Once server-authoritative** this is the seam the network layer swaps…"* That is the first thing
anyone reads on the most authority-sensitive file in the layer, and the code 15 lines below it does the exact
opposite. Not a runtime bug; a trap for the next person or the next session.

**A4 — LOW. "Offline" is still written as a live state in comments the layer carries.**
`GameNetwork.cs:232` ("Returns FALSE offline"), `GameNetwork.cs:269` ("is offline"), `GameNetwork.cs:278`
("// offline — unchanged game"), `FieldMobSpawner.cs:116` ("No-op offline."). Each names a branch as the
offline case when the rule now says no such state exists. Historical references elsewhere (e.g.
`CombatAuthority.cs:59`, "the ones the offline game has always produced") read as history and are fine — these
four read as current behaviour.

**A5 — LOW. A seam kept open for a future that has been ruled out.**
`IProfileStore.cs:105-109`: `ProfileStore.Current` exists, in its own words, so *"a test or a future offline
mode can stand in without every caller knowing"*. There is exactly one implementation
(`IProfileStore.cs:30`). Harmless, but its stated reason is now void.

**A6 — LOW, and it is an opinion, not a bug.** The very first connection attempt shows the word
"Reconnecting…" (`ReconnectNotice.cs:19`, shown from `GameRoot.cs:60`). The overlay's own documentation says
this reuse is deliberate and covers start-up, so the author already decided it. Wording judgement, the user's
call, no change proposed.

**OUTSIDE MY ANGLE** — handed to whoever owns it, not investigated by me:
- `GameNetwork.cs:277-278` gates on `NetworkServer.active` and `NetworkClient.isConnected` — wire state, which
  the notes explicitly ban gating on. → Brain D.
- `GameRoot.cs:52-61`: `GameNetwork.Begin()` sits inside a handler that can fire more than once before
  `worldUp` is set for a player. The notes say one caller only. → Brain C.

**UNVERIFIED by me:** I did NOT run the game, so every symptom above is traced by reading, not observed. I did
NOT check whether any GM command other than kill/spawn/summon bypasses a networked rule — I stopped after the
damage sinks and the mob spawners came back clean apart from those.

**CONFIDENCE:** A1 and A2 — 90% that the code paths are as described; 0% observed live. A3–A5 are plain
readings of text and are certain.

### Brain B — "What crosses the wire, and how often"

**Confidence: 72%.** High on the facts (every citation is a line opened this run); the 28% is about SIZE — no
profiler, no packet counter, no run under packet loss. Brain B's own closing line: *"two-player movement
working proves almost nothing about any of this. Every cost here is invisible at two players by
construction."*

**B/F1 — Every player is told about every entity in the world.** Interest management is genuinely off:
`grep -rn "aoi\b|InterestManagement"` over the scripts returns **zero hits**, so `NetworkServer.aoi` stays
null, and with it null Mirror hands every entity to every connection
(`Assets/Mirror/Core/NetworkServer.cs:2025` → `RebuildObserversDefault` → `:1979`
`AddAllReadyServerConnectionsToObservers`). Both prefabs sit on the visibility that takes that branch
(`NetMob.prefab` and `NetPlayer.prefab`, `visibility: 0`). Cadence is 10 Hz on both sides (`NetMob.cs:60`,
`NetPlayer.cs:51`); payload ≈24 bytes per moving entity per update, itemised from the actual writers
(`NetworkWriterExtensions.cs:153` — `WriteVector3` is 12 raw bytes — plus 4 for yaw, 2 for the message id,
4 for the netId, two varint dirty masks).
**Scale — Brain B labels this as ARITHMETIC on measured values, NOT an observation**, and worst-case
(every entity moving): 10 players + 200 mobs → ~500 KB/s server upload; 50 players + 200 mobs → **~3.1 MB/s
(~25 Mbit/s) server upload**. Client-side receive stays survivable (~50–62 KB/s). The server's upload is the
wall, and it is the only cost here that is multiplicative rather than flat. A second cost on the same axis:
the broadcast loop at `NetworkServer.cs:2122` walks every observed entity for every connection 60× a second —
12,500 iterations per tick at 50×250 — **per-iteration cost NOT measured**.

**B/F2 — Position and facing cross on the RELIABLE channel.** Both prefabs carry `syncMethod: 0`, and Mirror's
enum (`NetworkBehaviour.cs:12`) makes 0 = `Reliable`, described one line above as *"oldschool reliable sync"*;
`Hybrid` at `:11` is *"quake style unreliable sync ('hybrid' to make it scale)"* — the mode built for this
data. Reliable means ordered, so one dropped position packet stalls every later one for that connection:
a visible freeze-then-snap on every body you are watching, not one stale frame. **Impact NOT measurable
without running under real loss** — free on a clean LAN, which is consistent with two-player movement looking
fine. Brain B's honest caveat on the fix: `syncMethod` is per-component, so flipping it also moves health and
identity strings onto the unreliable path — *"that is my read of the trade, not a measured consequence"*.

**B/F3 — The server runs the whole presentation layer for every hit, on a machine with no screen.** Chain
opened link by link: `GameNetwork.cs:377` → `CombatAuthority.cs:26` (returns false on a server via
`GameNetwork.cs:277`) → `Land` at `:38` → `Health.cs:283` `SpawnElementNumber` → `Health.cs:397`
`DamagePopup.Spawn` → `DamagePopup.cs:102`, which has **no headless or server guard anywhere in the file**.
Per hit that is a string allocation (`DamagePopup.cs:76`), a coroutine (`Health.cs:291`), particle cues
(`Health.cs:289`) and positional audio (`Health.cs:288`), plus a canvas rebuild that `PERFORMANCE.md:55` names
explicitly. A grep for any headless/server guard across the popup, VFX, SFX, spawner and nameplate systems
returned **nothing — not one of them knows it might be on a server**. **Byte and millisecond cost NOT
measured**; whether audio actually plays headless NOT verified.

**B/F4 — On a server the freeze-far switch never engages, and every mob searches the whole scene twice a
second, forever.** Every mob gets the switch (`MonsterAI3D.cs:215`, `:238`); it hunts for a player when it has
none (`SimulationLod.cs:70-73`, `FindAnyObjectByType<Player3DController>()`); on a server there never is one,
so it declares itself still simulating (`SimulationLod.cs:75`, with `Simulating` initialised true at `:25`)
and retries every ~0.5 s (`SimulationLod.cs:50`). At 200 mobs that is **~400 whole-scene type searches per
second on the server, permanently**, and it also means no creature ever quiets down. It obeys the letter of
`PERFORMANCE.md:50` (the retry IS throttled) but the retry was written for a case that resolves in seconds and
here never resolves at all. **Per-search cost NOT measured.**

**B/F5 — A watched creature's body is moved every frame regardless of distance, and every watched body pays a
full angle computation each frame even after arriving.** No distance gate on the follow (`NetMob.cs:80-84`,
`:135`); the client's freeze-far switch cannot reach it because it only disables the mover and the sprite
animator (`MonsterAI3D.cs:240`). The "has it arrived?" test itself is `Quaternion.Angle` on every watched body
every frame (`WatchedBody.cs:54`) — ~15,000 times a second at 250 entities — while the file's own comment at
`:13-14` claims an arrived body is left alone. Brain B explicitly refuses to price it: *"I did not read
Unity's implementation of `Quaternion.Angle`, so I will not claim what it costs internally"* — the standable
point is that the same question is answerable with a dot product against a constant.

**B/F6 — Message fields carrying no information.** `HitResult.attackerHit` (`GameNetwork.cs:187`) is filled
from `Character.cs:526` `BasicHit => Mathf.Max(0, Hit)` — **never negative** — and the only other value it
could hold is refused before a message is built (`GameNetwork.cs:250`), so the receiver's branch
(`Health.cs:287`) is always true: 4 bytes to every observer per swing carrying a constant. `element` is
int-backed for 10 members (`Element.cs:14-26`) — 4 bytes where 1 would do. Position and yaw go raw and
uncompressed. Together a **single-digit percentage** of F1 — worth doing after F1, not before. Brain B flags
`attackerHit` for a second reason: it is a field whose meaning is currently unreachable, which is the kind of
thing that quietly becomes wrong when skills get their own path.

**B/F7 — A person's position has a dead-zone; a creature's does not.** The player's (`NetPlayer.cs:55-56`,
applied `:101-103`) has no equivalent on the creature push (`NetMob.cs:103-104`, written raw every 100 ms).
What saves it today is Mirror only marking a value dirty on exact float inequality
(`NetworkBehaviour.cs:569`, `:1208`). **Brain B could NOT size this and did not measure it:** if server-side
creatures sit bit-stable when idle it costs zero; if they drift sub-millimetre from crowd separation, a
controller settle or a ground snap, every idle creature pays a full packet 10× a second to every player —
roughly doubling F1's bill in a quiet field. Needs one run with a counter on the push.

**Checked and CLEAN, stated plainly:** send cadence and the unconditional first update (`NetPlayer.cs:63`,
`:101`; `NetMob.cs:77`); the request message carries exactly one field (`GameNetwork.cs:161`) — the spec's own
rule honoured exactly; per-hit lookups are a dictionary with no fallback and the watched target is cached at
build time (`NetMob.cs:25`, `:31-36`, `:169-170`); no `GetComponent`/`Find`/`Camera.main` per frame in any of
the four networking files; the announcement packs once into a pooled writer and copies per observer
(`NetworkServer.cs:755-758`) — no garbage per recipient; transport configuration is Mirror's shipped defaults
and all sane (`KcpTransport.cs:26-46`), **no finding**.

**Brain B's proposed next step:** turn interest management on (F1) — the only finding whose cost is
multiplicative rather than additive, it is configuration rather than new code, and it makes F6's byte-shaving
worth doing instead of pointless.

**Brain B's OUTSIDE MY ANGLE hand-offs:**
- → D: `Player3DAttacker.cs:234` builds `new DamageInfo(damage, MeleeElement)` and `IDamageable.cs:31` defaults
  `attackerHit = -1`, so that path is classified as a skill strike and refused by `GameNetwork.cs:250`.
- → D: on a client `CombatAuthority.Apply` always returns 0, so `BasicSwing.cs:60` `if (dealt > 0)` never fires
  locally, and with it the on-basic-attack passive announcement at `:66`.
- → C: `Agent3DVisual.cs:123` asks `agent.Moving` whenever the mover reference is non-null, but `NetMob.cs:193`
  sets `agent.enabled = false` without nulling the reference — a watched live-3D creature may glide while
  reporting it is standing still.
- → C: `NetworkServer.cs:1974` `if (initialize)` means the all-connections observer list is built once at
  spawn; how a player arriving LATER comes to observe an already-spawned creature is a lifecycle question.

### Brain C — "Every state a thing passes through"

**Confidence: 70%.** Its first run was cut off by a usage limit; it was resumed with its context intact and
told to fill its most important gaps and write up rather than re-sweep. It says so itself: coverage of domain
reload and of event-subscription symmetry outside one file is *"genuinely thin, and that is where I would
expect a fourth real bug to be sitting."*

**C/F1 — CRITICAL, and the largest player-facing finding in the whole council. Walking through a portal
permanently blanks every other player and every creature.** A remote player's body (`NetPlayer.cs:151`) and a
watched creature's body (`MobSpawner.cs:37`, called from `NetMob.cs:164`) are created with no parent and no
`DontDestroyOnLoad`, so both land in the **map scene**. A portal unloads that scene
(`Portal3D.cs:44` → `MapManager.cs:41`), killing the bodies — and both builders are one-shot latches
(`NetPlayer.cs:135` `if (bodyRequested) return;`, `NetMob.cs:154` `if (viewRequested …) return;`), so neither
ever rebuilds even though the shells are alive and still receiving updates.
*What a player sees:* you step through a portal in town and the far side is empty. Other players invisible,
creatures invisible, unclickable and unhittable — **permanently, until you quit and reconnect**. Nothing is
logged. Reachable on the very first map: `town_0.unity` contains two portals.
*Secondary, same cause:* the body→shell lookup (`NetMob.cs:174`) is only ever cleaned in `OnStopClient`
(`NetMob.cs:203`), so every scene-unloaded view leaves a dead key in that static table for the life of the
process.

**C/F2 — HIGH. The freeze-far switch turns a watched creature's mover back ON, undoing the net layer's
silencing.** `NetMob` silences a watched creature by disabling its mover (`NetMob.cs:193`, called from the
spawn callback at `:166` — i.e. AFTER `Awake`). But the freeze switch already captured that same component in
its list at `Awake` time (`MonsterAI3D.cs:202`, `:215`, `:243`) and re-asserts `enabled = on` unconditionally
on every near/far flip (`SimulationLod.cs:94-95`). The re-enabled mover then applies a crowd shove plus
gravity every frame (`PathAgent3D.cs:43`, `:228-231`), fighting the net layer's follow-blend
(`WatchedBody.cs:49`).
*What a player sees:* creatures that jitter and shove each other apart, drifting from where the server has
them. **Two players standing side by side see the same creature in two different places.** Fires the first
time you walk past a creature's range and back.
*Brain C's own framing:* this is the exact shape the spec warns about at
`docs/2-systems/networking-implementation-notes.md:66` — *one component over*. The brain follows the rule; the **mover**
does not, and the mover has an owner that turns it back on.

**C/F3 — HIGH. The training dummy in town is unhittable on every player's machine.** A damageable unit baked
into a map scene (`town_0.unity:479`, carrying the AI script at `:573`) is never published to the net layer —
the publish call has exactly two callers, both inside the field spawner (`FieldMobSpawner.cs:102`, `:117`). So
no shell speaks for it, the lookup returns null (`GameNetwork.cs:282-283`), and every blow is dropped
(`CombatAuthority.cs:32-36`).
*What a player sees:* you swing at the training dummy and nothing happens at all. One warning line, once.

**C/F4 — HIGH for user experience. The reconnect overlay swallows every mouse click for up to ~30 seconds.**
The dim is a full-screen raycast target on the topmost canvas (`ReconnectNotice.cs:43`, `:52`, `:54-57`;
`UiKit.cs:150` sets `raycastTarget = true`), so the game's single "is the pointer over UI" gate
(`UiBlock.cs:37`) reports true for the whole retry window — and both movement and targeting consult it
(`Player3DController.cs:990`, `UnitTargeter.cs:204`). The same overlay is shown on the **very first dial**
(`GameRoot.cs:60`).
*What a player sees:* the world is visible behind the dim exactly as designed, but clicking to walk or to
target does nothing for ~30 s. Reads as a hang, not a reconnect.
*Brain C did NOT verify* whether keyboard movement is also gated; it traced only the two mouse gates. The
~30 s is the spec's own figure, not measured.

**C/F5 — LOW. Give-up teardown forgets five static caches.** `Forget()` (`GameNetwork.cs:493-500`) clears two
and leaves `warnedSkillsUnsupported` (`GameNetwork.cs:256`), `warnedNoFighter` (`:288`), `swingLedgers`
(`:310`), `warnedAbout` (`CombatAuthority.cs:44`) and `byView` (`NetMob.cs:25`).
*Counter-note in the layer's favour, volunteered by Brain C:* the PER-CONNECTION halves ARE cleaned on server
disconnect (`GameNetwork.cs:429-430`), so the spec's claim that nothing grows per connection **holds**.

**C/F6 — LOW, latent and not currently reachable.** Whether a creature wears a rigged body is decided on the
spawning machine only (`FieldMobSpawner.cs:122`) and never crosses the wire, so the watching side would build
a plain sprite. Not reachable today — the fraction that triggers it defaults to 0 (`FieldMobSpawner.cs:52`)
and no scene serializes it.

**Brain B's lead 1 — CONFIRMED, same root as C/F2.** `Agent3DVisual.cs:122-124` reads `agent.Moving`, which is
written only at the end of the mover's `Update` (`PathAgent3D.cs:234`); a disabled component's `Update` never
runs, so it freezes at its last value. **Cannot fire today** for C/F6's reason, but it is the same
reference-not-nulled defect that makes C/F2 fire — and C/F2 IS reachable.

**Brain B's lead 2 — CHECKED, NOT a bug.** A late joiner does get already-spawned creatures:
`NetworkServer.cs:1344` calls `SpawnObserversForConnection` from inside `AddPlayerForConnection`, which this
project calls on every arrival (`GameNetwork.cs:405`). *The fragile edge Brain C flags:* it refuses outright
when the connection is not ready (`NetworkServer.cs:1432-1439`) **with no retry** — a connection that became
ready after its body was added would see an empty world forever. Brain C did NOT read Mirror's default
`OnClientConnect` body, so it cannot say the ordering is guaranteed, only that the code depends on it.

**Brain D's lead 3 — CONTRADICTED, and Brain C is right.** D handed over `MonsterAI3D.cs:465` as an unguarded
dereference. C found the guard three lines above. **The coordinator verified this independently:**
`MonsterAI3D.cs:458` reads `if (player != null && touch3DSqr <= touch * touch && Time.time >= nextTouch &&
Character.Instance != null)`, and `:437` requires it again. On a server the block is skipped, not
dereferenced. **Not a null-safety hole.** D labelled it as an outside-my-angle lead rather than a finding, so
nothing false was claimed — this is the council doing its job.

**Spec traps Brain C re-checked that DO hold:** the map-loading-mid-handshake race is closed
(`FieldMobSpawner.cs:69` gates on the job, and `GameRoot.cs:64-68` loads the map only after joining); the
unconditional first update holds for both kinds (`NetPlayer.cs:63`, `:101`; `NetMob.cs:77` before the spawn at
`GameNetwork.cs:78`); a killed-not-destroyed mob's shell does let go (`NetMob.cs:94`, with the pooled return at
`FieldMobSpawner.cs:147`, and a fresh shell on reuse at `:102`); a watched copy does not announce a kill
(`Health.cs:490`). Subscription symmetry verified on `GameRoot` only (`:31-33` vs `:40-42`) — symmetric.

**State coverage — Brain C's own honest table.** Checked: join/connect · late join · spawn · live/steady ·
damage and death · disconnect (clean and unclean) · reconnect · give-up/teardown · scene reload. Partially
checked: respawn/reuse (server side only — the watching side destroys rather than pools, so there is no reset
path there to get wrong) · event subscribe/unsubscribe symmetry (only `GameRoot`; **NOT** checked:
`CombatAuthority.OnHPAdded`, `SimulationLod.OnSimulatingChanged`, and whether the crowd register at
`MonsterAI3D.cs:207` has a matching unregister). **NOT checked at all: Unity domain reload / enter-play-mode
statics** — if domain reload is off, the overlay's cached root (`ReconnectNotice.cs:20`) and the body→shell
table (`NetMob.cs:25`) would survive a Play restart holding dead references. *(Note: Brain D separately DID
read this — `ProjectSettings/EditorSettings.asset:29-30` shows domain reload IS on, so the statics do reset
between plays. The two brains cover each other here.)*

**Brain C's proposed next step:** re-assert the net layer's silencing after the freeze switch acts, the way
the revive path already does (`MobRespawn.cs:97` — `lod.Resync()`). One change kills C/F2 and pre-empts B's
lead 1. It explicitly declines to propose a fix for C/F1: *"needs a design answer first (should a map change
on a client mean re-requesting bodies, or should map identity be part of what the server owns?), and that is
not mine to decide."*

---

## The gate — Round 1 verdict

**All four brains are in. There is no unresolved disagreement.**

- The one direct contradiction (D's lead 3 vs C) was **resolved by measurement** in C's favour, and the
  coordinator verified it independently at `MonsterAI3D.cs:458`. Nothing false was ever claimed as a finding.
- Three findings were reached **independently by two brains each**: the GM spawn commands (A1 = D's hand-off),
  the GM kill commands (A2 = D/F13), and the wire-state gates (A's hand-off = D/F10). Independent convergence.
- The brains proposed three DIFFERENT next steps (B: interest management · C: re-assert silencing · D: server
  accuracy roll). That is not disagreement — each named the top item in its own lane. Merged ranking is below.
- **Nobody agreed on anything without evidence.** Every brain returned `file:line` citations and an explicit
  list of what it did not verify. No finding rests on reasoning alone.

**The common caveat, stated by all four independently:** nothing here was observed running. Everything after
the second-to-last commit is compile-verified only, and two-player movement — the one thing actually watched —
is, in Brain B's words, *"invisible at two players by construction"* for every cost it found.

**Nothing has been edited. Awaiting the user's go.**

---

## Round 2 — CONVERGE

Three brains, all aimed at the SAME eleven claims: the coordinator plus two **fresh** verifiers (deliberately not
the authors — a brain defends its own finding, which is not verification). Both were told to assume every claim
was wrong, to treat every citation as unproven, and to hunt for the guard/override/dead-caller that kills it.
One worked the list forwards, one backwards.

Both returned **88%** confidence. Neither observed anything running.

### The headline: ONE CLAIM COLLAPSED, and it collapsed on a bad citation

**C10 — the reconnect overlay swallows clicks — is FALSE.** Both verifiers, independently:

- The overlay's canvas is built with only a `Canvas` and a `CanvasScaler` (`ReconnectNotice.cs:35`) and **never
  gets a `GraphicRaycaster`**. Without one, uGUI does not raycast that canvas at all, so
  `EventSystem.IsPointerOverGameObject()` (`UiBlock.cs:37`) can never return true over the veil. It blocks
  nothing.
- This codebase states the pattern explicitly elsewhere: `PerfHud.cs:117` — *"Non-interactive (no
  GraphicRaycaster) so it can't steal clicks."*
- **The citation that carried the claim was pointing at the wrong function.** `UiKit.cs:150`
  (`img.raycastTarget = true;`) is real — but it lives inside `UiKit.ClickShield` (`UiKit.cs:144-152`), which
  `ReconnectNotice` never calls. The veil is built by `UiKit.Img` (`:128-135`), which does not touch
  `raycastTarget` at all.
- Both also answered the question Brain C left open: **keyboard movement was never gated anyway.** Walking
  reads through `Hotkeys.Held` (`Player3DController.cs:502-512` / `:1000-1003`) with no pointer gate; the only
  two gated sites are `:474` (click-to-move) and `:904`.

**This is exactly what the round existed to catch** — a real line number attached to a claim it does not
support, which reads as verified and is not.

### The second headline: four claims are REAL CODE but UNREACHABLE today

Both verifiers independently found the same thing, and neither of the four original brains mentioned it:

`NetPlayer.BindCombatUnitOnServer` (`NetPlayer.cs:47`) has **zero callers anywhere in `unity/Assets/`**. The
coordinator verified this a third time: grepping the whole Assets tree returns exactly one hit — the method's
own definition — and `ServerCombatUnit` has only three references total (`NetPlayer.cs:44`, `:47`, and the read
at `GameNetwork.cs:353`). **Nothing ever writes it.** So it is permanently null and
`GameNetwork.cs:353-362` refuses **every** networked swing before reach, damage or broadcast are ever computed.

This is the project's own documented blocker (per-connection fighters) — not a new bug. But its consequence
was not written down: **C2, C4, C6 and C7 are correctly read and currently inert.** A scripted client's "4× the
legal DPS" is 4× of zero. Evasion is not "inert", it is untested. Fixing them changes nothing observable until
fighters exist.

### Merged verdicts

| Claim | V1 (forwards) | V2 (backwards) | Merged |
| --- | --- | --- | --- |
| C1 portal blanks the world | PARTLY TRUE | CONFIRMED | **CONFIRMED, and BIGGER** |
| C2 server never rolls accuracy | CONFIRMED, inert | CONFIRMED, inert | **Real, unreachable today** |
| C3 mover re-woken by freeze switch | CONFIRMED, stronger | PARTLY TRUE, conditional | **CONFIRMED, fires after one radius round-trip** |
| C4 skill downgraded to plain swing | CONFIRMED, impact smaller | CONFIRMED, symptom differs | **Real, unreachable today** |
| C5 no authentication at all | CONFIRMED | CONFIRMED | **CONFIRMED, every part** |
| C6 reach 5.6× | PARTLY TRUE | CONFIRMED w/ caveats | **Real; 5.6× is melee-only** |
| C7 flood guard 4× | CONFIRMED, unreachable | CONFIRMED, unreachable | **Real, unreachable today** |
| C8 GM spawn / C8b GM kill | CONFIRMED both | CONFIRMED both | **CONFIRMED, incl. the popup ordering** |
| C9 training dummy unhittable | CONFIRMED | CONFIRMED | **CONFIRMED, and only that one exists** |
| C10 overlay eats clicks | **FALSE** | **FALSE** | **FALSE — claim withdrawn** |
| C11 interest management off | CONFIRMED | CONFIRMED | **CONFIRMED, incl. reliable channel** |

### What the verifiers ADDED that the finders missed

- **C1 is worse than reported.** Mirror's client spawn is also a bare `GameObject.Instantiate`
  (`NetworkClient.cs:1267`) with no parent and no don't-destroy, so the **shells** live in the map scene and die
  with it too — not just the bodies. Mirror does not re-spawn a client-destroyed identity
  (`NetworkIdentity.cs:655`), and this project drives scenes through `SceneManager` directly, never Mirror's
  scene messages. The latch is the belt; the shell destruction is the braces.
  *V1's honest caveat:* which scene an object lands in during the **initial connect burst** cannot be settled by
  reading — anything arriving before the first map finishes loading lands in the persistent scene and would
  survive every portal. That is a race needing a live run.
- **C3 applies to every mob.** **0 of 86** baked mob prefabs carry their own freeze component, so every watched
  mob gets the re-enabling one; and `separateWhenIdle: 1` is serialized in **all 86**, so the drift branch is
  live on all of them. *V2's counter-nuance:* it needs one genuine far→near round-trip to fire
  (`SimulationLod.cs:25`, `:80-84`), so it is not wrong from the first frame — but in normal play that happens
  constantly.
- **C6's 5.6× is the unarmed/sword number only.** Both sides read reach through the same weapon lookup
  (`WeaponReach.cs:22-23`); the gap is a **constant +1.5**, not a multiplier. `Sword.asset:20` is
  `attackRange: 0` (falls back to 0.5) ⇒ 5.6×; `Bow.asset:20` is `attackRange: 5` ⇒ **1.46×**.
- **C7's open flag is closed.** The prefab Brain D could not find: `Systems.unity:406` serializes
  `cooldown: 0.6`, matching the code default — so the 4× figure stands.
- **C4's damage loss costs nothing today.** On the one shipped asset the three discarded things are all
  neutral (`damageMult.value: 1`, `useStacks: 0`, empty affinity). The real live consequence is different: the
  on-basic-attack passive trigger never fires. And **only one** asset sets the flag — swept across 41 skill
  assets, 57 occurrences of the field, exactly one set to 1.
- **C9's sweep widened.** Of all 41 scene files, only `town_0.unity` contains a mob brain or a health
  component. The "only one" holds. Extra: on a server the dummy DOES take damage while every client's copy shows
  nothing — the two silently diverge.
- **C11 adds a separate dial.** Both prefabs also carry `syncInterval: 0`, which is not the same knob as the
  10 Hz write cadence — relevant to anyone reasoning about send frequency.

### Citations that did not support their claim

1. `UiKit.cs:150` (C10) — real line, **wrong function**. Load-bearing for a claim that is false.
2. `town_0.unity:479` (C9) — points at an icon field; the name is at `:477`. Right object, wrong line.
3. `FieldMobSpawner.cs:116` (C8) — a comment; the call is `:117`.
4. `Player3DAttacker.cs:168` (C7) — sets the cooldown; the gate it was cited as is the check at `:164`.
5. `Player3DAttacker.cs:16` (C6) — real, but it is the *unarmed fallback*, not "the client's reach".
6. Off-by-small, harmless: `MapManager.cs:41` (unload is `:42`), `NetMob.cs:166` (callback opens `:164`),
   `MonsterAI3D.cs:215` (a call, not a capture).

### The revised action list

**Worth fixing now — confirmed AND reachable:**
1. **C1** — the portal blanking. Needs a design answer first: does a map change mean re-requesting bodies, or
   does map identity become something the server owns?
2. **C3** — the mover being re-woken. One change; also pre-empts the stale-`Moving` defect behind it.
3. **C8 / C8b** — the three GM commands.
4. **C9** — publish scene-baked damageable units, or decide the dummy is server-only.
5. **C11** — turn interest management on; and separately decide whether position moves off the reliable channel.

**Real code, but fixing it changes nothing observable until per-connection fighters exist:** C2, C4, C6, C7.
Worth writing down so they are not re-discovered; not worth spending a round on now.

**Withdrawn:** C10.

**Nothing has been edited.**

### Brain D — "Who is allowed to decide, and the hit round-trip"

**Confidence: 80%.** Traced one ordinary swing end to end across 30 steps and reports *"nothing in this path was
inferred or unfindable"*. Nothing observed running.

**D/F1 — CRITICAL. The server never rolls accuracy, so every forwarded swing lands and FLEE does nothing.**
The server builds its result without a `missed:` argument (`GameNetwork.cs:375-376`) and the struct default is
`false` (`IDamageable.cs:31`); nothing downstream re-rolls, by design (`Health.cs:262`, `:266`). The path it
replaced DID roll it (`BasicSwing.cs:49`). Evasion is a stat the game charges for, derives from AGI, and
re-derives on every mob level-up (`UnitStatBlock.cs:170`) — and it is **inert on the only combat path the
server has**. A wrong number wearing the server's authority.

**D/F2 — CRITICAL. The skill refusal has a hole, and one shipped skill already falls through it.** The refusal
tests "did the attacker roll accuracy" (`GameNetwork.cs:250`). But a skill authored to resolve as a basic
attack routes through `BasicSwing.Land` (`SkillEffectApplier.cs:612`, `:616`), which passes the attacker's real
`BasicHit` — never negative (`Character.cs:526`). So the request looks exactly like an ordinary swing and IS
sent. The server then re-rolls it as a plain basic attack with the damage multiplier, the stack pile and the
ring/combo scale **all discarded**. The authored asset that does this today:
`Resources/Skills/skill.sword.onslaught.asset:193` — `resolveAsBasicAttack: 1`, a published skill whose own
description says *"spends them as real basic attacks, two per swing"*. This is exactly the failure the spec
says was closed.

**D/F3 — HIGH. The flood guard is the ONLY cadence limit, and it sits at 4× the fastest legitimate build.**
Guard is 20/second (`GameNetwork.cs:302`); the fastest legal cadence is 0.6 s ÷ a 3× cap
(`Player3DAttacker.cs:22`, `UnitStatMath.cs:62`) = **5 swings/second**. The server never asks whether that
connection's fighter was allowed to swing yet — the client alone owns the timer (`Player3DAttacker.cs:168`,
which also bypasses the shared cadence rule and so carries no floor at all). A scripted client looping the
request deals **4× the DPS of the fastest legal build without forging a single field** — the server rolls the
damage for it. Brain D notes the shared rule's own floor (`UnitOffense.cs:62`) is 20/s exactly, so the guard is
not "far above" it — it is equal to it. **Caveat stated:** no prefab serializes that cooldown, so 0.6 is the
code default and could be overridden somewhere Brain D could not locate.

**D/F4 — HIGH. Reach forgiveness is added OUTSIDE the size blend, so the server allows ~5.6× the client's own
reach.** `GameNetwork.cs:331-333` computes `reach = fullSize * blend + 1.5` — the `+` outside the multiply.
Unarmed, the client will not land past **0.325** world units (`Player3DAttacker.cs:16`, `UnitScale.cs:42`,
`:23`); the server accepts anything within **1.825**. With a 3-unit weapon it is still 1.77×. Secondary: the
two sides measure different things — server uses 3D distance (`GameNetwork.cs:334`), client uses planar XZ
(`Player3DAttacker.cs:119`).

**D/F5 — HIGH, and it depends on a threat model nobody has stated. There is no authentication of any kind.**
The join message carries a display name and a model name (`GameNetwork.cs:145-149`); the session holds an
access token and a character id (`AccountSession.cs:25`, `:28`) and **neither is ever sent**. No Mirror
authenticator is assigned anywhere — grep returns nothing. The server instantiates and accepts whatever it is
told (`GameNetwork.cs:398-405`) with no lookup and no check. Anyone who can reach the port joins under any
name, including an exact copy of another player's, and the unvalidated model string is handed straight to an
asset loader **on every other player's machine** (`NetPlayer.cs:140` → `CharacterModels.cs:43`). Brain D did
NOT trace what that loader does with a hostile string.

**D/F6 — The server re-checks four things and skips at least four.** Present: rate, target exists, target is
damageable, target in reach (`GameNetwork.cs:338-366`, read line by line). Absent: is the attacker **alive**
(never read — a dead player keeps swinging); is the attacker facing or did they even select the target; is the
attacker allowed to **see** that target; is the target already dead (the sink swallows it at `Health.cs:260`
but the broadcast still goes out with the full rolled number).
**Verified rather than assumed:** a client cannot aim at a player — `NetPlayer` does not implement the
damageable interface (`NetPlayer.cs:24`), so that lookup returns null and the request is dropped.

**D/F7 — A landed networked swing teaches its attacker nothing on-basic, and burns its charges for a number
the server discards.** The door returns 0 for a forwarded hit by design (`CombatAuthority.cs:26`), but the
swing uses that return as "did it land" (`BasicSwing.cs:57`, `:59`), so `OnBasicAttackDealt` (`:64`) **never
fires anywhere** — Momentum, Perfect Form and every other on-basic passive are dead in networked play.
Meanwhile the client already spent its charge pile one line earlier (`BasicSwing.cs:53`) and the server never
spends its own. Note the two sibling triggers now disagree: `OnDamageDealt` DOES still fire on the attacker's
machine via the announcement (`Health.cs:415`).

**D/F8 — The watching copy re-mitigates with numbers that measurably do NOT match the server's, and the number
on screen is never corrected.** The spec says "defence and element are data and agree; parry is a state."
Brain D measured that this is **not true of defence**: the shell syncs only id, position, yaw and health
(`NetMob.cs:39-44`) — **level is not synced** — while a mob levels up in combat (`MonsterAI3D.cs:281`, `:292`)
and that rewrites its defence (`UnitStatBlock.cs:168-169`), as does any sub-stat change
(`UnitStatBlock.cs:83`). And it is not just parry the copy lacks but **five** state-driven mitigations —
vulnerability amplify, guard cut, shield absorb, brace deferral (`Health.cs:170`) plus parry (`Health.cs:176`)
— against a state store nothing populates (`Health.cs:140`). **The bar corrects; the number does not**:
`SetHealthFromServer` (`Health.cs:203`) writes health, but the popup was already spawned and is never
retracted. The spec's "corrects it within a tenth of a second" is true of the bar only.

**D/F9 — Three of the server's four refusals are completely silent, on both machines.** Only the missing-fighter
case speaks (`GameNetwork.cs:361`). Flood, unknown target, non-damageable target and out-of-reach all `return`
with nothing logged and nothing sent back (`GameNetwork.cs:342`, `:344`, `:350`, `:366`). The player sees the
swing animation play, no number appear, and no line anywhere saying why — the exact failure mode
`CombatAuthority.cs:42-43` says its warning exists to prevent.
**Documentation/behaviour mismatch:** `GameNetwork.cs:245` says *"Told once per skill"* but the flag is one
process-wide bool (`GameNetwork.cs:256`), so the first skill of the session silences every other skill forever.
Same shape at `CombatAuthority.cs:44`, keyed by class name, of which there are two. Brain D checked whether
these survive a Play session — `ProjectSettings/EditorSettings.asset:29-30` shows domain reload still happens,
so they do reset between plays.

**D/F10 — The last wire-state gates the spec bans are still in the code** (`GameNetwork.cs:277`, `:278`, `:66`).
Brain D's verdict, in its own words: *"honestly, the right answer by accident"* — during a reconnect the blow
falls through to the correct refusal, and at boot the ordering happens to be safe (`GameRoot.cs:56` before
`:59`). Reported because the spec names this exact shape and these are the last three instances.
**This confirms Brain A's hand-off.**

**D/F11 — LATENT TRAP. The passive ownership gate is written as "if there is a local character", so on a server
it is skipped entirely.** `PassiveEvents.cs:128` — `if (Character.Instance != null && !…IsActive(def)) continue;`
The registry is process-global. The moment per-connection fighters exist and the server reaches
`Health.cs:415`, **every registered passive from every connection fires on every hit by anybody**, with no
ownership check, because the guard's own precondition is false. Brain D could find this written down nowhere.

**D/F12 — Health can go UP on a watched copy; only the subtraction is guarded.** The guard lives in one place
(`Health.cs:233`); three other writers of health bypass it — `Health.cs:476` (heal, reachable from a
heal-over-time tick at `StatusController.cs:414`), `Health.cs:454` (refill), `Health.cs:459` (revive). The
spec's rule is *"a copy writes no health"*, not "no reductions". Latent today; the asymmetry is the bug.

**D/F13 — A third path into damage that nobody gated: the GM kill commands.** `GmCommands.cs:301` and `:310`
call the sink directly, bypassing the raw-damage gate at `CombatAuthority.cs:129`. Pops an int-max number over
a mob and kills nothing. Chat runs on the client, so anyone with the command can reach it.
**This is independently the same finding as Brain A's A2 — two brains, separate hunts, same two lines.**
Brain D adds the census: grepping every damage-sink call across all game C#, *"the only callers outside
`Health.cs` and `CombatAuthority.cs` are those two GM lines"* — matching Brain A's identical grep.

**D/F14 — LATENT. The attacker's own machine re-runs the attacker-side lesson the server already ran.** The id
check is present and correct and is the only consumer of the announcement (`GameNetwork.cs:228`) — but for the
attacker themself both machines now run the same block (`CombatAuthority.cs:73`, `Health.cs:412` on the client;
the same two lines via `GameNetwork.cs:377` on the server). Once fighters exist, the consecutive-hit run, the
on-damage-dealt fire, the lifesteal payout and the mana-leech payout each happen **twice**. Written down
nowhere.

**Checked and CLEAN:** who gets credited — the id check exists, is correct, and is the sole consumer
(`GameNetwork.cs:200`). What the request carries — one field, nothing left the server could look up itself; the
element/magic removal genuinely holds (`GameNetwork.cs:375` reads both off the fighter). The three-way ask —
both live shells are now three-way and *"nothing else in the project reads `isOwned`, `isLocalPlayer` or
`hasAuthority`"* (`NetPlayer.cs:80-81`, `NetMob.cs:82-83`). Death suppression on a copy — present and correct
(`Health.cs:492`), and both overkill sinks route through the same call.

**Brain D's proposed next step:** move the accuracy roll onto the server — one added argument at
`GameNetwork.cs:375`. It closes F1 completely, restores a stat the game already charges for, and is *"the only
finding here that can be fixed without a design decision"*.

**Brain D's OUTSIDE MY ANGLE hand-offs:**
- → C: `MonsterAI3D.cs:465` reads `Character.Instance.Flee` unguarded; on a server that is null by design
  (`Character.cs:620`).
- → A/C: `GmCommands.cs:314-327` instantiates mobs locally on a client. **Confirms Brain A's A1.**
- → B: every arriving mob shell builds and destroys a whole body (`NetMob.cs:164`, `:202`).