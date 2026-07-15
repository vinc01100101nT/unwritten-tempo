---
name: vfx-looping-oneshot-double-play
description: Composed skill VFX (looping one-shot like Sanctuary) played TWICE despite repeatCount=1 — Vfx.cs emitted for duration+lifetime; fix = emit exactly one loop cycle
metadata: 
  node_type: memory
  type: project
  originSessionId: 36bc4cd3-1998-4f08-b812-8b320d4a7a87
---

2026-07-02: A composed skill's ground VFX (e.g. `skill.heal_circle` Sanctuary green circle) visually **looped a second time** even with Skill Composer **repeat count = 1**. Red herring: `repeatCount` was innocent — `SkillRunner.PlayCycles` correctly plays one cycle when repeatCount=1 (`extra = repeatCount-1 = 0`).

**TRUE root cause** (found on 3rd pass): the **Vfx Composer preview forces `main.loop = false` for every PlayAt step** (`VfxComposerWindow.cs:1069`) — so in the composer a PlayAt plays exactly ONE clip. But `Vfx.Begin` at RUNTIME left `main.loop` at the prefab's authored value (Hovl `Healing circle` = looping). So a looping prefab played for its ~10s window kept RESTARTING at each 4s loop boundary → the visible "second loop." Pure preview↔runtime parity bug. `repeatCount` (SkillRunner) always innocent (at 1, `extra=0`).

**FIX**: `Vfx.Begin` now sets `m.loop = oneShot ? false : baseLoop[i]` (cached `baseLoop[]` in Prepare). A one-shot (`PlayAt`) plays a single clip like the composer; attached auras (`Attach`, `oneShot=false`) keep authored looping. Duration then = one clip's `(duration+lifetime)/speed − trimEnd` = the composer's WYSIWYG length (so slow it down via `speed` to make it last longer, DON'T loop).

Also fixed same pass: `Stop()` fade shortened from a full extra `oneShotDuration` → `fadeTail` (one particle lifetime) so there's no doubled fade tail.

**⚠️ Two WRONG guesses I shipped first (don't repeat)**: (1) making looping one-shots emit only `loopCycle`=`m.duration` — broke WYSIWYG, cut Sanctuary 10s→5s. (2) thinking the `Stop()` fade tail was the loop cause. The real lever is `m.loop`. When a VFX loops at runtime but not in the composer, check `m.loop` parity FIRST.

Concrete data: `sanctuary.asset` PlayAt effectId `circle.healing` (EffectLibrary lifetime 0), speed 0.453, trimEnd 8.03; prefab `Healing circle` system duration 4s / startLifetime 0.5s / looping 1.
