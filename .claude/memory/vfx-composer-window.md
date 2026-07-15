# Vfx Composer — edit-mode skill-VFX authoring window (DONE 2026-07-01)

**Status:** SHIPPED + user-verified in edit mode (`unity/Assets/Scripts/Editor/VfxComposerWindow.cs`). URP + `PreviewRenderUtility` particle rendering WORKS (the flagged risk cleared). DELETED all 3 old tuners (+ .meta): F8 `EffectTuner.cs`, F9 `SequenceTuner.cs`, editor `EffectPreviewWindow.cs` ("Effect Preview" row pulled from TOOLS.md), plus the F8-only `EffectHandle.TuneLive` from `Vfx.cs`. ⚠️ GAP (intended per design): F8/Effect-Preview were the only UIs that wrote an effect's BASELINE `scale/offset/lifetime` into `EffectLibrary.asset`; the composer only tunes per-composition overrides (multipliers on authored values). To fix a bad baseline now: edit `EffectLibrary` in the Inspector, or add a "baseline" mode to the composer.

## Gotchas found during the build (reusable if you touch editor particle previews)
- **Transparent floor = everything draws behind it.** A `Sprites/Default` (ZWrite-off) ground plane painted OVER the character/effect by sort order. FIX: ground uses an OPAQUE depth-writing material (`Universal Render Pipeline/Unlit`, `_BaseMap`). Rule: the preview floor MUST write depth.
- **Character ruler grounding**: baked strip frame has transparent padding below the feet → bottom-of-CELL pivot floats. FIX: `TryOpaqueRows` GPU-readback (temp RenderTexture + `ReadPixels`, works on non-readable baked strips) finds lowest/highest opaque rows → pivot at feet + scale ART height (not padded cell) to `rulerHeight`.
- Ruler is a camera-facing billboard (always shows front, no 8-dir cycle) — intended for a size reference.
- `SfxSet` is a plain `[Serializable]` class, NOT a `UnityEngine.Object` → can't `ObjectField` it (its `AudioClip[]` clips ARE Objects — edit those inline).
- Resizable: EditorWindow + a draggable splitter under the preview (`previewHeight`, persisted via `EditorPrefs`).

## What it is
Single edit-mode `EditorWindow` (`Tools ▸ unwritten ▸ Art ▸ Vfx Composer`) that REPLACES the in-Play **F8 EffectTuner** + **F9 SequenceTuner**. Authors composed VFX against a true-world-scale character ruler, no Play mode.

## KEY DISCOVERY — the runtime/data model already existed
The user's whole design was already embodied in code; we build only the authoring front-end, reusing (NOT forking) the data model:
- `VfxSequence` + `VfxStep` = the composition: flat timeline, per-clip `offset`(xyz)/`scale`/`speed`/`delay`(timestamp)/`effectId`. Overrides live ONLY in the SO; source prefab untouched (so one fireball → many sizes).
- `VfxDirector`/`VfxSequenceRunner` already runs it; `Vfx.EffectHandle.Stop()` + `Attach` loop = code-owned kill switch; `Carrier.carrierMaxLife` + `EffectHandle.MaxOneShot`(30s) = the safety cap.
- Did NOT rename `VfxSequence`→`VfxComposition` (would break existing .asset script refs); kept the type, named the TOOL "Composer".

## Locked design decisions (from the design chat)
1. **Event-decoupled phases, no design maxLife.** Keep a composition to ONE beat (travel OR on-hit); code stitches them because code knows when a skill lands. Travel = loop code stops on hit; on-hit = separate reusable composition.
2. **Kill switch lives in the VFX RUNTIME, not each caller** (centralization rule). Caller owns *intent* (Stop now); runtime owns *safety* (reap ceiling). One-shots get a ceiling; loops (aura/buff) are ceiling-exempt because a clear owner (the status/buff) always stops them.
3. **ScriptableObject is the right store.** Per-effect values live only in the asset.
4. **Editor-only preview via `ParticleSystem.Simulate`** off an editor clock (`EditorApplication.update`) — no Play. True-scale capsule ruler + ground grid as the size reference.

## How the window works
Ported F9's proven engine verbatim: `RebuildPreview` (Instantiate prefab → deterministic `randomSeed` → force `scalingMode=Hierarchy` → cache root systems) + `ResolveStarts` (AtTime/OnEvent chains, carrier emits on arrival) + `RenderPreviewAt(T)` (re-bake whole composition at clock T so edits show live). Preview renders in-window via `PreviewRenderUtility` (isolated preview scene, orbit/zoom cam). Asset mgmt via `AssetDatabase` (New/Dup/Del), edits wrapped in `Undo.RecordObject`+`SetDirty`, Save = `AssetDatabase.SaveAssets`.

## RISKS to verify (I could not, Unity closed)
- **URP + `PreviewRenderUtility`**: preview camera may render pink/nothing, and particle `Simulate` inside the preview scene is the main unknown. If broken, fallback = dedicated preview Camera→RenderTexture in a `NewPreviewScene`, or render in the real Scene view with hidden/dontsave instances.
- Undo records AFTER the edit (minor correctness slip), acceptable.

## Also noted
- A THIRD tuner exists: editor `EffectPreviewWindow.cs` ("Effect Preview", scene-based single effect) — likely retire later too; user only asked to delete F8/F9.
- On delete: remove `EffectTuner.cs`(F8)+`SequenceTuner.cs`(F9) + `.meta`. Check `EffectHandle.TuneLive` (F8-only) refs before pruning it. CONFIRM before deleting (destructive).
