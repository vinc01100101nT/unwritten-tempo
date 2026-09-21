---
name: ui-shell-rebuild-bloated-the-scene
description: Build UI Shell used to drag a whole font atlas into Systems.unity on save (96,000 lines); the builder now reverts the scene instance so the prefab is the only copy.
metadata:
  type: project
---

Running **Tools ▸ unwritten ▸ UI ▸ Build UI Shell (Systems scene)** and then saving the scene used to add about
**96,000 lines** to `Systems.unity` — a whole TextMeshPro font asset, its atlas texture, its material and its
glyph-adjustment table, serialized into the scene file.

**Why:** the HUD's text is built in code, and its font assets are created **at runtime, in memory**
(`UiKit.MakeFont` → `TMP_FontAsset.CreateFontAsset`). A prefab cannot hold a reference to an in-memory object,
so the prefab saved those font fields as nothing — but the scene CAN hold one, as a per-instance override on the
canvas. Every text object in the HUD therefore wrote `m_fontAsset` and `m_sharedMaterial` overrides into the
scene, and Unity embedded the whole font with them.

**The fix (2026-09-21):** the builder calls `PrefabUtility.RevertPrefabInstance` immediately after
`SaveAsPrefabAssetAndConnect`, so the scene's copy is made identical to the prefab and carries no overrides at
all. Measured after: the scene is 112 lines **shorter** than before the change, and the font is not in it.

**Watch for the same shape anywhere else** a runtime-created asset is assigned to a component that a scene then
saves — the scene will embed it, silently, and only the file size shows it.

Related: [[hud-build-process]], [[damagepopup-pooled-clone-savable-gap]].
