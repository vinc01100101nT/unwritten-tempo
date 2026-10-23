---
name: skill-vfx
description: Use whenever a game skill's LOOK is chosen, changed or fixed — picking an effect for a skill, sizing it, a shot or something riding an arrow, a buff glow, importing a VFX pack into the game, or an effect that draws wrong in game (a dark or grey sheet, pink, invisible, lying on its side). Loaded from the compose-skill skill. Covers sizes in this world, looking before using, the never-make-your-own rule, importing a pack, and fixing a broken effect at the smallest place first.
---

# A skill's effects (VFX)

The `compose-skill` skill owns what a skill DOES; this one owns what it LOOKS like. Load both for skill work.

## ⛔ First rule: fix the smallest thing that is wrong, and look before you build

**When an effect draws wrong, find the PART of the effect that is wrong and fix that part — before touching code,
a project setting, or anything every other effect shares.** An effect is a handful of separate parts (particle
systems). Render it with each suspect part hidden, one at a time; the render where the problem vanishes and nothing
else changes names the part. Delete or fix that part. Only when no single part is to blame is a wider fix a
question — and then the user decides it, with its running cost, before anything is built.

*(Scar, 2026-10-23: the Free Slash projectiles drew a dark sheet beside the slash. I measured why — that sheet bends
the picture behind it, which needs the camera's copy of the screen, and the game keeps that copy off — and went
straight to a game-wide answer: first a choice between turning the copy on for every frame or not, then a whole new
system switching the copy on and off around those effects, with a new saved field and a new script. The user asked
the obvious question: "why can't you just delete that black flat ground?" One render with that part hidden showed
the sheet was one separate part, and deleting it left the slash untouched. Three replies and a system nobody needed,
for a fix that was one deleted part. The reasoning was right about the cause and wrong about where to fix it: I
started at the engine instead of at the asset. The user then asked for the heat haze itself back — and THAT is a
want only the screen copy can meet, so the switch was built then, because a decision asked for it, not as the first
answer to a broken look.)*

**The check, before proposing ANY fix to a look:** *"Is the broken thing one part of one asset? Did I render it
with that part hidden?"* No render ⇒ go make one before saying anything about the fix.

## ⛔ Never create a VFX of your own (hard rule set by the user, 2026-10-23)

A skill's look is only ever an effect that already exists as a finished effect in a pack, used as that effect was
made — the tornado for a tornado, a falling rock for a falling rock. Never borrow an effect made for something else to
stand in for a thing it is not (a blue flame is not a drip), and never build one out of parts — copies of the bow's
arrow model dropped from the sky as a "shower" are parts too (Rainbow and Cloudburst, 2026-10-23; replaced by the
effects the user chose — the Laser AOE and the thundercloud). The arrow model is for a shot the bow actually fires,
one per shot. When the project has no effect that IS the thing, say so and the user buys one on the Unity Asset Store.

Deleting a part that is broken is fixing a finished effect, not making one. Adding a part, recolouring one into
something else, or stitching two effects together is making one.

## Heat haze — the camera's screen copy

