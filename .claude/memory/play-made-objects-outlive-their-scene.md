---
name: play-made-objects-outlive-their-scene
description: Old character left standing after Select character — every Play-made object carries a do-not-save flag, and Unity never destroys a flagged object when its scene closes; it lingers in no scene.
metadata:
  type: project
---

**Symptom (2026-10-04/05, Mac and Windows builds):** after System window ▸ Select character, the character you left
stayed standing exactly where it was, in default colours, and one more copy piled up on every switch. Client only —
a fresh client saw nothing; the server removed the player correctly.

**Root cause (measured with a probe build driven through the agent port):** every object made during Play is
tagged `HideFlags.DontSaveInEditor` (or `DontSave`) so it can never be baked into a scene file. Unity does NOT
destroy an object carrying a do-not-save flag when its scene unloads — it is left in no scene at all
(`go.scene.IsValid() == false`), still active and drawn. Proven with two plain objects made side by side in Systems:
the flagged one survived the reload, the unflagged one did not. The worn player body (`Player3DBody.Configure`) is
such an object, and so were the ink, cloud-layer and speech-bubble layers and the target marker. `Configure`'s own
clean-up skips objects with no valid scene, so the leftovers were never removed. The codebase was written believing
a scene change destroys these (comments in `Vfx`, `DamagePopup`, `SpeechBubble`, `HandDrawnFX`, `SavedCharacters`).

**Fix:** `MapManager.DestroyWhatOutlivedItsScene`, called from both `OnSceneUnloaded` and `OnSceneLoaded`: destroys
every ROOT GameObject that is in no scene, carries the `DontSaveInEditor` bit, and is not `HideInHierarchy` (engine
internals, editor previews). Both callbacks are needed: a map unloaded additively is already out of its scene when it
reports closing, but a scene replaced by `LoadScene(Single)` still names itself as the objects' scene during its
closing callback (measured: closing-only removed the PREVIOUS switch's leftovers and left the current one).
`Afterimage.Step` now drops ghosts whose object is gone. Cost measured: 0–1 ms, 622–3696 objects looked at.

**Gotchas:**
- The 2026-10-04 editor census missed this because it filtered to `scene.IsValid()` — the exact property a leftover
  lacks. Census of leftovers: `Resources.FindObjectsOfTypeAll`, no scene filter, report `scene.IsValid()` instead.
- A release client build refuses plain http to anything but localhost: drive a probe build with
  `ACCOUNTS_URL=http://localhost:8080 CHAR_SERVER_URL=http://localhost:8081` set.
- The server had the same leak on every map it lets go (creature bodies are flagged); the fix covers it once the
  server is rebuilt.

Related: [[runtime-leftover-sweeper-findobjectsbytype-blind]], [[saved-character-filed-in-wrong-map]].
