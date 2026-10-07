---
name: weapons-are-3d-no-2d
description: Weapons are 3D models now — no 2D weapons; only Item defs with a real 3D equipModel are kept.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1c05d24a-e740-47fc-a4f8-b555d4f437fd
---

The project uses **3D weapon models only** — 2D weapons are retired. A weapon `Item` def is "real" only if it has a 3D `equipModel` prefab assigned (mounted in-hand by [[weapon-composition]] via `WeaponSocket` + `WeaponGrip`); an `Item` with `slot: Weapon` but no `equipModel` is a stale 2D weapon and should not be used.

The only weapons with their OWN 3D model are **Sword** (`item.sword`) and **Bow** (`item.bow`). Since 2026-10-05 every other weapon type has a stand-in item (Greatsword, Axe, Warhammer, Dagger, Katar, Spear, Staff, Wand, Scythe, Fist, Shuriken borrow the Sword model; Sniper — the firearm type, `item.sniper` — borrows the Bow) so skills can be composed and tested before the real models arrive — swap the model on the item when it does. The 2D weapons **Katana, Sai, Dawnbreaker** (no equipModel) are being removed. Note `Editor/ItemStarterSet.cs` still re-creates Katana/Sai/Dawnbreaker if run, and `Editor/MobRoster.cs` drops "Katana" — those code refs must be cleaned or the 2D weapons resurrect.

This is the weapon-side sibling of [[baked-character-billboards-removed]] / [[direction-2p5d-pivot]]: player + weapons are live 3D; only MOBS remain billboards.
