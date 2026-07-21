---
name: sprint-loco-blend-gait
description: "Sprint 3D anim went idle because it wasn't a loco-blend gait; fixed by adding Sprint as a 4th blend node (Speed=2) matched by clip STRING like Walk/Run"
metadata:
  node_type: memory
  type: project
---

**Symptom (2026-07-21):** In the Stance Composer, Run.clip="Run" + Sprint.clip="Sprint" → Run animated but Sprint was **idle**. SWAPPING them (Run.clip="Sprint", Sprint.clip="Run") → **both animated**. So the failure followed the *row/slot*, not the clip file.

**Root cause:** two DIFFERENT clip-match paths in `Character3DPose.Play`:
- **Loco blend path** (Idle/Walk/Run) matches by the **composer clip STRING** (`runClip`/`walkClip` cached from `StanceCatalog.Get(id).clip`) → robust, name-mismatch-proof.
- **Dedicated-state path** (every other stance clip) — the builder `AddState(b.clip)` names the state by the composer string, but `Character3DPose.Reinitialize` keys `stateHash` by `Clean(clip.name)` (the clip file's REAL internal name) and only registers it if `anim.HasState(hash(Clean(clip.name)))`. If the clip's internal name ≠ the composer string, `stateHash["Sprint"]` is never set → `Play("Sprint")` falls through → `isRun/isWalk` both false → `speedTarget = 0` → **Idle blend**. "Run" worked only because its internal name matched its string; Sprint rode Run's blend slot in the swap (string match, no internal-name dependency).

**Fix:** made **Sprint a first-class 4th locomotion gait** (the exact path Run uses), so it's matched by string, not the fragile dedicated-state path — and this also makes the Sprint foot-plant button live:
- `Character3DControllerBuilder.Build`: `AddLoco(blend, Resolve(sprintClip), 2f)` + add `sprintClip` to `locoClips` (so it isn't ALSO made a dedicated state). Blend: Idle@0, Walk@0.5, Run@1, **Sprint@2**.
- `Character3DPose`: `LocoGait.Sprint`; cache `sprintClip`; in `Play`, `isSprint = clip==sprintClip` (checked FIRST) → `speedTarget=2`, `Gait=Sprint`.
- `FootstepTracker`: `sprintPhases`/`sprintRight` built from `StanceId.Sprint.footPlants`; `DetectPlants` picks them when `gait==Sprint` (shares run's fwd-nudge + print scale).
- Brain gate (`Character3DVisual`): `hasSprint` = Sprint clip authored; else the sprint gear drives `StanceId.Run` (borrows the run clip). See [[stance-system]], [[gait-speed-sync]].

**Gotcha:** after any clip change, re-click **Stance Composer ▸ Save + Apply to 3D** — it rebuilds the controller (adds/updates the Sprint blend node) and re-inits the pose caches. If Sprint.clip == Run.clip (same string in both rows), the pose can't distinguish gears by clip → treats it as Sprint; harmless (identical clip).

**Follow-up bug — sprint footstep SFX/ripple timing off (2026-07-21, MEASURED via execute_code):** root cause was DATA, not code. `Sprint.fbx`'s AnimationClip had internal `clip.name = "Run"` (exported/duplicated from Run) — so `HumanoidClipResolver` mapped composer "Sprint" → Sprint.fbx's clip, but that clip's `.name` was "Run". `Character3DPose.Reinitialize` keys `clipFrames`/`clipLen`/`stateHash` by `Clean(clip.name)`, so there was NO "Sprint" key (and it collided with the real Run's key). Result: `pose.NaturalFrameCount("Sprint")` returned **0** → `FootstepTracker.Build(Sprint)` silently fell back to the generic `DefaultRunPhase {0.1,0.6}` instead of the authored plants → wrong step timing. **Same class as [[oneshot-stance-mixamo-clipname]]** (clip.name collision). **Convention (enforced by `HumanoidClipPostprocessor`): a Humanoid clip's internal name MUST equal its file/composer name** — but the postprocessor only renames on FIRST import (`importSettingsMissing`), and Sprint.fbx was rigged afterward, so the rename never fired. **Fix = data:** set `ModelImporter.clipAnimations[i].name = "Sprint"` + `SaveAndReimport` (did it via execute_code). After that `clipFrames["Sprint"]=31`, footsteps use the authored frames. **Diagnostic recipe for "clip X animates but its per-clip data (frames/steps/duration) is wrong":** resolve the clip and compare `clip.name` to the composer string — a mismatch breaks every by-name lookup silently. Systemic gap: `BulkHumanoidRig`/manual "Fix Humanoid Clip Roots" don't re-assert the file-name rename, so a clip rigged before-first-import naming keeps a stale internal name.
