---
name: damagepopup-pooled-clone-savable-gap
description: Scene-bloat prevention — every runtime-created ROOT object is tagged DontSaveInEditor (project-wide sweep 2026-07-19); children of a flagged parent are NOT saved (measured).
metadata: 
  node_type: memory
  type: project
  originSessionId: 1b3d7f27-afbe-4d15-8cdd-5cd4baf5499a
---

Phantom-at-load / scene-bloat class bug (Water_Splash(Clone) bake, DamagePopup edit-mode junk, a full 33-object `Casual3_Male(Clone)` rig found baked at origin in `town_0.unity` and deleted 2026-07-19). Root cause of every instance: an object created at runtime (or by an edit-mode `execute_code` session) with no `HideFlags.DontSaveInEditor`, sitting in an open scene when the scene got saved.

**MEASURED serialization fact (probe scene, 2026-07-19):** `SaveScene` with a `DontSaveInEditor` parent + an UNTAGGED child → **neither** appears in the saved file (`parentInFile=False, childInFile=False`, untagged loner root saves normally). So **tagging the ROOT of a runtime-created hierarchy is sufficient** — pooled children under a flagged host are covered automatically. (Per-instance tags like `DamagePopup.cs:143` are harmless belt-and-suspenders, not required.)

**Project-wide sweep (2026-07-19):** every runtime creation site in `Assets/Scripts/` (non-Editor) now tags its root with `DontSaveInEditor` — pool hosts (SpeechBubbleSystem, VfxSequenceRunner…), all weather/env `Ensure()` hosts (Fog/Rain/Snow/Wet/Cloud/Sky/Wind/Planar/FootstepTracker…), bootstraps (SaveManager, PerfHud, DevSettings, WaterStep, ImguiClickShield…), combat FX (Projectile3D, SlashEffect, StatusFX, GuardShield, MultiHitBurst, targeters), pickups, AmbientLife townsfolk clones, GmCommands spawn clones, and runtime children of SAVED parents (WeaponSocket mount, Ears, SfxVoices, DummyHP, drag ghosts, TooltipLayer, Minimap camera/blips, DeathScreenCanvas). Deliberately NOT tagged: `UiKit` helpers (shared by editor scene-bakers — tagging would silently drop baked HUD), `NightLight`/`PropLight` reuse-or-create children (idempotent by design), and `Editor/` tools (intentional saves).

**Tripwire:** `Editor/SceneSaveBloatGuard.cs` — on every edit-mode scene save, warns (never deletes) about any savable `*(Clone)` object in the scene. Safe because all legit paths either rename, destroy, or preview-render their clones.

**Gotcha #2 — leftover pool hosts after Play (safety net, not always needed):** pool hosts (DamagePopupSystem, VfxSystem ×53 clones, CastBarSystem) were once seen lingering in the EDIT-mode hierarchy after a play session (white RectTransform outlines in Scene view) — harmless (inactive + unsavable) but ugly. `Editor/RuntimeLeftoverSweeper.cs` sweeps them: on `playModeStateChanged → EnteredEditMode`, DestroyImmediate every ROOT with `DontSaveInEditor` that is NOT `HideInHierarchy` (skips editor-window previews, which use HideAndDontSave). It ALWAYS logs its count (even 0) so "no log" can't be mistaken for "didn't run".
**MEASURED 2026-07-19 (live console):** on a real play→skills→stop cycle the sweep logged `swept 0` — i.e. Unity's own teardown had already cleared the tagged pools before EnteredEditMode fired. So with the current tagging the objects do NOT reliably linger; the sweeper is a belt-and-suspenders net, not a constant fix. (Earlier "they linger, measured" claim was based on pre-fix debris and is superseded by this.)

**Diagnosing a phantom pile-at-origin:** grep the saved `.unity` by GameObject NAME (`m_Name: Foo(Clone)`). If it's in ZERO saved scenes, it's unsaved edit-mode junk — reload the scene (don't save) to clear it. The real `Systems.unity` is 8 objects: Spike Camera, GameSystems, Character, Sun, Player3D, GameRoot, WaterFX, Pathfinder3DConfig.

See [[persistent-systems-layer]] and the phantom-bake prevention rule in project CLAUDE.md.
