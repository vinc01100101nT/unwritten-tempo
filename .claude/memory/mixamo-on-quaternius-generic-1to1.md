---
name: mixamo-on-quaternius-generic-1to1
description: Mixamo anims auto-rigged from a Quaternius model play 1:1 via Generic Copy-From-Other-Avatar — NOT Humanoid
metadata: 
  node_type: memory
  type: project
  originSessionId: 103b083d-880d-4e90-b082-336a01c28a5a
---

**Play Mixamo animations on Quaternius as GENERIC "Copy From Other Avatar" — do NOT reach for Humanoid.**

The dev auto-rigged the Quaternius model IN Mixamo, so the downloaded clips come back on the **Quaternius
skeleton** (`Bone/Body/Hips/Abdomen/Torso/Shoulder.L/…`), NOT Mixamo's `mixamorig`. Same skeleton the anim was
authored on → no retargeting needed. The only mismatch is a root path prefix (clip paths start at `Bone/…`;
`Ninja_Female` wraps its rig in `CharacterArmature/Bone/…`). Unity's Generic **Copy From Other Avatar** binds by
bone NAME (identity on the same skeleton) and reconciles the root → plays **1:1, exactly like the Mixamo preview**.

**Recipe (`CasterRig.cs`, `Tools ▸ unwritten ▸ Spike ▸ Build Mixamo Caster Spike`):**
1. Give an isolated Quat model a **Generic avatar**: importer `animationType=Generic`, `avatarSetup=CreateFromThisModel`. (Used `Caster_NinjaFemale.fbx`, a duplicate, so the committed player is untouched.)
2. Each Mixamo clip FBX: `animationType=Generic`, `avatarSetup=CopyFromOther`, `sourceAvatar=<that generic avatar>`. Loop the idle.
3. Play the clips on that model (Generic Animator, `avatar=<generic avatar>`). Verified by `clip.SampleAnimation` moving `Fist.R`/`Head` on the mesh.
Because it's all Generic on the ONE Quaternius skeleton, it plugs straight into the committed
[[character-3d-visual-system]] `Character3DPose` (Generic) — a caster class can cast with Mixamo + walk/roll with Quat clips.

**⚠️ The expensive mistake (process lesson):** I assumed "Mixamo = standard `mixamorig` Humanoid" and burned
~30–50k tokens building a hand-mapped Humanoid avatar (AvatarBuilder + IK-foot reparenting + mesh-name
disambiguation) — which also *distorted* the motion (chibi-vs-human proportion retarget). The one cheap check that
would have skipped all of it: **inspect the downloaded FBX's actual bone names FIRST** (`go.GetComponentsInChildren<Transform>()`).
Seeing `Bone/Body/Hips` instead of `mixamorig:` instantly says "same skeleton → Generic, not Humanoid." GENERAL
RULE: before choosing a rig strategy, read the asset's real skeleton — supersedes the ⚠️ in [[quaternius-humanoid-drops-clips]]
for the Mixamo case (Humanoid was never needed here).

**Gotchas:** Mixamo names every take `mixamo.com` → key clips by FBX FILE, not clip name. Two downloads
(`Two Hand`/`Wide Arm Spell Casting`) came back **skin-only, 0 takes** — re-download if wanted. Spike scenes now
boot themselves (`SystemsPlayMode` skips any `*Spike*` scene) so Play doesn't error on a missing boot-map.
