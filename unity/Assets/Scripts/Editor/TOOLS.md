# `Tools ▸ unwritten` menu — reference

Every one-click editor tool the project ships, grouped by its submenu. This is the canonical map of the
`Tools ▸ unwritten` menu (the project's setup/authoring hub). Prefer extending this menu over hand-steps
(see the `editor-tools-menu` memory).

**Conventions used below**
- **What** — what the tool does. **Use when** — when you'd reach for it.
- *Idempotent* = safe to re-run; it updates in place instead of duplicating.
- *Run in a map scene* / *Systems scene* notes which scene must be open.
- Each tool lives in `Assets/Scripts/Editor/<File>.cs` (named per entry).

---

## Top level

| Menu | What | Use when |
|---|---|---|
| **Build Game (Windows .exe)** | `npm run build` for the game. Collects enabled Build-Settings scenes (forces `Systems.unity` first), builds a StandaloneWindows64 player into `Builds/Windows/`, opens the folder. | You want a shippable `.exe` without opening the Build Profiles window. (`BuildGame.cs`) |
| **Create Character Sandbox Scene** | Builds a standalone `CharacterSandbox.unity` (checkered ground + light + camera + `CharacterSelectSandbox` wired to the baked roster). | You want to pick/walk any baked character in isolation, no real game involved. (`SandboxBuilder.cs`) |
| **Setup Hand-Drawn Grade** | Registers `HandDrawnGradeFeature` on every URP renderer in the project (as a sub-asset). *Idempotent.* | After importing/resetting URP renderers, so the DevSettings ▸ Hand-Drawn Saturation/Contrast knobs take effect. (`SetupHandDrawnGrade.cs`) |
| **Bake Minimap (active map)** | Snapshots the active map from straight above into `Assets/Art/Minimaps/<Scene>.png` and writes `MinimapData` onto the scene, so the HUD minimap is a static chart (no live 2nd camera). | After a map's layout changes. (`MinimapBaker.cs`) |

---

## Scene Setup

| Menu | What | Use when |
|---|---|---|
| **Setup Systems Scene** | Builds the persistent `Systems.unity` (Sun + billboard Player + follow Camera + Character + HUD + GameSystems + GameRoot), makes it Build index 0 + the Play-start scene. Maps then load additively under it. *Re-runnable.* | First-time setup, or after changing the player rig / global systems. (`SystemsSceneBuilder.cs`) |
| **Build UI Shell** | Rebuilds the in-scene UI shell (HUD + Bag/Character/Skills panels) skinned with the last-chosen Ninja Adventure theme; saves `Resources/GameUICanvas.prefab`. Also seeds starter items. | After changing UI, icons, or theme. (`UIShellBuilder.cs`) |
| **UI Shell Theme…** | Window: pick a UI theme + frame thickness with a live preview, then build. Choice is saved so plain *Build UI Shell* reuses it. | You want to change the HUD skin. (`UIShellWindow.cs`) |
| **Add Camera Occlusion Fade** | Drops a `CameraOccluderFader` onto the existing follow camera (props between camera & player fade out) without rebuilding Systems. | Retrofitting occlusion fade onto an older Systems scene. (`OcclusionFadeSetup.cs`) |
| **Create Starter Items** | Generates a small set of `Item` assets in `Resources/Items` (weapons/consumables/armour) so the bag has draggable content. *Idempotent.* | Seeding the inventory; also called automatically by Build UI Shell. (`ItemStarterSet.cs`) |

---

## World (map building & warps)

| Menu | What | Use when |
|---|---|---|
| **World Creator…** | Window: digests a `world.json` (from the browser grid tool) → in one *Build World* bakes every map from its template `MapRecipe` and wires all paired Portal+Spawn warps + the world-map atlas. ⚠ Overwrites those scene files. | Generating/regenerating a whole multi-map world topology. (`WorldCreator.cs`) |
| **Import Painted Map…** | Phase 3 of the Map Painter pipeline: bakes a hand-painted `map.json` (from `tools/map-painter/`) into a scene — exact roads, houses, and yaw. The intra-map complement to the recipe scatter. | You hand-painted a precise map layout and want it in Unity. (`MapPainterImporter.cs`) |
| **Brush Palette…** | Window: edits the `BrushPalette` (brush→prefab/colour), auto-fills it from `Prefabs/Props` by naming convention, and exports `prop-manifest.json` (with measured footprints) into `tools/map-painter/`. | Maintenance touch-point for the Map Painter pipeline; after adding new prop prefabs. (`BrushPaletteWindow.cs`) |
| **Apply Foliage Wind to Open Scene** | Re-materials plant props in the OPEN scene onto the `Unwritten/FoliageWind` shader (decor sways whole; trees sway leaves only). *Idempotent.* | Making foliage sway in a hand-authored map without re-baking it. (`RecipeGenerator.cs`) |
| **Fill Ground Under Map** | Measures everything placed in the active scene and carpets a `ground_grass` tile rectangle (+ one flat Ground-layer collider) under it. *Re-runnable.* | After hand-placing props, to lay the walkable floor in one click. (`GroundFill.cs`) |
| **Create Spawn Point** | Drops a bare `SpawnPoint` GameObject (set its Id; match it from a Portal's Spawn Id). | Hand-wiring an arrival point. (`WorldTools.cs`) |
| **Fit Portal Triggers (active scene)** | Fixes/seats portal trigger volumes in the active scene (raycast-snaps the portal pivot to ground first). | After hand-dragging portals (which can drift onto prop colliders). (`RegionBuilder.cs`) |
| **Add Ambient Townsfolk** | Drops an `AmbientLife` director into the open scene, wires it to the `CharacterRoster` + Ground mask → wandering, chattering townsfolk at Play. *Idempotent.* | Making a map feel alive. Run in a map scene. (`AmbientLifeTools.cs`) |

---

## World UI (global, persistent)

| Menu | What | Use when |
|---|---|---|
| **Build Dialogue UI** | Builds the global screen-space dialogue box → `Resources/DialogueCanvas.prefab`. Systems pulls it in. | (Re)building the global dialogue box. (`WorldTools.cs`) |
| **Build Interact Prompt** | Builds the global "press to interact" badge → `Resources/InteractPromptCanvas.prefab`. | (Re)building the interact prompt. (`WorldTools.cs`) |

> After either, run **Setup Systems Scene** to pull the prefab into the persistent layer.

---

## UI

| Menu | What | Use when |
|---|---|---|
| **Build Progression Windows** | Ensures starter Items + Cards exist, then drops a `ProgressionWindows` canvas (Inventory/Equipment/Character, toggled I/E/C) into the open scene, reading the live `Character`. | Testing the inventory/equip/character window trio. (`ProgressionWindowsBuilder.cs`) |

---

## Mobs & NPCs

| Menu | What | Use when |
|---|---|---|
| **Spawn Training Dummy** | Spawns an inert, click-targetable mob (10M HP, no aggro/movement, full-HP reset every 5s) so you can read your damage/DPS off its HP label. *Re-runnable.* | Measuring damage/DPS. (`TrainingDummyTool.cs`) |
| **Clear Training Dummy** | Removes the training dummy. | Cleanup after a damage test. (`TrainingDummyTool.cs`) |
| **Spawn Field Boss (open field map)** | Hand-places a unique `MobRoster.Boss` (name+HP banner, wide aggro, 120s respawn) into the open field map (refuses to spawn in Town). | Placing a boss encounter. Run in a field/dungeon scene. (`RegionBuilder.cs`) |
| **Create NPC from selected SpriteSheet** | Builds a simple billboard NPC with an `Interactable` (editable dialogue) from a selected sliced character sheet. | Quick-dropping a talking NPC. (`WorldTools.cs`) |

---

## Authoring (hand-placement helpers)

The keyboard-friendly companions to the Prop Palette. All bounds-aware (via `PropPlacement`) and Undo-able.

| Menu | What | Use when |
|---|---|---|
| **Prop Palette** | Figma/Storybook-style window: pick a prop from a thumbnail grid, click in the Scene view to place it — auto-scaled, ground-snapped, optionally grid-snapped, Undo-able. Q/E rotate, Esc/right-click stops. | The main hand-authoring tool for placing props. (`PropPalette.cs`) |
| **Reset 2.5D Authoring View** | Resets the Scene-view camera to a known-good high 3/4 top-down angle. | When scene navigation flips you under the ground. (`AuthoringViewTool.cs`) |
| **Drop to Ground** | Re-seats the current selection on the ground by its bounds. | After nudging placed props off the floor. (`PlacementTools.cs`) |
| **Drop Selected to Ground** | Same intent for freshly dragged-in objects (snaps to the Ground layer). | After dragging houses/props from the Project window. (`RegionBuilder.cs`) |
| **Scale ×0.3 (pack scale)** | Sets the selection's local scale to the pack default (0.3). | After dragging in raw pack prefabs. (`PlacementTools.cs`) |
| **Snap to Grid (0.5)** | Snaps the selection's position to a 0.5 grid. | Tidying placement. (`PlacementTools.cs`) |
| **Rotate 90°** | Rotates the selection 90° about Y. | Facing a prop a different way. (`PlacementTools.cs`) |
| **Remove Colliders (selected)** | Strips colliders off the selected props (walk through them). | Making specific props non-blocking. (`PlacementTools.cs`) |
| **Strip Decor Colliders (grass, flowers…)** | Scene-wide: strips colliders from all walk-through decor by name (grass/flowers/ferns/mushrooms…), never the floor. | One-shot cleanup so decor doesn't block the player. (`PlacementTools.cs`) |

---

## Prefabs & Art (asset → prefab pipelines)

| Menu | What | Use when |
|---|---|---|
| **Build From Folder…** | Bakes a Kenney-style kit (one FBX per prop, shared colormap atlas) into drop-ready prefabs (material + MeshCollider). Skips characters. *Re-runnable.* | Importing a new Kenney/FBX prop kit. (`KenneyPrefabBaker.cs`) |
| **Build Fantasy Assets (Pxltgr) → Props** | One-click: slices the Pxltgr Fantasy Assets multi-object FBX into per-prop prefabs in `Prefabs/Props` (preserves each child's own material; foliage left collider-free). | (Re)building the Pxltgr prop set. (`FbxPropExtractor.cs`) |
| **Split Multi-Object FBX…** | Asset-agnostic version of the above: pick any single FBX bundling many objects → one prefab per top-level child. | Slicing a different multi-object pack. (`FbxPropExtractor.cs`) |
| **Fix Pxltgr Materials (URP + leaf alpha)** | Re-points extracted Pxltgr materials to URP/Lit, re-binds Base Maps by name, enables alpha-clip on leaves (fixes magenta + gray leaf quads). *Idempotent.* | After an FBX *Extract Materials* / re-import turns props magenta. (`PxltgrMaterialFixer.cs`) |
| **Light Up Props (night)** | Adds a `NightLight` to glow-at-night props (houses, candlesticks, tavern lamp) by editing the prefab assets, so all instances inherit it. *Re-runnable.* | Making a town light up after dark off the `TimeOfDay` clock. (`LightUpProps.cs`) |

---

## Art (character/sprite baking)

| Menu | What | Use when |
|---|---|---|
| **Bake 8-Dir Sprites** | Window for a one-off 8-direction sprite bake from a rigged model + clips (offscreen ortho render → billboard strips), with an in-window rotate/play preview. | Baking a single character/model to billboard sprites. (`SpriteBaker8.cs`) |
| **Bake All Quaternius Characters** | Batch baker for a whole folder of rigged characters with embedded anims (Quaternius pack) → 8-dir strips + `BakedBillboard8` prefabs + a `CharacterRoster`. Supports a "first N" trial run. | (Re)baking the whole character roster. (`BatchCharacterBaker.cs`) |

---

## Player

| Menu | What | Use when |
|---|---|---|
| **Use Baked Character** | Window: swap the real player's visuals to any baked roster character (via `BakedPlayerVisual`) without touching movement/combat. Fully reversible. Run with Systems open. | Playing as a different baked character in the real game. (`UseBakedCharacter.cs`) |

---

## Weather

| Menu | What | Use when |
|---|---|---|
| **Generate Sequence Assets from Code** | Turns the hard-coded `WeatherSequences` timelines into editable `WeatherSequenceAsset`s + a `WeatherSequenceLibrary` in Resources (exact copies, nothing changes visually). *Idempotent, keeper.* | Moving weather timelines into the Inspector to tune live. (`WeatherSequenceAssetBaker.cs`) |

---

## Save

| Menu | What | Use when |
|---|---|---|
| **Delete Save File (new game)** | Deletes the character save → next Play is a fresh level-1 start. | Testing the new-game / first-boot flow. (`SaveTools.cs`) |
| **Open Save Folder** | Reveals the save JSON in the OS file browser. | Inspecting the save on disk. (`SaveTools.cs`) |
| **Print Save Path** | Logs the absolute save path + whether it exists. | Quick check of where the save lives. (`SaveTools.cs`) |

---

## Debug

| Menu | What | Use when |
|---|---|---|
| **Print Skill Ring + Soft-Cap Table** | Logs the Trio-Ring layout + the soft-cap percentage curve — pure logic, no Play mode. | Sanity-checking the skill maths. (`SkillSelfTest.cs`) |
| **Simulate Skill Draft Walk (20 levels)** | Simulates a 20-level draft walk through the Trio-Ring/DraftService and logs it. | Verifying draft steering rules without Play. (`SkillSelfTest.cs`) |

---

## Notes & related

- **Map generation** proper is driven by **`MapRecipe`** assets (Create ▸ unwritten ▸ Map Recipe) with a
  **Generate** button on the asset's inspector — that's `RecipeGenerator`, not a menu item. The menu's
  *World Creator…* / *Import Painted Map…* sit on top of it for whole-world / hand-painted flows.
- **Setup order for a fresh project:** Build From Folder / Build Fantasy Assets (prefabs) → Bake characters →
  Setup Systems Scene → Build UI Shell → generate maps (recipe / World Creator) → Bake Minimap.
- **Removed 2026-06-25** (cleanup): *Set Melee Attack Range* and *Remove HUD Widgets Demo* (one-shot
  migrations, done), plus *Strip Missing Scripts* and *Reset Saved Class*.
- One-off "quick fix" tools are tagged `// QUICKFIX` and parked under a **Quick Fixes/** submenu so a future
  purge is a grep (see `Scripts/CLAUDE.md`).
