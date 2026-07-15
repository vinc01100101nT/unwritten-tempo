---
name: glb-prop-deferred-import-2x-bug
description: "Optimize+Import GLB prop needed 2 runs (child left in, empty Meshes) — ImportAsset deferred inside StartAssetEditing batch."
metadata: 
  node_type: memory
  type: project
  originSessionId: df3823ce-85c8-451d-88ba-7c4afb79cb38
---

**Symptom:** *Tools ▸ unwritten ▸ Props ▸ Optimize + Import GLB Prop…* needed to be run **twice** to
produce a correct prop. First run: prefab kept a `geometry_0` CHILD node, pivot at mesh centre, and the
`Assets/Prefabs/Props/Meshes/` folder was EMPTY. Second run: single node, base pivot, Meshes folder filled.

**Root cause:** `GlbPropPipeline.Run` reimported the freshly-optimized GLB bytes with
`AssetDatabase.ImportAsset(glb, ForceUpdate)` **inside** a `StartAssetEditing()`/`StopAssetEditing()` batch.
Asset editing batches **defer** reimports until `StopAssetEditing()`, so `GlbPropImporter.BakeOne` (called in
the same loop) `LoadAssetAtPath`'d the **stale pre-optimize hierarchy** — where the mesh still sits on a child
node (`mf.transform != root`), so the base-pivot vertex-bake (`BakePivotIntoMesh`) was skipped and only the
child-transform-shift path ran. The optimized single-node mesh only appeared after the batch closed → the
*next* run finally baked it correctly.

**Fix (`GlbPropPipeline.cs`):** reimport all optimized GLBs SYNCHRONOUSLY in a loop BEFORE opening the
`StartAssetEditing` batch; keep only the bake creates inside the batch. One run now suffices.

**Related gotcha (same file):** `AssetDatabase.CreateFolder` is likewise not visible mid-batch — calling
`EnsureFolders()` per-mesh inside the batch spawned duplicate `Meshes 1..N` folders. Create folders ONCE,
before batching.

**Pivot approach:** base pivot is baked into a saved copy of the mesh (verts shifted by `-offset`, RW mesh
required — optimized glTFast meshes are `isReadable=true`), single node, no wrapper parent. See
[[direction-2p5d-pivot]] and the pivot=base-anchor rule.
