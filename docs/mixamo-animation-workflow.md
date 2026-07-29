# Mixamo Animation Integration — Workflow (No-Code)

How to add a new Mixamo animation to the 3D caster player. **No coding required** — the one-click menu does all the wiring. Follow the steps top to bottom.

## The golden rule

**Always rig the animation on our OWN Quaternius character inside Mixamo.** Upload our in-game model to Mixamo, apply the animation there, then download it. Because the animation is authored directly on our skeleton, it drops in with **zero retargeting** and the motion matches the Mixamo preview exactly. Never download a Mixamo clip that was made on Mixamo's default "Y-Bot" / `mixamorig` skeleton — that path is broken and requires messy fixes.

## Step 1 — Download from Mixamo

1. Go to mixamo.com → **Upload Character** → upload our model FBX (`Caster_NinjaFemale.fbx`).
2. Pick the animation. Preview it on our character to confirm it looks right.
3. Click **Download** with these settings:
  - **Format:** FBX Binary
  - **Skin:** **Without Skin** ← important (we only want the motion, not another copy of the body)
  - **Frames per Second:** 30
  - **Keyframe Reduction:** none
  - For walk/run/idle loops, tick **In Place**.

## Step 2 — Drop it into the project

1. Put the downloaded `.fbx` into the folder `**Assets/Art/Mixamo/**`.
2. Keep the file name clean and meaningful (e.g. `Wide Arm Spell Casting`). **This file name is the ID** used everywhere else — remember it exactly.

## Step 3 — Name it in the Stance Composer

1. Open `**Tools ▸ unwritten ▸ Stance Composer**`.
2. Find (or add) the stance that should play this animation.
3. Set that stance's **clip name** to the **exact file name** from Step 2 (no `.fbx`).
  - Example: file `Wide Arm Spell Casting.fbx` → clip name `Wide Arm Spell Casting`.

> The clip name is the contract that links everything: **file name = stance clip name = animation state name.** If they don't match, the animation won't play.

## Step 4 — One-click apply

1. Open the **Systems** scene.
2. Run `**Tools ▸ unwritten ▸ Player ▸ Use 3D Caster (Systems scene)**`.
3. Press **Play** and watch the caster perform the new animation.

That's it. The menu re-imports the clip correctly (Generic rig), wires it onto our skeleton with **no motion distortion**, and rebuilds the animation controller.

> **Note:** only animations that a stance actually references get processed. Unused `.fbx` files in the folder are simply ignored.

## Step 5 — Two small tune-ups (if needed)

After the animation plays, you may need to adjust two things. Both are **sliders / values in the Inspector — still no code.**

- **Grounding (feet height):** if the character floats or sinks during this animation, select the caster in the scene and tweak `**Character3DVisual ▸ Mixamo Foot Offset Y**` until the feet sit on the floor.
- **Facing direction:** the body may point a fixed amount off from where it's moving/aiming. This is a **known, separate issue** being fixed globally — you don't fix it per-animation. Just note it if you see it.

## What NOT to do

- Don't upload a different character to Mixamo than our in-game model.
- Don't download **With Skin** — it re-imports another body mesh we don't need.
- Don't hand-edit the generated `.anim` files under `Assets/Art/Mixamo/Generated/` — they're rebuilt every time you run the menu.
- Don't try to "fix" a wrong direction or a floating pose by editing animation curves. Grounding is one slider; facing is a separate global fix.

## Why this works (one-paragraph background)

The animation and our character share the exact same skeleton (they were rigged together in Mixamo), so the tool only has to **re-point the animation onto our character's bone layout and play it as-is** — it never rewrites the motion. An older version of the tool tried to mathematically "correct" the root bone and that distorted every pose (the "messed target points" bug). That correction has been removed: the motion now plays byte-for-byte as downloaded.

---

# Humanoid migration (planned) — kill the stretched-foot problem for good

The Generic workflow above works, but it's **fragile per-clip**: some Mixamo animations show a stretched/detached foot and need the grounding slider, because Generic replays raw bone transforms that don't fit our proportions. **Unity Humanoid** fixes this permanently — it replays animation in normalized "body-part language," so every clip re-fits our character and any Mixamo clip becomes usable with no distortion.

## Key finding (2026-07-04): our native Quaternius rig CANNOT be made Humanoid directly

Tested on `Assets/Art/Quat/FBX/BaseCharacter.fbx`. Setting it to Humanoid → `**isHuman = False**` (invalid humanoid). Root cause, confirmed by dumping the skeleton + skin weights:

- Humanoid **requires** the leg chain `hips → upper leg → lower leg → foot`.
- In the Quat rig, the **feet are parented to the ROOT bone** as siblings of the body — NOT under the legs. The legs dead-end at a weightless `LowerLeg_end` marker.
- The foot **mesh is skinned to those root-parented `Foot.L`/`Foot.R` bones** (~43 weight each), so we can't just remap the foot to the leg-end without detaching the foot mesh.

Unity can't form a valid humanoid leg from that. **This same foot-vs-leg disconnect is the likely root cause of the Generic "stretched foot."**

**Consequence:** the shortcut "make one Quat avatar, Copy-From-Other to all" is dead. The native rig is Humanoid-hostile.

## The path forward: re-rig each body via Mixamo Auto-Rigger

Mixamo's auto-rigger builds a **proper** `mixamorig` skeleton with feet correctly under the legs → that IS a clean Humanoid → every Mixamo animation retargets with zero stretched foot. Plan:

- **Once per character body:** upload the plain Quat **mesh** → auto-rig → download **With Skin** (any pose). In Unity set that FBX to **Humanoid → Create From This Model**.
- **Animations stay shared:** download clips **Without Skin** and import them **Humanoid → Copy From Other Avatar** → any one rigged body's avatar. One avatar serves all (all No-Fingers Mixamo rigs share the same hierarchy).
- Keep **Skeleton LOD identical** everywhere (pick **No Fingers (25)** for bodies and animations).
- After migration, the Generic rebind machinery (`RebindMixamoToModel`, the `Generated/` folder, the `Mixamo Foot Offset Y` slider) is no longer needed — a Humanoid clip plays directly.

> You only run the auto-rigger **once per body (~N characters)**, NOT once per animation. Animations are never re-rigged in Mixamo — they're skinless clips retargeted by Unity.

## Speeding up the manual auto-rig (marker placement)

The auto-rigger asks you to place 5 markers (chin, wrists, elbows, knees, groin) each upload. Ways to cut the labor:

- **Biggest saver — trust auto-placement.** All Quat bodies are the *same base body, same proportions, same A-pose*. Mixamo auto-places the markers on upload; on identical proportions those defaults are usually already correct. Verify once, then just click **Next** without touching them.
- **No official "save/copy markers" preset** exists in Mixamo's UI (as far as we know).
- **DevTools DOM paste is unreliable.** The marker `<span>`s carry `left/top` px styles, but Mixamo reads positions from its React state, not the DOM — editing the styles by hand likely won't register. Not recommended as the primary method; test before trusting it.
- Because the bodies are identical, the practical workflow is: place markers carefully **once**, note roughly where they land, and reproduce that by eye each time (auto-placement usually gets you there anyway).
