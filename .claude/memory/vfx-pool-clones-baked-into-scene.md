---
name: vfx-pool-clones-baked-into-scene
description: "Phantom water splashes at load = pooled Vfx clones + VfxSystem host accidentally SAVED into Systems.unity in edit mode; delete the root, no code bug."
metadata: 
  node_type: memory
  type: project
  originSessionId: f6401eeb-41fd-4b92-a045-5471a0b08e52
---

**Symptom:** On game load, ~3 water splashes flash bottom-of-screen (first frame, FPS 0) and the hierarchy
shows **~55 `Water_Splash_A(Clone)`** under a runtime **`VfxSystem`** object. Idle (particleCount=0) shortly after.

**Root cause (measured, not reasoned):** NOT a runtime spawner. The 55 clones AND the `VfxSystem` pool host
were **baked into `Assets/Scenes/Systems.unity`** as plain saved GameObjects. A prior debugging session ran
`Vfx.Prewarm` / an instantiate **in EDIT mode** (likely via MCP `execute_code`) — that created the pool host +
55 disabled clones as real edit-mode scene objects — then the scene got saved with them in. So they exist on
frame 0 of every load with no spawner running.

**Why the earlier handoff chased the wrong thing:** it grepped `Systems.unity` for the splash prefab GUID
(`224c4574d4d272a4b92df19ede61b338` = `Water_Splash_A.prefab`) and found **0** hits → wrongly concluded
"not scene-placed." But `Object.Instantiate` clones lose the prefab link when saved, so they sit in the scene
as plain GameObjects named `Water_Splash_A(Clone)` — grep by NAME, not by prefab GUID. Also: `Vfx.Rent` probe
never fired and `Vfx.Prewarm` has zero live callers (only WaterStep.cs commented-out line) — both facts are
consistent with "already in the scene, nobody spawns them at runtime."

**Fix:** delete the baked `VfxSystem` root from `Systems.unity` (took all 55 children with it), save. Done via
MCP `execute_code`: `EditorSceneManager.OpenScene(path, Additive)` → find root `VfxSystem` → verify ALL children
are `Water_Splash*` (guard against nuking a legit child) → `DestroyImmediate` → `SaveScene` → `CloseScene` to
restore the user's view. `VfxSystem` is a **runtime-only pool host** (`Vfx.EnsureHost`) — it must NEVER appear
in a saved scene; its presence in a `.unity`/`.prefab` is itself the bug.

**Structural fix now in code:** every runtime pool host + pooled instance is tagged
`hideFlags = HideFlags.DontSaveInEditor` at creation so it can never be serialized into a scene again —
`Vfx.EnsureHost` + `Vfx.NewInstance` (new centralized instantiate for Rent pool-miss + Prewarm),
`DamagePopup.EnsureHost` (`DamagePopupSystem`), `MobNameplate.EnsureHost` (`MobNameplateSystem`),
`VfxDirector.EnsureHost`. This flag is inert at runtime (only blocks editor scene-save), zero per-frame cost.

**Gotcha / prevention (still holds):** don't run `Vfx.Prewarm` or any pool instantiate via `execute_code`
while a persistent scene (`Systems.unity`) is open and could be saved — the DontSaveInEditor guard now catches
this, but treat it as belt-and-suspenders. When DEBUGGING a phantom-at-load object, grep the `.unity` by
GameObject NAME (`m_Name: Foo(Clone)`), NOT by prefab GUID — saved clones drop the prefab link, so a GUID grep
falsely reports "not in any scene" (this is what misdirected the original handoff). See
[[changes-must-persist-in-repo]], [[vfx-effect-library]].
