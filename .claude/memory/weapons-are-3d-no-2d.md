---
name: weapons-are-3d-no-2d
description: Weapons are 3D models now — no 2D weapons; only Item defs with a real 3D equipModel are kept.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1c05d24a-e740-47fc-a4f8-b555d4f437fd
---

The project uses **3D weapon models only** — 2D weapons are retired. A weapon `Item` def is "real" only if it has a 3D `equipModel` prefab assigned (mounted in-hand by [[weapon-composition]] via `WeaponSocket` + `WeaponGrip`); an `Item` with `slot: Weapon` but no `equipModel` is a stale 2D weapon and should not be used.

Since 2026-10-08 `1_Authored/Prefabs/Weapons/` holds ONLY one low-poly pack's weapons (`0_Raw/TossedSalads`): all 41 hand-weapon shapes as `<pack name>_blue` prefabs (blue only — the user tints weapons in game). The 100 older weapon models and their meshes/textures/materials were `git rm`'d that day (recoverable from git). Items: Sword sword_1, Greatsword longsword_1, Axe axe_1, Warhammer hammer_1, Dagger dagger_1, Spear spear_1, Staff staff_1, Bow bow_1; stand-ins Katar dagger_2, Wand staff_2, Scythe axe_4, Shuriken dagger_3, Sniper (`item.sniper`, the firearm type) bow_2; **Fist has no model — bare hands** (a null `equipModel` mounts nothing). Any model work uses the `import-weapon-models` skill. A backup of the pre-pack sword, bow and all 14 items is in `backups/weapons-before-medieval-pack-2026-10-08/`. The 2D weapons **Katana, Sai, Dawnbreaker** (no equipModel) are being removed. Note `Editor/ItemStarterSet.cs` still re-creates Katana/Sai/Dawnbreaker if run, and `Editor/MobRoster.cs` drops "Katana" — those code refs must be cleaned or the 2D weapons resurrect.

This is the weapon-side sibling of [[baked-character-billboards-removed]] / [[direction-2p5d-pivot]]: player + weapons are live 3D; only MOBS remain billboards.
