# Handoff — 0_Raw asset promotion + bucket reorg (unwritten)

**Task** — Promote the assets the game actually uses out of `0_Raw/` (vendor packs) into `1_Authored/` with GUIDs preserved, keep unused as reserve, and neutralize vendor folder nesting so `1_Authored` is function-organized and consistent. No Tempo issue/doc.

**Status** — IN PROGRESS. Done: VFX, ground tiles, Pxltgr props, Quaternius chars, Blink weapons, NinjaAdventure (all moved + code paths fixed). All changes **uncommitted** and **unverified in Unity**.

## ⚠️ SESSION-END CRITICAL (2026-07-17) — read FIRST
- **A catastrophe happened & is fixed in code but NOT verified.** User ran **Setup Systems Scene** (`SystemsSceneBuilder`), which rebuilds `Systems.unity` from scratch AND **saves over it** (line 106). Because the earlier Quaternius move left STALE hardcoded paths in editor tools (`Player3DVisualBuilder.RiggedDir` etc. still pointed at deleted `0_Raw/Quat/...`), the rebuild found no player model → built a **playerless** scene → saved it over the good `Systems.unity`. Player vanished.
- **FIXED**: all stale `0_Raw/Quat` paths repointed to `1_Authored/Art/Characters/{Models,Animations,Animators}` across 6 files (Player3DVisualBuilder, HumanoidClipResolver, PlayerModelSwap, BulkHumanoidRig, BatchCharacterBaker, StanceComposerWindow). Verified all targets exist (BaseCharacter.fbx, 17 clips incl Idle/Walk/Run, Animators/). `grep 0_Raw/Quat *.cs` = 0.
- **RECOVERY the user still needs to do** (NOT yet confirmed): either `git checkout -- unity/Assets/Scenes/Systems.unity` (restores the committed working scene instantly — GUIDs resolve) OR Ctrl+R then re-run Setup Systems Scene (now path-fixed → rebuilds a good scene). **Do not trust "player is back" until user confirms Play.**
- **PENDING / user was angry, wants it addressed**: harden all 7 scene-SaveScene tools so they can't silently wipe a good scene — (a) ABORT before save if a critical asset (player model) is missing, (b) auto-backup the scene file before overwrite. Tools: SystemsSceneBuilder, RecipeGenerator, RegionBuilder, SandboxBuilder, WorldCreator, MapPainterImporter, MinimapBaker. User floated "delete Setup Systems Scene" — DON'T (31 dependents, it's the core build pipeline); harden instead. NOT yet done.
- **Kenney cursors: vector swap REVERTED.** Moved 182 `Vector/Outline` SVGs → `1_Authored/Art/UI/Cursors/` (kept). Rewired BuildCursor to SVG then **reverted to the original PNG paths** (working) to de-risk the scene rebuild. PNGs still in `0_Raw/KenneyCursorPack` (914 files, NOT deleted). Vector cursor swap DEFERRED — needs the 5 SVGs set to Generated Asset Type=Texture2D in Inspector + verify render before re-attempting.

## Core principle used (keep following it)
- **MOVE, don't copy**: moving an asset's file **+ its `.meta`** together preserves the GUID, so every reference (scenes, prefabs, EffectLibrary, animator) keeps resolving with zero repointing. This is why all the moves are reference-safe.
- `0_Raw/` = untouched vendor **reserve** to migrate more from later (user is early-dev; "used" counts are small because most content isn't built yet). **Do NOT delete reserve** unless the user explicitly says so for a specific pack.
- Promote into **functional homes**, not vendor folders: `Prefabs/Props/{Meshes,Materials,Textures}`, `Prefabs/VFX/<category>+{Materials,Textures,Shaders,Meshes}`, `Art/Characters/{Models,Animators,Animations}`, `Art/GroundTiles/`.
- **FBX moves require Unity CLOSED** (raw move while open can purge FBX w/ external deps — project memory). Always `tasklist | grep -ci "^Unity.exe"` → must be 0 before moving.
- Dependency tracing MUST read each asset's **`.meta` too** (FBX material remaps / `externalObjects` live there, not in the asset body) — this gap was found & fixed mid-session.

## What's done (files/dirs)
1. **Ground tiles** — 5 Pxltgr ground textures COPIED (fresh GUIDs) into `1_Authored/Art/GroundTiles/`, `2_Generated/Materials/Ground_*_blend.mat` repointed. (This one was copy, pre-reserve-decision.)
2. **VFX** — 3 packs (Hovl, NamuFX, Eric) fully promoted then their **raw packs deleted** (user OK'd; ~157MB used moved, rest was demo junk). Then reorganized into `1_Authored/Prefabs/VFX/` (category folders Sparks/AoE/Fire/Water/… + Materials/Textures/Shaders/Meshes). 105 effect prefabs; EffectLibrary resolves all. 3 material name-collisions resolved with `_fire`/`_eric` suffixes. Rescued `VFXatlas.psd`. Active fire shader = `FireSphere.shadergraph` (the `.shader` twin was a dup, deleted).
3. **Pxltgr props** — 68 files (meshes/materials/textures incl. `Assets.fbx`) dissolved into `1_Authored/Prefabs/Props/{Meshes,Materials,Textures}` alongside the 206 prop prefabs that use them. `Art/Pxltgr/` removed. 56 Pxltgr files remain reserve.
4. **Characters** — DELETED entire billboard roster `1_Authored/Art/Characters/` (52 baked chars + `CharacterRoster.asset`, 7229 files / 365MB) — confirmed unused in any scene (billboard is stale; live player uses `Character3DPose` in `Systems.unity`). Then promoted 17 live-3D Quat files into `Art/Characters/{Models,Animators,Animations}` (BaseCharacter.fbx, Casual3_Male.fbx, HumanoidPlayerController.controller, 14 anim clips).
   **2026-07-17 — Quaternius pack FULLY promoted + emptied.** Moved the whole `Rigged/` set into `Art/Characters/`: **52 Models** (rigged FBX), **52 Textures** (png), **17 Animations** (14 prior + Idle/ReleaseAOESkill3/ReleaseSingleSkill2), **3 Animators** (HumanoidPlayerController + QuatPlayerController + CastBarSystem). DELETED `FBX/` (unrigged base) and `RiggedBackup/` (backup) per user. Skipped the stale `Rigged/Casual3_Male` dup (live one already in Models/). `0_Raw/Quat` is **gone entirely** (0 files). Verified reference-safe (0 external refs before; 0 reaching back into 0_Raw after, fresh guid index). GUID-preserving raw move — **safe because Unity Auto Refresh is always OFF here** (see memory `unity-auto-refresh-off`). User must Ctrl+R in Unity to reimport.
5. **README** — `1_Authored/README.md` reworded: "curated & place-ready (made OR promoted by us)".

## Uncommitted working-tree changes
Everything above is unstaged. `git status` is huge: ~14k D (billboard delete), ~10k R (renames/moves), 72 M. Nothing committed. Reverting = `git checkout`/`git clean` would undo all promotions. Scratchpad scripts (NOT in repo, in session scratchpad): `promote_used.py` (pack filter arg2, reads .meta deps), `reorg_vfx.py`, `survey_raw.py`, `promote_vfx.py`.

## What's left (packs NOT yet promoted — still reaching into 0_Raw)
Per usage survey (`used` = entrypoints referenced from outside 0_Raw):
- ~~**Blink** (weapons)~~ **DONE 2026-07-17.** User said "move everything" — pack was pure content (100 weapon prefabs, 0 scenes/scripts/junk, 0 name collisions), so promoted the WHOLE pack (547 files) into `1_Authored/Prefabs/Weapons/` flattened to `{root=prefabs, Meshes, Materials, Textures}` = 100 prefab / 45 fbx / 103 mat / 299 png. `0_Raw/Blink` gone entirely. Verified: `Iron Sword.asset`→`Sword2_3_3.prefab` and `Bow Epic.asset`→`Bow_Epic.prefab` still resolve; Weapons/ has 0 refs back into 0_Raw. GUID-preserving raw move (Auto Refresh off).
- **KenneyCursorPack** — IN PROGRESS 2026-07-17. User: move the vector/outline cursors, switch game to vector, delete the rest. DONE so far: moved all 182 `Vector/Outline` SVGs → `1_Authored/Art/UI/Cursors/`; rewired `RigBuilder25D.BuildCursor` to load 5 `.svg` cursors as Texture2D from there (dropped the PNG Default/Double dirs + dirBig). PENDING USER VERIFY: set the 5 SVGs (pointer_b, tool_sword_a, target_b, target_round_b, hand_open) Generated Asset Type → **Texture2D** in Inspector + Apply; run Build 2.5D Spike; confirm cursor renders. THEN delete the 914 leftover Kenney files (PNG/ + Vector/Basic). Until verified, PNGs still on disk as fallback.
- ~~**NinjaAdventure**~~ **DONE 2026-07-17.** User: move ALL under `Art/Billboards`. Moved 1843 files `0_Raw/NinjaAdventure/*` → `1_Authored/Art/Billboards/` (kept internal tree Actor/Items/Ui; dropped vendor-name folder). Rewrote hardcoded base path `Assets/0_Raw/NinjaAdventure` → `Assets/1_Authored/Art/Billboards` in 6 .cs (MobBiomeImporter, RigBuilder25D, TrainingDummyTool, UIShellBuilder, MonsterArchetype, MonsterDef). 0 old paths remain. NOTE code says `/UI` while disk is `/Ui` — resolves on Windows (case-insensitive), was already so pre-move. GUID refs from scenes/mob defs preserved. USER VERIFY after Ctrl+R: mobs/UI not pink, "Import NinjaAdventure Monsters" tool still works.
- Fully-unused (0 refs), user chose KEEP as reserve 2026-07-17, do NOT delete: `20 Ground Material Sets MAN MADE`, `Phoenix3D`, `Pxltgr` (56 leftover), `Standard Assets (Mobile)`.

Other open follow-ups the user floated:
- Rename `Prefabs/Props/Meshes/Assets.fbx` → `PropKit.fbx` (generic vendor name; GUID-safe, 200 prefabs + 34 mat links unaffected).
- Dead **billboard SCRIPTS** still present (BakedBillboard8, AmbientLife, TownNPC, CharacterDef, CharacterRoster, SpriteBakeCore, BatchCharacterBaker, CharacterBuilder + their editor menu items). NOT removed (code removal is riskier; share IStancePose/StanceController with live-3D). User may want them ripped out + `Editor/TOOLS.md` updated.

## Dead ends / gotchas (proven)
- `grep -rl <guid>` per-guid across the whole tree = TIMES OUT (2min). Build a single guid→path index first: `grep -rE "^guid: " --include=*.meta . | sed -E 's#^\./(.*)\.meta:guid: ([a-f0-9]+).*#\2 \1#'`.
- Prefab-only promotion is a trap: props were moved without their mesh/texture data → still reached into `0_Raw`. Always move the full dependency **closure**.
- `/tmp/guidindex.txt` from earlier steps goes STALE after moves — rebuild before resolving paths.

## Next single step
Ask the user which remaining pack to do (Blink is the biggest win), confirm **Unity closed**, then: dry-run closure → move (file+meta, GUID preserved) → verify `grep -rl -f <pack-guids> 1_Authored` returns 0 (self-contained) → leave rest as reserve.

## How to resume
Read this file: `handoffs/raw-asset-promotion-and-bucket-reorg.md`. Reserve packs live in `unity/Assets/0_Raw/`; promoted homes in `unity/Assets/1_Authored/`. First action: `tasklist | grep -ci "^Unity.exe"` and confirm the user has verified the prior moves in Unity (player renders, effects/props not pink) before doing more.
