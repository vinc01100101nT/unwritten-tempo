---
status: Humanoid migration DONE — this is the current workflow
owner: vince
last-updated: 2026-08-30
tags:
  - art-animation-pipeline
---

# Mixamo Animation Integration — Workflow (No-Code)

How to add a new Mixamo animation to the live-3D player and NPCs. **No coding required.** Follow the steps top to bottom.

> ## ⚠️ Rewritten 2026-08-30 — the old Generic workflow on this page was retired
>
> This doc used to describe a **Generic**-rig pipeline: drop the FBX into `Assets/Art/Mixamo/`, run a *Use 3D Caster* menu item, and fix floating feet with a per-clip offset slider. **None of that is true any more**, and following it would send you hunting for things that do not exist. Measured against the project on 2026-08-30: there is no `Assets/Art/Mixamo/` folder, no `Generated/` folder, no `Caster_NinjaFemale.fbx`, no *Use 3D Caster* menu item, and the rebind helper the old page relied on is gone from the codebase.
>
> **The Humanoid migration this doc used to file under "planned" actually happened.** Bodies are rigged Humanoid, clips retarget through avatars, and a dropped clip is prepared automatically. The history — including *why* the native rig had to be re-rigged — is kept at the bottom.

## The golden rule

**Always rig the animation on a Mixamo-rigged body, never on Mixamo's default "Y-Bot".** Because everything is Humanoid now, a clip authored on any properly-rigged body retargets onto ours — but keeping the skeleton consistent is what keeps the retarget clean.

## Step 1 — Download from Mixamo

1. Go to mixamo.com → **Upload Character** → upload one of our body FBX files from `Assets/1_Authored/Art/Characters/Models/` (e.g. `BaseCharacter.fbx`).
2. Pick the animation. Preview it on our character to confirm it looks right.
3. Click **Download** with these settings:
   - **Format:** FBX Binary
   - **Skin:** **Without Skin** ← important (we only want the motion, not another copy of the body)
   - **Frames per Second:** 30
   - **Keyframe Reduction:** none
   - **Skeleton LOD:** **No Fingers (25)** — keep this identical across every body and every clip.
   - For walk/run/idle loops, tick **In Place**.

## Step 2 — Drop it into the clip folder

1. Put the downloaded `.fbx` into **`Assets/1_Authored/Art/Characters/Animations/`**.
2. Keep the file name clean and meaningful (e.g. `Cast`). **This file name is the ID** used everywhere else — remember it exactly.

**That is the whole import.** A postprocessor watches that folder and, on a clip's **first** import, sets Animation Type = Humanoid and applies the feet-planted, looped clip-root settings for you — zero menu clicks. It only seeds on the first import, so if you later hand-tune a clip's Root Transform in the Inspector and hit **Apply**, your tweak sticks instead of being reset.

## Step 3 — Name it in the Stance Composer

1. Open **Tools ▸ unwritten ▸ Composers ▸ Stance Composer**.
2. Find (or add) the stance that should play this animation.
3. Set that stance's **clip name** to the **exact file name** from Step 2 (no `.fbx`).
   - Example: file `Cast.fbx` → clip name `Cast`.
