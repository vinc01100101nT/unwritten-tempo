---
name: layout-group-dirties-scene-on-reload
description: "Modified Scenes must be saved" at every build — Unity's own components saved into a scene (a layout, a fontless text) rewrite themselves in the editor; banned by check-scene-stays-clean, and build-game.sh settles a no-change mark itself.
metadata:
  type: project
---

**Symptom:** every build (tools/build-game.sh) stopped at "Unsaved Scenes — Modified Scenes must be saved to continue"
(the Addressables content step refuses any dirty open scene, with a dialog that froze the editor 7m49s on 2026-10-09).

**Root cause (measured 2026-10-09 with an [InitializeOnLoad] probe on `EditorSceneManager.sceneDirtied` +
`Undo.postprocessModifications`):** the GameUICanvas prefab (placed in Systems) carried a stale `HorizontalLayoutGroup`
on `HUD_Hotbar`. After a code compile + script reload Unity itself (no game code in the stack) recorded resets of the
4 `Hotslot` positions to their saved 0,0 and marked Systems dirty — the file was byte-identical. A plain
`RequestScriptReload` with no compile did not reproduce it. Separately, 4 calendar texts were baked with NO font
(UiKit.Font used to be created at runtime, so the bake saved null); the scene carried font overrides TMP had written.

**General fix (2026-10-09):**
- `tools/check-scene-stays-clean.py` (+ prover, wired into the end-of-turn gate): no layout component and no fontless
  text in any scene or any prefab a scene places.
- UiKit.Font is now the SAVED asset `Resources/Fonts/DejaVuSansCondensed SDF.asset`, so baked text carries a font.
- UIShellBuilder bakes no layout; HotbarPanel adds the grid at runtime.
- `tools/build-game.sh` checks every dirty open scene first: a saved copy identical to disk → saves and goes on;
  different → stops before deleting the old build and prints the diff.

**Still unexplained:** a scene override on `CalendarPanel/Fill/Content` `m_AnchoredPosition.y` equal to the prefab value
(history shows -15.0000305 noise) kept reappearing; reverted 2026-10-09, writer not caught (DirtySceneProbe left in
Editor/ to catch it).

Related: [[hud-build-process]]
