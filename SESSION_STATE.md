# SESSION_STATE — unwritten (Unity 2D)

> ⚠️ **COMMIT REMINDER:** the whole pile (audio, day/night, respawn, bugfixes) is UNCOMMITTED on
> `experiment/3d-world`. **Committing/pushing to GitHub is the USER's job** — Claude only reminds, never commits.

---
## ⏩⏩⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17, latest) — PHASE G SKILL SYSTEM: PLANNING — READ FIRST
Branch `experiment/3d-world`, UNCOMMITTED. Starting Phase G (the signature skill-draft) — PLANNING stage,
no code yet. Web prototype to port lives in `src/skill-draft/`:
- `graph.ts` = the BRAIN (pure, deterministic): `SKILLS` (28 nodes, tiers 0–6, 7 branches), `TIERS` rows,
  `BRANCH_META` (label/color), and the draft algorithm `getDraft(owned, seen)` = **2 guaranteed (strongest
  on-path) + 1 wildcard (random off-path pivot)**, no-repeat via a `seen`/burned ledger, **classes are
  auto-granted never drafted** (`classUnlockedBy`). Helpers: `neighborsOf`, `dominantBranch`, `isDraftable`.
- `SkillCard.tsx` = a draft card (guaranteed=blue, wildcard=gold, lookahead "next picks" on guaranteed only).
- `SkillTree.tsx` = tier-row graph with owned/available/burned(missed) node states + computed connector lines.
- `Tooltip.tsx`, `BuildPanel.tsx` = hover tooltip + current-build summary.

Hooks into existing code: `Character.OnLeveledUp(int)` ALREADY fires; the empty **Skills (K)** panel
(built by `UIShellBuilder`) is the home for the tree. Planned port = 4 layers (data → state/hook → draft
popup → tree view). See chat for the full plan + open decisions (data-as-static-C# vs ScriptableObjects;
icons; whether picks apply mechanical effects yet). Tracked by issues VIN-1 epic + VIN-3/4/5/6.

---
## ⏩⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — COMBAT BUGFIXES: phantom damage + mob respawn
Branch `experiment/3d-world`, UNCOMMITTED.

