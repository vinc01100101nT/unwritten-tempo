---
name: perfhud-imgui-self-pollution
description: "PerfHud's own OnGUI/IMGUI allocates ~15KB/frame — it swamps the GC/s number it reports (observer effect)."
metadata: 
  node_type: memory
  type: project
  originSessionId: 6a9c1bd1-4e5a-44c8-bf7c-ebfabab6396f
---

**The F3 PerfHud overlay pollutes its OWN GC/s reading.** MEASURED live (81 mobs, in-editor Play, via `ProfilerRecorder` "GC Allocated In Frame" sampled over ~150 frames while toggling suspects):

- Baseline: **22.5 KB/frame**.
- Disable all 81 mobs' AI+pathing → 20.2 KB. Disable per-mob render/nameplate comps → 20.8 KB. Clear `MonsterAI3D.Active` → 20.4 KB. (So the mobs add only ~2 KB/frame — the pooled/Addressable mob system is fine.)
- **Disable `PerfHud` → 6.8 KB/frame.** So PerfHud alone was **~15.7 KB/frame** — the dominant allocator.

Root cause: `PerfHud.OnGUI()` used IMGUI (`GUI.Label`, `GUI.DrawTexture`), which allocates internally EVERY frame, and OnGUI runs twice/frame (Layout + Repaint). The old doc-comment wrongly claimed it "only allocates rebuilding text 4×/sec" — the real cost was the per-frame IMGUI draw. First trim (Repaint-only guard + cached `GUIContent`) cut it 15.7→10.9 KB/frame AND frame time 18→10.6 ms (FPS 55→95) but IMGUI still allocated ~11 KB/frame internally. **Final fix: rewrote PerfHud from IMGUI to a self-booting uGUI overlay Canvas + `TextMeshProUGUI`** (no `OnGUI`); text is written via `label.SetText(StringBuilder)` only 4×/sec (no per-frame string), the canvas draws allocation-free, non-interactive (no GraphicRaycaster). PerfHud is `#if UNITY_EDITOR || DEVELOPMENT_BUILD` only, so it's ZERO in a release build regardless — the inflated GC/s never shipped anyway; the rewrite just makes the in-editor meter read the game's true ~0.

**Lessons:**
- When reading GC/s off the in-editor HUD, subtract the HUD's own floor — or judge from a Development Build (per PERFORMANCE.md). A high in-editor GC/s is often the meter + editor + IMGUI, not the game.
- **`FindObjectsByType`/`FindFirstObjectByType` OMIT `HideFlags.DontSaveInEditor` objects** — the spawned mobs AND PerfHud (both DontSaveInEditor) were invisible to it (`FindObjectsByType<MonsterAI3D>` returned 1 of 81). Use `MonsterAI3D.Active` for mobs and `Resources.FindObjectsOfTypeAll<T>()` to reach hidden objects. Same scar as [[perf-runtime-hotpath-patterns]] / the DontSave-omission memory.

Related: [[perf-runtime-hotpath-patterns]], [[mob-summon-addressables]], [[performance-first-prebake]].
