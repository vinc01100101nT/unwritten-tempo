---
name: baked-character-billboards-removed
description: The baked-character 8-dir billboard system is DELETED — only MOBS are billboards now; players+townsfolk are live-3D.
metadata: 
  node_type: memory
  type: project
  originSessionId: 7267e5ec-04a8-408b-a4c3-ccb46759fd1f
---

The entire baked-character billboard system was removed 2026-07-21 (this session). Players and townsfolk
are LIVE-3D (Character3DVisual / Character3DPose via the StanceController→IStancePose choke point). **Only
MOBS are billboards** (Billboard.cs + BillboardAnimator3D + BillboardShadow + the mob sprite pipeline —
ALL KEPT).

**Deleted (gone — do not look for them):** `BakedBillboard8`, `Facing8`, `CharacterDef`, `CharacterRoster`,
`CharacterSelectSandbox`, and editor tools `SpriteBaker8`, `SpriteBakeCore`, `BatchCharacterBaker`,
`CharacterBuilder`, `SandboxBuilder` (+ the `2_Generated/BakedChars/` sprite output, + their `Tools ▸ unwritten`
menu rows: "Bake 8-Dir Sprites", "Bake All Quaternius Characters", "Create Character Sandbox Scene").
`BakedPlayerVisual` was already gone before this. `SpriteSheetCache.Row()` was removed (only BakedBillboard8 used it).

**Why:** live-3D is the committed direction ([[direction-2p5d-pivot]]); the baked-character billboard path was
dead weight that kept causing "is this still used?" confusion. Verified before deleting: no scene/prefab/asset
referenced any of these GUIDs (grep'd), mobs use none of them.

**Gotcha:** `VfxComposerWindow`'s size-ruler used to render a baked character sprite; it now uses its existing
plain-capsule ruler at `rulerHeight`. `CharacterAction` enum + `StanceLibrary` + `IStancePose` are KEPT (live-3D
still uses them). Don't assume any "billboard character" tooling exists — it doesn't. Related: [[npc-walk-feet-bury-dual-grounder]].
