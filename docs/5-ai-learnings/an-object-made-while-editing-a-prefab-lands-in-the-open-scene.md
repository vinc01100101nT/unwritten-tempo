# An Object Made While Editing a Prefab Lands in the Open Scene

**Reach for this when:** a script that adds objects to prefabs reports success, but the saved prefab does not have them — or stray objects with the new names turn up in whatever scene is open in the editor.

## The rule

`new GameObject(...)` always creates the object in the editor's **active scene**. Opening a prefab's contents with `PrefabUtility.LoadPrefabContents` does not change that, so the object only becomes part of the prefab once it is parented under the prefab's root. Parent every new object explicitly, and when the "sibling" you are copying is the prefab's own root (it has no parent), put the new object **under** the root, not beside it.

## Why

On 2026-10-11 a level-of-detail baker added each simpler mesh as a sibling of the renderer it copied (`SetParent(original.parent)`). On House_A the renderer sat on the prefab's root, so its parent was null, and the new objects stayed in the scene the user had open (lastline). The prefab saved with a level switch pointing at nothing, so the simpler level drew as empty space. It took one wasted bake and a rendered picture to notice. Two runs left four stray objects in the user's scene. That scene was not marked as changed, so nothing was saved into it, but a later save of that scene would have written them into the user's hand-made town.

## How to apply

- When adding objects in prefab-contents mode: `go.transform.SetParent(parent != null ? parent : root, false)`. Never leave it unparented.
- After any bulk prefab edit, read back one result and check the new objects are inside it (`GetComponentsInChildren` on the loaded asset), and list the open scene's roots for strays:
  ```
  foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects()) if (r.name.Contains("<new name part>")) ...
  ```
- Run the first pass on ONE asset whose renderer sits on its root, since that is the case that breaks.
