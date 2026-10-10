# Your own preview can play a clip differently from the game

**Reach for this when:** an animation looks right in the pictures or videos you rendered yourself, but the user
says it is wrong in game — a foot slides, the body does not travel, a turn is missing.

## The rule

A preview script that poses the body from the clip's curves applies EVERYTHING in them, including the body's travel,
height and turn. The game plays the clip through Unity's animation system, which first splits those three off as
root motion unless the clip says to bake them into the pose — and then throws them away when the character does not
apply root motion. So before trusting a home-made preview, compare the new clip's settings with a clip that already
plays right in game, field by field.

## Why

2026-10-09: twelve weapon attacks were built by script and checked in videos where the body lunged forward over a
planted foot. In game the back foot slid backward in every one. The first fix pinned the foot in the build — the
videos still looked perfect and the game still slid, because the videos had never shown the real problem: the saved
clips had `m_LoopBlendPositionXZ`, `…PositionY` and `…Orientation` at 0 (not baked), while every imported clip that
plays right had them at 1, and the character never applies root motion. One wasted fix and a play-test round.

## How to apply

- Read the root-motion block of the new clip (`.anim`: `m_LoopBlend*`, `m_KeepOriginal*`) next to a clip that works
  (`.fbx.meta`: `loopBlend*`, `keepOriginal*`). Any difference is a lead before any theory.
- Set them where the clip is saved (`AnimationUtility.SetAnimationClipSettings`), never by hand afterwards.
- Neither a `PlayableGraph` nor `AnimationMode` sampling in edit mode can tell the settings apart (the first does
  not pose at all outside Play, the second applies the body movement like a preview) — the game, in Play, is the test.
- This project's asset check now fails a character `.anim` that does not bake all three (ASSETS.md rule 14).
