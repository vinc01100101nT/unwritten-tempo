# `Tools ▸ unwritten` menu — reference

Every one-click editor tool the project ships, grouped by its submenu. This is the canonical map of the `Tools ▸ unwritten` menu (the project's setup/authoring hub). Prefer extending this menu over hand-steps (see the `editor-tools-menu` memory).

**Conventions used below**

- **What** — what the tool does. **Use when** — when you'd reach for it.
- *Idempotent* = safe to re-run; it updates in place instead of duplicating.
- *Run in a map scene* / *Systems scene* notes which scene must be open.
- Each tool lives in `Assets/Scripts/Editor/<File>.cs` (named per entry).

---

## Top level

| Menu | What | Use when |
| --- | --- | --- |
| **Build Game (Windows .exe)** | `npm run build` for the game. Collects enabled Build-Settings scenes (forces `Systems.unity` first), builds a StandaloneWindows64 player into `Builds/Windows/`, opens the folder. | You want a shippable `.exe` without opening the Build Profiles window. (`BuildGame.cs`) |
| **Create Character Sandbox Scene** | Builds a standalone `CharacterSandbox.unity` (checkered ground + light + camera + `CharacterSelectSandbox` wired to the baked roster). | You want to pick/walk any baked character in isolation, no real game involved. (`SandboxBuilder.cs`) |
| **Setup Hand-Drawn Grade** | Registers `HandDrawnGradeFeature` on every URP renderer in the project (as a sub-asset). *Idempotent.* | After importing/resetting URP renderers, so the DevSettings ▸ Hand-Drawn Saturation/Contrast knobs take effect. (`SetupHandDrawnGrade.cs`) |
| **Bake Minimap (active map)** | Snapshots the active map from straight above into `Assets/Art/Minimaps/<Scene>.png` and writes `MinimapData` onto the scene, so the HUD minimap is a static chart (no live 2nd camera). | After a map's layout changes. (`MinimapBaker.cs`) |

---

## Scene Setup

| Menu | What | Use when |
| --- | --- | --- |
| **Setup Systems Scene** | Builds the persistent `Systems.unity` (Sun + billboard Player + follow Camera + Character + HUD + GameSystems + GameRoot), makes it Build index 0 + the Play-start scene. Maps then load additively under it. *Re-runnable.* | First-time setup, or after changing the player rig / global systems. (`SystemsSceneBuilder.cs`) |
| **Build UI Shell (Systems scene)** | Rebuilds the in-scene UI shell (HUD + Bag/Character/Skills panels) skinned with the last-chosen Ninja Adventure theme; saves `Resources/GameUICanvas.prefab`. Also seeds starter items. Run with **Systems.unity open** so the canvas is replaced in place (not stranded in a map scene). | After changing UI, icons, or theme. (`UIShellBuilder.cs`) |
| **UI Shell Theme… (Systems scene)** | Window: pick a UI theme + frame thickness with a live preview, then build. Choice is saved so plain *Build UI Shell* reuses it. Build with **Systems.unity open**. | You want to change the HUD skin. (`UIShellWindow.cs`) |
| **Add Camera Occlusion Fade (Systems scene)** | Drops a `CameraOccluderFader` onto the existing follow camera (props between camera & player fade out) without rebuilding Systems. Needs **Systems.unity open** (the follow camera lives there). | Retrofitting occlusion fade onto an older Systems scene. (`OcclusionFadeSetup.cs`) |
| **Create Starter Items** | Generates a small set of `Item` assets in `Resources/Items` (weapons/consumables/armour) so the bag has draggable content. *Idempotent.* | Seeding the inventory; also called automatically by Build UI Shell. (`ItemStarterSet.cs`) |

---

## World (map building & warps)

| Menu | What | Use when |
| --- | --- | --- |
| **World Creator…** | Window: digests a `world.json` (from the browser grid tool) → in one *Build World* bakes every map from its template `MapRecipe` and wires all paired Portal+Spawn warps + the world-map atlas. ⚠ Overwrites those scene files. | Generating/regenerating a whole multi-map world topology. (`WorldCreator.cs`) |
| **Import Painted Map…** | Phase 3 of the Map Painter pipeline: bakes a hand-painted `map.json` (from `tools/map-painter/`) into a scene — exact roads, houses, and yaw. The intra-map complement to the recipe scatter. | You hand-painted a precise map layout and want it in Unity. (`MapPainterImporter.cs`) |
| **Create Biome Starter Recipes** | Stamps one starter `MapRecipe` per `Biome` (Grassland/Snow/Desert/Volcanic/Corrupted) into `Assets/Recipes/Biomes/` — biome ground textures+hue + sensible starter props/mobs. *Create-if-absent (won't clobber edits).* | Seeding themed recipes to wire world-map cells to. (`BiomeRecipes.cs`) |
| **Brush Palette…** | Window: edits the `BrushPalette` (brush→prefab/colour), auto-fills it from `Prefabs/Props` by naming convention, and exports `prop-manifest.json` (with measured footprints) into `tools/map-painter/`. | Maintenance touch-point for the Map Painter pipeline; after adding new prop prefabs. (`BrushPaletteWindow.cs`) |
| **Apply Foliage Wind to Open Scene** | Re-materials plant props in the OPEN scene onto the `Unwritten/FoliageWind` shader (decor sways whole; trees sway leaves only). *Idempotent.* | Making foliage sway in a hand-authored map without re-baking it. (`RecipeGenerator.cs`) |
| **Fill Ground Under Map** | Measures everything placed in the active scene and carpets a `ground_grass` tile rectangle (+ one flat Ground-layer collider) under it. *Re-runnable.* | After hand-placing props, to lay the walkable floor in one click. (`GroundFill.cs`) |
| **Create Spawn Point** | Drops a bare `SpawnPoint` GameObject (set its Id; match it from a Portal's Spawn Id). | Hand-wiring an arrival point. (`WorldTools.cs`) |
| **Fit Portal Triggers (active scene)** | Fixes/seats portal trigger volumes in the active scene (raycast-snaps the portal pivot to ground first). | After hand-dragging portals (which can drift onto prop colliders). (`RegionBuilder.cs`) |
| **Add Ambient Townsfolk** | Drops an `AmbientLife` director into the open scene, wires it to the `CharacterRoster` + Ground mask → wandering, chattering townsfolk at Play. *Idempotent.* | Making a map feel alive. Run in a map scene. (`AmbientLifeTools.cs`) |
| **Add Prop Light to Selection** | Adds a warm `PropLight` (real-time point light) as a child of each selected prop, so nearby billboard sprites brighten & take its colour. *Idempotent.* | Lighting a door/torch/portal so the player glows beside it. Retint/flicker on the new child. (`PropLightTools.cs`) |
| **Decorate Selected (Foliage)** | Rule-based, hands-off foliage scatter on the selected prop: raycasts its surface and seats Pxltgr decor (Flower\*/Grass\*/Fern\*/Bush\*/Mushroom\*) as a wall-hugging garden ring + a few wall-mounted accents, seeded per object name, under an `AutoDecor` child (colliders stripped, walk-through). *Idempotent — rebuilds* `AutoDecor`*.* | Auto-decorating a bare Hunyuan house (no hand-placing) — the detail stripped out of the gen to keep it clean. (`MeshDecorator.cs`) |

---

## World UI (global, persistent)

| Menu | What | Use when |
| --- | --- | --- |
| **Build Dialogue UI** | Builds the global screen-space dialogue box → `Resources/DialogueCanvas.prefab`. Systems pulls it in. | (Re)building the global dialogue box. (`WorldTools.cs`) |
| **Build Interact Prompt** | Builds the global "press to interact" badge → `Resources/InteractPromptCanvas.prefab`. | (Re)building the interact prompt. (`WorldTools.cs`) |

> After either, run **Setup Systems Scene** to pull the prefab into the persistent layer.

---

## UI

| Menu | What | Use when |
| --- | --- | --- |
| **Build Progression Windows** | Ensures starter Items + Cards exist, then drops a `ProgressionWindows` canvas (Inventory/Equipment/Character, toggled I/E/C) into the open scene, reading the live `Character`. | Testing the inventory/equip/character window trio. (`ProgressionWindowsBuilder.cs`) |

---

## Mobs & NPCs

| Menu | What | Use when |
| --- | --- | --- |
| **Import NinjaAdventure Monsters** | Bulk-creates a `MonsterDef` asset in `Resources/Monsters` for every sprite folder under `Actor/Monster` (mobs) + `Actor/Boss` (bosses), pre-tagged with a `Biome` + difficulty `tier 1–4` (mobs: 3 per biome×tier cell + the remainder as biome-agnostic commons; bosses: 1 per cell, `isBoss`). Idempotent — skips assets that already exist. *Re-runnable.* | Standing up / refreshing the biome-grouped mob roster. (`MobBiomeImporter.cs`) |
| **Spawn Training Dummy** | Spawns an inert, click-targetable mob (10M HP, no aggro/movement, full-HP reset every 5s) so you can read your damage/DPS off its HP label. *Re-runnable.* | Measuring damage/DPS. (`TrainingDummyTool.cs`) |
| **Clear Training Dummy** | Removes the training dummy. | Cleanup after a damage test. (`TrainingDummyTool.cs`) |
| **Spawn Field Boss (open field map)** | Hand-places the unique boss flagged in the `MonsterCatalog` (the `isBoss` `MonsterDef`; falls back to `MobRoster.Boss` if no assets) — name+HP banner, wide aggro, 120s respawn — into the open field map (refuses to spawn in Town). | Placing a boss encounter. Run in a field/dungeon scene. (`RegionBuilder.cs`) |
| **Create NPC from selected SpriteSheet** | Builds a simple billboard NPC with an `Interactable` (editable dialogue) from a selected sliced character sheet. | Quick-dropping a talking NPC. (`WorldTools.cs`) |

---

## Data (table views)

| Menu | What | Use when |
| --- | --- | --- |
| **Mob Table** | Spreadsheet window of every `MonsterDef` in the project — one mob per row with a live front-facing thumbnail (top-left cell of its `sheet`, tinted), and the balance stats (id/name/biome/tier/flags/HP/DEF/MDEF/ATK/HIT/FLEE/element/weight/XP/gold) editable inline. Click a column header to sort, the search box to filter, **◎** to select the asset; **Save All** writes + reloads the `MonsterCatalog`. Edits are Undo-able. | Reviewing or balancing the whole mob roster in one view. (`MobTableWindow.cs`) |

---

## Authoring (hand-placement helpers)

The keyboard-friendly companions to the Prop Palette. All bounds-aware (via `PropPlacement`) and Undo-able.

| Menu | What | Use when |
| --- | --- | --- |
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
| --- | --- | --- |
| **Build From Folder…** | Bakes an FBX prop kit (one FBX per prop, shared colormap atlas) into drop-ready prefabs (material + MeshCollider). Skips characters. *Re-runnable.* | Importing a new FBX prop kit. (`FbxKitPrefabBaker.cs`) |
| **Import GLB Prop…** | Bakes selected `.glb` model asset(s) into drop-ready prefabs in `Prefabs/Props`: forces materials to URP/Lit (keeps the GLB's baked texture), adds a MeshCollider per mesh, normalises scale to a sensible footprint, and re-pivots to bottom-centre for ground-snap. Overwrites in place. | Dropping any single self-textured GLB prop into the world (vs the FBX-kit bakers). (`GlbPropImporter.cs`) |
| **Decimate Selection…** | Author-time mesh decimation with a measured break-point loop: select meshes, set a **target tri count**, **Preview** swaps the live mesh (non-destructive) to judge it at the iso camera, **Apply** bakes a saved low-poly `.mesh` asset, **Revert** restores. Quadric-error (UnityMeshSimplifier) with border/UV-seam preservation; solves toward the target across correction passes. Bake, not runtime. Sweet spot = B/C/D props + finding break-points; hero S-tier still wants hand retopo. | Trimming dense TRELLIS/Hunyuan meshes to budget, or finding the real tri break-point. (`MeshDecimateWindow.cs`) |
| **Build Fantasy Assets (Pxltgr) → Props** | One-click: slices the Pxltgr Fantasy Assets multi-object FBX into per-prop prefabs in `Prefabs/Props` (preserves each child's own material; foliage left collider-free). | (Re)building the Pxltgr prop set. (`FbxPropExtractor.cs`) |
| **Split Multi-Object FBX…** | Asset-agnostic version of the above: pick any single FBX bundling many objects → one prefab per top-level child. | Slicing a different multi-object pack. (`FbxPropExtractor.cs`) |
| **Fix Pxltgr Materials (URP + leaf alpha)** | Re-points extracted Pxltgr materials to URP/Lit, re-binds Base Maps by name, enables alpha-clip on leaves (fixes magenta + gray leaf quads). *Idempotent.* | After an FBX *Extract Materials* / re-import turns props magenta. (`PxltgrMaterialFixer.cs`) |
| **Light Up Props (night)** | Adds a `NightLight` to glow-at-night props (houses, candlesticks, tavern lamp) by editing the prefab assets, so all instances inherit it. *Re-runnable.* | Making a town light up after dark off the `TimeOfDay` clock. (`LightUpProps.cs`) |

---

## Art (character/sprite baking)

| Menu | What | Use when |
| --- | --- | --- |
| **Bake 8-Dir Sprites** | Window for a one-off 8-direction sprite bake from a rigged model + clips (offscreen ortho render → billboard strips), with an in-window rotate/play preview. | Baking a single character/model to billboard sprites. (`SpriteBaker8.cs`) |
| **Bake All Quaternius Characters** | Batch baker for a whole folder of rigged characters with embedded anims (Quaternius pack) → 8-dir strips + `BakedBillboard8` prefabs + a `CharacterRoster`. Supports a "first N" trial run. | (Re)baking the whole character roster. (`BatchCharacterBaker.cs`) |
| **Import Item Icons** | Scans `Assets/Art/Items` for PNGs named by item id (`item.iron-sword.png`), applies icon import settings (Sprite/Single, point, uncompressed, alpha), and assigns each to the matching `Item` asset's icon (match: id → kebab(name) → asset name). Idempotent; reports unmatched files + items lacking a stable id. | After dropping a SINGLE-icon-per-PNG into `Assets/Art/Items` (hero/legendary quality). (`ItemIconImporter.cs`) |
| **Import Item Sheets** | Bulk path: grid-slices a sheet PNG in `Assets/Art/Items/Sheets` into per-cell sub-sprites and assigns each to an `Item` by a row-major id list. Grid size + ids live in a same-named `.txt` manifest (`cols:`/`rows:` lines + ids; `-` skips a cell), so grid size is a one-line text edit per sheet. Idempotent; reports unmatched ids + cell/id mismatches. | After dropping a GPT grid sheet + its manifest (many icons in one image). (`ItemSheetImporter.cs`) |
| **Rebuild Effect Library** | Scans the effect-prefab folders (`Magic effects pack/Prefabs` nested-by-category, `Procedural fire/Prefabs` flat) and (re)builds `Resources/EffectLibrary.asset` — the id→prefab table `EffectCatalog`/`Vfx` resolve against. Each prefab → an `EffectDef` id'd by category prefix + kebab name (`Hits and explosions/Explosion` → `hit.explosion`; the fire pack is flat → `fire.*`). Add a pack via the `Roots` array (fixed prefix = flat pack, null = per-category). Idempotent + non-destructive: existing entries (matched by prefab) keep their id + hand-tuned scale/lifetime; only adds new prefabs, reports ones whose prefab was deleted. | After importing/adding effect prefabs, so gameplay can spawn them by id via `Vfx.PlayAt`/`Vfx.Attach`. (`EffectLibraryBuilder.cs`) |
| **Vfx Composer** | Edit-mode authoring workspace for composed effects (`VfxSequence` assets) — the single window that REPLACES the in-Play F8 Effect Tuner + F9 Sequence Tuner. Left = list of all compositions (New/Dup/Del); right = timeline of effects (each with its own scale / xyz offset / speed / start-time or event) over a live `ParticleSystem.Simulate` preview driven by an editor clock (no Play). A human-height capsule + ground grid = true-world-scale ruler. Owns PRESENTATION only: each spawning step can expose a named **tick id** (+ count/interval) that the Skill Composer binds gameplay onto — NO damage/element/multiplier here. | Authoring/tuning any composed effect (skill, environment, event) against real character scale + naming its gameplay ticks. (`VfxComposerWindow.cs`) |
| **Build Default Vfx Sequences** | (Re)builds the built-in `VfxSequence` assets under `Resources/VfxSequences/` — the DATA recipes that compose a skill's look from library effects + shake + sound on a flat timeline (played by `VfxDirector`). Builds `meteor` (carrier fall → "impact" → ground explosion + shake) and exposes its ticks (`meteor_ball` ×1, `fire_pillar` ×5). Idempotent — rebuilds the default step list (**hand-tuning is overwritten**). | After adding a new default sequence, or to regenerate `meteor` from code defaults. (`VfxSequenceTools.cs`) |
| **Vfx Cue Composer** | Edit-mode table that binds each gameplay EVENT (`VfxCueId`: OnAttack/OnHit/OnCrit/OnHeal/OnLevelUp/OnDeath, DoT Start+Tick for Poison/Bleed/Burn, and Stun/Freeze/Slow/Haste/Silence/Blind) to a composed `VfxSequence` + anchor (Self/Target/Midpoint) + scale/offset — the reverse of a skill (gameplay→VFX, no ticks). Writes `Resources/VfxCueLibrary.asset` (auto-created) and reloads `VfxCueCatalog` on save. Fired at runtime via `VfxCue.Fire`/`Begin` from `StatusController`/`Character`/`Player3DAttacker`. `*Start` cues run as a persistent aura for the ailment's duration (author as an Attach/looping sequence); everything else is one-shot. | Binding a status/verb event to a look after authoring it in the Vfx Composer. (`VfxCueComposerWindow.cs`) |

---

## Skills (data-driven skills)

| Menu | What | Use when |
| --- | --- | --- |
| **Skill Composer** | Edit-mode authoring workspace for `SkillDef` assets (`Resources/Skills/`) — where you COMPOSE SKILLS: pick a composed `VfxSequence`, and the window lists its exposed **ticks** live; bind each tick to gameplay (damage/heal/status, element, × multiplier, radius, targets, per-level growth) + identity (icon, mana, cooldown, max level, aim). Owns WHAT each tick does; the final numbers stay in code (`SkillEffectApplier` rolls them off the live caster). | Authoring/tuning a data-driven skill's gameplay against a composed VFX. (`SkillComposerWindow.cs`) |
| **Build Default Skills** | (Re)builds the built-in `SkillDef` assets under `Resources/Skills/` — the PROOF of the pipeline. Builds `skill.meteor` (references the `meteor` VfxSequence; binds `meteor_ball` → Fire ×1.5 AoE and `fire_pillar` → Fire ×0.4 ×5). Idempotent. | To (re)generate the default data skills from code. (`SkillDefTools.cs`) |
| **Attach Data Skills To Player (Systems scene)** | Adds a `SkillRunner` per catalog skill onto the player (so `IHotbarSkill` shows them on the bar). Skips skills already attached. Needs **Systems.unity open** (the player lives there). Mostly redundant now — `Player3DController` auto-attaches composed skills on Awake. | Force-attaching data skills without pressing Play. (`SkillDefTools.cs`) |

---

## Player

| Menu | What | Use when |
| --- | --- | --- |
| **Use Baked Character (Systems scene)** | Window: swap the real player's visuals to any baked roster character (via `BakedPlayerVisual`) without touching movement/combat. Fully reversible. Needs **Systems.unity open** (targets the live player). | Playing as a different baked character in the real game. (`UseBakedCharacter.cs`) |

---

## Weather

| Menu | What | Use when |
| --- | --- | --- |
| **Generate Sequence Assets from Code** | Turns the hard-coded `WeatherSequences` timelines into editable `WeatherSequenceAsset`s + a `WeatherSequenceLibrary` in Resources (exact copies, nothing changes visually). *Idempotent, keeper.* | Moving weather timelines into the Inspector to tune live. (`WeatherSequenceAssetBaker.cs`) |

---

## Audio

| Menu | What | Use when |
| --- | --- | --- |
| **Map BGM Table** | Assign a `BgmTrack` to each map (scene→track) in the `Resources/MapBgmTable` asset; `MusicDirector` reads it on map load. No re-generate needed. *Keeper.* | Attaching / changing per-map background music. (`MapBgmTableWindow.cs`) |

---

## Save

| Menu | What | Use when |
| --- | --- | --- |
| **Delete Save File (new game)** | Deletes the character save → next Play is a fresh level-1 start. | Testing the new-game / first-boot flow. (`SaveTools.cs`) |
| **Open Save Folder** | Reveals the save JSON in the OS file browser. | Inspecting the save on disk. (`SaveTools.cs`) |
| **Print Save Path** | Logs the absolute save path + whether it exists. | Quick check of where the save lives. (`SaveTools.cs`) |

---

## Debug

| Menu | What | Use when |
| --- | --- | --- |
| **Print Skill Ring + Soft-Cap Table** | Logs the Trio-Ring layout + the soft-cap percentage curve — pure logic, no Play mode. | Sanity-checking the skill maths. (`SkillSelfTest.cs`) |
| **Simulate Skill Draft Walk (20 levels)** | Simulates a 20-level draft walk through the Trio-Ring/DraftService and logs it. | Verifying draft steering rules without Play. (`SkillSelfTest.cs`) |
| **Print Modifier Catalog + Eligibility** | Logs every authored skill modifier and whether it's legal on Bash (melee) vs Fireball (magic) — proving the per-skill tag filter. | Checking the modifier pool + that `requires`/`excludes` tags filter correctly per skill. (`SkillModSelfTest.cs`) |
| **Simulate Modifier Rolls — Bash + Fireball (lvl 1-&gt;20)** | Rolls both skills' six-phase loadouts (born with one + one per even level), logs each walk, asserts the support-only mod never lands and that the two pools differ. | Verifying the §12 random modifier cadence + tag exclusion without Play. (`SkillModSelfTest.cs`) |
| **Recompile Scripts** (`Ctrl/Cmd+Shift+R`) | Forces an asset refresh + script recompilation. | Pushing a C# edit into the Editor without alt-tabbing to focus Unity. (`RecompileTool.cs`) |
| **Verify Render Settings** | Checks critical, silently-revertible project/render settings (URP Additional Lights = Per Pixel, Additional Light Shadows on, …) against a known-good table; reports drift. *Data-driven — add an invariant as one row.* | A feature "mysteriously disappeared" (lamp shadows, etc.); sanity check before a session. (`RenderSettingsGuard.cs`) |
| **Verify & Repair Render Settings** | Same check, then writes the correct values back to disk and saves. *Commit the changed .asset afterward so it can't revert.* | Drift was found and you want it fixed + committed. (`RenderSettingsGuard.cs`) |

---

## Notes & related

- **Map generation** proper is driven by `MapRecipe` assets (Create ▸ unwritten ▸ Map Recipe) with a **Generate** button on the asset's inspector — that's `RecipeGenerator`, not a menu item. The menu's *World Creator…* / *Import Painted Map…* sit on top of it for whole-world / hand-painted flows.
- **Setup order for a fresh project:** Build From Folder / Build Fantasy Assets (prefabs) → Bake characters → Setup Systems Scene → Build UI Shell → generate maps (recipe / World Creator) → Bake Minimap.
- **Removed 2026-06-25** (cleanup): *Set Melee Attack Range* and *Remove HUD Widgets Demo* (one-shot migrations, done), plus *Strip Missing Scripts* and *Reset Saved Class*.
- **Mobs are data now:** the source of truth is `MonsterDef` assets in `Resources/Monsters` (Create ▸ unwritten ▸ Monster), surfaced via `MonsterCatalog` (lookup by stable `id` like `mob.slime`, or display name). Recipes/boss spawner read the catalog; the old hardcoded `MobRoster` array is now just the migration seed + a fallback.
- One-off "quick fix" tools are tagged `// QUICKFIX` and parked under a **Quick Fixes/** submenu so a future purge is a grep (see `Scripts/CLAUDE.md`). Current: **Migrate MobRoster to Assets** (materializes the seed list into `Resources/Monsters/*.asset`; one-shot, delete after `MobRoster` is removed — `MobRosterMigration.cs`). **Backfill Item + Map IDs** (gives pre-existing `Item`/`MapRecipe` assets that serialized before the `id`field existed a stable kebab id from their display name; dedupes + warns on collisions; one-shot, delete after every asset has an id — `IdBackfillTool.cs`). **Create Starter Dungeon Recipe** (scaffolds `Assets/DungeonRecipe.asset` — a `MapRecipe` with perimeter walls + scattered boulders + mid/elite biome mobs — for Build World's dungeon template slot; one-shot, delete after the recipe exists and is hand-tuned — `DungeonRecipeScaffold.cs`). **Reground Baked Mobs** (drops mobs baked BEFORE the feet-pivot convention onto the ground + stacks their capsule up from the feet; idempotent — skips mobs already migrated; run once per hand-finished map to adopt the convention without a full regenerate — `RegroundBakedMobs.cs`).