# The Editor Draws the Mesh You Overwrote

**Reach for this when** a generated mesh still LOOKS like an earlier build in the editor — an old colour band, an old
shape — while reading the mesh's own data says it is right.

## The rule

A mesh asset overwritten in place with `EditorUtility.CopySerialized` keeps drawing its OLD data in the editor, even
after a re-import or `Mesh.MarkModified()`. Write every channel through the mesh's own setters instead
(`Clear()`, then `SetVertices`, `SetNormals`, `SetColors`, `SetUVs`, `SetTriangles`). And to tell stale drawing apart
from wrong data, draw a fresh copy (`Object.Instantiate(mesh)`) next to it: if the copy looks right, the data is right
and only the drawing is stale.

## Why

2026-10-26, the seamless test world: a soil band an earlier build painted along Lastline's edge stayed on screen after
the builder removed it. The mesh's vertex colours read back as plain grass, a raycast's colour at the spot read grass,
and two reasoned fixes (a forced re-import, `MarkModified`) changed nothing. A render with a fresh copy of the same mesh
showed grass at once; setting the colours again on the asset fixed it. It cost three render rounds, and the user's
in-editor play-test showed the stale ground in the meantime. Deleting the folder and creating the meshes anew was
worse: the editor then held prefabs whose meshes read as missing until re-imported.

## How to apply

- In a builder that rewrites mesh assets: load the existing asset, `Clear()` it, set each channel from the new mesh,
  `SetDirty`, `SaveAssetIfDirty`. Never delete-and-recreate at the same path in one run.
- When a picture disagrees with the data: before any more theories, render once with
  `filter.sharedMesh = Object.Instantiate(filter.sharedMesh)` on the suspect objects.
