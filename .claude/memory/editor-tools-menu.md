---
name: editor-tools-menu
description: All one-click setup scripts live under the Tools ▸ unwritten menu — prefer these over manual editor steps
metadata: 
  node_type: memory
  type: project
  originSessionId: 7e46c3f0-a8b6-4991-952c-5c03f2890bdc
---

On **unwritten**, the workflow is built around **one-click editor scripts** under the **`Tools ▸ unwritten`** menu (source in **`unity/Assets/Scripts/Editor/`** inside this repo — see [[unity-2d-direction]]). The user is **new to Unity**, so prefer adding/extending a menu item over manual click-by-click steps (a standing rule in CLAUDE.md). New setup scripts get a `[MenuItem("Tools/unwritten/…")]` and should be **idempotent + undoable**.

**📒 ALWAYS DOCUMENT A NEW TOOL (rule set 2026-06-25):** whenever I add (or remove/rename) a `Tools ▸ unwritten` menu item, I MUST update the canonical reference doc **`unity/Assets/Scripts/Editor/TOOLS.md`** in the same change — add a row to the right submenu section with *What it does* / *Use when* / source file (and note idempotency + which scene must be open). That file is the full per-tool catalog (built 2026-06-25, ~39 live commands across 13 submenus). Keep it in sync; don't let it drift.

