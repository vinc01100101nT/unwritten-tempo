# Mixamo caster clips: rebind curve PATHS onto the model (root-vs-child), not root-strip

**Context:** `Use3DCaster` (QUICKFIX spike, `experiment/3d-characters`) makes the real player a 3D "caster" =
one Quaternius body (`Assets/Art/Mixamo/Caster_NinjaFemale.fbx`) driven by ONE AnimatorController
(`CasterPlayerController.controller`) that mixes TWO clip sets on the same skeleton:
- **Quaternius** built-in clips (locomotion/jump/roll/hurt/pickup),
- **Mixamo** clips (cast/attack/death) — auto-rigged in Mixamo on the Quat skeleton, imported Generic + Copy-From-Other.

## Symptoms (cascaded over several rounds)
1. Idle = model **lying flat, face up**; walk = **locked facing north**; attack = **flat on ground**.
2. After partial fixes: facing correct but **stuck in Idle** — moves on WASD but only idle clip, no walk/run/attack.

## Root cause (the real one)
The two clip sets animate **different transform PATHS**, because Generic clips bind by PATH, not bone name:
- Model hierarchy: `""` (Animator root, `Character3DPose` yaws THIS for facing) → **`CharacterArmature`** → `Bone` → bones. Plus a sibling `Body` mesh.
- **Quaternius** clips bind `CharacterArmature`, `CharacterArmature/Bone/…` → MATCH the model. ✅
- **Mixamo** clips bind **`""` (root)** + **`Bone/…`** — i.e. rooted one level higher, NO `CharacterArmature` prefix.
  On this model only the `""` curve resolves (spins the whole object → flat + hijacks facing); the `Bone/…` bone
  curves bind to **nothing** → no limb motion.

Measured proof: Mixamo `Bone`@0 = `(90,0,0)` == Quaternius `CharacterArmature/Bone`@0 = `(90,0,0)` (same skeleton);
model `CharacterArmature` bind = `(270,0,0)` (the Blender-Z-up stand-up correction). So the 270 stand-up lives on the
model's `CharacterArmature` node, NOT baked into `Bone`.

## Fix (in `Use3DCaster.cs`)
`RebindMixamoToModel(clip, fileName)` → writes an editable `.anim` in `Assets/Art/Mixamo/Generated/*_rebound.anim`:
- **DROP** every `path==""` curve (root pos+rot) — root must stay free for facing; `CharacterArmature` bind supplies the 270.
- **PREFIX** every other path with `"CharacterArmature/"` (`Bone/…` → `CharacterArmature/Bone/…`) so bone curves resolve.
Result: Mixamo motion plays 1:1 on the child bones, upright + facing-free, like the Quaternius clips.
Also: **Idle** in the locomotion blend tree = the QUATERNIUS idle (not Mixamo — same root-path trap); **Write Defaults
OFF** on all states (belt-and-braces so no state re-writes the root default and clobbers yaw).

## Gotchas / lessons
- **⚠️ Don't use `execute_code` reflection to call `AnimatorController.CreateAnimatorControllerAtPath` on a controller a
  live scene Animator references** — it orphaned the scene Animator's `runtimeAnimatorController` → **NULL** → looked like
  "stuck idle" (bind/idle only). Re-run the real menu tool (`Tools ▸ unwritten ▸ Player ▸ Use 3D Caster`) which reassigns
  it, or reassign `anim.runtimeAnimatorController` explicitly. The menu tool pops a modal `DisplayDialog` that BLOCKS MCP
  (timeouts) until dismissed.
- **3D player locomotion needs `StanceCatalog` bindings** for Walk/Run/Attack (they exist: `StanceId.Walk→action Walk`,
  etc.). Player visual is `selfDrive=false`, so a missing binding → silent fall-through to the Idle fallback (looks like
  "always idle"). Billboards don't hit this (they self-drive).
- **Root-strip was the WRONG tool** (my first 2 attempts): removing only the root curve left bone curves that bind to
  nothing → attack did nothing. The problem was PATH mismatch, not root motion. Inspect a clip's bound PATHS vs the
  model's transform paths BEFORE assuming root-motion.
- The "Mixamo plays 1:1 via Generic Copy-From-Other" memory (`mixamo-on-quaternius-generic-1to1`) was optimistic — it's 1:1
  only if the clip's root PATH lines up with the model; here the extra `CharacterArmature` wrapper broke it.

## Authoring map (which anim for which cue) — 3 layers
1. cue → `StanceId`: gameplay (`PlayerStanceBrain`). code.
2. `StanceId` → `CharacterAction` + play mode/speed/priority/FitWindow: **Stance Composer** (`StanceLibrary`/`StanceCatalog`).
3. `CharacterAction` → real clip FILE: **code** — `Character3DClipMap.cs` (Quaternius suffix SSOT) + `MixamoOverride` dict in
   `Use3DCaster.cs` (caster's Mixamo swaps). NOT author-facing yet — candidate to surface into the Composer/a caster asset.

## Reusability
The runtime plumbing (`Character3DVisual`/`StanceController`/`Character3DPose`) + rebind are **body-agnostic** — every Quat
model shares the rig. `Caster_NinjaFemale` is hardcoded only because it's a spike wired to one body + its Mixamo set. Fold
into the class system as data: `class = { bodyFbx, quaterniusClips, mixamoOverrides[] }`; name the spawned object by role
(`Player3DAvatar`), not the seed asset.

Files: `unity/Assets/Scripts/Editor/Use3DCaster.cs` (`RebindMixamoToModel`, `MixamoOverride`, WD-off),
`Character3DPose.cs` (yaws root for facing), `Character3DVisual.cs`, `StanceController.cs`, `Character3DClipMap.cs`.
