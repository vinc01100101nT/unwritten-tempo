# Asset Guide — unwritten (2.5D)

**Read this before changing any import setting, prefab, project setting or package in** `unity/`**.**

An asset setting is a number in a text file, and a wrong one is silent: the editor does not complain, the console
does not complain, the game runs. It only shows up as a build that is 100 MB heavier than it should be, a frame
that is doing thousands of draws for grass, or a creature that is level 1 when its data says 46. Everything below
was measured on 2026-09-20, on this project, before it was fixed. The rules are what stop it coming back; the
checker at the bottom is what makes them stick without anybody remembering.

> **The build is what ships, not the folder.** `Assets/` was 9.5 GB on disk and the client build was 307 MB of
> assets — and 75.8% of THAT was textures, 117 files carrying 229 MB. Never reason from folder sizes; read the
> build's own size report (`Used Assets and files from the Resources folder, sorted by uncompressed size` in
> `~/Library/Logs/Unity/Editor.log`).

---

## The golden rules

1. **Every authored texture ships compressed, unless it is pixel art.** Block compression is invisible on a
   painted photo of ground and costs a quarter to an eighth of the memory. Nine ground tiles imported with
   compression set to None were 85 MB of the 307 MB build. Pixel art is the one exception — block
   compression bleeds across a pixel grid — the sprites and the textures of weapons made from sprite sheets alike,
   and the import rule (below) owns them. The head pieces' colour strips (`2_Generated/Characters/HeadPieces/`,
   written by `HeadPieceBaker`) are the same case: a handful of exact colours, a few hundred bytes each, which
   compression would shift off the colours a chosen hair colour is matched against.
2. **Nothing above 2048 square, and only 2048 when the camera can see it.** 34 textures at 2048 were 160 MB of the
   build for a 2.5D camera that never fills the screen with a prop. Halving one saves three quarters of its memory.
3. **A texture is Read/Write Enabled only when a script reads its pixels.** It doubles the texture's memory. The
   hardware cursor needs it; nothing else here does.
4. **Plants carry no collider and cast no shadow.** 4,608 placed plants were solid physics meshes (the most
   expensive kind, tested against every moving thing) and were each drawn a second time into the shadow map.
   Plants receive shadows — grass under a tree still darkens — but never cast them, and you walk through them.
5. **A palette prop is static.** Nothing in `1_Authored/Prefabs/Props` moves, and a prop with no static flags can
   be neither batched with its neighbours nor hidden behind a wall. 8,954 placed props were drawn one by one.
   Hard props (walls, rocks, houses) get batching + occluder + occludee = **22**; foliage gets occludee only = **16**.
6. **Foliage is never batching-static.** The wind shader reads each plant's own pivot to bend it from the base;
   static batching pre-transforms every vertex and gives the whole batch one identity matrix, so the bend reads
   the world origin instead. Occludee only.
7. **A simplified copy is wired, or it is dead weight.** 17 prop families had `_LOD1`/`_LOD2` decimated copies
   sitting beside them, placed zero times; only the full copy was ever drawn. A family is wired when its `_LOD0`
   prefab carries an `LODGroup` with every sibling level as a child (`LodGroupWirer.WireAllCore()`). Every other
   palette prop of 500 triangles or more gets two generated simpler levels (`PropLodBaker.BakeCore(null)`), and
   the simplest level of every prop is never hidden: a far prop is drawn simpler, never left out.
8. **Every field a script declares is in the baked prefab.** Unity silently drops a field whose type it cannot
   serialize — a `struct` with no `[System.Serializable]` — and the baker writes the rest and says nothing. That is
   how all 84 creatures loaded at level 1 with every stat at 1 while their data said 3 to 46.
9. **Every package is claimed.** A package nobody uses still ships its code and slows every domain reload. Eight
   were installed with zero references, one of them 10 MB of the client. `tools/packages-in-use.txt` names what
   uses each one; an unclaimed package fails the check, so installing one means writing one line.
10. **Settings are pinned.** The main shadow map is 1024 (it was 4096 — 33 MB of VRAM re-rendered every frame to
    cover 18 units), GPU skinning is on (every character was skinned on the CPU), no quality level uses unlimited
    bone weights (4 is the ceiling). Change a pin in `tools/check-asset-rules.py` and here together, with the reason.