4. For **Walk**, **Run** or **Sprint**, picking the clip is all there is: the composer measures it on every character body —
   the frames each foot lands (shown on the row's step button) and how fast the clip walks — and the legs then play at
   the character's real ground speed, so a planted foot never slides. After adding a new body, click that row's step
   button to measure again.

> The clip name is the contract that links everything: **file name = stance clip name = animation state name.** If they don't match, the animation won't play.

## Step 4 — Put the model in the scene

1. Open the **Systems** scene.
2. Run **Tools ▸ unwritten ▸ Rigs ▸ Set 3D Player Model (Systems scene)**.
3. Press **Play** and watch the character perform the new animation. *(Play needs a running server — see [Starting the Network](../2-systems/networking/starting-the-network.md).)*

> **Note:** only animations that a stance actually references get used. Unused `.fbx` files in the folder are simply ignored.

## If something looks wrong

- **A clip imported before the postprocessor existed** — run **Tools ▸ unwritten ▸ Rigs ▸ Fix Humanoid Clip Roots (Anim folder)**. It applies the same feet-planted/looped settings to everything already in the clip folder.
- **A body that isn't retargeting** — run **Tools ▸ unwritten ▸ Rigs ▸ Bulk Set Humanoid (Rigged Mixamo)** over `Assets/1_Authored/Art/Characters/Models`, then **Tools ▸ unwritten ▸ Rigs ▸ Validate Humanoid Avatars (Rigged Mixamo)** to confirm every body produced a valid avatar.
- **Facing direction:** the body may point a fixed amount off from where it's moving/aiming. This is a **known, separate issue** fixed globally — you don't fix it per-animation. Just note it if you see it.

## What NOT to do

- Don't download **With Skin** for an animation — it re-imports another body mesh we don't need. (With Skin is only for a *body*, once, in Step 1 of the re-rig.)
- Don't mix Skeleton LODs. Everything is **No Fingers (25)**.
- Don't try to "fix" a wrong direction by editing animation curves. Facing is a separate global fix.

---

# History — why the rig had to be re-rigged (finding, 2026-07-04)

Kept because it explains why we cannot simply mark our source bodies Humanoid and why every body goes through Mixamo's auto-rigger once.

Tested on the native Quaternius rig. Setting it to Humanoid produced `isHuman = False` (invalid humanoid). Root cause, confirmed by dumping the skeleton + skin weights:

- Humanoid **requires** the leg chain `hips → upper leg → lower leg → foot`.
- In the Quaternius rig, the **feet are parented to the ROOT bone** as siblings of the body — NOT under the legs. The legs dead-end at a weightless `LowerLeg_end` marker.
- The foot **mesh is skinned to those root-parented `Foot.L`/`Foot.R` bones** (~43 weight each), so the foot couldn't be remapped to the leg-end without detaching the foot mesh.

Unity can't form a valid humanoid leg from that, so the shortcut "make one avatar, Copy-From-Other to all" was dead and the native rig is Humanoid-hostile. **This same foot-vs-leg disconnect was also the root cause of the Generic path's "stretched foot".**

**The fix, now carried out:** each body is uploaded once to **Mixamo's Auto-Rigger**, which builds a proper skeleton with feet correctly under the legs, and comes back **With Skin** as a clean Humanoid. Animations are never re-rigged — they are skinless clips Unity retargets. You run the auto-rigger **once per body**, not once per animation.

**Speeding up the manual auto-rig (marker placement).** The auto-rigger asks you to place 5 markers (chin, wrists, elbows, knees, groin) each upload.

- **Biggest saver — trust auto-placement.** All the bodies share the same base body, proportions and A-pose, so Mixamo's auto-placed markers are usually already correct. Verify once, then just click **Next**.
- **No official "save/copy markers" preset** exists in Mixamo's UI (as far as we know).
- **DevTools DOM paste is unreliable.** The marker `<span>`s carry `left/top` px styles, but Mixamo reads positions from its React state, not the DOM — editing the styles by hand likely won't register.

## Leftover to clean up

The per-clip grounding slider from the Generic era (`Character3DVisual`'s Mixamo foot-offset field) is still in the code and still applied, but only for a pose flagged as coming from the old Mixamo path. Whether it still earns its place now that clips are Humanoid is a question for whoever next touches the rig — flagged, not changed.

---

## Related

- [GPT Concept Prompts — Paste-Me](gpt-concept-prompts.md) — the sibling art pipeline (props and weapons, not characters).
- [Story & Setting (Canon)](../3-world/story-setting-canon.md) — why the player body is charcoal (unwritten, not yet on the page), which is what these clips animate.
- [HUD/UI System — Design & Relay](../2-systems/hud-ui-system.md) — the character preview that shows this rig.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — Play needs a world before you can watch a clip run.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the art-pipeline line under the foundation.
