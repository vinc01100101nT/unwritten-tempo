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

**NEXT:** pick the Kenney-map authoring flow (A: hand-place Kenney prefabs in a clean 2.5D scene · B: a 3D
town generator · C: reskin an existing layout) and stand a real Kenney map under a 2.5D Systems boot.
Prior spike session doc: `q1755ftddqpyy9qm7e357hdpf188sajr`.
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