11. **A ground tile is one .png in `1_Authored/Art/GroundTiles/`, imported at 1024, compressed, with no alpha.**
    Every tile the game paints ground with — base, patch, path, sidewalk, curb, corner — lives in that one folder,
    and nothing else lives there: no materials, no copies, no other formats. A tile is named by its uses, then its
    own name: an UPPERCASE prefix listing every way it can be laid (BASE, PATCH, PATH, SIDEWALK, CURB, CORNER),
    joined by `_OR_` when there is more than one, then the lowercase name — `BASE_OR_PATCH_meadow_grass`,
    `PATH_dirt_road`, `CURB_blocks`. Code looks a tile up by that exact file name. To add a tile,
    drop the .png in the folder; the import rule applies 1024 / compressed / no alpha / repeat / mipmaps by itself,
    and code reaches it only through `GroundTiles.Load(name)`. The source can be any size (image-generator tiles
    arrive about 1254 wide), but must be seamless and carry no border. Why: tiles blended on one ground must match
    in sharpness and format, and a second copy or a second lookup is how the duplicate stones tile shipped twice.
    Enforced, not advised: the asset-rules check fails any tile that breaks it, and it runs before every commit.
12. **A shader the code looks up by name is in Always Included Shaders.** `Shader.Find` returns nothing in a built game
    unless the shader is on Project Settings ▸ Graphics ▸ Always Included Shaders or a material the build ships uses
    it — and the editor always finds it, so the feature works in Play mode and silently does nothing in every build.
    The ink outline, the paper grade and the old character-colour shader (`InkScreen`, `HandDrawnGrade`, and one
    since removed) were missing from every build until 2026-10-02, each build's log saying "not found". Adding a
    `Shader.Find` means adding the shader to that list in the same change. Checked by `tools/check-runtime-shaders.py`
    (proven by `tools/prove-runtime-shaders-check.py`), written after the afterimage's URP Unlit — missing from every
    build — threw inside a network message on 2026-10-12 and disconnected every Thousand Petals dash.
    **Being on that list ships the shader, not every switch on it.** A switch a shader declares with `shader_feature`
    (the text shaders' `OUTLINE_ON`) is cut from a build unless a material the build ships has it on, so code that
    turns one on at runtime needs a saved material with it on: `Resources/Fonts/Floating Text Outline.mat` keeps the
    black edge on every floating word, which was missing from every build after those words moved to the
    always-on-top text shader on 2026-10-12.
13. **Every character model has its outfit parts saved.** Which colours of a model can be recoloured is worked out
    once in the editor and saved (`CharacterDyePartsBaker.BakeAll()`, run through the live editor link, writes
    `3_Data/Resources/CharacterDyeParts.asset`). Working it out in game read the whole texture back from the
    graphics card and froze the game for 18.7 ms the first time each model appeared. A model with no entry offers
    no colour choices and is never recoloured. Enforced: the asset-rules check fails any `.fbx` in
    `1_Authored/Art/Characters/Models` the table does not list.
14. **A character clip made by a script bakes the body's movement into the pose.** The game's characters never apply
    root motion, so whatever a clip leaves as root motion — the body's travel, its height, its turn — is thrown away
    in game: the body stays put and a planted foot slides. Imported clips are set this way in their import settings;
    a clip a script saves (`.anim` in `1_Authored/Art/Characters/Animations`) must set it itself. All 12 weapon
    attacks shipped without it on 2026-10-09, and the back foot slid in every one. Enforced: the asset-rules check
    fails any such `.anim` that does not bake all three.

---

## Established patterns in THIS project — reuse them, don't reinvent

| Need | Use | Why |
| --- | --- | --- |
| Import settings for anything under `1_Authored/Art/`, and the textures under `2_Generated/Weapons/` | **The import rule** `Editor/PixelArtImportPostprocessor.cs` | It is the single OWNER of those settings and re-applies them on every import. Hand-editing a `.meta` there is reverted on the next import — change the rule, then reimport the folder. Its `/GroundTiles/` branch is the one non-pixel-art case: Default + Repeat + mipmaps + **Compressed** + ToNearest + **max 1024** + **no alpha**, the same for every tile. |
| Ground textures for the blended ground, the curbs and the prop materials | **One copy**, in `1_Authored/Art/GroundTiles/` — every ground tile the game uses, new ones included, lives there and nowhere else | The same four images used to live twice, once compressed and once not, and both shipped — 48 MB of the build was the same three pictures twice. |
| Level-of-detail for a placed prop | **`LodGroupWirer.WireAllCore()`** for a family with hand-made `_LODn` copies, **`PropLodBaker.BakeCore(null)`** for every other prop — both through the live editor link | The wirer nests each `_LODn` copy's mesh under the `_LOD0` prefab; the baker makes copies at 40% and 25% of the triangles (`MeshDecimation`, texture seams let go — they block almost all reduction on the generated props) into `2_Generated/PropLODs/`. Neither copies a collider (the placed prefab already blocks), both switch on one table (`LodGroupWirer.Levels`) whose last level never culls. Both skip a prop that already switches; re-run after adding props. |
| Creature stats, gear, drops | **The creature data files** → `Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs` | The bake is the only road from data to a prefab. After ANY change to a struct a baked component holds, re-bake — adding the missing `[Serializable]` alone changes nothing on disk. |
| Which packages exist and why | **`tools/packages-in-use.txt`** | One line per package, what uses it, measured. The checker refuses an installed package with no line. |
| Writing a scene, prefab or asset from editor code | **A menu item, or `// editor-write-ok: <why>`** | RULE NOEDITORWRITES. `tools/check-editor-writes.py` follows every load-time / import-time / save-time trigger to the writes it reaches and refuses one that says nothing. |

---

## Anti-patterns (these regressed us — don't do them)

- ❌ **Fixing an import setting in the Inspector for a texture the import rule owns.** Apply triggers a reimport, the rule fires, the setting is back. Change the rule.
- ❌ **Copying a texture into a second folder to use it from there.** Two copies ship. Reference the one.
- ❌ **Leaving decimated copies beside a prefab and never wiring them.** Placed zero times, drawn never, 35 prefabs of dead weight.
- ❌ **A collider on something the player is meant to walk through** (grass, rye, flowers). A MeshCollider is tested by physics against every moving thing on its layer, every frame.
- ❌ **Shadow casting on tiny props.** Every caster is drawn again into the shadow map per light.
- ❌ **Batching-static on anything that uses `FoliageWind`.** The sway reads the object's pivot; batching erases it.
- ❌ **A `public` field on a component the baker fills, of a type Unity cannot serialize** — a plain `struct`, a `Dictionary`, an interface. It vanishes on save with no warning.
- ❌ **Installing a package to try it and leaving it.** It ships.
- ❌ **Two shadow maps at 4096 for one light and 18 units of distance.** Memory and fill for nothing.
- ❌ **`Read/Write Enabled` on a texture "just in case".** It is two copies in memory.

---

## The automatic check

```bash
tools/check-asset-rules.py          # every rule above, on the whole project; --all also lists the debt (WARN)
tools/check-editor-writes.py        # editor code that writes with no click
tools/prove-asset-rules-check.py    # re-run after ANY edit to the checker — 21 cases: each planted fault named, the sound copy quiet
```

They run by themselves at the end of any turn that edited a texture or model setting, a prefab, a project setting
or the package manifest, and refuse to end the turn while a rule is broken in a file that turn touched. **Their
silence is not approval:** they read what is SAVED. Whether a prop looks right at 1024, whether a shadow is soft
enough, is still the user's eye in Play mode.

An unwired LOD family is a WARN, not a FAIL — it is debt, listed so it can only shrink, and it does not block a
turn. Everything else on this page is a FAIL.

---

## Checklist before committing an asset change

- [ ] A new texture: compressed (or pixel art under the rule), ≤ 2048, not readable, and referenced from ONE place.
- [ ] A new prop prefab: static flags set (22, or 16 for foliage); no collider and no shadow casting if it is a plant.
- [ ] A new decimated copy: wired into its family's `LODGroup`.
- [ ] A new baked component field: its type is serializable, and every prefab that carries it has been re-baked.
- [ ] A new package: one line in `tools/packages-in-use.txt` saying what uses it.
- [ ] A new or changed character model: `CharacterDyePartsBaker.BakeAll()` re-run, so its outfit parts are saved.
- [ ] A changed pin (shadow map, skinning, bone weights): the checker and this page changed together, with the reason.
