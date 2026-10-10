# Rebuilding an asset in place can blank every reference to it, or leave it drawn as nothing

**Reach for this when:** after a script rebuilt or re-created an asset, something lost it, even though the asset is
on disk and kept its ID. The symptoms are:
- characters stand in a T-pose;
- a material shows pink;
- a field reads "None";
- a prefab's mesh reads null;
- a rebuilt mesh is simply not drawn, with the console saying `Rendering stopped because the data for mesh '…' does
  not match the expected mesh data size and vertex stride`.

## The rule

There are three ways to rewrite an asset a script has already made, and only one of them is safe:

1. **Writing a NEW asset over the path** (`CreateAsset`, `CreateAnimatorControllerAtPath`, or delete then create)
   keeps the file's ID on disk, but destroys the object in memory. Every open scene and loaded prefab that pointed at
   it now points at nothing, and saving writes the blank.
2. **Copying serialized data over the live object** (`EditorUtility.CopySerialized`) keeps the links, but a Mesh copied
   that way keeps its old drawing data in the running editor. Once the vertex count changes, it is drawn as nothing.
3. **Rewriting the asset's contents in place, through its own API** is the one that keeps every link and every
   drawing buffer correct. For a mesh that means `Clear()`, then `SetVertices`, `SetNormals`, `SetUVs`,
   `boneWeights`, `bindposes`, `subMeshCount` and `SetTriangles`. For a prefab it means
   `PrefabUtility.LoadPrefabContents` → edit → `SaveAsPrefabAsset` → `UnloadPrefabContents`, which also keeps the
   root object's ID, so anything linking to the prefab keeps working.

After any script rebuild of a shared asset:
- read every reference back from memory, not from disk;
- render the result once;
- read the console.

## Why

- **2026-10-09.** The attack animations were wired in by rebuilding the shared character animator controller with
  the bare `Player3DVisualBuilder.BuildController()`.
  - The check afterwards was "the controller kept its ID". That was true, and the wrong question.
  - The open Systems scene's player visual had lost its controller in memory. The user's next build saved the scene,
    writing `m_Controller: {fileID: 0}`. The server and client builds made from it had no animation at all, so every
    3D character T-posed in game.
  - The project's own Stance Composer path (`RebuildActiveFromLibrary`) re-attaches the controller after rebuilding
    for exactly this reason; calling the builder directly skipped that.
  - Cost: a play session and a full round of diagnosis, plus rebuilding both the client and the server.
- **2026-10-10.** `HeadPieceBaker` re-baked 51 bare bodies and 28 head pieces over their earlier copies.
  - **With `CopySerialized`,** the Elf, Witch and Wizard bodies, whose vertex counts had changed, were drawn as nothing
    in every picture. The console said "does not match the expected mesh data size and vertex stride" 26 times. An
    `Instantiate`d copy of the same mesh drew correctly, which proved the data was right and the live object was
    stale.
  - **The "fix" of deleting and re-creating each mesh** then left all 79 prefabs reading a null mesh in memory while
    their files on disk were correct.
  - **Also, saving a fresh GameObject over an existing prefab** gives its root a new ID, which would have broken the
    15 Head items that link to the hat prefabs.
  - Cost: three re-bakes and a round of diagnosis before the third way above was found.

## How to apply

- Before rebuilding a shared asset from a script, list what references it in open scenes; afterwards, compare each
  again.
- Prefer the project's own rebuild entry point when there is one — it usually carries the re-attach.
- **For a mesh:** rewrite it in place through the Mesh API (see `HeadPieceBaker.Rewrite`); never `CopySerialized`,
  never delete and create.
- **For a prefab others link to:** `LoadPrefabContents` → edit → save, so its root keeps its ID. Check it with
  `AssetDatabase.TryGetGUIDAndLocalFileIdentifier` before and after.
- **Measure from memory, not disk:** load each prefab and read its mesh or controller field. To check a scene, use
  the scene-copy diff (save a COPY of the open scene and diff it against the file); a reference turned into
  `{fileID: 0}` shows up as one line.
- **Render once and read the console:** a stale mesh is only visible as missing pixels and the "vertex stride"
  error.
- The server's log names a lost controller at startup: `Character3DPose: "…" has no AnimatorController — posing
  skipped for it.`
- If the editor already holds stale objects, force a reimport of the folder
  (`AssetDatabase.ImportAsset(folder, ForceUpdate | ImportRecursive)`) before measuring again.
