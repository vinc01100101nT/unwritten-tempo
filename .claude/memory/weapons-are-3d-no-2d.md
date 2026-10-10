---
name: weapons-are-3d-no-2d
description: Weapons are 3D models now — no 2D weapons; only Item defs with a real 3D equipModel are kept.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1c05d24a-e740-47fc-a4f8-b555d4f437fd
---

The project uses **3D weapon models only** — 2D weapons are retired. A weapon `Item` def is "real" only if it has a 3D `equipModel` prefab assigned (mounted in-hand by [[weapon-composition]] via `WeaponSocket` + `WeaponGrip`); an `Item` with `slot: Weapon` but no `equipModel` is a stale 2D weapon and should not be used.

Since 2026-10-10 every weapon item points at a model made with Trellis.2 from its 2D sheet's front view (`tools/image-to-3d.py`, ~1,450 triangles each), named `<weapon type>_<Kashira>`: prefab in `1_Authored/Prefabs/Weapons/`, model in `2_Generated/WeaponModels/<name>/`. Items: Sword sword_veyra, Greatsword greatsword_mordane, Katar katar_nysara, Sniper (`item.sniper`, the firearm type) sniper_vael, Dagger dagger_velmora, Axe axe_varkhaz, Warhammer warhammer_tharos, Spear spear_orion, Bow bow_celestia, Staff staff_kaelen, Scythe scythe_morwen, Shuriken shuriken_riven, Wand wand_seraphine; **Fist has no model — bare hands** (a null `equipModel` mounts nothing). That folder holds nothing else: the older pack prefabs (`<pack name>_blue`) and sprite-sheet weapons (`kashira_*`, `weapon_*`, `sample_sword`) were removed at the user's ask on 2026-10-10 (copies in `backups/weapons-folder-cleanup-2026-10-10/`); the pack's raw files stay in `0_Raw/TossedSalads`. The pack sword's and bow's grips survive as numbers in the weapon and animation skills. The 100 weapon models before the pack were `git rm`'d on 2026-10-08. Any model work uses the `import-weapon-models` skill. A backup of the pre-pack sword, bow and all 14 items is in `backups/weapons-before-medieval-pack-2026-10-08/`. The 2D weapons **Katana, Sai, Dawnbreaker** (no equipModel) are being removed. Note `Editor/ItemStarterSet.cs` still re-creates Katana/Sai/Dawnbreaker if run, and `Editor/MobRoster.cs` drops "Katana" — those code refs must be cleaned or the 2D weapons resurrect.

This is the weapon-side sibling of [[baked-character-billboards-removed]] / [[direction-2p5d-pivot]]: player + weapons are live 3D; only MOBS remain billboards.
