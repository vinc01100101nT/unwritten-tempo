# SESSION_STATE — unwritten (Unity 2D)

---
## ⏩ LATEST — DIRECTION LOCKED: Kenney 3D maps + billboard characters (2026-06-17) — read first
Active branch **`experiment/3d-world`**. After spiking it, we **rejected the "2D sprite-props in a 3D
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