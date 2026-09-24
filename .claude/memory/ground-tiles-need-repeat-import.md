---
name: ground-tiles-need-repeat-import
description: Tiled 3D ground textures rendered smeared/clamped because PixelArtImportPostprocessor forces all /Art/ textures to Sprite+Clamp; carve out GroundTiles → Default+Repeat.
metadata: 
  node_type: memory
  type: project
  originSessionId: fe99d51a-f5a4-4b62-82f4-ae06fbc9d422
---

**Symptom:** a tiling texture on a plain material (URP/Lit) showed the picture correctly on ONLY one cell/band and smeared/stretched everywhere else — the classic **Clamp-wrap** signature (UVs outside 0..1 pinned to the edge pixel). Hit the curb/border strip (`ground_curb.png`) when first added.

**Root cause (measured, not guessed):** the mesh UVs were CORRECT (decoded the `.asset` vertex data: U ran the full map, ~2 tex-widths/cell, V 0..1). The texture was the problem: `PixelArtImportPostprocessor.OnPreprocessTexture()` (`unity/Assets/Scripts/Editor/PixelArtImportPostprocessor.cs`) force-sets **every** texture under `Assets/1_Authored/Art/` to `TextureImporterType.Sprite` + `FilterMode.Point` + `TextureWrapMode.Clamp` on every (re)import. So hand-editing the `.meta` (textureType/wrap/mipmap) is USELESS — it's reverted on the next import. The other ground textures dodge this only because `GroundBlend.shader` samples with its own inline `sampler_trilinear_repeat`, ignoring the texture's wrap; a plain material honours the Clamp → smear.

**Fix:** carved out `if (path.Contains("/GroundTiles/"))` in that postprocessor → import as `Default` + `Repeat` + mipmaps + Bilinear. Since 2026-09-20 that branch also sets `Compressed` + `npotScale=ToNearest` — the carve-out had chosen `Uncompressed`/`None` in passing, which shipped the 9 tiles at 85 MB of a 307 MB build. Since 2026-09-24 it also sets `maxTextureSize=1024` + `alphaSource=None`, so every tile imports identically (1024, DXT1 on this Mac); the last duplicate tile (`Prefabs/Props/Textures/ground_stones.png`) was removed and its prop material repointed, so `GroundTiles/` is the one home. The postprocessor is the single OWNER of import settings — fix there, not in metas — and a change to it needs **right-click the folder ▸ Reimport** (or `AssetDatabase.ImportAsset` through the live link) to reach existing textures.

**Gotchas:** ground textures (`ground_grass` etc.) are read via `GroundBaseMap` from PROP prefab materials and sampled by GroundBlend's inline repeat sampler, so they're unaffected by this change. Mesh `.asset` is text-YAML: vertex data is packed hex in `m_VertexData._typelessdata` (stride here 48B: pos12+normal12+color16+uv0 8 @offset40), indices in `m_IndexBuffer` (UInt16); submesh `localAABB` in `m_SubMeshes` is a cheap way to see if geometry spans the map vs one cell. See [[assets-reorg-2026-07]].
