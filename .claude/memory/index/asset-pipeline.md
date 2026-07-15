# Asset / art pipeline (3D props, sprites, style guides)

- [Hunyuan3D-2 prop pipeline + Style Guide v1.0](../hunyuan3d-asset-pipeline.md) — concept→Hunyuan3D→Blender low-poly; chunky silhouette.
- [Hunyuan concept-input contract](../hunyuan-concept-input-contract.md) — geometry from INPUT IMAGE; chunky closed plinth, bright light, white bg.
- [Asset Style Guide v2.1 (TRELLIS 2) LOCKED](../asset-style-guide-v2-trellis.md) — doc q17axe2t5tymwkh1f6bfd1xmdh89g0g8; silhouette-first.
- [GPT→Trellis 2 town-prop pipeline PROVEN](../trellis2-tier1-town-pipeline-proven.md) — STOCK 1024/250k/2048; beats Hunyuan (clean base ZERO tuning) but still needs Blender retopo; doc q17013b7vgpxyqp01rsw53xyc589n6yd.
- [Bake 8-Dir sprites pipeline](../bake-8dir-sprites-pipeline.md) — Art ▸ Bake 8-Dir Sprites (`SpriteBaker8.cs`) → `BakedBillboard8.cs`.
- [Fantasy town props from Pxltgr pack (×0.3)](../fantasy-town-pxltgr.md) — `Assets.fbx`→`FbxPropExtractor` (FantasyTownBuilder DELETED).
- [Merged Kenney 3D terrain trick](../kenney-3d-terrain-overlap.md) — overlap platform tiles ~1.3× to merge; flat per-tier colliders.
- [Optimize dense GLBs offline](../glb-optimize-offline-gltftransform.md) — glTFast has NO mesh/tex knobs; `tools/optimize-glb.sh` + one-click `Optimize + Import GLB Prop…`. COMPRESS=quantize (meshopt imports EMPTY w/o pkg); bash-from-C# relative-path/exit-code/freeze gotchas.
- [Trellis imperfection IS the linework](../trellis-imperfection-is-the-linework.md) — ART DIRECTION: keep faceted/noisy Trellis meshes; crease ink pass turns topology noise into interior line art; don't over-smooth for "cleanliness".
- [Optimize+Import needed 2 runs (deferred import)](../glb-prop-deferred-import-2x-bug.md) — `ImportAsset`/`CreateFolder` are DEFERRED inside `StartAssetEditing`; reimport + make folders BEFORE the batch. Base pivot baked into a shifted mesh copy (single node, no wrapper) in `Props/Meshes/`.
