---
name: bake-8dir-sprites-pipeline
description: Mixamo/Quaternius 3D model → true 8-direction billboard sprite strips via the Bake 8-Dir editor tool
metadata: 
  node_type: memory
  type: project
  originSessionId: 68ff91b6-f4be-4ab2-9428-73d9766e37e1
---

Solves "true 8-dir hand-drawn sprites don't exist": render them from a rigged 3D model instead. Built 2026-06-24.

**Source pipeline (chosen):** Quaternius/Kenney/Synty CC0 rigged character → Mixamo for auto-rig + animations (download FBX, "In Place" ON) → bake. User validated a Quaternius char in Mixamo works.

**Tool:** `Tools ▸ unwritten ▸ Art ▸ Bake 8-Dir Sprites` (`Editor/SpriteBaker8.cs`). Offscreen ORTHO camera at a fixed pitch on isolation layer 31, model yawed in 45° steps for the 8 octants, each clip sampled frame-by-frame via `AnimationMode.SampleAnimationClip` (retargets humanoid), captured to a transparent RT. **Pixelate** = render at cell size, no AA, point filter (RO crunch). **Hi-res** = supersample ×N + box-downscale, bilinear. Outputs one strip PNG per (action,octant) to `Assets/Art/BakedChars/<name>/<action>_<DIR>.png`, ppu=cellSize (1 cell = 1 unit).

**Runtime:** `BakedBillboard8.cs` — drives a CHILD sprite (lifted by `feetOffset` to ground feet, + auto `Billboard`) from `Facing8.OctantFromWorldDir` with NO mirroring (one unique strip per octant — unlike the Sscary 4+flip path). Slices once via `SpriteSheetCache.Row`, zero-alloc LateUpdate. **Actions keyed by `CharacterAction` enum** (`List<ActionSet>`); self-drives Idle/Walk/Run from movement speed; `Play(action)`/`SetFacing`/`SetMoving` for AI. "Bake + spawn test character" button drops a wired test GO in front of the player.

