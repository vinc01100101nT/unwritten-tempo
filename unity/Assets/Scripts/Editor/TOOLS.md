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

---

## Scene Setup

| Menu | What | Use when |
| --- | --- | --- |
| **Setup Systems Scene** | Builds the persistent `Systems.unity` (Sun + live-3D Player + follow Camera + Character + HUD + GameSystems + GameRoot), makes it Build index 0 + the Play-start scene. Maps then load additively under it. *Re-runnable.* | First-time setup, or after changing the player rig / global systems. (`SystemsSceneBuilder.cs`) |
| **Setup Hand-Drawn Grade** | Registers `HandDrawnGradeFeature` on every URP renderer in the project (as a sub-asset). *Idempotent.* | After importing/resetting URP renderers, so the DevSettings ▸ Hand-Drawn Saturation/Contrast knobs take effect. (`SetupHandDrawnGrade.cs`) |
| **Setup Occluded Silhouette** | Registers `OccludedSilhouetteFeature` on every URP renderer (sub-asset) + adds its shader to Always Included. *Idempotent.* | Enabling the x-ray silhouette that shows the player through occluding buildings/trees. (`SetupOccludedSilhouette.cs`) |
| **Add Occluded Silhouette to Player** | Adds `OccludedSilhouetteTarget` to the player in the open scene so its renderers are flagged for the silhouette. *Idempotent.* | Retro-tagging an existing player (Build Player adds it automatically). Save the scene after. (`SetupOccludedSilhouette.cs`) |
| **Add Camera Occlusion Fade (Systems scene)** | Drops a `CameraOccluderFader` onto the existing follow camera (props between camera & player fade out) without rebuilding Systems. Needs **Systems.unity open** (the follow camera lives there). | Retrofitting occlusion fade onto an older Systems scene. (`OcclusionFadeSetup.cs`) |

---

## World (map building & warps)

