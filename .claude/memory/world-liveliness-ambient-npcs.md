# World liveliness / ambient life (layers 1+3 BUILT 2026-06-25)

Goal: make the solo 2.5D RO-style world feel alive even though the player plays alone.
Discussed 2026-06-24. **Layers 1 (wandering townsfolk) + 3 (reactive barks) BUILT & compile-clean 2026-06-25:**
- WHO LIVES WHERE (since 2026-10-04): the code-driven list `Resources/WorldTownsfolk` (`WorldTownsfolk.cs`), the
  townsfolk twin of `WorldSpawns` — one row per NAMED person (name, description, civilian model, map, home in map
  coords, wander radius, level, barks). The description is the smaller line under the name on their always-on plate,
  meant later for the service an NPC offers (Smith, Tool Dealer); it crosses as `NetNpc.personDescription`.
  A row with Wander Radius 0 STANDS at home facing `facing` (shopkeepers). A row's `station` (CraftStation) or `shop`
  (id in `Resources/WorldShops`) makes F open that window (`ShopPanel` / `CraftStationPanel`); buy/sell are
  `NetPlayer.CmdBuy/CmdSell`, server-only totals. ONE sell rule (user, 2026-10-04): a shop buys anything with
  `Item.zeny > 0` (`Item.CanBeSold`), any item type incl. gear; 0 = unsellable, shown as "Cannot be sold to NPC". Lastline's four (2026-10-04): Gunnar Smith, Odette Weaver (loom),
  Rook Tanner, Fenwick Tool Dealer — at the doors of the 4 houses round the fountain, door sides measured off the
  house meshes (House_A door is on its local -X side; House_C and House_D on +Z). `MapManager.Activate` calls `WorldTownsfolk.Populate(map)` on the server; each person is
  built by `Townsperson.Build` and announced by `GameNetwork.PublishNpc`, their name crossing as `NetNpc.personName`.
  No spawner object lives in any scene any more (the old `AmbientLife` director was deleted). Lastline's 12 homes were
  picked off its minimap on road/paving pixels.
- `TownNPC.cs` — dumb cousin of `MonsterAI3D` (no aggro/combat): strolls to random points near `home`, pauses,
  and barks when player is within `barkRadius`. Rides the SAME `PathAgent3D` mover + `UnitCrowd` pass-through.
  Perf: simulate-near/freeze-far on a staggered ~0.4s LOD cadence (disables agent + `BakedBillboard8` when far).
- `NpcBarks.cs` — static `Town`/`Field` canned-line pools (LLM-FREE; no Claude API).
- `DamagePopup.SpawnBubble(pos,label,color,life,rise)` — new pooled-popup overload for barks; `Begin()` resets
  timing so a stretched bark can't leak its long lifetime into the next combat number (zero-alloc).
- `Editor/CharacterCastTools.cs` — `Tools ▸ unwritten ▸ World ▸ Refresh Character Cast` refreshes the CharacterSpec's
  civilian/fighter model lists only; it places nothing.
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

## Proposed shape (when we build): townsfolk rows in `WorldTownsfolk` + `TownNPC` brain on PathAgent3D,
wired into RecipeGenerator so a recipe can request "N townsfolk + barks + critters". Write a short
design doc first, then build the wandering-townsfolk+barks slice.