**Character pipeline (built 2026-06-24, the SO+button flow like [[recipe-generator]]):**
- **`CharacterAction` enum** = the pre-declared CONTRACT KEY (Idle/Walk/Run/Attack/Cast/Hurt/Die/Sit/Dash/Jump) used for filenames + CharacterDef + runtime lookup. **Action name is an enum dropdown, NOT free text** (user's call — free text silently misses at lookup). Add new members at the END.
- **`SpriteBakeCore.cs`** (editor) = the shared bake pipeline (offscreen render + the 3 gotcha fixes); BOTH `SpriteBaker8` window and `CharacterBuilder` call it — render path lives in ONE place.
- **`CharacterDef.cs`** = `Assets ▸ Create ▸ unwritten ▸ Character` ScriptableObject (model, albedo, enum-keyed actions, look + grounding). Runtime SO (no editor refs).
- **`CharacterBuilder.cs`** = custom inspector with **Bake & Build Prefab** button → bakes all actions into `Sprites/` BESIDE the asset + writes a wired `BakedBillboard8` prefab. Guards: duplicate-action + required-Idle. **Convention: one CharacterDef per `Assets/Art/Characters/<Name>/` so asset + Sprites/ + prefab live together.** AnimationMode matches clips BY BONE NAME → use each model with the clip from its OWN FBX.

**Batch + roster + sandbox (built 2026-06-24):**
- **Quaternius Ultimate Animated pack** (`Assets/Art/Quat/FBX`, ~50 chars): verified each FBX has **embedded clips** (`CharacterArmature|Idle/Walk/Run/Jump/Roll/Punch/SwordSlash/Shoot_OneHanded/RecieveHit/Death/Defeat/Victory/PickUp/SitDown/StandUp`, rig=Generic, **custom rig — NOT mixamorig**, so skip Mixamo). **NOT modular**: 1 merged body mesh; "Hair"/"Helmet" are MATERIAL SLOTS not meshes → per-region tint only, no mesh swap. Variety = 50 chars + tint (runtime mask-tint shader would avoid per-color sheets — discussed, not built).
- **`CharacterAction` enum expanded** with Punch/Shoot/Roll/Victory/Defeat/PickUp/StandUp/WalkCarry/RunCarry (appended).
- **`Editor/BatchCharacterBaker.cs`** = `Tools ▸ unwritten ▸ Art ▸ Bake All Quaternius Characters` — reads each FBX's embedded clips, keyword-maps names→action, bakes via SpriteBakeCore to `Assets/Art/Characters/<name>/`, writes prefab, fills **`CharacterRoster.cs`** SO. Has **"Bake first N"** limit (test 3 before the full ~50-char batch), skip-already-baked, cancelable. cellSize default 64.
- **⚠️ Quaternius YAW OFFSET = 0** (NOT 180). They face the opposite way from Mixamo (+Z-forward → 180). Wrong yaw makes movement+facing look reversed. Batch baker default is now 0; single-FBX SpriteBaker8 stays 180 for Mixamo.
- **Sandbox**: `Tools ▸ unwritten ▸ Create Character Sandbox Scene` → checkered ground+light+cam+`CharacterSelectSandbox.cs`. Play → IMGUI roster picker → WASD walk (Shift run, camera-relative via `SetVelocity`), number keys 1-6 test Attack/Jump/Hurt/Die/Cast/Sit. Standalone QA; `SystemsPlayMode` clears the play-from-Systems override for scene "CharacterSandbox" so the game doesn't boot over it. `BakedBillboard8` is SELF-CONTAINED (own child "BakedSprite" + own FaceCamera, no game `Billboard`/depth dependency). Confirmed working 2026-06-24. NEXT (deferred): wire chosen baked char into the real player.

**Two correction knobs (can't Play-test from here):** if E/W look swapped → re-bake with **Clockwise** flipped; if you see its back → nudge **Yaw offset** by 180 (default 180). **⚠️ URP caveat:** uses `cam.Render()` for the offscreen capture — if frames come out blank that's the URP manual-render quirk; fallback is `RenderPipeline.SubmitRenderRequest` (Unity 6).

**⚠️ THE THREE OFFSCREEN-BAKE GOTCHAS (cost a long debug session 2026-06-24 — don't regress):**
1. **Leftover-rig capture** → a bake instance left at RigOrigin on the isolation layer gets captured by the next bake as a frozen "ghost"/double. Fix: build the rig INSIDE the try (finally always cleans up) + `DestroyStaleRigs()` at bake start + `HideAndDontSave`.
2. **Off-screen skinning cull** → SkinnedMeshRenderer skips skinning when judged off-screen ("bald static body" while bone-parented hair/sword/armor still move). Fix: `smr.updateWhenOffscreen = true`.
3. **Off-loop matrix recalculation** → bones sample fine (verified: a foot bone moved 0.13u between frames) but the mesh draws at BIND POSE on a manual `cam.Render()` because the SMR only rebuilds bone matrices in the normal render loop. **Fix: `smr.forceMatrixRecalculationPerRender = true`** — THIS was the final unlock.

**Sampler:** use `AnimationMode.SampleAnimationClip` (the supported EDIT-MODE sampler). ❌ A manual `PlayableGraph.Evaluate()` does NOT drive an Animator outside Play mode (froze the pose) — tried and reverted. AnimationMode matches the clip by bone name (no cross-retarget), so use each model with the clip from its OWN FBX (Mixamo "With Skin" bundles both). **Debug trick that cracked it:** sample a limb bone at t=0 vs t/2 and log the position delta → instantly separates "animation broken" from "skinning broken".

Relates to [[sscary-player-sprite]] (the 4-drawn+flip predecessor) and [[direction-2p5d-pivot]].