| Menu | What | Use when |
| --- | --- | --- |
| **World Creator…** | Window: digests a `world.json` (from the browser grid tool) → in one *Build World* bakes every map from its template `MapRecipe` and wires all paired Portal+Spawn warps + the world-map atlas. ⚠ Overwrites those scene files. | Generating/regenerating a whole multi-map world topology. (`WorldCreator.cs`) |
| **Import Painted Map…** | Phase 3 of the Map Painter pipeline: bakes a hand-painted `map.json` (from `tools/map-painter/`) into a scene — exact roads, houses, and yaw. The intra-map complement to the recipe scatter. Two modes: bake a **new** scene, or **merge into the open scene** with a per-category checklist (Ground / Props / Walls / Spawn) — ticked categories are wiped & rebuilt, unticked left as-is (e.g. re-import only the ground, keep hand-placed props). | You hand-painted a precise map layout and want it in Unity, or want to re-import part of one. (`MapPainterImporter.cs`) |
| **Create Biome Starter Recipes** | Stamps one starter `MapRecipe` per `Biome` (Grassland/Snow/Desert/Volcanic/Corrupted) into `Assets/3_Data/Recipes/Biomes/` — biome ground textures+hue + sensible starter props/mobs. *Create-if-absent (won't clobber edits).* | Seeding themed recipes to wire world-map cells to. (`BiomeRecipes.cs`) |
| **Brush Palette…** | Window: you **drag your own assets** into rows and pick each row's role — **Object** (a group of prefabs, scatters a random variant), **Wall** (the autotiled kit), or **Ground** (a blend texture; row order = its shader channel, max 5). Ids/colours/channels are auto-derived — no preset grass/path/stone rows. **Auto-fill from Props** seeds a working starter set by naming convention. Exports `prop-manifest.json` (measured footprints) into `tools/map-painter/`; with **Render prop thumbnails** ticked it also snaps a top-down PNG cutout of each prefab into `tools/map-painter/thumbnails/` so the painter draws the real prop. | Maintenance touch-point for the Map Painter pipeline; after adding new prop prefabs. (`BrushPaletteWindow.cs`) |
| **Apply Foliage Wind to Open Scene** | Re-materials plant props in the OPEN scene onto the `Unwritten/FoliageWind` shader (decor sways whole; trees sway leaves only). *Idempotent.* | Making foliage sway in a hand-authored map without re-baking it. (`RecipeGenerator.cs`) |
| **Sway Selected Tree** | Sways the SELECTED object(s) whole, with bend auto-sized to each object's height (trunk base stays rigid, crown rustles). *Idempotent.* | Hand-dropped single-mesh trees (GPT/Trellis exports) whose one baked material isn't named `*leaves`, so the open-scene tool skips them. (`RecipeGenerator.cs`) |
| **Sway Trees Folder (leaves only)** | Bakes leaves-only canopy sway into every prefab in `Prefabs/Props/Trees` (bark stays rigid, bend sized per tree). A tree with no leaf-named material falls back to whole-tree sway and is reported. Writes the wind material into each prefab asset. *Idempotent.* | One-click swaying of ALL tree prefabs — the folder is the tag, no name-guessing per prop. (`RecipeGenerator.cs`) |
| **Sway Plants Folder (whole plant)** | Bakes whole-plant decor sway into every prefab in `Prefabs/Props/Plants` (every material sways). Writes the wind material into each prefab asset. *Idempotent.* | One-click swaying of ALL plant prefabs (flowers/grass/ferns/bush). (`RecipeGenerator.cs`) |
| **Stylize Selected Solid Prop** | Re-materials the SELECTED solid prop(s) onto `Unwritten/StylizedLit` — the same clean storybook lighting as the sway shader (flat lambert + soft shadow + SH ambient + lamps, no specular) but solid, single-sided, and NO wind. *Idempotent.* | Raw Trellis/GPT houses/walls/rocks that look like a muddy glossy blob on URP/Lit. (`RecipeGenerator.cs`) |
| **Fill Ground Under Map** | Measures everything placed in the active scene and carpets a `ground_grass` tile rectangle (+ one flat Ground-layer collider) under it. *Re-runnable.* | After hand-placing props, to lay the walkable floor in one click. (`GroundFill.cs`) |
| **Create Spawn Point** | Drops a bare `SpawnPoint` GameObject (set its Id; match it from a Portal's Spawn Id). | Hand-wiring an arrival point. (`WorldTools.cs`) |
| **Fit Portal Triggers (active scene)** | Fixes/seats portal trigger volumes in the active scene (raycast-snaps the portal pivot to ground first). | After hand-dragging portals (which can drift onto prop colliders). (`RegionBuilder.cs`) |
| **Add Ambient Townsfolk** | Refreshes the shared `CharacterSpec` cast (creating `3_Data/Resources/CharacterSpec.asset` if absent) — CIVILIAN models for townsfolk, FIGHTER models for shadow mobs — plus the shared Humanoid controller; then drops an `AmbientLife` director into the open scene with the Ground mask → wandering, chattering **live-3D** townsfolk at Play. *Idempotent.* | Making a map feel alive, or re-syncing the character cast after adding models. Run in a map scene. (`AmbientLifeTools.cs`) |
| **Add Prop Light to Selection** | Adds a warm `PropLight` (real-time point light) as a child of each selected prop, so nearby billboard sprites brighten & take its colour. *Idempotent.* | Lighting a door/torch/portal so the player glows beside it. Retint/flicker on the new child. (`PropLightTools.cs`) |
| **Decorate Selected (Foliage)** | Rule-based, hands-off foliage scatter on the selected prop: raycasts its surface and seats Pxltgr decor (Flower\*/Grass\*/Fern\*/Bush\*/Mushroom\*) as a wall-hugging garden ring + a few wall-mounted accents, seeded per object name, under an `AutoDecor` child (colliders stripped, walk-through). *Idempotent — rebuilds* `AutoDecor`*.* | Auto-decorating a bare Hunyuan house (no hand-placing) — the detail stripped out of the gen to keep it clean. (`MeshDecorator.cs`) |
| **Bake Minimap (active map)** | Snapshots the active map from straight above into `Assets/2_Generated/Minimaps/<Scene>.png` and writes `MinimapData` onto the scene, so the HUD minimap is a static chart (no live 2nd camera). | After a map's layout changes. (`MinimapBaker.cs`) |

---

## UI (HUD, windows & global prompts)

| Menu | What | Use when |
| --- | --- | --- |
| **Build UI Shell (Systems scene)** | Rebuilds the in-scene UI shell (HUD + Bag/Character/Skills panels) skinned with the last-chosen Ninja Adventure theme; saves `Resources/GameUICanvas.prefab`. Also seeds starter items. Run with **Systems.unity open** so the canvas is replaced in place (not stranded in a map scene). | After changing UI, icons, or theme. (`UIShellBuilder.cs`) |
| **UI Shell Theme… (Systems scene)** | Window: pick a UI theme + frame thickness with a live preview, then build. Choice is saved so plain *Build UI Shell* reuses it. Build with **Systems.unity open**. | You want to change the HUD skin. (`UIShellWindow.cs`) |
| **Build Progression Windows** | Ensures starter Items + Cards exist, then drops a `ProgressionWindows` canvas (Inventory/Equipment/Character, toggled I/E/C) into the open scene, reading the live `Character`. | Testing the inventory/equip/character window trio. (`ProgressionWindowsBuilder.cs`) |
| **Build Dialogue UI** | Builds the global screen-space dialogue box → `Resources/DialogueCanvas.prefab`. Kept as the reusable UI half for a future 3D press-to-talk system; nothing instantiates it today (the 2D `Interactable`/`PlayerInteractor` finder half was retired). | (Re)building the global dialogue box. (`WorldTools.cs`) |
| **Build Interact Prompt** | Builds the global "press to interact" badge → `Resources/InteractPromptCanvas.prefab`. Same status as the dialogue box: reusable UI half, no live consumer yet. | (Re)building the interact prompt. (`WorldTools.cs`) |

> After Build Dialogue UI / Build Interact Prompt, run **Setup Systems Scene** to pull the prefab into the persistent layer.

---

## Mobs & NPCs

| Menu | What | Use when |
| --- | --- | --- |
| **Import NinjaAdventure Monsters** | Bulk-creates a `MonsterDef` asset in `Resources/Monsters` for every sprite folder under `Actor/Monster` (mobs) + `Actor/Boss` (bosses), pre-tagged with a `Biome` + difficulty `tier 1–4` (mobs: 3 per biome×tier cell + the remainder as biome-agnostic commons; bosses: 1 per cell, `isBoss`). Idempotent — skips assets that already exist. *Re-runnable.* | Standing up / refreshing the biome-grouped mob roster. (`MobBiomeImporter.cs`) |
| **Bake Mob Prefabs** | Bakes every `MonsterDef` in `Resources/Monsters` into a prefab under `2_Generated/MobPrefabs/Mobs/<name>` and marks it **Addressable** (group `Mobs`, address `Mobs/<name>`) — built via `RigBuilder25D.BuildMonster`, `BossBanner` added for `isBoss` defs. This is what lets the in-game `/summon <count> <normal\|boss>` chat command spawn mobs from data during Play (the builder is editor-only). *Re-runnable; overwrites in place.* | After adding/editing mob assets, so `/summon` can spawn them. (`MobPrefabBaker.cs`) |
| **Add Field Mob Spawner** | Drops a `FieldMobSpawner` into the open scene — an RO-style, data-driven, **object-pooled** runtime spawner that keeps a `population` of random catalog mobs alive within a `radius` and respawns them on a timer after death (reusing pooled objects, not Destroy+Instantiate). Tune Population/Radius/Biome/Level band in the Inspector. Contrast with the generator's BAKED mobs (fixed set, recycled in place) — use this to A/B the pooled spawner vs baked. Needs **Bake Mob Prefabs** run first. (`FieldMobSpawnerTool.cs` → `FieldMobSpawner.cs`) |
| **Spawn Training Dummy** | Spawns an inert, click-targetable mob (10M HP, no aggro/movement, full-HP reset every 5s) so you can read your damage/DPS off its HP label. *Re-runnable.* | Measuring damage/DPS. (`TrainingDummyTool.cs`) |
| **Clear Training Dummy** | Removes the training dummy. | Cleanup after a damage test. (`TrainingDummyTool.cs`) |
| **Spawn Field Boss (open field map)** | Hand-places the unique boss flagged in the `MonsterCatalog` (the `isBoss` `MonsterDef`; falls back to `MobRoster.Boss` if no assets) — name+HP banner, wide aggro, 120s respawn — into the open field map (refuses to spawn in Town). | Placing a boss encounter. Run in a field/dungeon scene. (`RegionBuilder.cs`) |

---

## Catalogs (whole-table reference/balancing views — `Tools ▸ unwritten ▸ Catalogs`)

One spreadsheet per data domain — open one to review or balance a whole table at once. The Mob/Item tables edit their ScriptableObject assets inline.

| Menu | What | Use when |
| --- | --- | --- |
| **Mob Table** | Spreadsheet window of every `MonsterDef` in the project — one mob per row with a live front-facing thumbnail (top-left cell of its `sheet`, tinted), and the AUTHORED stat block (id/name/biome/flags/**LVL/STR/AGI/VIT/INT/DEX/LUK**) editable inline, a **pts** column showing spent/budget for that level (red = over-budget; an over-typed stat auto-snaps to what the level can afford), a **gear** summary, and READ-ONLY derived readouts (=HP/=ATK/=DEF/=FLEE) computed live through `UnitStatMath` — plus element/weight/XP/gold. Click a row's **▸** to expand a per-mob **equipment + ring-ranks editor** (one object field per equip slot; ring ranks appear only when a ring is worn, each showing the % it's actually worth). Click a column header to sort, the search box to filter, **◎** to select the asset; **Save All** writes + reloads the `MonsterCatalog`. Edits are Undo-able. | Reviewing or balancing the whole mob roster in one view. (`MobTableWindow.cs`) |
| **Item Table** | Spreadsheet window of every `Item` in the project — one row per item with an icon preview and the balance fields editable inline: id/name, **Type** (Weapon/Armor/Consumable/Misc — DERIVED from slot+category via `ItemTypes`, editing remaps them), **Kind** (weaponType for weapons, equip slot for armor, blank else), element, rarity, ATK/DEF/MATK/MDEF/HP, weight, zeny, heal. Click a header to sort, search to filter, **◎** to select the asset; **Save All** writes to disk. Edits are Undo-able. The item sibling of the Mob Table. (`ItemTableWindow.cs`) |
| **Skill Channels & Effects** | Read-only reference for the skill authoring palette (design "Active Skill System — V3"). Two tables: the **Channels** catalog (numeric outputs, split effect-output vs skill-delivery, + whether the ring scales each) and the **Effects** catalog (every `SkillEffectKind` with its owned channels, what the ring actually scales, combat sink, and details). The effect rows are generated from the enum, so a new effect missing a catalog entry shows a **⚠**; a **⚠** in "ring scales" flags a composer channel the runtime ignores (dead knob). | Authoring/reviewing skills; checking which channels an effect owns and what scales. (`SkillEffectCatalogWindow.cs`) |

---

## Authoring (hand-placement helpers)

The keyboard-friendly companions to the Prop Palette. All bounds-aware (via `PropPlacement`) and Undo-able.

| Menu | What | Use when |
| --- | --- | --- |
| **Prop Palette** | Figma/Storybook-style window: pick a prop from a thumbnail grid, click in the Scene view to place it — auto-scaled, ground-snapped, optionally grid-snapped, Undo-able. Q/E rotate, Esc/right-click stops. | The main hand-authoring tool for placing props. (`PropPalette.cs`) |
| **Reset 2.5D Authoring View** | Resets the Scene-view camera to a known-good high 3/4 top-down angle. | When scene navigation flips you under the ground. (`AuthoringViewTool.cs`) |
| **Drop to Ground** | Re-seats the current selection on the ground by its bounds. | After nudging placed props off the floor. (`PlacementTools.cs`) |
| **Drop Selected to Ground** | Same intent for freshly dragged-in objects (snaps to the Ground layer). | After dragging houses/props from the Project window. (`RegionBuilder.cs`) |
| **Snap to Grid (0.5)** | Snaps the selection's position to a 0.5 grid. | Tidying placement. (`PlacementTools.cs`) |
| **Rotate 90°** | Rotates the selection 90° about Y. | Facing a prop a different way. (`PlacementTools.cs`) |
| **Remove Colliders (selected)** | Strips colliders off the selected props (walk through them). | Making specific props non-blocking. (`PlacementTools.cs`) |
| **Strip Decor Colliders (grass, flowers…)** | Scene-wide: strips colliders from all walk-through decor by name (grass/flowers/ferns/mushrooms…), never the floor. | One-shot cleanup so decor doesn't block the player. (`PlacementTools.cs`) |

---

## Props (asset → prefab pipelines)

| Menu | What | Use when |
| --- | --- | --- |
| **Build From Folder…** | Bakes an FBX prop kit (one FBX per prop, shared colormap atlas) into drop-ready prefabs (material + MeshCollider). Skips characters. *Re-runnable.* | Importing a new FBX prop kit. (`FbxKitPrefabBaker.cs`) |
| **Import GLB Prop…** | Bakes selected `.glb` model asset(s) into drop-ready prefabs in `Prefabs/Props`: forces materials to URP/Lit (keeps the GLB's baked texture), adds a MeshCollider per mesh, and re-pivots to bottom-centre for ground-snap. Keeps the GLB's **native size** — no auto-scaling, so drag-in gives real dimensions (size each prop deliberately). Overwrites in place. | Dropping any single self-textured GLB prop into the world (vs the FBX-kit bakers). (`GlbPropImporter.cs`) |
| **Optimize + Import GLB Prop…** | The mass-production path: chains `tools/optimize-glb.sh` (in place — weld + quadric simplify + texture downsize + meshopt compress; originals backed up to `tools/glb_backups/`) then the *Import GLB Prop…* bake, in one pass over the selected raw GLB(s) — or, with **nothing selected**, every raw `.glb` under `Assets/1_Authored/Prefabs` (skips `*_opt.glb`; confirm dialog shows the count). *Idempotent:* the mass path skips a GLB only if it's already optimized (carries `KHR_mesh_quantization`) AND has a baked prop prefab — so a fresh dense GLB dropped in under an old name still gets processed. Any GLB already quantized skips the optimize pass (no double-simplify) but still re-bakes. Explicit selection is always honored (force re-bake). Needs **Git Bash + Node** (the script calls `npx @gltf-transform/cli`); reuses `GlbPropImporter` for the bake half. | Bulk-processing dense TRELLIS/Hunyuan house/tree/prop GLBs into game-ready prefabs without a separate shell step. (`GlbPropPipeline.cs`) |
| **Bake Bark-Only Tree Colliders** | Batch: for every tree prefab in `Prefabs/Props/Trees`, rebuilds its collider from the **bark sub-part only** (material slot 0 = bark, slot 1 = leaves) so leaves are walk/jump-through. No eyedropping — the Pxltgr trees are already split bark-vs-leaf by material, so it just keeps the bark triangles. Saves a `Colliders/<mesh>_bark.mesh` per tree and re-points the MeshCollider. *Idempotent.* | Making Pxltgr tree leaves non-solid (only trunk+branches collide). (`TreeBarkColliderBaker.cs`) |
| **Bake Tree Collider…** | Trims a single-mesh tree's collider to the **branches only** so characters can jump through foliage and land on branches (Trellis fuses trunk+branch+leaf into one blob, so the full MeshCollider makes leaves solid and walls off jump paths). Eyedrop a **Bark** + **Leaf** reference colour off the rendered tree; every triangle is kept solid or dropped by which colour its texture sample sits nearer (Bark-bias dial). **Preview** swaps the live collider to judge the green outline, **Revert** restores, **Bake** saves a `<name>_col.mesh` and overwrites the collider in place (applies to the prefab). Colour lives in the texture (sampled per-triangle via UVs), not per-vertex. | Making tree branches standable while leaves stay jump-through. (`TreeColliderBaker.cs`) |
| **Decimate Selection…** | Author-time mesh decimation with a measured break-point loop: select meshes, set a **target tri count**, **Preview** swaps the live mesh (non-destructive) to judge it at the iso camera, **Apply** bakes a saved low-poly `.mesh` asset, **Revert** restores. Quadric-error (UnityMeshSimplifier) with border/UV-seam preservation; solves toward the target across correction passes. Bake, not runtime. Sweet spot = B/C/D props + finding break-points; hero S-tier still wants hand retopo. | Trimming dense TRELLIS/Hunyuan meshes to budget, or finding the real tri break-point. (`MeshDecimateWindow.cs`) |
| **Build Fantasy Assets (Pxltgr) → Props** | One-click: slices the Pxltgr Fantasy Assets multi-object FBX into per-prop prefabs in `Prefabs/Props` (preserves each child's own material; foliage left collider-free). | (Re)building the Pxltgr prop set. (`FbxPropExtractor.cs`) |
| **Split Multi-Object FBX…** | Asset-agnostic version of the above: pick any single FBX bundling many objects → one prefab per top-level child. | Slicing a different multi-object pack. (`FbxPropExtractor.cs`) |
| **Fix Pxltgr Materials (URP + leaf alpha)** | Re-points extracted Pxltgr materials to URP/Lit, re-binds Base Maps by name, enables alpha-clip on leaves (fixes magenta + gray leaf quads). *Idempotent.* | After an FBX *Extract Materials* / re-import turns props magenta. (`PxltgrMaterialFixer.cs`) |
| **Light Up Props (night)** | Adds a `NightLight` to glow-at-night props (houses, candlesticks, tavern lamp) by editing the prefab assets, so all instances inherit it. *Re-runnable.* | Making a town light up after dark off the `TimeOfDay` clock. (`LightUpProps.cs`) |

---

## Composers (authoring workspaces — `Tools ▸ unwritten ▸ Composers`)

The edit-mode authoring surfaces, grouped under one **Composers** menu. Each owns one authored data table/asset; the "Build Default…" generators that seed them stay in their domain sections (Art / Skills).

| Menu | What | Use when |
| --- | --- | --- |
| **Vfx Composer** | Edit-mode authoring workspace for composed effects (`VfxSequence` assets) — the single window that REPLACES the in-Play F8 Effect Tuner + F9 Sequence Tuner. Left = list of all compositions (New/Dup/Del); right = timeline of effects (each with its own scale / xyz offset / speed / start-time or event) over a live `ParticleSystem.Simulate` preview driven by an editor clock (no Play). A human-height capsule + ground grid = true-world-scale ruler. Owns PRESENTATION only: each spawning step can expose a named **tick id** (+ count/interval) that the Skill Composer binds gameplay onto — NO damage/element/multiplier here. | Authoring/tuning any composed effect (skill, environment, event) against real character scale + naming its gameplay ticks. (`VfxComposerWindow.cs`) |
| **Vfx Cue Composer** | Edit-mode table that binds each gameplay EVENT (`VfxCueId`: OnAttack/OnHit/OnCrit/OnHeal/OnLevelUp/OnDeath, DoT Start+Tick for Poison/Bleed/Burn, and Stun/Freeze/Slow/Haste/Silence/Blind) to a composed `VfxSequence` + anchor (Self/Target/Midpoint) + scale/offset — the reverse of a skill (gameplay→VFX, no ticks). Writes `Resources/VfxCueLibrary.asset` (auto-created) and reloads `VfxCueCatalog` on save. Fired at runtime via `VfxCue.Fire`/`Begin` from `StatusController`/`Character`/`Player3DAttacker`. `*Start` cues run as a persistent aura for the ailment's duration (author as an Attach/looping sequence); everything else is one-shot. | Binding a status/verb event to a look after authoring it in the Vfx Composer. (`VfxCueComposerWindow.cs`) |
| **Stance Composer** | Edit-mode table binding EVERY character pose (`StanceId`: held Combat/Guard/Casting/Sit; locomotion Idle/Walk/Run; one-shots Attack/Jump/Roll/Hurt/PickUp/Die/Fall; skill-release cues AoeSpell/SingleSpell/PhysicalSkill ×3) to the baked `CharacterAction` it plays + Clip (3D) + speed modulation (`StanceSpeedSource` = Baked/AttackSpeed/CastSpeed/MoveGait/FitWindow) + × mult + priority + `StancePlayMode` (Normal/FallTail) + fall phase. The persistent-state sibling of the Vfx Cue Composer, resolved by the single choke-point `StanceController` (held = push/pop priority-resolved; action = brain drives one, beats held; a skill-release override beats even that). Stat/window sources read the live owner each frame so nothing is hand-copied. `StanceDefaults` seeds new rows AND back-fills unsaved ids at runtime. Writes `Resources/StanceLibrary.asset` (auto-created), reloads `StanceCatalog` on save. | Authoring which pose/clip a character plays for any state (incl. a skill's release cue) + how its cadence tracks a stat/window. (`StanceComposerWindow.cs`) |
| **Skill Composer** | Edit-mode authoring workspace for `SkillDef` assets (`Resources/Skills/`) — where you COMPOSE SKILLS: pick a composed `VfxSequence`, and the window lists its exposed **ticks** live; bind each tick to gameplay (damage/heal/status, element, × multiplier, radius, targets, per-level growth) + identity (icon, mana, cooldown, max level, aim) + **skill type** (the `SkillReleaseStances` release cue played at fire). Owns WHAT each tick does; the final numbers stay in code (`SkillEffectApplier` rolls them off the live caster). | Authoring/tuning a data-driven skill's gameplay + release animation against a composed VFX. (`SkillComposerWindow.cs`) |

---

## Art (character/sprite baking)

| Menu | What | Use when |
| --- | --- | --- |
| **Import Item Icons** | Scans `Assets/1_Authored/Art/Items` for PNGs named by item id (`item.iron-sword.png`), applies icon import settings (Sprite/Single, point, uncompressed, alpha), and assigns each to the matching `Item` asset's icon (match: id → kebab(name) → asset name). Idempotent; reports unmatched files + items lacking a stable id. | After dropping a SINGLE-icon-per-PNG into `Assets/1_Authored/Art/Items` (hero/legendary quality). (`ItemIconImporter.cs`) |
| **Import Item Sheets** | Bulk path: grid-slices a sheet PNG in `Assets/1_Authored/Art/Items/Sheets` into per-cell sub-sprites and assigns each to an `Item` by a row-major id list. Grid size + ids live in a same-named `.txt` manifest (`cols:`/`rows:` lines + ids; `-` skips a cell), so grid size is a one-line text edit per sheet. Idempotent; reports unmatched ids + cell/id mismatches. | After dropping a GPT grid sheet + its manifest (many icons in one image). (`ItemSheetImporter.cs`) |
| **Rebuild Effect Library** | Scans the effect-prefab folders (`Magic effects pack/Prefabs` nested-by-category, `Procedural fire/Prefabs` flat) and (re)builds `Resources/EffectLibrary.asset` — the id→prefab table `EffectCatalog`/`Vfx` resolve against. Each prefab → an `EffectDef` id'd by category prefix + kebab name (`Hits and explosions/Explosion` → `hit.explosion`; the fire pack is flat → `fire.*`). Add a pack via the `Roots` array (fixed prefix = flat pack, null = per-category). Idempotent + non-destructive: existing entries (matched by prefab) keep their id + hand-tuned scale/lifetime; only adds new prefabs, reports ones whose prefab was deleted. | After importing/adding effect prefabs, so gameplay can spawn them by id via `Vfx.PlayAt`/`Vfx.Attach`. (`EffectLibraryBuilder.cs`) |
| **Build Default Vfx Sequences** | (Re)builds the built-in `VfxSequence` assets under `Resources/VfxSequences/` — the DATA recipes that compose a skill's look from library effects + shake + sound on a flat timeline (played by `VfxDirector`). Builds `meteor` (carrier fall → "impact" → ground explosion + shake) and exposes its ticks (`meteor_ball` ×1, `fire_pillar` ×5). Idempotent — rebuilds the default step list (**hand-tuning is overwritten**). | After adding a new default sequence, or to regenerate `meteor` from code defaults. (`VfxSequenceTools.cs`) |

---

## Rigs (Mixamo auto-rig pipeline)

The Unity side of the batch Mixamo auto-rig flow (see the `mixamo-batch-autorig-api` memory). The upstream steps are Python: `tools/export_mesh_for_rigging.py` (strip to mesh-only FBX) → `tools/mixamo_batch_rig.py` (upload→rig→download; `--report` for done-vs-pending). These two make the downloaded rigs Humanoid + verify them.

| Menu | What | Use when |
| --- | --- | --- |
| **Bulk Set Humanoid (Rigged Mixamo)** | Sets Animation Type = Humanoid + Avatar = Create From This Model on every selected FBX (or, with nothing selected, all under `Assets/0_Raw/Quat/Rigged`) and reimports. Skips already-correct ones. The one-click complement to `tools/mixamo_batch_rig.py` — no per-file Rig tab. *Idempotent.* | After batch-rigging FBX through Mixamo, to make them Humanoid in one click. (`BulkHumanoidRig.cs`) |
| **Validate Humanoid Avatars (Rigged Mixamo)** | Read-only pass over the same FBX set: loads each Avatar sub-asset and asserts `isValid && isHuman` (Unity only builds a valid+human avatar when every REQUIRED bone mapped — i.e. the mapping diagram is all green). Logs ✅ if all pass, else a warning listing each failing file. | Auto-checking a whole batch's rigs are green without opening Configure per file. (`BulkHumanoidRig.cs`) |
| **Fix Humanoid Clip Roots (Anim folder)** | For every animation FBX in `Assets/0_Raw/Quat/Anim/Humanoid` (or the selection): sets Animation Type = Humanoid, then on each clip bakes Root Transform Rotation (Body Orientation) + Position Y (**Based Upon = Feet** — the un-bury fix) + Position XZ into the pose, and enables Loop Time. One click instead of the per-clip Animation-tab foldouts. *Idempotent.* | After dropping Mixamo animation FBX into the Humanoid clip folder, so they retarget cleanly and the character's feet sit on the ground (not half-buried). (`BulkHumanoidRig.cs`) |
| **Set 3D Player Model (Systems scene)** | Seats a rigged Humanoid model as the live-3D player visual: instantiates the selected FBX (or `Rigged/BaseCharacter.fbx`), builds the shared Humanoid AnimatorController (StanceLibrary × `HumanoidClipResolver`), and adds the `Character3DPose`+`StanceController`+`Character3DVisual` rig. Needs **Systems.unity open**. | Changing which rigged character the player wears (the player is live-3D by default — `Setup Systems Scene` seats `BaseCharacter`). (`Player3DVisualBuilder.cs`) |
| **Enable Read-Write on Character Models** | Turns on Read/Write (mesh `isReadable`) for every FBX under `Assets/0_Raw/Quat/Rigged` and reimports (skips ones already enabled). Lets the in-game wardrobe read each model's real UV footprint at runtime, so it only offers colors the mesh actually wears (no phantom atlas colors). *Idempotent.* | Once, so the wardrobe's clothing swatches match the visible character. (`CharacterMeshReadWrite.cs`) |

> **Animation source:** live-3D player/NPC clips are shared **Humanoid** clips in `Assets/0_Raw/Quat/Anim/Humanoid/` (`HumanoidClipResolver`), matched to Stance Composer clip strings by cleaned name. Drop clips named `Idle`/`Walk`/`Run`/`SwordSlash`/… there; they retarget onto every rigged avatar automatically. Empty folder → the character rests/T-poses.
>
> **Auto-import:** `HumanoidClipPostprocessor` (an `AssetPostprocessor`) watches that folder — any FBX dropped in is forced to Humanoid + feet-planted/looped clip roots **on its FIRST import only, zero clicks** (gated on `assetImporter.importSettingsMissing`). It deliberately does **not** re-run on later reimports, so **hand-editing a clip's Root Transform in the importer and hitting Apply STICKS** instead of being clobbered back to canonical every time. To force a clip back to canonical on purpose, run the *Fix Humanoid Clip Roots* menu item (the manual re-fixer). Both share `BulkHumanoidRig.ApplyClipRoots`. ⚠️ General trap: an `AssetPostprocessor` that runs on *every* import silently overwrites hand-edited import settings — gate seeding on `importSettingsMissing` (first-import-only) for any future importer tool.

---

## Skills (data-driven skills)

| Menu | What | Use when |
| --- | --- | --- |
| **Attach Data Skills To Player (Systems scene)** | Adds a `SkillRunner` per catalog skill onto the player (so `IHotbarSkill` shows them on the bar). Skips skills already attached. Needs **Systems.unity open** (the player lives there). Mostly redundant now — `Player3DController` auto-attaches composed skills on Awake. | Force-attaching data skills without pressing Play. (`SkillDefTools.cs`) |

---

## Player

> **The player is now LIVE-3D by default** (committed 2026-07-04; mobs stay billboards). `Setup Systems Scene` seats a rigged Mixamo-Humanoid model on the player via `Player3DVisualBuilder` — there's no billboard-player swap anymore. To change WHICH rigged model the player wears, use **Tools ▸ unwritten ▸ Rigs ▸ Set 3D Player Model** (see the Rigs section). The retired billboard-player tools (`Use Baked Character`, `Use 3D Character`, `Use 3D Caster`, `Revert 3D Character`) and `BakedPlayerVisual` were deleted in this commit.

---

## Weapons

| Menu | What | Use when |
| --- | --- | --- |
| **Fix Blink Weapon Materials (URP)** | Converts every material under `Assets/0_Raw/Blink` from built-in **Standard** (renders magenta in URP) to **URP/Lit** in one click, carrying albedo/normal/metallic/emission across the property rename. Idempotent. (`BlinkWeaponMaterialFixer.cs`) |
| **Weapon Grip Tuner** | Window: WYSIWYG authoring of a weapon prefab's `WeaponGrip` (how the mesh sits in the hand). *Spawn Preview* drops a rigged character with the weapon mounted on the SAME hand bone the runtime uses (`HandSocketResolver`); drag the weapon into the palm with Scene handles; *Bake Grip → Prefab* writes the offset onto the prefab. Then assign that prefab to an `Item`'s **Equip Model**. **Batch:** with a tuned prefab assigned, a name prefix (e.g. `Sword`) + *Copy Grip → all* bakes that grip onto every matching same-shaped prefab — reusing the rotation, auto-scaling to the seed's length, and seating each handle-end at the same palm spot (one tune per weapon shape instead of ~100). Author-time only. (`WeaponGripTuner.cs`) |

> **How wielded weapons work:** a weapon `Item` carries an `equipModel` (3D prefab); its root `WeaponGrip` is the single source of truth for the hand offset. At runtime `WeaponSocket` (added to the live-3D visual by `Player3DVisualBuilder`) watches `Character`'s Weapon slot and parents the model under the Humanoid hand bone — the skinned rig then carries it through every animation. Equip-driven: swapping the equipped weapon swaps the model. One-hand (right) for now; left/off-hand is reserved in `WeaponHand`.

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
| **Recompile Scripts** (`Ctrl/Cmd+Shift+R`) | Forces an asset refresh + script recompilation. | Pushing a C# edit into the Editor without alt-tabbing to focus Unity. (`RecompileTool.cs`) |
| **Verify Render Settings** | Checks critical, silently-revertible project/render settings (URP Additional Lights = Per Pixel, Additional Light Shadows on, …) against a known-good table; reports drift. *Data-driven — add an invariant as one row.* | A feature "mysteriously disappeared" (lamp shadows, etc.); sanity check before a session. (`RenderSettingsGuard.cs`) |
| **Verify & Repair Render Settings** | Same check, then writes the correct values back to disk and saves. *Commit the changed .asset afterward so it can't revert.* | Drift was found and you want it fixed + committed. (`RenderSettingsGuard.cs`) |
| **Sweep Runtime Leftovers** | Destroys leftover runtime pool objects (VfxSystem, VfxDirector, Projectile3D, MultiHitBurst…) still lingering in the edit-mode scene. Runs automatically on exiting Play; this is the manual fallback for when that auto-sweep didn't fire (e.g. Play was exited during a compile error). Skips editor-window previews. | Stray effects/projectiles are stuck in the Scene view after Stop. (`RuntimeLeftoverSweeper.cs`) |

---

## Notes & related

- **Map generation** proper is driven by `MapRecipe` assets (Create ▸ unwritten ▸ Map Recipe) with a **Generate** button on the asset's inspector — that's `RecipeGenerator`, not a menu item. The menu's *World Creator…* / *Import Painted Map…* sit on top of it for whole-world / hand-painted flows.
- **Setup order for a fresh project:** Build From Folder / Build Fantasy Assets (prefabs) → Bake characters → Setup Systems Scene → Build UI Shell → generate maps (recipe / World Creator) → Bake Minimap.
- **Removed 2026-06-25** (cleanup): *Set Melee Attack Range* and *Remove HUD Widgets Demo* (one-shot migrations, done), plus *Strip Missing Scripts* and *Reset Saved Class*.
- **Removed 2026-07-05** (cleanup): *Backfill Item + Map IDs* (every asset now has an id) and *Create Starter Dungeon Recipe* (`DungeonRecipe.asset` exists) — both one-shots, done.
- **Removed 2026-07-27**: *Create Starter Items* (`ItemStarterSet.cs`) — deleted with the 2D-weapon item purge (weapons are 3D now; only Iron Sword + Bow Epic remain). Its two callers (`UIShellBuilder`, `ProgressionWindowsBuilder`) no longer seed starter Items; Card seeding is unaffected.
- **Mobs are data now:** the source of truth is `MonsterDef` assets in `Resources/Monsters` (Create ▸ unwritten ▸ Monster), surfaced via `MonsterCatalog` (lookup by stable `id` like `mob.slime`, or display name). Recipes/boss spawner read the catalog; the old hardcoded `MobRoster` array is now just a fallback (its one-shot migration tool was deleted 2026-08-10).
- One-off "quick fix" tools are tagged `// QUICKFIX` and parked under a **Quick Fixes/** submenu so a future purge is a grep (see `Scripts/CLAUDE.md`). Current: **Reground Baked Mobs** (drops mobs baked BEFORE the feet-pivot convention onto the ground + stacks their capsule up from the feet; idempotent — skips mobs already migrated; run once per hand-finished map to adopt the convention without a full regenerate — `RegroundBakedMobs.cs`).