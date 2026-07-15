---
name: humanoid-import-type-tpose
description: "Rigged models T-pose on the shared Humanoid player controller when their FBX is imported Generic, not Humanoid."
metadata: 
  node_type: memory
  type: project
  originSessionId: f3418117-4ed2-480c-be8f-88f921c62116
---

**Symptom:** swapping the live-3D player to some `Art/Quat/Rigged/*.fbx` animates fine; others stand in a **T-pose** (or throw `MissingComponentException: no Animator`). Same folder, same code — the difference is per-ASSET, not per-code.

**Root cause:** the player rides ONE shared **Humanoid** AnimatorController (built by `Player3DVisualBuilder.BuildController` from the Humanoid clip library). A model can only wear it if its FBX is imported **Animation Type = Humanoid (`animationType: 3`) with a valid biped avatar**. A **Generic** (`animationType: 2`) or quadruped rig (Cow/Pug) can't retarget the Humanoid muscle clips → T-pose. As found 2026-07-13, the Rigged folder was **8 Humanoid / 44 Generic** — only the 8 worked.

**Fix (one-time, on the ASSETS not the code):** `Tools ▸ unwritten ▸ Rigs ▸ Bulk Set Humanoid (Rigged Mixamo)` (`BulkHumanoidRig.SetHumanoid` — sets Human + avatarSetup=CreateFromThisModel + reimport on the whole folder), then `Validate Humanoid Avatars`. Non-bipeds (Cow/Pug) will fail validation — expected, they aren't playable humanoids.

**Reconciles with [[quaternius-humanoid-drops-clips]]** ("keep Generic"): that concern is about preserving a model's OWN embedded Quaternius clips. Player models DON'T use embedded clips — they use the shared Humanoid clip library — so Humanoid is correct for anything meant to be the player. Keep Generic only for models you drive by their embedded clips.

**Robustness:** `PlayerModelSwap.IsPlayableHumanoid` filters the swapper list to Humanoid + valid-biped-avatar FBX only, so the dropdown / `[` `]` never offer a model that would T-pose or crash. See also [[character-3d-visual-system]], [[editor-runtime-delegate-bridge]].
