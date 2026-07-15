# World liveliness / ambient life (layers 1+3 BUILT 2026-06-25)

Goal: make the solo 2.5D RO-style world feel alive even though the player plays alone.
Discussed 2026-06-24. **Layers 1 (wandering townsfolk) + 3 (reactive barks) BUILT & compile-clean 2026-06-25:**
- `AmbientLife.cs` — drop-in director: spawns ONCE at Start `count` townsfolk within `areaRadius` of itself,
  drawn from `CharacterRoster` (same baked chars as the player → zero new art), ground-snaps each via one
  Ground-layer raycast, attaches `PathAgent3D` + a `TownNPC` brain.
- `TownNPC.cs` — dumb cousin of `MonsterAI3D` (no aggro/combat): strolls to random points near `home`, pauses,
  and barks when player is within `barkRadius`. Rides the SAME `PathAgent3D` mover + `UnitCrowd` pass-through.
  Perf: simulate-near/freeze-far on a staggered ~0.4s LOD cadence (disables agent + `BakedBillboard8` when far).
- `NpcBarks.cs` — static `Town`/`Field` canned-line pools (LLM-FREE; no Claude API).
- `DamagePopup.SpawnBubble(pos,label,color,life,rise)` — new pooled-popup overload for barks; `Begin()` resets
  timing so a stretched bark can't leak its long lifetime into the next combat number (zero-alloc).
- `Editor/AmbientLifeTools.cs` — `Tools ▸ unwritten ▸ World ▸ Add Ambient Townsfolk` (idempotent; finds roster +
  Ground layer, centers on SpawnPoints). **Run it in a MAP scene (Town), NOT Systems.unity** (needs ground).
- GROUNDING (fixed 2026-06-25): baked chars float if you rely on CharacterController gravity (root rests at
  ground+skin, and they catch on prop/snow colliders). `TownNPC.LateUpdate` raycast-pins root Y to the **Ground
  layer only** (the [[world-ground-standard]] basis `BakedBillboard8.feetOffset` is tuned for) while moving →
  feet flush on the floor. Bark bubble placed off `BakedBillboard8.VisualWorldHeight` (~0.12u above head);
  note `DamagePopup.Begin` adds a fixed +0.5 lift you must subtract.
- ⚠️ PLAYTEST GOTCHA that cost ~3 rounds: an UNFOCUSED Unity editor doesn't tick Update → townsfolk spawned in
  Start but never moved/settled/barked (looked frozen-floating-silent). Fix = focus the Game view, or
  `Application.runInBackground = true` (PlayerSettings checkbox didn't take for the session; set the runtime
  prop). When driving Play via MCP to verify movement, set runInBackground first or nothing ticks.
Still TODO from the plan: layer 2 (critters), layer 4 (day/night schedules off `TimeOfDay`), RecipeGenerator
wiring (expose as a recipe group so every map gets it), layer 5 fake-MMO, layer 6 narrow LLM.

## Core stance
"Lively" = motion + believable purpose + reactivity, NOT intelligence. RO felt alive with
near-zero NPC AI. So **lead with ambient SIMULATION, not LLM "AI NPCs".** Reserve real
LLM-driven NPCs (Claude API) for a tiny handful of named/signpost characters much later, if ever
(LLM = depth for 1–2 characters, not breadth; latency/cost/offline issues, can't drive motion).

## Build order (cheapest impact first) — ride existing systems
Reuse: `PathAgent3D`, `MonsterAI3D.Active` registry, `SpriteSheetCache` billboards, `TimeOfDay`
clock, and the `MapRecipe`/RecipeGenerator (expose ambient life as just another prop/mob group so
every map gets it for free).
1. Wandering townsfolk — billboard NPCs pick nearby waypoints, stroll/pause/idle (`PathAgent3D` brain).
2. Ambient critters/mob life — chickens, birds, fish ripples; idle mobs graze/wander vs frozen-until-aggro.
3. Reactive "barks" — short canned lines from a string pool on a cooldown when player passes (greet/shop/gossip).
4. Day/night SCHEDULES — vendors at stalls by day, walk home at night, torches at dusk; driven off TimeOfDay clock (adds purpose).
5. Fake-MMO presence (optional, high impact for solo) — a few scripted "other players" running around, fighting, emoting.
6. LLM layer LAST & NARROW — 1–2 NPCs get real conversation via Claude API, cached + rate-limited; everyone else scripted.

Recommended first slice: layers 1+2+3 (wandering townsfolk + critters + barks). Then 4 (schedules).

## Perf guardrails (HARD constraint — PERFORMANCE.md)
Simulate-near / freeze-far (only NPCs near camera tick brains; distant ones snap to schedule state, LOD like mobs);
pool everything (no Instantiate churn), reuse SpriteSheetCache, 0 B/frame; drive schedules off the shared clock not per-NPC timers.

## Proposed shape (when we build): `AmbientLife` component + `TownNPC` brain on PathAgent3D,
wired into RecipeGenerator so a recipe can request "N townsfolk + barks + critters". Write a short
design doc first, then build the wandering-townsfolk+barks slice.