A **heat haze** is the ripple that bends whatever is behind it, like air over a hot road. It works by reading a copy
of the screen, and the game camera keeps no such copy normally (the pipeline's opaque texture is off). So:

- The effect table marks every effect whose materials read the picture behind them (`measuredReadsScreen`, written
  by `EffectTimingBaker` at author time — a Shader Graph with a Scene Color node, or a shader reading
  `_CameraOpaqueTexture`).
- While at least one marked effect is playing, `ScreenCopy` turns the game camera's copy on; when the last one ends it
  goes back off. Cost: a half-size copy of the screen each frame, only while a haze plays; nothing otherwise.
- After importing an effect, run `EffectTimingBaker.Sweep(true, true)` so the new effect is measured and marked.
  An unmarked haze draws a dark sheet in the game.

## Sizes — this world is small

Learned 2026-10-21 on the Bow, after effects five times too big filled the screen and target effects played on the
caster.

- **A person is `CharacterSpec.Active.bodyHeight` = 0.327 world units tall.** Library effects are authored at human
  size, so a step scale of 1 is about five people tall. The game's own world-sized references: cast cue `aura.star`
  0.226, heal cue `aura.buff` 0.212, sprint cue `smoke.dust-ground` 0.26, normal hit `hit.holy` 1 (its library scale
  is already 0.21). Chest height is bodyHeight × 0.5; a bow's hand height about bodyHeight × 0.6.
- **An area effect is sized to the skill's area.** Render it with person-sized figures standing at the skill's
  radius and scale it until its edge lands on them (Rainbow's Laser AOE: 0.55 for 2.5 m).
- The Meteor sequence's sizes are the user-approved reference for fire impacts.

## Look before you use one

- **Render each effect next to a capsule of person height in a hidden preview scene**
  (`EditorSceneManager.NewPreviewScene`, a camera with `cam.scene` set, `ParticleSystem.Simulate`, `cam.Render`) and
  look at the picture. Never open or change the user's scene for it, and never pop the picture onto their screen.
- **Render it the way the game places it, or it lies:** set the instance's world rotation to the step's rotation
  (the game OVERWRITES the prefab's own root rotation) and every particle system's scaling to Hierarchy (the game
  does). A tornado whose prefab stands up with a -90° root rotation rendered upright in a preview that kept that
  rotation, and lay flat on its side in the game (2026-10-23). Check `prefab.transform.localRotation` before using
  any effect; a non-zero one needs the same rotation on a Play At step (an Attach step has no rotation).
- **Render with the game's camera settings.** The game camera has the screen copy only while a marked heat-haze
  effect plays (see Heat haze above). So a preview camera uses `UniversalAdditionalCameraData.requiresColorOption =
  On` for an effect marked `measuredReadsScreen`, and `Off` for every other — an unmarked effect that still bends the
  picture behind it looks fine with the copy on and draws a dark sheet in the game.
- **A moving effect is rendered moving:** step its position along the flight and `Simulate` in small steps; a still
  render of a projectile shows its trail piled on top of itself.
- **To choose among many, render a contact sheet**: one cell per effect id, frozen at a moment (0.15s catches quick
  hits, 0.6s the bigger ones), a person-height capsule in each cell, the ids printed in grid order.
- **Known broken, measured:** the `water.splash*` effects render as nothing; the smoke effects carry a stray blue
  mesh; the `env.crystal-effect-*` effects render pink; the `weaponenchant.*` effects need a weapon to emit from
  (played alone they log "spawn on a mesh with zero surface area").

## Importing a VFX pack

1. Move the whole pack (with its `.meta` files, through `AssetDatabase.MoveAsset` so links survive) under
   `Assets/1_Authored/Prefabs/VFX/<category folder>/` — the first folder under `VFX` sets the id prefix
   (`Slashes` → `slash.*`). Nothing stays at the `Assets/` root or in `0_Raw`.
2. `EffectLibraryBuilder.RebuildCore()` adds every prefab under `VFX` to the effect list (the menu version opens a
   dialog — call the method). Remove any entry that is not an effect (a demo model: the Free Slash katana).
3. `EffectTimingBaker.Sweep(true, true)` measures the new effects' lengths — a skill's beats are timed by them —
   and marks the ones with a heat haze.
4. Render every new effect (contact sheet, game camera settings) before any skill uses one.
5. Remove every missing-script slot and every pack script nothing uses, in the same import — the project's
   standing rule "always remove scripts nothing uses". Unity refuses to save a prefab that still has a missing
   script, so this comes before any other edit to the pack's prefabs.

## Building a skill's look

- **One sequence per skill** in `Resources/VfxSequences/`, its tick named to match the skill's binding `tickId`.
- **A single-target shot is an arrow that arrives:** a Carrier step from the caster (`carrierFromCaster`, offset at
  hand height) to the Target (offset at chest height), `carrierDrawsArrow` on, the tick on arrival (`tickWhen: End`),
  and the hit effects on `emitEventOnEnd`. A lob adds `carrierArc` with a launch angle (25° stays inside the world's
  scale).
- **An effect riding the arrow** (Thunderhead's electric slash): put it on the same Carrier step's `effectId` with
  `carrierDrawsArrow` on and `carrierFaceTravel` off — it then points the way the arrow points, every frame, arcs
  included. `carrierFaceTravel` is the other shape: it lays the effect's bottom along the flight, for a falling
  meteor's flame.
- **Self buffs** are an Attach step on the caster with the tick at its start; a splash around the caster uses the
  binding's `AroundCaster` delivery and needs no aim.
- **A look that follows the caster through a run** (Cloudburst's cloud): an Attach step with `stopPreviousOnReplay`
  on, so each beat replaces the last copy instead of stacking one per beat.
- **Running cost goes in the report:** how many effects one cast starts, how long each lives, and whether any
  repeats per beat.
