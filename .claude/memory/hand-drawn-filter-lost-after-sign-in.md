---
name: hand-drawn-filter-lost-after-sign-in
description: Ink outline (HandDrawnFX) vanished in Play after sign-in — the map manager's leftover sweep deleted it on the Systems reload and its re-create check saw it still alive; later re-creates landed in a map and died at the next door.
metadata:
  type: project
---

**Symptom (2026-10-16):** in Play inside the editor the hand-drawn ink outline is gone from the world; HandDrawnFX.Instance was null and no HandDrawnFX / InkQuad existed in any scene (read with Resources.FindObjectsOfTypeAll — FindAnyObjectByType is blind to do-not-save objects, see [[runtime-leftover-sweeper-findobjectsbytype-blind]]).

**Measured:** the editor log line `[map] a scene opened — 10 thing(s) made during play went with it: FrontCamera, InkQuad, HandDrawnFX, …` on every sign-in (Systems reloaded with LoadScene Single leaves do-not-save objects in no scene; MapManager.DestroyWhatOutlivedItsScene deletes them at end of frame). MapManager subscribes sceneLoaded at BeforeSceneLoad, so it runs before HandDrawnFX's handler, which then saw Instance still non-null and never replaced it. A later map load did re-create it, but into the active MAP scene, which the next door unloaded.

**Fix:** HandDrawnFX.OnSceneLoaded treats an instance in no scene as gone; Create() parks the filter in Systems via MapManager.KeepAcrossMaps; InkQuad is a child of the filter. Weather quads were never hit because their owners rebuild them; the filter is its own owner.