**⚠️ BIG MENU CLEANUP (2026-06-17):** committed to the artist-low-poly-nature 2.5D direction (see [[direction-2p5d-pivot]]), cut the menu ~40 → 16. **DELETED tools (don't expect them):** all 2D-tilemap authoring (DepthSortTools = Create Decor+Overhead Layers / Refresh Depth Sorting, FloorRuleTileBuilder, TilemapTools = Clear Tilemaps, GroundFillTool, MapBaker, BuildingBakeTools, MapBoundaryBuilder, NewMapSceneTool, PropTemplateTools/Editor, MapGeneratorWindow = Generate Town) and the Kenney-3D experiments (Town3DBaker = Generate 3D Town, TerrainSandbox, Stage25DBuilder, Build25DElevationSpike, HouseAssembler, KenneyPaletteRemap, PlayerBuilder, Terrain3DArt/Field). Also gone: `Scripts/MapGen/` runtime, `TerrainProjected.shader`, scenes NewTown/Town3D, art (Kenney kits, Tiles, Terrain3D, Palettes).

**Current menu surface (2026-06-17):**

*The nature world pipeline (NEW — the live path):*
- **Build Prefabs ▸ Low Poly Nature** (`KenneyPrefabBaker.cs`) — bakes the Just Create "Stylized Low Poly Nature" FBX (`Assets/Art/LowPolySimpleNature_FBX/`, single shared atlas) → drop-ready prefabs at `Assets/Prefabs/LowPolyNature/`. `BakeKit(name,dir,folder,colormapPath?)` is generic — add a sibling `[MenuItem]` line per new single-atlas pack from this artist. (File still named KenneyPrefabBaker; it's the generic baker now.)
- **New Nature Stage (low-poly ground)** (`NatureStageBuilder.cs`) — builds `Assets/Scenes/NatureStage.unity`: Ground_01 artist terrain (baked-in hills = free elevation) as the floor on the **Ground** layer + collider + height-aware `Pathfinder3DConfig`, plus the billboard rig (reuses `Build25DSpike` public helpers). Self-contained (owns `EnsureGroundLayer`).
- **Scatter Nature (fill map)** / **Clear Nature Scatter** (`NatureScatter.cs`) — area-based weighted scatter (trees/bushes/grass/flowers/rocks/mushrooms/stumps) raycast onto the terrain; re-runnable (reseeds). Colliders per category: trees/stumps = trunk capsule (walk under canopy), rocks = box, decor = none. Scatter sits on Default layer (NOT Ground) so the height-ray ignores it; obstacles = non-ground colliders (`Pathfinder3DConfig`). Tunables = consts at top of the file.
- **Populate Mobs (Nature)** / **Clear Mobs (Nature)** (`NatureMobs.cs`, 2026-06-17) — surface-snapped billboard mobs (`Build25DSpike.BuildMonster`) clear of spawn; combat loop already wired (`Player3DController`→`Player3DAttacker`→`Health`; `MonsterAI3D` chases via `Character`).
- **Reset 2.5D Authoring View** (`AuthoringViewTool`, `Ctrl/Cmd+Shift+V`) — frames the Scene view for 2.5D editing.

⭐ **2.5D SYSTEMS PORT (2026-06-17):** the player/camera/HUD/cursor now live in the PERSISTENT `Systems.unity`, maps are CONTENT-ONLY (terrain + `Pathfinder3DConfig` + a `SpawnPoint`). **Setup Systems Scene** is REWRITTEN to 2.5D (Sun + billboard player +`PersistentPlayer3D` + 3D follow cam + Character + **GameUICanvas** [HP bar + Bag(I)/Character(C)/Skills(K) panels] + `GameCursor3D` + `GameRoot`); **New Nature Stage** no longer builds a player/cam/HUD. Boot model: press Play → Systems boots (playModeStartScene) → loads the map under it → `PersistentPlayer3D` spawns the player at the SpawnPoint. Player tweaks happen ONCE in Systems — maps never rebuild the player. (`SystemsSceneBuilder` reuses GameRoot/MapManager/SceneTravel; the old 2D `SystemsSceneBuilder`/`PersistentPlayer` are superseded by the 2.5D ones.)

*Kept gameplay/systems tools (rendering-agnostic):*
- **Run All** (pinned, `RunAll.cs`) — safe no-selection chain: starter items → UI shell → ensure player mouse-combat.
- **Setup Global Systems** / **Setup Systems Scene** (`GlobalSystemsBuilder` / `SystemsSceneBuilder`) — the persistent `Systems.unity` layer (HUD/cursor/player/camera/data; maps load additively). **KEPT** — it's where the global UI/HUD lives and the future multiplayer/scene architecture. See [[persistent-systems-layer]].
- **Build UI Shell** (`UIShellBuilder`) — HUD/panels (Phase E).
- **Build Dialogue UI** / **Build Interact Prompt** / **Create NPC from selected SpriteSheet** / **Create Portal** / **Create Spawn Point** (`WorldTools`) — Phase C world-building.
- **Create Starter Items** (`ItemStarterSet`) — seed inventory items.

`MonsterBuilder.cs` no longer has menu items (its 2D mob-creation menus were deleted); it's now just the shared **`EnsurePlayerCombat`** + **`ConfigureCursor`** helpers that Run All / Setup Global Systems call. `Build25DSpike.cs` keeps all its public rig helpers (BuildPlayer/Camera/Light/Character/HUD/Cursor/Monsters, `BillboardScale`, `FootOffsetY`, `EnsureUniversalRenderer`) used by the nature builder — only its demo `[MenuItem]` was removed.

Builders set sane defaults in code so existing units pick up fixes at Play without a rebuild; rebuild/re-run only to surface new components in the Inspector.

**MENU RE-CATEGORIZED (2026-06-21):** the scattered top-level items are now grouped into submenus — only **Run All** stays pinned at top level (priority 1). Categories (priority bands set submenu order + separators): **Scene Setup** (100s: Setup Global Systems, Setup Systems Scene, Build UI Shell, Add Camera Occlusion Fade, Create Starter Items) · **World** (200s: New Nature Stage, Add Forest Ambience, Scatter/Clear Nature, Build Town Hub, Build Fantasy Town, Link Field→Town, Create Portal, Create Spawn Point) · **Mobs & NPCs** (300s: Populate/Clear Mobs, Spawn Field Boss, Create NPC) · **Authoring** (400s: Prop Palette, Reset 2.5D View, Drop to Ground, Drop Selected to Ground, Scale ×0.3, Snap to Grid, Rotate 90°, Remove/Strip Colliders) · **Prefabs & Art** (500s: Build Low Poly Nature, Build From Folder, Build Fantasy Assets, Split FBX, Fix Pxltgr Materials, Light Up Props) · **Player** (600s) · **World UI** (700s: Dialogue, Interact Prompt) · **Debug** (800s: ex-Skills self-tests) · **Save** (900s). Old submenus **Region/ Skills/ Setup/ Build Prefabs/** are GONE (folded into the above). When adding a new tool, drop it in the right category with a priority in that band; keep keyboard shortcuts (%#v/%#g/%#r) intact.

**QUICKFIX tag convention (2026-06-21):** throwaway/one-off editor tools (temporary repairs, migration helpers, spikes) get a `// QUICKFIX <date> — why; delete after <condition>.` line above the class AND live under a `Tools/unwritten/Quick Fixes/…` submenu, so "clean up the quick fixes" = grep `QUICKFIX` in `Assets/Scripts/Editor/` → delete file + `.meta` (check refs first). Recurring idempotent maintenance tools (e.g. `Fix Pxltgr Materials`) are NOT quick fixes. Rule documented in `unity/Assets/Scripts/CLAUDE.md`. Audit on this date found **zero** existing quick-fix tools to remove; `Build25DSpike` is self-labelled "throwaway" but is actually load-bearing shared infra (~15 callers) — its misleading doc comment was corrected.
