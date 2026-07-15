---
name: caster-grounding-stretched-foot-notodos
description: "3D caster's stretched foot won't hide under floor like Mixamo preview — UNSOLVED; list of approaches ALREADY TRIED and FAILED, do NOT repeat them."
metadata: 
  node_type: memory
  type: project
  originSessionId: 8d2935ba-f6d7-45dc-8f6a-068ce5117930
---

# Caster grounding / "stretched foot" — NOT-TO-DO list (UNSOLVED as of 2026-07-04)

**Symptom:** the live-3D CASTER player (Mixamo clips on the auto-rigged Quaternius/Ninja_Female body,
green-hair + purple-sash in-game) renders with an **extra stretched foot poking ABOVE the floor** on
lean/cast frames. Mixamo's own web preview hides that same stretch UNDER its ground grid (proven by their
lowered-camera shot). We want to reproduce Mixamo's grounding. Root cause of the residual failure is still
unknown — the stretch is a rig artifact of the Mixamo auto-rig, and none of the seating math below has made
the in-game result match the preview.

**Key fact:** Mixamo does NOT export any "foot pivot / put-this-on-ground" variable. It simply plants the
armature ORIGIN at Y=0 in the standing pose and lets stretched geometry sink below. So "grounding" is purely
about WHERE we seat the armature Y. See [[pivot-is-anchor-source-of-truth]], [[mixamo-clip-rebind-to-model-paths]].

## ESTABLISHED FACTS — do NOT re-assume these (verified 2026-07-04)

- **The dev DID upload the actual Quaternius model to Mixamo, previewed it there, then downloaded the
  clips.** This is settled. PROOF: the downloaded clips' curves bind to `Bone/Body/Hips/Abdomen/Torso`
  (the Quaternius skeleton), NOT `mixamorig:*`. If a different FBX had been uploaded, curves would be on
  `mixamorig`. Never again infer from a filename (e.g. `Caster_NinjaFemale.fbx`) that a non-Quaternius rig
  was used — READ THE CLIP PATHS.
- **The skeleton match is already 1:1.** The rebind machinery is therefore NOT fixing any bone/skeleton
  mismatch. The ONLY structural difference between the raw Mixamo download and the in-game prefab is:
  (1) the prefab nests the same skeleton one level deeper under a `CharacterArmature` wrapper node, and
  (2) that wrapper carries a static `270°` (X) stand-up rotation. The `"CharacterArmature/"` path prefix +
  the root→CharacterArmature delta-rotation redirect in `Use3DCaster.cs` exist SOLELY to paper over those
  two prefab-side differences — nothing to do with the rig itself.
- **Ideal flow the dev wants:** upload Quaternius → Mixamo → download skinless → Quaternius model references
  the clip DIRECTLY. This is achievable BECAUSE the skeleton already matches. The blocker is only the
  wrapper node + stand-up rotation; fix those at the prefab/import level (Animator on the direct parent of
  `Bone`, upright rest pose) and the rebind can be deleted. See [[mixamo-on-quaternius-generic-1to1]].

## ROOT BINDING — SOLVED (2026-07-04): re-path raw, do NOT bake

The "messed target points" were caused by `BakeRootMotion` in `Use3DCaster.cs` — it yaw-stripped +
delta'd the Mixamo root rotation (quaternion surgery) to pre-solve facing, distorting the pose. FIX:
the Mixamo root `""` IS the model's `CharacterArmature` node (measured: both parent of `Bone`; Quat
clips drive it via path `CharacterArmature`, Mixamo via `""`). So `RebindMixamoToModel` now just
**re-paths RAW**: `""` → `CharacterArmature`, everything else → `CharacterArmature/` prefix, curves
copied verbatim. Verified byte-identical: raw `Wide Arm Spell Casting` root (rot.x f0=-0.5706,
pos.y=-0.3179) == Generated `CharacterArmature`. `BakeRootMotion` DELETED. Do NOT reintroduce root
quaternion baking — target-point fidelity beats facing pre-solving.

Deferred consequences (by design, user said defer facing):
- **Facing offset**: the raw root carries a constant baked heading → body faces a fixed offset from
  `Character3DPose`'s facing yaw. This is the separate wrong-direction-facing problem.
- **Grounding**: raw root rests at its own y (e.g. -0.317), no longer zero-based to frame 0, so the
  single knob `Character3DVisual.mixamoFootOffsetY` needs RE-TUNING; per-clip rest y now varies.
- Only stance-referenced clips get baked (`Character3DControllerBuilder.Resolve` runs the resolver only
  for names not in the Quaternius embedded set). Stale unused `Generated/*.anim` (e.g. `Standing 2H Cast
  Spell 01`) keep OLD baked content — ignore them or delete; the controller only used `Wide Arm Spell Casting`.

## Approaches TRIED and FAILED — do NOT repeat

1. **GroundCalibrate = lift whole body so the clip's LOWEST contact touches the floor.** ❌ This is the
   *opposite* of Mixamo. It raises the body until the stretched foot sits ON the surface → exposes the "extra
   thin foot" above the floor. The retired original approach. NEVER seat by clip-minimum.

2. **Drop the root POSITION curve entirely** (old rebind bug). ❌ Removes the crouch/lean vertical body-dip
   that Mixamo bakes on the root's `m_LocalPosition.y` → bent-knee angles with no matching body-lower →
   feet punch BELOW ground.

3. **Drop the root ROTATION wholesale.** ❌ Deletes the body lean (11°–51° pitch lives on root rotation) →
   limbs do lean-motion but the torso never tilts → "same height + weird feet".

4. **`SeatStandingFrame` = seat by the STANDING (frame-0) foot, sink the armature Y by that, let later dips
   sink under the floor.** ❌ STILL NOT WORKING per user test (2026-07-04). Theoretically matches Mixamo
   (seat standing pose on floor, clip stretch below), but the in-game caster still shows the stretch. This is
   uncommitted working-tree code (~+151 lines in `Use3DCaster.cs`, newer than the last clip bake). Whether it
   was ever actually baked into the `.anim` files before the failing test is UNCERTAIN — verify the bake ran
   before concluding the MATH is wrong vs. the STALE-CLIPS problem below.

## Process NOT-TO-DOs (things that wasted time this session)

- **Do NOT claim a fix works without a baked + play-tested result.** The fix code compiling ≠ clips rebaked ≠
  in-game verified. The `.anim` files are the ground truth the game plays; editing `Use3DCaster.cs` does
  nothing until `Tools ▸ unwritten ▸ Player ▸ Use 3D Caster` RE-BAKES them (requires Systems scene open,
  Play STOPPED).
- **Do NOT try to re-bake / switch scenes while Unity is in Play mode** — scene load is blocked and asset
  writes during Play are unsafe. Confirm `manage_scene get_active` isn't a play-loaded map first.
- The caster visual is spawned into Systems.unity at edit-time by the menu; it is NOT in the map scenes, so
  `find_gameobjects Player3DController` returns empty when a map is the active (play) scene.

## Open leads NOT yet tried (candidates, not confirmed)

- Fix the artifact at SOURCE: delete/reweight the stretched foot vertex/bone in Blender so no frame stretches.
- A RUNTIME clamp: never let a foot bone render below/above the ground plane (foot-IK style), instead of
  baking a per-clip Y seat.
- Verify the failing test actually used freshly-baked clips (rule out stale-clip false negative) BEFORE
  discarding `SeatStandingFrame`.
