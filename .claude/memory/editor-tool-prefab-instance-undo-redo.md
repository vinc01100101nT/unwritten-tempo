---
name: editor-tool-prefab-instance-undo-redo
description: Editor tool changing a prefab-instance property — Ctrl+Z works but Ctrl+Y (redo) silently does nothing without RecordPrefabInstancePropertyModifications
metadata: 
  node_type: memory
  type: project
  originSessionId: 581c7110-5382-4038-92d2-ccfb6b8b0c74
---

**Symptom:** an editor tool that mutates a component on a scene object (e.g. `Stylize Selected Solid Prop`
swapping `MeshRenderer.sharedMaterials`) — undo (Ctrl+Z) reverts it correctly, but redo (Ctrl+Y) does NOT
re-apply. Also caused a confusing "detected 0" on a re-run: the first apply stuck (undo hadn't been wired at
all yet), so the second run saw the object as already-changed and skipped it.

**Root cause (measured, not guessed):** most props here are PREFAB INSTANCES. A property change on a prefab
instance is stored as a prefab OVERRIDE. `Undo.RecordObject(rend, ...)` records the state for UNDO but the
override change is NOT in the REDO diff — so redo leaves the renderer on its original material. Proven by
replaying `Undo.PerformUndo()`/`PerformRedo()` offline: a plain Cube (not a prefab) redid fine; the real
`House_B` (prefabInstance=True) redid to the ORIGINAL material every time.

**Fix (`RecipeGenerator.cs`, ApplyStylizedLitToSelected):** wrap the op in one undo group
(`Undo.IncrementCurrentGroup` + `SetCurrentGroupName` + `CollapseUndoOperations`), call
`Undo.RecordObject(rend, ...)` BEFORE assigning `sharedMaterials`, and AFTER the assign call
`PrefabUtility.RecordPrefabInstancePropertyModifications(rend)` when `PrefabUtility.IsPartOfPrefabInstance(rend)`.
That last call is what makes redo work. Verified live: before→apply→undo→redo = orig→styl→orig→styl.

**Gotchas / reusable facts:**
- `Undo.RecordObject` is NOT enough for prefab-instance changes — always pair with
  `RecordPrefabInstancePropertyModifications` for redo.
- The sibling tools `Sway Selected Tree` / `Apply Foliage Wind` mutate `sharedMaterials` with NO Undo at all —
  same class of bug (no undo AND no redo); fix them the same way if it comes up.
- MCP `execute_code` uses the C#6 CodeDom fallback here: NO local functions, NO `using` directives in the body,
  disambiguate `UnityEngine.Object.FindObjectsOfType`. Loops instead of lambdas.

Related: [[act-editor-actions-via-mcp]].
