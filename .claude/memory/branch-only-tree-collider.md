---
name: branch-only-tree-collider
description: "Tree collider baker — trim single-mesh tree collision to branches only by texture colour, so leaves are jump-through"
metadata: 
  node_type: memory
  type: project
  originSessionId: ce596b52-de17-4dd5-a712-63f4135e88f2
---

Trellis trees are ONE fused mesh (trunk+branch+leaf), so their full `MeshCollider` makes leaves solid and walls off jump paths between branches. **`Tools ▸ unwritten ▸ Props ▸ Bake Tree Collider…`** (`TreeColliderBaker.cs`) fixes this: eyedrop a **Bark** and a **Leaf** reference colour off the rendered tree, and each triangle is kept solid or dropped by which colour its texture sample sits nearer (a **Bark bias** dial tips borderline patches). Keeps only bark triangles → new collision mesh → overwrites the collider in place.

Key facts:
- Colour lives in the **texture** (sampled per-triangle via UV centroid, `GetPixelBilinear` + wrap), NOT per-vertex — Trellis meshes have no vertex colours.
- Sampling needs the texture flagged **readable**; the tool flips `TextureImporter.isReadable` on temporarily and restores it.
- Persists to the PREFAB: Prefab Mode → `SaveAsPrefabAsset(stage.prefabContentsRoot, ...)`; prefab asset selected in Project → `SavePrefabAsset`; scene instance → `ApplyPrefabInstance`.
- Result is a tuning dial, not surgical (Oak_Tree first pass ≈4% tris solid = too thin; drag Bark bias RIGHT for more solid branches).

GOTCHA (the bake-crash bug): always trim from the **visible mesh** (`MeshFilter.sharedMesh`) — it's guaranteed present and never swapped. Trimming from the collider's own mesh slot crashed (NRE) because that tree's `MeshCollider.sharedMesh` was null; the stash of the pre-bake collider mesh is for **Revert only**, never the trim source.

**Pxltgr trees (current tree set) don't need the colour guess.** Their mesh is split into two sub-parts by material — slot 0 = bark, slot 1 = leaves — consistently across all 15 prefabs in `Prefabs/Props/Trees`. So `TreeBarkColliderBaker.cs` (**`Tools ▸ unwritten ▸ Props ▸ Bake Bark-Only Tree Colliders`**) just keeps sub-part 0's triangles: bakes a `Colliders/<mesh>_bark.mesh` per tree and re-points the MeshCollider, no eyedropping. Idempotent batch over the Trees folder. The colour-based `TreeColliderBaker` is only for fused single-material meshes (Trellis). Applied 2026-07-15 (leaf tris dropped from every tree collider).

See also [[glb-optimize-offline-gltftransform]], and TOOLS.md is the canonical tool catalog.
