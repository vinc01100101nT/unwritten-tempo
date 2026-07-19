---
name: runtime-leftover-sweeper-findobjectsbytype-blind
description: RuntimeLeftoverSweeper reported swept=0 while leaked runtime objects lingered — FindObjectsByType skips DontSave objects; use Resources.FindObjectsOfTypeAll.
metadata: 
  node_type: memory
  type: project
  originSessionId: 63cec94b-a131-4da7-b3d6-12bdfea65b00
---

**Symptom:** After Stop Play, runtime combat objects (GoldPickup/ItemPickup from `LootDropper`, damage text, VFX) stayed **rendered in both Scene AND Game view**, were selectable (Inspector showed real `GoldPickup` + SpriteRenderer), survived a Gizmos-off toggle and `SceneView.RepaintAll()` — but were **absent from the Hierarchy**. `RuntimeLeftoverSweeper` (auto on Play-exit + `Tools ▸ unwritten ▸ Sweep Runtime Leftovers`) reported **swept 0** every time.

**Root cause (measured, from Unity docs):** the sweep enumerated with `Object.FindObjectsByType`, which is documented to **NOT return objects that have `HideFlags.DontSave` set**. Every runtime object in this project is tagged `HideFlags.DontSaveInEditor` (a DontSave bit) so it never bakes into a scene — so FindObjectsByType skipped **exactly** the objects the sweeper existed to destroy. The `0` was honest; the sweeper could never see them. Compounding it: a `DontSaveInEditor` object that survives Play-exit becomes **sceneless** (belongs to no loaded scene) → gone from the per-scene Hierarchy but still a live renderer, and FindObjectsByType only searches loaded scenes.

**Fix:** `unity/Assets/Scripts/Editor/RuntimeLeftoverSweeper.cs` `Sweep()` — enumerate with `Resources.FindObjectsOfTypeAll<GameObject>()` (returns sceneless + hidden + DontSave objects), then filter: skip `EditorUtility.IsPersistent(go)` (asset/prefab on disk), keep roots only, require `DontSaveInEditor`, skip `HideInHierarchy` (editor-window previews like composer snapshots). Also added `SceneView.RepaintAll()` on Play-exit (harmless belt-and-suspenders).

**Gotchas:**
- Auto-sweep only fires if the project **compiles cleanly** — `[InitializeOnLoad]` won't register the `playModeStateChanged` callback (and the menu item won't exist) under a compile error. If no `[unwritten] RuntimeLeftoverSweeper: swept…` line appears on Stop, suspect a compile error, not the sweeper.
- Debugging leftover-at-edit objects: "not in the Hierarchy but selectable + renders in Game view" == **sceneless runtime object**, not a stale framebuffer ghost. A repaint clears stale pixels; if a repaint DOESN'T clear it, it's a live object. Distinguish early.
- Related: [[perf-runtime-hotpath-patterns]] pool hosts, the `HideFlags.DontSaveInEditor` phantom-bake prevention rule in project CLAUDE.md.
