---
name: glb-optimize-offline-gltftransform
description: Optimizing dense TRELLIS/Hunyuan GLBs — glTFast import has no mesh/texture knobs; re-encode offline with gltf-transform.
metadata: 
  node_type: memory
  type: reference
  originSessionId: 9ce8718f-88e1-4c2a-a3cd-89bc1478eeff
---

Trellis/Hunyuan GLB props arrive ~250k tris + 2× embedded 2048 textures (TRELLIS.glb 243k tris/19MB, TOWN_HALL_2 247k/24MB).

**Ground truth (read glTFast 6.19 source):** the glTFast importer (`com.unity.cloud.gltfast`) exposes ONLY mipmaps, textures-readable, filter mode, aniso, node-name, animation method — **no mesh compression, no texture-size clamp, no tri reduction**. Its `GltfImporter` class is `internal`, so an AssetPostprocessor can't set even those without reflection. So a Unity import-postprocessor CANNOT optimize these — that pitch is a dead end.

**Fix that works:** re-encode offline (Node is installed) — `tools/optimize-glb.sh` wraps `npx @gltf-transform/cli optimize` = weld + meshoptimizer quadric simplify (UV/normal error-bounded → no texture "squish") + texture downsize + geometry compression. Measured TRELLIS.glb at ratio 0.3 / tex 1024: 243k→73k tris, 19.2→4.6 MB (4.1×). Script defaults SAFE (writes `*_opt.glb`); `INPLACE=1` overwrites (keeps prefab GUID refs) with backup to `tools/glb_backups/`.

**⚠️ COMPRESS default = `quantize`, NOT `meshopt`** (fixed 2026-07-14). meshopt (`EXT_meshopt_compression`) is smaller but glTFast can only decode it if the *"meshoptimizer decompression for Unity"* package is installed — else the mesh imports EMPTY and the prop is UNDRAGGABLE (Inspector Report says so). `quantize` (`KHR_mesh_quantization`) imports natively with NO extra package. The earlier claim "glTFast imports EXT_meshopt_compression natively" was WRONG.

**One-click end-to-end:** `Tools ▸ unwritten ▸ Props ▸ Optimize + Import GLB Prop…` (`GlbPropPipeline.cs`) = optimize (shells the script) → reimport → bake via [[GlbPropImporter]]. Idempotent: mass path (empty selection) filesystem-scans `Assets/Prefabs` for `*.glb` and skips ones already baked into a `Props/<name>.prefab`; per-file skips the optimize pass if the GLB already carries `KHR_mesh_quantization` (detected by reading the GLB JSON chunk) so re-runs don't double-simplify.

**Gotchas hit building that tool (2026-07-14):**
- **`FindAssets("t:GameObject")` does NOT index glTFast `.glb` assets** → returned zero. Scan the filesystem (`Directory.GetFiles`) and map back to `Assets/…` paths instead.
- **Shelling bash from a Unity C# `Process`:** don't pass `D:/…` or `/d/…` abs paths — the bash flavor that fires (WSL uses `/mnt/d`, some MSYS launches don't auto-mount `/d`) may not resolve them. Set `WorkingDirectory=repoRoot` and pass **repo-RELATIVE** paths (`tools/optimize-glb.sh`, `unity/Assets/…`); resolves under every flavor. Also force Git Bash (`…\Git\bin\bash.exe`) and exclude `System32` from PATH search so you never grab WSL's `bash.exe`.
- **Script exit-code trap:** a trailing `[ test ] && echo …` as the script's LAST line returns 1 when the test is false (e.g. under `INPLACE=1`), so the whole script exits non-zero despite success and the C# wrapper flags failure. Use a plain `if … then … fi`.
- **`WaitForExit()` on the main thread freezes the editor silently** ("nothing happens" during npx cold-start) → poll `WaitForExit(200)` in a loop with `EditorUtility.DisplayProgressBar`.
- **`Renderer.bounds` is STALE/HALF for a freshly-instantiated glTFast QUANTIZED mesh** (the dequant scale isn't applied to bounds yet at bake time) → the base-recenter in `GlbPropImporter.NormalizeScaleAndPivot` under-shifted the pivot (landed ~¼ up the mesh) and doubled the footprint scale. FIX (2026-07-14): compute bounds from each `MeshFilter.sharedMesh.bounds` transformed by `root.worldToLocalMatrix * child.localToWorldMatrix` (mesh-asset space, always correct), never `Renderer.bounds`. Verified via glb node parse (POSITION ±32767 normalized = ±1, node T≈0 = mesh centered) + live prefab bounds (base −1.19, pivot 0). See [[pivot-base-anchor]].
- **MCP diagnosis:** `execute_menu_item` returns `success:false` for a nonexistent path but `true` for a real one — so it confirms an item is registered/invoked; a modal `DisplayDialog` then auto-cancels under MCP (no log = returned at a dialog).

In-Unity alternative for by-eye control: `MeshDecimateWindow` (`Tools ▸ unwritten ▸ Props ▸ Decimate Selection…`, UnityMeshSimplifier) — but that's per-mesh manual. See [[performance-first-prebake]].
