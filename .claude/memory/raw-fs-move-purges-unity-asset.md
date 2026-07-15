---
name: raw-fs-move-purges-unity-asset
description: Moving Unity assets with raw filesystem mv while the editor is OPEN can make Unity purge an inconsistent asset + its deps (lost Oak_Tree cluster) — use AssetDatabase.MoveAsset.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7665eeea-83a0-47f1-b61d-b469d7be032d
---

Reorganizing `Assets/Prefabs/Props/` into subfolders (Trees/Plants/Indoor/Outdoor/Colliders) via raw
Git-Bash `mv` (moving each `.prefab` + its `.prefab.meta` together) worked fine for ~150 prefabs — GUIDs live
in the `.meta`, so scene/recipe references (stored by id) stayed intact. BUT one prop, **`Oak_Tree`**,
DISAPPEARED entirely: git showed `Oak_Tree.prefab`, `Meshes/Oak_Tree.mesh`, and `Materials/Oak_Tree_0*.mat`
all Deleted — including the mesh/materials I never touched.

**Root cause (measured from git, mechanism is a strong hypothesis):** Unity was running. `Oak_Tree` was the one
GLB-imported prop with SEPARATE dependent files in `Props/Meshes/` + `Props/Materials/`, and it was already
mid-edit (modified at session start). Moving its prefab out from under a live Unity via raw FS left an
inconsistent asset, and Unity purged the whole cluster (prefab + mesh + materials). The other ~150 prefabs had
no external Meshes/Materials deps and survived cleanly.

**How to apply:** When Unity is OPEN, do NOT move/rename assets with raw `mv`/filesystem ops — use
`AssetDatabase.MoveAsset` via MCP `execute_code` (it updates Unity's DB atomically), or move only while Unity
is CLOSED. Raw FS moves are only safe for the common case (prefab has no external mesh/mat deps); a GLB prop
with files in `Meshes/`+`Materials/` is exactly the case that breaks. Recovery: tracked files → `git checkout
HEAD -- <paths>`; untracked (e.g. an accidental `Foo 1.prefab` duplicate) → gone, not in git.

Related: [[act-editor-actions-via-mcp]], [[unity-mcp-bridge]]. Oak_Tree was intentionally retired here (new
Pxltgr Oak_tree1/2/3 replace it), so no recovery was done.
