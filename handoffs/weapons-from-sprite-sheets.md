# Handoff: weapons from 2D sprite sheets

**Task:** turn a 2D weapon sprite sheet (front and side view side by side, one weapon per sheet) into a
low-poly 3D weapon the characters hold — for all 13 weapon types that need a model (the Fist is bare hands).
No Tempo issue or doc.

**Status:** IN PROGRESS. All 13 weapon items now point at models made with Trellis.2 from the sheets' front views
(2026-10-10), named `<weapon type>_<Kashira>`, and the weapons prefab folder holds nothing else (the user's
ask, done 2026-10-10). **Not play-tested yet.**

## What's done

- **The Trellis weapons (2026-10-10)** — made with `tools/image-to-3d.py` (the `image-to-3d` skill), 1,500
  triangles asked for, 1,410–1,484 came out. Each: `2_Generated/WeaponModels/<name>/<name>.obj` + `.png` +
  `_input.png`, `1_Authored/Prefabs/Weapons/<name>.prefab`, `Materials/<name>.mat`; the mesh inside each OBJ is
  named after the file. Item → prefab:
  Sword `sword_veyra`, Greatsword `greatsword_mordane`, Katar `katar_nysara`, Sniper `sniper_vael`, Dagger
  `dagger_velmora`, Axe `axe_varkhaz`, Warhammer `warhammer_tharos`, Spear `spear_orion`, Bow `bow_celestia`, Staff
  `staff_kaelen`, Scythe `scythe_morwen`, Shuriken `shuriken_riven`, Wand `wand_seraphine`.
  - Fitted with the weapon skill's Trellis section against the model each item used before (same length, same
    rotation, fist on the same fraction of the length). Trellis turned some round: the axe got an extra quarter turn
    about its length, the bow and scythe a half turn.
  - The shuriken is held at the star's centre on the fist's middle `(0.0045, 0.2135, -0.024)` and turned
    (-75, 60, -15) so the star lies beside the leg at idle (points inside the leg: 738 → 4).
  - Put-away places carried from the old katar and dagger (bone Hips, same turn) so their grip points rest where
    the old ones did; pictured from behind, the side and the game camera beside the old ones — they match. The other
    eleven had no place to carry and are hidden while put away.
  - Only the `equipModel` line of each item changed. Backup of the items before the switch:
    `backups/trellis-switch-2026-10-10/Items` (26 files).
- **Projecting the drawings onto the Trellis models** was tried and rejected by the user ("it looks ugly"; the
  outlines overlapped only 73% front / 41% side).
- **Converter** — `tools/sprite-sheet-to-weapon.py` (runs outside Unity, needs numpy + PIL), the two-view rounded
  loft. Its full description is the `import-weapon-models` skill's sprite-sheet section. No item uses its output
  any more; it stays for blocky weapons that suit it.
- **Import rule** — `unity/Assets/Scripts/Editor/PixelArtImportPostprocessor.cs`: branch for `/2_Generated/Weapons/`
  (point-filtered, uncompressed). `tools/check-asset-rules.py` counts those textures as pixel art.

## The old weapons, removed 2026-10-10

- The 41 pack prefabs `*_blue`, the 14 sheet weapons (`kashira_*`, `weapon_*`, `sample_sword`) and their 15
  materials were copied byte for byte (with metas, 140 files) to `backups/weapons-folder-cleanup-2026-10-10/`, then
  deleted from the project. A check first proved no asset outside the folder used any of them.
- The pack's raw files stay in `0_Raw/TossedSalads/`, and the converter's models stay in `2_Generated/Weapons/`
  (no prefab uses them now).
- The pack sword's and bow's grips, which the weapon and animation skills measured from, are now written as numbers
  in both skills' scripts. The animation build script's edge list names the Trellis models (axe edge +z, bow and
  scythe +x); a dry run of the sword, axe and scythe attacks ran clean, the sword matching its saved clip exactly.

## What's left

1. User play-tests all thirteen weapons.
2. The Fist has no model (bare hands) — its sheet is being designed (raging fire theme asked for).
3. Put-away places for the eleven weapons without one are a separate job, agreed with the user.

## Dead ends

- Corner pixel as background colour: wrong. Median border colour works.
- The converter cannot build an ornate head whose parts sit at different depths (the wand): the side view is only a
  thickness per row. Trellis can.
- Shrinking a Trellis model afterwards with gltf-transform stalls at ~6,800 triangles; Trellis's own exporter
  reduces before laying out the texture.
- Editing a `.cs` file (even in the scratchpad) through Bash is refused by the performance hook until
  `PERFORMANCE.md` is read; use the Write/Edit tools for editor-link scripts.

## Next single step

Wait for the user's play-test of the thirteen Trellis weapons.

## How to resume

```text
Read handoffs/weapons-from-sprite-sheets.md and continue: the user's play-test of the thirteen Trellis weapons, then the Fist.
```
