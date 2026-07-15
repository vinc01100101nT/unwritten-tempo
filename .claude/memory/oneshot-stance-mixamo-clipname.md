# One-shot stances (Attack/Roll) don't animate — Mixamo clip.name = "mixamo.com"

## Symptom
Live-3D player: Idle/Run animate, but Attack/Roll/Jump one-shots never play (character
just stands during a swing/dash). Putting the SAME clip in the Run slot animates it fine.

## Root cause
Every Mixamo FBX take is internally named `mixamo.com` (see `takeName: mixamo.com`). The
Humanoid clip FBXs in `Assets/Art/Quat/Anim/Humanoid/` were imported WITHOUT a clip-name
override, so all clips shared `clip.name == "mixamo.com"`.
- `Character3DControllerBuilder` names one-shot STATES by the composer string ("Punch","Roll") — correct.
- `Character3DPose.Reinitialize` keys `stateHash` by `Clean(clip.name)` = "mixamo.com" for
  every clip, then `HasState(0, hash("mixamo.com"))` → false → NO one-shot ever registers.
  `Play("Punch")` finds nothing → falls through to the locomotion blend (idle).
- Run/Idle work because locomotion uses the blend tree, which never needs the name match.
- Also collapsed `clipLen` onto one "mixamo.com" key → FitWindow durations wrong.

## Fix
`HumanoidClipPostprocessor.OnPreprocessAnimation` now names the single-take clip after its
FILE (`c.name = Path.GetFileNameWithoutExtension(assetPath)`), so clip.name == state name ==
composer string. Aligns with HumanoidClipResolver's file-name-is-the-contract design.
Existing FBXs needed a per-FILE reimport (folder reimport did NOT reimport the FBXs).

## Gotchas
- Reimport the FBX itself, not the folder, to re-run the postprocessor.
- No controller rebuild needed; pose picks it up on next Awake (or Save+Apply to 3D live).
- Every new Mixamo clip dropped in the folder is now auto-named on import.