**BUG 1 — "still taking damage, no mobs around."** `MonsterAI3D` touch-damage used PLANAR (XZ) distance, so a
mob that walked off a cliff/map-edge and fell BELOW the terrain (invisible, same XZ as the player) kept biting
forever. FIX: gate the bite on TRUE 3D distance (`Vector3.Distance`) — a mob far below can't reach you.
(A killed mob already can't bite: `DeathDissolve.Play` disables its `MonsterAI3D` synchronously.)

**BUG 2 — mobs don't respawn.** There was NO respawn system (`NatureMobs` is an editor one-shot). Since mobs
are built procedurally (no runtime prefab), added a RECYCLE-based respawner:
- NEW `MobRespawn.cs` — on `Health.Die`, after `DeathDissolve` finishes, hides the mob for `respawnDelay`
  (8s), then revives it at a fresh ground spot within `homeRadius` of its spawn, kept `playerClearRadius`
  (7s) from the player. Re-enables everything DeathDissolve switched off (AI/agent/CC/colliders/renderer).
- `Health.Revive()` (NEW) = full HP + base color; `Health.Die` now hands off to `MobRespawn.OnFadedOut` if
  present (else Destroy, unchanged for 2D/spike mobs).
- `DeathDissolve` captures original material/color on Awake + `ResetAfterRespawn()` to undo the fade.
- `Build25DSpike.BuildMonster` now adds `MobRespawn` to every mob.

**⚠️ RE-BAKE:** **Populate Mobs (Nature)** (bakes `MobRespawn` onto mobs + the 3D-touch fix is code-only).
Test: kill a mob → ~8s later it returns elsewhere; walk the map → no more phantom hits. Tune knobs on the
mob's `MobRespawn` (respawnDelay/homeRadius/playerClearRadius).

---
## ⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — UI CLICK + HURT GRUNT + DAY/NIGHT AMBIENCE
Branch `experiment/3d-world`, still UNCOMMITTED. Extends the TomMusic SFX system (checkpoint below) with
the 3 requested sounds. All TomMusic WAV foley (no vocals in the pack).

**Built this session:**
- **UI click sounds** — NEW `UISounds.cs` (runtime) on `GameUICanvas`: `open`/`close`/`click` SfxSets +
  static `PlayOpen/PlayClose/PlayClick`; auto-wires every `Button` under the canvas to the click. Clips
  (TomMusic "Doors Gates and Chests"): open = Chest Open 1/2, close = Chest Close 1/2, click = Lock Unlock,
  assigned in `UIShellBuilder`. `PanelToggle` plays open/close on I/C/K toggle + Esc-close-all.
  `DeathScreen.OnRespawnClicked` calls `UISounds.PlayClick()` (its canvas is separate, so direct call).
- **Player hurt grunt** — `Character.hurtSfx` (NEW SfxSet), played in `Character.TakeDamage` on UNblocked
  damage (before OnDamaged). PLACEHOLDER = low-pitched "chop" (pitch 0.7–0.85) as a meaty body-hit; assigned
  in `Build25DSpike.BuildCharacter`. ⚠️ swap in a CC0 human grunt later (TomMusic has no vocals).
- **Day↔night CYCLE (light + ambience + clock).** Central NEW `TimeOfDay.cs` (singleton on GameSystems) =
  accelerated clock split EVENLY: `halfCycleSeconds` 3600 → **1h day + 1h night** (full = 7200s). `time01`
  0=sunrise, 0.25=noon, 0.5=sunset, 0.75=midnight. Exposes `Daylight01` = (cos((t−0.25)2π)+1)/2 and
  `Night01`. **It IS the live in-game tuner**: `paused` + `[Range] time01` slider to scrub, `testSpeed`
  fast-forward, `[ContextMenu]` Jump ▶ Sunrise/Noon/Sunset/Midnight, and a `_readout` string showing live
  phase/clock in the Inspector during Play.
  - **Light** — NEW `DayNightLight.cs` on the Systems Sun (added in `SystemsSceneBuilder`; `BuildLight()`
    now RETURNS the Light). Lerps sun color (warm↔blue) + intensity (1.1↔0.18) + sky pitch + scene
    `RenderSettings.ambientLight` (sets ambientMode=Flat) by `Daylight01`.
  - **Ambience** — `DayNightAmbience` REFACTORED to read `TimeOfDay.Instance.Night01` (falls back to its own
    `fallbackCycleSeconds` timer only if no clock). Forest Day↔Night still wired by `NatureStageBuilder`.

**⚠️ RE-BAKE for these:** **Build UI Shell** (UISounds + click clips into GameUICanvas prefab) → **Setup
Systems Scene** (Character.hurtSfx + DayNightLight on Sun + TimeOfDay clock + new UI prefab) → **Add Forest
Ambience (day/night)** with NatureStage open (or **New Nature Stage** rebuild). Test: open Bag/Char/Skills =
latch click; get hit by a mob = thud grunt + red flash; day/night = select **GameSystems** in the hierarchy
during Play, tick **TimeOfDay ▸ paused**, drag **time01** (or right-click ▸ Jump ▶ Midnight) → sun dims to
blue, ambient darkens, forest audio crossfades to night. Untick paused + raise testSpeed to watch it cycle.

---
## ⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — SFX SYSTEM (TomMusic)
Branch `experiment/3d-world`, still UNCOMMITTED. Audio direction: **TomMusic "Free Fantasy 200 SFX"**
(realistic foley, royalty-free; the user rejected Kenney as too chiptune). RO-rip audio is OFF-limits
(copyright). Pack lives at `Assets/Audio/TomMusic/` (we use the **WAV** files).

**System built this session:**
- `Sfx.cs` — `SfxSet` (clip[] + volume + pitch range; `Pick()` = random, no immediate repeat) + `Sfx`
  (pooled one-shot player, 16 2D voices, `Sfx.Play(set)` static). Lives on Systems (`GameSystems`).
  RANDOM VARIATION is ~free (one Random + index); cost is voices, capped by the pool.
- `Editor/SfxAssets.cs` — `SfxAssets.Set(vol, folder, names…)` loads clips from
  `Assets/Audio/TomMusic/WAV Files/SFX/` and forces **Decompress On Load** import (right for short SFX).
- Equipment-driven audio: `Item` gained `attackSfx`/`hitSfx` (weapon) + `guardSfx`/`blockChance` (off-hand).
  Direct AudioClip refs on the SO (NOT an id→wav registry — the SO ref IS the link).
- Hooks: **attack** (swing) + **hit** (impact) in `Player3DAttacker` (equipped weapon overrides player
  defaults via `WeaponSfx`); **footsteps** in `Player3DController` (Dirt Walk, `stepInterval` 0.32s);
  **block** = new off-hand mechanic in `Character.TakeDamage` (rolls `off.blockChance`, on proc negates
  the hit, plays guardSfx/`blockSfx`, pops "Guard", fires `OnBlocked`). `MonsterAI3D` skips the dmg popup
  when blocked (dealt==0).
- Defaults assigned in `Build25DSpike` (player attack/hit/footstep + Character blockSfx = Sword sets);
  `ItemStarterSet` gives weapons Sword attack/hit, the "Wooden Guard" off-hand Sword Blocked + 0.25 block.

**MORE SFX added (same session):**
- **Mob death** — `DeathDissolve.deathSfx`, played on death (assigned "Spell Impact 1/2/3" = poof; TomMusic
  has NO creature vocals — swap in CC0 monster cries later). Set in `Build25DSpike.BuildMonster`.
- **Level-up sting** — `Character.levelUpSfx`, played in `AddXP` on level (assigned "Firebuff 1/2"). Set in
  `BuildCharacter`.
- **Forest ambience** — per-map looping `AudioSource` (TomMusic "BGS Loops/Forest Day", 2D, vol 0.22,
  Streaming import). `SfxAssets.Ambient()` loads it. Added by `NatureStageBuilder` on fresh builds AND via a
  NEW menu **Tools ▸ unwritten ▸ Add Forest Ambience** that injects it into the OPEN map WITHOUT rebuild
  (so you don't lose scatter/mobs). Survives Systems load (MapManager strips listeners, not AudioSources).

**⚠️ RE-BAKE for audio:** **Setup Systems Scene** (Sfx player + player/Character defaults incl. levelup) +
**Create Starter Items** (Item audio) + **Populate Mobs (Nature)** (mob deathSfx) + **Add Forest Ambience**
(open NatureStage, no rebuild). Test: walk=footsteps; hit mob=swing+impact; kill mob=poof; level=Firebuff;
forest hum in bg; equip Wooden Guard = ~25% Guard clang. NEXT possible: UI click sounds, player hurt grunt,
day/night ambience swap, more weapon-distinct sets (bow).

---
## ⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — PHASE F death/respawn DONE (code)
Branch **`experiment/3d-world`**, still **UNCOMMITTED**. Phase F (the last foundation gap) is coded; user
verifies in Unity (press Play, let a mob kill you).

**WHAT CHANGED:**
- `Character.TakeDamage` NO LONGER instant-revives at 0 HP — it just fires `OnDied` and stays dead
  (`IsDead`). New `Character.Revive()` (full HP + `OnRevived` event) is the only way back.
- `DeathScreen.cs` (NEW, runtime) — persistent overlay on the Systems layer. Listens to `OnDied`, fades
  in a full-screen "YOU DIED" vignette, counts down `respawnDelay` (3s), then a **Respawn** button →
  `Character.Revive()` + teleport to the map's SpawnPoint. **Builds its own ScreenSpaceOverlay canvas
  in code** (sortingOrder 5000, no theme assets) — fully self-contained.
- `Player3DController.Update` freezes input/orders while `Character.Instance.IsDead` (corpse only gets
  gravity; can't walk a dead player).
- Respawn teleport reuses existing placement: `PersistentPlayer3D.Respawn()` (NEW public, = re-`PlaceInScene`)
  with a 2D fallback `PersistentPlayer.Respawn()` (NEW public). Both just re-run their SpawnPoint snap.
- Wired in: `SystemsSceneBuilder` now `gameSystems.AddComponent<DeathScreen>()`.

**DEATH ANIMATIONS (same session):**
- **Player** — `Player3DController` shows a defeated pose while `IsDead` (new `deadSheet` Texture2D field,
  sliced at runtime in Awake like `attackSheet`; from NinjaAdventure `SeparateAnim/Dead.png`, one 16×16
  frame). `Build25DSpike.BuildPlayer` assigns it (`LoadDeadSheet`). DeathScreen now waits `preFadeDelay`
  (0.5s) before fading in so the fall reads first.
- **Mobs** — `DeathDissolve.cs` (NEW, per user spec): NO shrink. On death → **smoke puff** (NinjaAdventure
  `FX/Smoke/Smoke/SpriteSheet.png`, 6×32px, via `SlashEffect.Spawn`), freeze the mob (disable
  AI/agent/CC/colliders), **remains lie `remainSeconds` (3s, tunable)**, then **fade `fadeSeconds` (1s)** →
  destroy. The fade swaps the renderer to `Sprites/Default` (alpha-blend) first, because `BillboardLit`
  is alpha-CLIPPED and can't fade. `Health.Die()` defers to `DeathDissolve` if present (else
  instant-destroy — 2D mobs unchanged). Added in `Build25DSpike.BuildMonster`.

**EXTRA ANIMS (this round):**
- **Mob attack tell** — `AttackLunge.cs` (NEW): squash-stretch "chomp" (scale punch only, never position,
  so it can't fight CC/pathing). `MonsterAI3D` calls `lunge.Punch()` on each bite. Added in BuildMonster.
- **Player hurt flash** — `PlayerHurtFlash.cs` (NEW): red tint for 0.12s on damage (red works under the
  cutout shader; white wouldn't). Driven by new `Character.OnDamaged` event. Added in BuildPlayer.
- **Idle breathing** — procedural squash-stretch loop (no idle art exists). In `BillboardAnimator3D`
  (mobs) + `Player3DController` (player), gated to standing-still; random per-instance phase desyncs the
  herd. Single scale owner per object: idle YIELDS to `AttackLunge.Punching`; DeathDissolve disables the
  mob animator so no fight during the corpse linger/fade; player resets to base when moving/attacking/dead.
  Knobs: `breathe`/`breatheAmount`(0.03)/`breatheSpeed`(2.5).

**⚠️ USER MUST RE-RUN `Setup Systems Scene`** (bakes DeathScreen + player `deadSheet` + `PlayerHurtFlash`
into `Systems.unity`) **AND `Populate Mobs (Nature)`** (so mobs get `DeathDissolve` + `AttackLunge` +
smoke). Then Play → mob bite = chomp + red flash; kill a mob = smoke → body lingers 3s → fades; die =
defeated pose → overlay → Respawn.

**NEXT:** verify in Unity → then Phase G skill-draft (the empty K panel) OR NPCs+dialogue. Also: COMMIT
the whole uncommitted pile when ready.

---
## ⏩⏩⏩ CHECKPOINT (2026-06-17, late) — gameplay + 2.5D Systems port
Branch **`experiment/3d-world`**, **UNCOMMITTED** (user is staying uncommitted for now; offer to commit on resume).
All work below is in the working tree; compile-clean by reference scan (user verifies in Unity = presses Play).

**WHAT'S NEW since the nature-direction commit (all this session):**
1. **Mobs + combat in the nature world.** `Populate Mobs (Nature)` / `Clear Mobs (Nature)` (`NatureMobs.cs`) —
   surface-snapped billboard mobs (`Build25DSpike.BuildMonster`), clear of spawn, area-density. The 2.5D
   combat loop was already wired: `Player3DController` click-to-attack (`MonsterUnderCursor`, A-attack-move,
   S-stop) → `Player3DAttacker` → `Health`; `MonsterAI3D` chases + bites via `Character`. `PathAgent3D` has
   gravity so mobs stick to hills.
2. **Player attack POSE** — `Player3DController` slices 4 directional poses at RUNTIME from a `Texture2D
   attackSheet` (NinjaAdventure SeparateAnim/Attack.png auto-slices are BROKEN, so we slice 16px cols
   ourselves; player is NinjaBlue2). Holds pose ~0.22s per swing.
3. **Target indicator** — gold down-arrow ported to 3D: `Player3DController` creates a `TargetIndicator` in
   Awake (+`Billboard`), Follows the attack target, clears on stop/kill/move.
4. **Slash FX** — `Player3DAttacker` spawns `SlashEffect` (SlashCurved sheet, 32px×4@22fps) on each landed
   hit; `SlashEffect.Spawn` now adds a `Billboard`.
5. **Damage numbers** — `DamagePopup` faces the camera each LateUpdate (no more skew when orbiting).
6. **⭐ 2.5D SYSTEMS PORT (the big one).** Player/camera/HUD/cursor are now PERSISTENT in `Systems.unity`,
   maps are CONTENT-ONLY. So player tweaks never require a map rebuild again.
   - `SystemsSceneBuilder` REWRITTEN → 2.5D: Sun + billboard player (+`PersistentPlayer3D`) + 3D follow cam
     + Character + **GameUICanvas** (full UI shell: HP bar + Bag(I)/Character(C)/Skills(K) panels via
     `PanelToggle`/`CharacterUIBinder`/`BagSeeder`, bound to `Character.Instance`) + `GameCursor3D` +
     `GameRoot` (firstMap="NatureStage"). Sets Build index 0 + playModeStartScene=Systems. (NOTE: Skills/K
     panel is an EMPTY placeholder — Phase G skill-draft still unbuilt.)
   - `NatureStageBuilder` REFACTORED → content-only map: terrain (Ground layer) + `Pathfinder3DConfig` +
     a `SpawnPoint` (id "Start") + an edit-mode Sun; registered in Build Settings. NO player/cam/HUD.
   - `PersistentPlayer3D.cs` (NEW) — 3D spawn: on map load, teleport to SpawnPoint, raycast-snap feet to
     terrain (precise via CC geometry → groundY+0.28), cancel order (`Player3DController.Halt`), aim
     `CameraFollow3D`. Reuses GameRoot/MapManager/SystemsPlayMode/SceneTravel UNCHANGED.
   - `MapManager.StripForeignGlobals` now also disables a map's Lights at Play (Systems owns the one light).
   - `BuildPlayer`/`BuildMonster` CC bumped to slopeLimit 45 / stepOffset 0.3 so units walk the hills.

**RUN FLOW (the new boot model):** Build Prefabs ▸ Low Poly Nature → **New Nature Stage** → **Scatter
Nature** → **Populate Mobs** → **Setup Systems Scene** → press **Play** (Systems boots, NatureStage loads
under it, player spawns at SpawnPoint). Re-running `Setup Systems Scene` is cheap (no content loss); the
map content is in NatureStage. `activeInputHandler: 2` (Both) so PanelToggle's old `Input` works.

**KNOWN/UNVERIFIED:** levitation fix + light-in-edit + slash + panels were JUST coded — user hadn't
confirmed the last round in-Unity before this checkpoint. If player still floats a hair, bump
`Build25DSpike.SpriteFoot` (0.1) up (sprite bottom padding). Editor-light: add a Directional Light to
NatureStage by hand (auto-disabled at Play) OR re-run New Nature Stage.

**NEXT options (user to pick):** verify this round → then Phase G skill-draft (fills the K panel) /
death-respawn (Phase F gap) / NPCs+dialogue+a clearing. Also: COMMIT the cleanup+gameplay+port when ready.

---
## ⏩⏩ DIRECTION FULLY COMMITTED: artist low-poly nature + billboard characters (2026-06-17)
Active branch **`experiment/3d-world`**. **The world is now built from Just Create Studio's "Stylized Low
Poly Nature" pack** (`unity/Assets/Art/LowPolySimpleNature_FBX/`, 25 FBX sharing ONE atlas
`SimpleNature_Texture_01.png`). The pack's **Ground_0x meshes are artist-sculpted terrain with baked-in
hills** — so we get elevation FOR FREE (no procedural terrain gen needed). Characters stay 2D billboards
(NinjaAdventure). Play `Assets/Scenes/NatureStage.unity`. **VERIFIED Play-confirmed: forest renders, player
walks the hills, paths around trees/rocks, walks through grass; clean Console.** User will add MORE free
packs from the same artist (drop each in `Assets/Art/`, add a sibling line in `KenneyPrefabBaker`).

**The nature pipeline = 3 one-click tools (all NEW this session):**
- `Build Prefabs ▸ Low Poly Nature` (`KenneyPrefabBaker.cs`) — bakes the 25 FBX → drop-ready prefabs at
  `Assets/Prefabs/LowPolyNature/` (shared atlas material + MeshCollider each). `BakeKit` now takes an
  optional `colormapPath` so any single-atlas pack works.
- `New Nature Stage (low-poly ground)` (`NatureStageBuilder.cs`) — builds `NatureStage.unity`: Ground_01 as
  the floor (Ground layer + collider + height-aware `Pathfinder3DConfig`) + the standard billboard rig
  (reuses `Build25DSpike` public helpers). Self-contained (owns `EnsureGroundLayer`).
- `Scatter Nature (fill map)` + `Clear Nature Scatter` (`NatureScatter.cs`) — area-based weighted scatter of
  trees/bushes/grass/flowers/rocks/mushrooms/stumps onto the terrain via raycast. Re-runnable (reseeds).
  Colliders per category: trees/stumps = trunk capsule (walk under canopy), rocks = box, decor (grass/
  flowers/mushrooms/bushes) = none. All scatter on Default layer (NOT Ground) so the height-ray ignores it.
  Obstacles = non-ground colliders (see `Pathfinder3DConfig`). Tunables = consts at top of NatureScatter.cs.

**BIG CLEANUP done this session — committed to nature, deleted the dead experiments.** Tools menu cut
~40 → 16. **Deleted:** all 2D-tilemap authoring (DepthSortTools, FloorRuleTileBuilder, TilemapTools,
GroundFillTool, MapBaker, BuildingBakeTools, MapBoundaryBuilder, NewMapSceneTool, PropTemplate ×2, Generate
Town) · Kenney 3D experiments (Town3DBaker, TerrainSandbox, Stage25DBuilder, Build25DElevationSpike,
HouseAssembler, KenneyPaletteRemap, PlayerBuilder, Terrain3DArt/Field) · the whole `Scripts/MapGen/` runtime
(town-generator brain) · `TerrainProjected.shader` · scenes NewTown/Town3D · art (Kenney FantasyTown +
Platformer kits, Tiles, Terrain3D, Palettes, baked Kenney prefabs). **Surgical edits:** `MonsterBuilder`
stripped to its shared combat+cursor helpers (still called by Run All / Setup Global Systems — the Systems
layer is KEPT per user); `Build25DSpike` kept all rig helpers, demo menu removed.
⚠️ **SCAR (don't repeat):** `rm -rf Shaders` nuked `BillboardLit.shader` too → player went magenta;
restored from git (same .meta/GUID). And the batch delete + recompile triggered a transient URP
`get_scriptableRenderer IndexOutOfRange` (renderer-cache desync) — cleared by reimporting UniversalRP.asset /
Unity restart, NOT a data bug.

**NEXT:** commit the cleanup on `experiment/3d-world` (pending), then content: more packs from the artist,
mob/NPC placement in the nature world, and folding combat/RPG into the nature stage. Spike doc:
`q1755ftddqpyy9qm7e357hdpf188sajr`.

---

## (Superseded) Kenney 3D maps + billboard characters (2026-06-17)
After spiking it, we **rejected the "2D sprite-props in a 3D
world" approach** (billboard trees/houses). The killer: a billboard building can't have honest collision —
a collider that rotates with the always-camera-facing art confuses players, and a static collider
mismatches the rotating art. Large/enterable structures force real 3D meshes.

**DECISION (final): the ENVIRONMENT (ground, buildings, trees, props, terrain) is built from Kenney 3D
mesh assets; only CHARACTERS (player / mobs / NPCs) stay 2D billboards** — the Ragnarok-Online model, and
what `Build25DSpike` already does (Kenney FBX props + billboard player). With 3D meshes, occlusion is the
depth buffer (billboards write depth via `BillboardLit`), so the old "camera-depth vs world-Y sprite-prop"
problem doesn't exist — that whole sub-system is gone.

**CLEANUP done this session — deleted the 2D-in-3D experiments:** `Town25DConverter.cs` (billboard town
from tilemaps), `Build25DTerraceDemo.cs` (2D-tile-skinned terraces), `CameraDepthManager.cs` +
`CameraDepthProp.cs` (static sprite-prop sorting — moot with 3D meshes), and scenes `NewTown25D.unity` +
`Terrace25D.unity`.

**KEPT (valid for the Kenney direction):** `Build25DSpike.cs` (Kenney-props + billboard rig — the
foundation; its helpers are now public, incl. `BuildPlayer(foot)` / `BuildMonsters(center,r)` overloads);
the height-aware `Pathfinder3D` + `Pathfinder3DConfig` (multi-level terrain pathing works on ANY 3D
ground, incl. Kenney elevation blocks); `Build25DElevationSpike.cs` (primitive plateau/ramp proof of it,
holds the `Ground`-layer helper); `CameraDepthSorter` + all billboard-character/combat code.

**Kenney 3D asset location:** existing kit = `unity/Assets/Art/KenneyFantasyTownKit/FBX format/`
(FBX models + `Textures/colormap.png`). Put each NEW downloaded pack in its OWN folder under
**`unity/Assets/Art/`** (e.g. `unity/Assets/Art/KenneyMiniDungeon/`), using the pack's **FBX format**
folder + its colormap texture. (`Build25DSpike` loads `.fbx` and re-applies the kit colormap material.)

**FLOW CHOSEN (2026-06-17): A — hand-place Kenney prefabs in a clean 2.5D scene.** New pack
`KenneyPlatformerKit` is in `unity/Assets/Art/KenneyPlatformerKit/` (153 FBX + `FBX format/Textures/colormap.png`).
Two new one-click tools built this session (both reuse `Build25DSpike` public helpers):
- `Tools ▸ unwritten ▸ Build Kenney Platformer Prefabs` (`KenneyPrefabBaker.cs`) — bakes every kit FBX
  (skips `character-*`, since chars are 2D billboards) into drop-ready prefabs at
  `Assets/Prefabs/KenneyPlatformer/` with the colormap material + a MeshCollider each, plus a shared
  `PlatformerKit.mat`. Re-runnable (overwrites in place).
- `Tools ▸ unwritten ▸ New 2.5D Stage (hand-place)` (`Stage25DBuilder.cs`) — builds clean
  `Assets/Scenes/Stage25D.unity`: boot rig only (3D follow cam, sun, flat grass ground, billboard
  player, HP/HUD, Dota cursor), NO scattered props/mobs. Drag the baked prefabs in by hand.

**AUTO-GENERATION (2026-06-17): reuse the 2D generator for 3D maps — Phases 1–3 BUILT.** Decision:
both kits + (eventual) elevation. The 2D layout brain (`TownGenerator.Generate(recipe)`→`TownLayout`)
is reused UNCHANGED; only the backend differs.
- Phase 1 — `KenneyPrefabBaker` generalized → menu **Build Prefabs ▸ {Platformer · Fantasy Town · Both Kits}**
  (→ `Assets/Prefabs/Kenney{Platformer,FantasyTown}/`). Old single menu name retired.
- House — `HouseAssembler` → **Build Prefabs ▸ Assemble House (Fantasy Town)** tiles modular wall/door/
  window/roof FBX into `Assets/Prefabs/KenneyFantasyTown/house-small.prefab`. VERIFIED good (door front,
  windows, eaves). Tuning consts at top of the file.
- Phase 2 — `Town3DTheme` + `Prop3D` (`MapGen/Town3DTheme.cs`): prefab palette mirroring the fields the
  generator reads. Tiny additive hook: `TownLayout.placed` (List<PlacedProp>) + a 2-line record in
  `TownGenerator.Stamp()` (2D path ignores it).
- Phase 3 — `Town3DBaker` → menu **Generate 3D Town**. Feeds the generator a synthetic MARKER TownTheme
  (sentinel tiles + synthesized PropTemplates from Prop3D, mapped back), runs it, then emits Kenney
  prefabs: ground tiles + roads + Fence boundary + one prefab per placed prop; ONE big Ground-layer
  floor box collider (perf) + per-prop box colliders; `Pathfinder3DConfig` height-aware. Auto-creates a
  default `Assets/MapGen/Town3DTheme_Default.asset` wired from baked prefabs (no manual setup). Output
  `Assets/Scenes/Town3D.unity`. FLAT for now.
- Phase 4 — elevation/slopes: TERRAIN SANDBOX WORKING (`TerrainSandbox.cs`, menu **Terrain Sandbox**).
  Merged, tiered, ramp-gated Kenney terrain, VERIFIED good + walkable:
  • merged lawn via the OVERLAP trick (blocks scaled ~1.3× so rounded shoulders fuse — memory
    `kenney-3d-terrain-overlap`); blocks visual-only, flat per-cell box collider on `Ground` layer.
  • quantised-Perlin heightmap; every tier edge is an IMPASSABLE cliff; the ONLY way up is generated
    grass RAMPS placed on a union-find spanning tree of tier regions (all areas reachable).
  • ramp = same block-grass, oversized + thick + ROTATED ABOUT ITS BOUNDS CENTRE (its pivot is off-base
    — rotating about pivot flung it into the air; centre-rotate fixed it). Player stepOffset 0.1 (cliffs
    are walls) + slopeLimit 45 (ramps walk); `Pathfinder3DConfig` maxStep = TierStep*0.75.
  • KNOWN MINOR BUG (deferred to a pathfinding-polish pass): when the player is flush against a cliff
    right by a ramp mouth, PathAgent3D steers straight into the wall (walks in place) instead of backing
    off to the ramp. Not structural.
  NOT YET: fold the terrain into `Generate 3D Town` (houses/roads/props at terrain height).
- FLAT+SCENIC town: BUILT. `Town3DBaker` rings the flat walkable town with tiered Kenney hills
  (`EmitScenicBorder`) as the natural boundary (no fence); `Town3DTheme.scenicBorder` toggles it.
  Billboards (player/mobs/NPCs) scaled to `Build25DSpike.BillboardScale = 0.7` (30% smaller vs the 3D
  map; only the 2.5D scenes, NOT the 2D game).
- TERRAIN TEXTURING: WORKS (2026-06-17). 2D pixel-art ground tiles painted on the merged 3D terrain via
  world-projection shader `Unwritten/TerrainProjected` (canonical URP unlit: GetVertexPositionInputs +
  instancing macros) + `Terrain3DArt.cs` (grass/dirt tiles extracted to `Art/Terrain3D/`, point+repeat).
  ⚠️ **GOTCHA that cost ~6 round-trips:** the shader renders in the GAME (3D renderer, Play mode) but
  NOT in the Scene view (which uses the project's 2D Renderer2D, default renderer index 0) — so terrain
  looks INVISIBLE while editing and only shows on **Play**. Stock URP/Lit/Unlit DO draw under the 2D
  renderer, which made diagnosis misleading. **Always verify 3D-shader work by pressing Play, not Scene
  view.** 2nd gotcha: the `Art/` import postprocessor forces `wrapMode=Clamp`, so tiles didn't repeat
  (detail only at world origin) — fixed two ways: shader wraps UVs with `frac()`, and the postprocessor
  now exempts `Art/Terrain3D/` → Repeat. Shader is now LIT: gentle Lambert + RECEIVES soft main-light
  shadows (player/props/cliffs darken the ground) + a ShadowCaster pass (cliffs self-shadow). Tune on
  `Art/Terrain3D/Terrain3D.mat`: `_Tiles` (grass density, default 4), `_ShadeFloor`, `_Ambient`.
  TERRAIN MILESTONE COMPLETE: generated, walkable, ramp-gated, textured, lit/shadowed 2.5D terrain+town.

**NEXT (user step):** Unity → **Build Prefabs ▸ Both Kits**, then **Assemble House (Fantasy Town)**, then
**Generate 3D Town**; press Play + screenshot. Watch for: house footprint vs grass cell-size match,
road look, prop overlap. Prior spike doc: `q1755ftddqpyy9qm7e357hdpf188sajr`.
---

**Last updated:** 2026-06-16 · **Status:** 🟢 foundation A–F built; Phase G is next. **Repo: the Unity game was relocated INTO `unity/` of this workspace (committed `98b796c`); old `D:\Unity3d\unwritten` is now just an archive.**

> **Map Generator (VIN-13) — BUILT & WORKING (2026-06-16).** Editor tool bakes *permanent* town scenes from a `MapRecipe` (params + seed) — NOT runtime/roguelike. `Tools ▸ unwritten ▸ Generate Town`. Generic `PropTemplate` objects (houses/trees/wells, placement Lot/OpenGround/PlazaCenter/Roadside); auto-tiled terrain; gate Portals + spawns. **Per-prop collision is authorable** (`PropTemplate.collisionMode` Auto/Custom/None, **half-cell** resolution, edited via the Inspector grid). **Everything is PRE-BAKED at generate time** (static foot-sorted props w/ fixed sort orders + plain run-merged static box colliders) — **no on-Play generation** (perf rule: memory `performance-first-prebake`). Hand-painted maps → `Bake Map (props + collision)`. Spec doc `q178ppzhynajt660nadeh6ras988n88a`; **workflow how-to** doc `q171zzcsyy34n52cga9v1zn95d88sn9g`; detail in memories `map-generator-tool` + `ysort-depth`. **Live build checkpoint =** `WAKE_UP.md` **(project root) — on resume, read it first.** Safe to `/clear`: on-disk code + notes are the source of truth.

> Cheap-resume file. Canonical status lives in the **"unwritten — Development Roadmap"** doc (Tempo Docs); this is the quick pointer. On "resume": read this, then the roadmap doc, then the on-disk files in the Unity project — **now at `unity/Assets/Scripts/` inside THIS repo** (relocated + committed 2026-06-16 as `98b796c`; edits show in Tempo's diff). The standalone `D:\Unity3d\unwritten` / GitHub `unwritten-unity` are just the archive now.

---

## Where we are

The playable foundation is essentially complete (Phases A–F). Most recent work (2026-06-14) **reworked movement & combat into Dota-style controls**:

- `PlayerCommander` (new) — order brain: **right-click** move / attack (enemy) / follow (`FriendlyUnit`); **A** then click = attack-click (enemy = attack, ground = attack-move); **S** = stop & cancel everything (incl. un-landed damage) + `onStop` hook for future skill-casts.
- `PlayerController2D` is now a click-to-move **service** (`MoveTo`/`MoveToward`/`Halt`); **WASD removed**.
- `PlayerAttacker` is now a swing **executor** with a `windupTime` — damage lands at the end of the swing, so **S** cancels it.
- `GameCursor` (new) drives a **hardware cursor** from the **Kenney Cursor Pack** (`Assets/Art/KenneyCursorPack`, Outline) — pointer / sword / crosshair. Replaces the old UI `CombatCursor` (deleted).
- Also new: `ClickMarker` (click ripple) and `FriendlyUnit` — the **reusable allegiance marker** for friendlies (non-attackable + right-click-to-follow); it's the seam a future mercenary/companion reuses. Wired by `Setup Mouse Combat`. (The temporary `Add Test Ally` menu was removed — follow is code-complete but only testable once a real `FriendlyUnit` exists in a scene.)

(Earlier predecessor work had reworked combat into mouse-target click-to-attack with `SlashEffect` / `CharacterAnimator2D.PlayAttack` / `TargetIndicator` — those FX still apply.)

| Phase | State |
| --- | --- |
| A Engine/hello-world | ✅ done |
| B Character & tiled world | ✅ done |
| C Towns/portals/dialogue | ✅ systems done (town content hand-painted) |
| D Monsters & combat (mouse-target) | ✅ done |
| E UI shell (HUD + panels + drag-equip) | ✅ done |
| F RPG numbers (`Character`) | 🟢 core done — **only the death/respawn screen is missing** |
| G Skill-draft (signature) | ⬜ **next** |
| H Multiplayer | ⬜ deferred |

## Immediate next

**Phase G — the graph-based level-up skill draft.** Port the web prototype in `src/skill-draft/` (`graph.ts`, `SkillCard`, `SkillTree`, `SkillDraftPage`) to Unity: skill graph as ScriptableObjects → `DraftService` (2 guaranteed + 1 wildcard, no-repeat, class reveal) → 3-card draft UI → tree view. Hang it off `Character.OnLeveledUp` and the **Skills** panel (K). Tracked by issues **VIN-1** (epic) + **VIN-3/4/5/6**.

Small loose end if preferred first: Phase F **death/respawn screen** (at 0 HP you currently instant-revive at full HP; `Character.OnDied` already fires).

## Issue board (synced 2026-06-14)

- Done: **VIN-2** (setup), **VIN-7** (player+XP loop), **VIN-8** (Phase B), **VIN-10** (Phase D), **VIN-11** (Phase E).
- In progress: **VIN-9** (Phase C), **VIN-12** (Phase F).
- Planned (Phase G): **VIN-1** epic + **VIN-3/4/5**; **VIN-6** (tree view) in backlog.
- ⚠️ **Cross-artifact links are NOT wired yet** — `link_items` was down (server 500s) during the 2026-06-14 sync. Retry: link VIN-3/4/5/6 → VIN-1, and link VIN-1 + the new milestone issues → the roadmap doc.

## Gotchas / decisions

- Scripts are authored in the Unity project at **`unity/Assets/Scripts/` inside this repo** (the whole game was relocated here 2026-06-16 — one git repo, edits show in Tempo's diff). The standalone `D:\Unity3d\unwritten` + GitHub `unwritten-unity` are the archive. (A *separate* 2026-06-14 `unity/` script-mirror was deleted back then; this deliberate full relocation supersedes that note.)
- PPU 16 for all art; world content is hand-painted by the user (don't auto-generate).
- **Depth/z-index + player drift ✅ DONE (2026-06-14):** depth uses **per-object feet-Y sorting** (`YDepthSorter` on every entity) + fixed order **BANDS** for tilemaps/FX, all in `DepthSortRuntime` (auto-applied on Play, no menu needed). Layer stack: Ground/Decor (behind) → ENTITIES (per-object) → Overhead → FX. **Obstacle tilemaps auto-bake into grouped foot-sorted props at Play** (`DepthSortRuntime`, runtime + non-destructive) — just paint trees/houses on the Obstacles tilemap, no convert menu. Multi-tile props group into one foot-sorted unit (`YDepthSorter` GROUP mode); trees get a trunk base collider (walk under leaves), "building/house"-named tilemaps get full colliders. **Create Decor + Overhead Layers** adds two non-collider decoration tilemaps. The earlier transparency-axis-only attempt **flickered** on same-row ties and is retired. Full detail + gotchas (16-bit sortingOrder, stacked-tile caveat) in \[\[ysort-depth\]\]. Player **drift fixed** separately by turning OFF crowd-avoidance on the player (`PlayerController2D.Start` → `agent.avoidRadius = 0`). See \[\[pathfinding-grid-astar\]\].
- Pathfinding ✅ DONE (2026-06-14): grid A\* over the physics world (`Pathfinder.cs`, static, no package/scene-object) + `PathAgent.cs` (attachable mover, the "smart movement" component) for every moving unit. `PlayerController2D` delegates to a sibling `PathAgent` (auto-added) so `PlayerCommander` is unchanged; `MonsterAI` is now a pure brain issuing `PathAgent` orders. **Wall rule:** a non-trigger collider blocks unless its Rigidbody2D is *Dynamic* (so painted Tilemap/Composite obstacle colliders — backed by a *Static* body — block correctly; the early `attachedRigidbody==null` check was the "mobs stuck at the tree" bug, now fixed). Open ground = straight line (A\* only when blocked). **Units don't hard-block A**\* (you must be able to reach a chased/attacked unit); PathAgent instead does **soft local avoidance** (`Separation` + `avoidIgnore` = current target) so units flow around each other Dota-style instead of stacking/shoving. Existing scene player auto-gains routing on Play; re-run **Setup Mouse Combat** (or rebuild monster prefabs) to make the component Inspector-visible. **⚠️ Critical fix:** `PathAgent.Awake` sets `rb.sleepMode = NeverSleep` — a Dynamic body moved only by `MovePosition` falls asleep and then IGNORES `MovePosition`, which froze every unit mid-route (the whole "mobs stuck" saga; normal-looking body state). Validated working: monsters move/chase/route around obstacles. Remaining polish only: crowd-jam when mobs mob the player (avoidance under-spreads).