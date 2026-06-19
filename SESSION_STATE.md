# SESSION_STATE — unwritten (Unity 2D)

> ⚠️ **COMMIT REMINDER:** prior M1 pile was COMMITTED by the user. The new **M2 region skeleton** (Portal3D,
> Town hub baker, field↔town wiring, field boss) is UNCOMMITTED on `experiment/3d-world`. **Committing/pushing
> is the USER's job** — Claude only reminds, never commits.

---
## ⏩ CHECKPOINT (2026-06-19, LATEST) — visible-weapon spike CONCLUDED → not pursuing (reverted)
Branch `experiment/3d-world`, UNCOMMITTED. After seeing the placeholder 3D weapon hang/swing on the 2D player,
**user decided NOT to use it.** No visible-weapon system, no Low Poly RPG Fantasy Weapons purchase. **REVERTED
clean:** deleted `WeaponHolder.cs`, removed the `Facing` property + self-bootstrap line from `Player3DController`
(back to pristine); `grep` confirms zero "WeaponHolder" refs. Combat stays sprite-pose + `SlashEffect` FX as-is.
NEXT remains the JustCreate3D Medieval Village (houses/town) + M2 region. (Details of the spike below, historical.)

## ⏩ (history) the visible-weapon spike
User wanted visible weapons from the **Low Poly RPG Fantasy Weapons**
pack on the 2D billboard player, and **explicitly accepts the weapon NOT syncing to sprite frames**. Building the
engine side FIRST with a placeholder (asset-agnostic) so the buy is de-risked. RO-style approach: weapon = an
"equip layer" that follows the player + re-poses per facing, NOT welded to animation frames.

**NEW `WeaponHolder.cs`** (runtime): builds a **placeholder sword from primitives** (blade/guard/grip/pommel,
URP/Lit), hangs it on the player, and each LateUpdate: reads `Player3DController.Facing` (4-dir, camera-relative)
→ poses the weapon on the correct **side + depth** (front for down/left/right via a toward-camera offset so it
sorts over the sprite; behind for up-facing), **billboards** the flat side to the camera (orbit-proof via
`CameraRig`), and **sweeps on attack** (rising edge of `Player3DAttacker.Swinging`, sin out-and-back, no end-pop).
Tunables: handHeight/sideOffset/depthOffset/weaponScale/swingDuration/swingAmplitude. Perf: builds once,
LateUpdate is alloc-free vector math.
**EDITS:** `Player3DController` — added `public Vector2 Facing => facing;` + self-bootstrap line (marked SPIKE)
`if (GetComponent<WeaponHolder>()==null) gameObject.AddComponent<WeaponHolder>();`.

**⚠️ NO RE-BAKE** (self-bootstraps at runtime on the baked player). TEST: just press **Play** → a sword hangs at
the player's side, swaps side as you change facing, and sweeps when you attack a mob. JUDGE: does a 3D weapon on
the 2D sprite read well? Tune offsets in the WeaponHolder inspector. Possible caveat = sprite-vs-mesh depth
sorting on the "up" facing (weapon should hide behind). **If it reads well → confident buy** of the weapon pack +
swap placeholder for the real FBX (assign `weaponPrefab`). **REVERT:** delete `WeaponHolder.cs` + the bootstrap
line + the `Facing` property in `Player3DController`.

---
## ⏩ CHECKPOINT (2026-06-19) — DECISION: STAY 2D billboard characters (hybrid spike REJECTED, KayKit deleted)
Branch `experiment/3d-world`, UNCOMMITTED. After the full spike (3D KayKit Knight w/ idle/walk/run/attack/block +
sword, next to sprite mobs), **user decided on FEEL: keep 2D billboard characters.** RO-style 2.5D identity stands
(3D world + 2D sprite player & mobs). The 3D-player hybrid is OFF the table.
**CLEANUP DONE:** deleted all of `Assets/Art/KayKit/` (both the Adventurers free pack + Character Animations 1.1,
~65 MB) and the spike scripts `Editor/KayKitSpike.cs` + `KayKitSpikeMover.cs`. No scene referenced them; `grep`
confirms zero "KayKit" refs remain. Spike concluded — its value was the decision, cheaply (CC0/free assets, no
production code committed).
**BACK TO THE PLAN:** houses/town art = **JustCreate3D Low Poly Fantasy Medieval Village** (Vol.1, $10 — still the
pending buy; premade houses + props + URP demo, same artist as our nature). Then M2 region: wire village houses/
props into `RegionBuilder`/`NatureScatter` for Town + field maps. Weapons/visible-gear on the 2D player = future
2D-sprite-layer system (NOT 3D), if ever.

---
## ⏩ CHECKPOINT (2026-06-19) — SPIKE: 3D KayKit hero + 2D sprite mobs (hybrid test) [CONCLUDED → rejected]
Branch `experiment/3d-world`, UNCOMMITTED. **Exploring a HYBRID art direction** (user's idea): keep mobs as 2D
billboards (cheap, big bestiary) but make the PLAYER a rigged 3D KayKit character so weapons/armor are visible +
animate (the thing billboards can't do). Concentrates the rigging/animation/perf cost on the ONE entity that
matters; mobs stay cheap. Decision = pure visual-coherence call (smooth 3D hero vs flat pixel mobs) → **spike it**
(KayKit is CC0/free, like how the 2.5D pivot itself was spike-validated).

**Pack:** `Assets/Art/KayKit/KayKit_Adventurers_2.0_FREE` — 6 chars (Knight/Mage/Ranger/Rogue/Rogue_Hooded/Barbarian),
single gradient texture per char, shared **Rig_Medium** animation FBX (General = idle/attacks/block/hit/death,
MovementBasic = walk/run/jump). Rig imports **Generic**; weapons socket into dedicated **`handslot.r/.l`** bones.

**NEW (spike, throwaway):** `Editor/KayKitSpike.cs` → **Tools ▸ unwritten ▸ Spike ▸ Build KayKit 3D Hero**. One
click: sets Knight rig=Generic+own Avatar, sets the anim FBX to CopyFromOther(Knight avatar) so clips retarget,
builds a URP/Lit material from the gradient texture (no steel), builds an AnimatorController (Idle⇄Walk by Speed,
AnyState→Attack trigger, Idle⇄Block bool — clips picked by keyword at build time since FBX names are truncated),
instantiates the Knight in the open scene, sockets `sword_1handed` into `handslot.r`, adds `KayKitSpikeMover`
(runtime, WASD move + J attack + K block). Generated assets in `Assets/Art/KayKit/_Spike`. `HeroScale` const if
size is off vs the sprite player.

**ITERATION (verified by user screenshots):** hybrid **LOOKS COHESIVE** — chunky 3D Knight reads fine beside the
pixel mobs. Fixes applied: HeroScale 0.6 (was too big), keys moved to **Arrows + U(attack) + I(block)** (avoid the
game's W/A/S/D/K bindings), re-runs now replace the old hero. Lighting: started Unlit (bright but ignored the tree
shadow → looked pasted-on) → switched to **URP/Lit** so it receives+casts shadows (ambient 0.55 grey at noon keeps
shade soft). Hero renderers forced receiveShadows+cast On.

**⚠️ KEY FINDING — the FREE Adventurers pack has NO attack/block/cast animations.** Its entire clip set =
Idle_A/B · Walking_A/B/C · Running_A/B · Jump_* · Hit_A/B · Death_A/B · Spawn · Use_Item · Interact · Throw (verified
by extracting clip names from the FBX). Combat anims live in the **separate free "KayKit Character Animations"**
pack (same Rig_Medium skeleton, the pack page links it). So attack/block "not working" was NOT a code bug — the
clips don't exist. The spike now **auto-discovers any anim FBX under `Assets/Art/KayKit`** (path contains
"animation") + sets it CopyFromOther(Knight avatar), so dropping that pack in + re-running makes attack/block
"just work." Until then idle/walk/run/hit play; U/I are no-ops (logs a warning).

**EventSystem "only one active" warning = harmless** (spike scene + Systems each have one).

**ANIMATION PACK ADDED:** user dropped **KayKit Character Animations 1.1** at `Assets/Art/KayKit/KayKit_Character_
Animations_1.1` (Rig_Medium + Rig_Large + Mannequin meshes + glb dupes). Spike's `AnimPaths()` tightened to
**Rig_Medium `.fbx` only** (excludes Rig_Large/Mannequin/glb). Real clips confirmed: attack=`Melee_1H_Attack_Slice_
Diagonal`, block=`Melee_Blocking` (held loop). Loop keywords now also loop `blocking`; clip freeze fixed (idle/
walk/run looped); Run state added (Shift). Controls FINAL: Arrows=move, Shift=run, U=attack, hold I=block.

**⚠️ TEST:** re-run the menu → full combat loop should play next to the sprite mobs (U slice, I block, walk/run
loop, no freeze, Lit + tree shadow). **DECISION still open:** hybrid good enough to pursue? (user leaning yes →
if yes, next is planning the REAL migration: 3D player on existing combat/stats/skills, mobs stay sprites).
**REVERT:** delete `Assets/Art/KayKit/_Spike` + `Editor/KayKitSpike.cs` + `KayKitSpikeMover.cs`.

---
## ⏩ CHECKPOINT (2026-06-19) — QUATERNIUS RETIRED → pivot to JustCreate3D Medieval Village
Branch `experiment/3d-world`, UNCOMMITTED. **Decision (user):** drop the procedural Quaternius house pipeline
entirely and buy JustCreate3D's **Low Poly Fantasy Medieval Village** ($10, itch.io) — same artist as our existing
`LowPolySimpleNature_FBX`, single 1024 texture, **9 premade houses** + props + nature + a **URP demo scene**. Why:
Quaternius was realistic-PBR (steel-look materials, didn't match our flat low-poly terrain) and modular-only (had
to assemble by hand); the JustCreate pack is premade + style-matched + perf-friendly (one atlas material). Vol.1
($10) chosen over Vol.2 ($20) — Vol.2 is modular/interiors-focused = more hand-building, against the user's goal.

**DELETED this session (clean, git-recoverable):** `Assets/Art/Quaternius/`, `Assets/Prefabs/Quaternius/` (3 baked
houses), and the Quaternius-only tooling `Editor/HouseAssembler.cs` + `Editor/QuaterniusMaterials.cs`. No scenes
referenced the prefabs; `grep` confirms zero "Quaternius" refs remain. The `Tools ▸ unwritten ▸ Build Houses` menu
is gone with those scripts.

**FOLDER CONVENTION (user):** group vendor art under `Assets/Art/<vendor>/`. Moving `LowPolySimpleNature_FBX` →
`Assets/Art/JustCreate3d/LowPolySimpleNature_FBX` (do the move INSIDE Unity so GUIDs/refs survive). Already updated
the one hardcoded path: `Editor/KenneyPrefabBaker.cs` BuildLowPolyNature now points at the JustCreate3d location.
New village pack lands as `Assets/Art/JustCreate3d/MedievalVillage` (or similar — user to confirm name on import).

**NEXT:** user imports the village `.unitypackage` → move under `JustCreate3d/` → wire its 9 house prefabs + props
into `RegionBuilder`/`NatureScatter` for the Town (and reuse props/nature/landscape for FIELD maps too — same
license covers unlimited maps). Possible later add-ons (same artist, same style): Grass & Plants pack (shader-wind,
URP, scatter as walkable no-collider clumps — watch density vs perf) and Village Vol.2 (detail/interiors). The OLD
Quaternius tuning checkpoints below are RETIRED/historical.

---
## ⏩ CHECKPOINT (2026-06-19, retired) — HOUSE ASSEMBLER tuning pass #1 (roof + wall facing fixed)
Branch `experiment/3d-world`, UNCOMMITTED. First tuning pass on `Editor/HouseAssembler.cs` after the user
looked at the baked houses: reported **wrong size · gaps/walls facing wrong · "no roof"** (NOT gray — materials
render fine). Two provable code bugs fixed:
- **ROOF (the "no roof"):** roof was rotated upright (−90°X) then scaled with `localScale` against WORLD bounds,
  so the scale axes were permuted — depth got 0.36 (should be ~0.60), height blew up → roof squished/invisible.
  NEW `ScaleRoofToFootprint` + `SetWorldScale`/`PickAxis` route each world target to the correct LOCAL axis
  (valid because all rotations here are axis-aligned 90° → clean permutation, no shear). Roof now sizes to the
  footprint + overhang with pitch proportional. Knobs: `RoofHeightMul` (1=natural), `RoofExtraYaw` (90 if ridge
  runs the wrong way).
- **WALL FACING:** opposite walls shared the same yaw → one of each pair showed its back inward. Now south/west
  get +180° vs north/east (antiparallel) so all show their outer face. `FlipFrontBack` bool swaps the whole house
  if it bakes inside-out (mesh front-face direction is unknown until seen).
- **SIZE:** left `HouseScale = 1` (a taste call, ambiguous direction) — dial it after seeing the fixed bake
  (~0.7 if too big, ~1.3 if too small). Player billboard ≈ 1u tall; house ≈ 4–5u.

⚠️ **RE-BAKE:** Tools ▸ unwritten ▸ Build Houses ▸ Medieval House — Small/Medium/Large (overwrites the 3 prefabs
in `Assets/Prefabs/Quaternius`).

**VERIFIED (user screenshot):** after re-bake the ROOF + FACING fixes WORK — clean red-tile roof, walls face out,
good proportions. Remaining issue was **"steel" walls**.

## ⏩ tuning pass #2 — MATERIALS (kill the chrome) — BUILT (code)
The kit is a **PBR texture set** (`Assets/Art/Quaternius/Textures/T_<Set>_BaseColor/_Normal/_ORM.png` for sets
Plaster · WoodTrim · Brick · UnevenBrick · RedBrick · RockTrim · RoundTiles), NOT vertex colors / not a single
atlas. The FREE pack's imported materials use a metallic-ORM setup that URP misreads → every surface looks like
polished STEEL. NEW `Editor/QuaterniusMaterials.cs`: builds one clean **URP/Lit** material per surface set
(BaseColor→Base Map, Normal→Normal Map auto-set to NormalMap import type, **Metallic 0**, Smoothness 0.12) into
`Assets/Art/Quaternius/Materials/M_<Set>.mat`, and `RetextureToUrp(root)` re-assigns each renderer by reading the
albedo texture it already uses (reliable key, no name-guessing). `HouseAssembler` calls it automatically before
saving, so re-baked houses come out matte. Also a standalone menu **Tools ▸ unwritten ▸ Build Houses ▸ Re-URP
Selected (de-chrome)** to fix any already-placed Quaternius object in a scene. Consts `Metallic`/`Smoothness` at
top to tune. ⚠️ ORM roughness is NOT channel-unpacked (flat smoothness instead) — fine for stylized; revisit if
they want per-pixel roughness. **RE-BAKE the houses again** to pick up the materials (or Re-URP Selected on the
instance already in the scene). Then report: matte now? right surfaces (plaster walls / wood beams / clay roof)?

---
## ⏩ CHECKPOINT (2026-06-19) — M2: REGION SKELETON (Town hub ↔ Field + field BOSS) — BUILT (code)
Branch `experiment/3d-world`, UNCOMMITTED. Start of **M2 (VIN-17)**. Decisions (user): skeleton loop first ·
one-click editor bakers · **RO-style FIELD boss, NO dungeon yet** (anthell-style dungeons deferred). The 3D
multi-map architecture was already 90% there (`MapManager.Travel` is scene-name based + works in 3D;
`PersistentPlayer3D` already places at the `SpawnPoint` a portal asks for). Only the **portal primitive** was
2D-only — that was the gap.

**NEW `Portal3D.cs`** (runtime) — 2.5D analogue of the 2D `Portal`: a trigger `Collider` that detects the
`Player3DController` (a CharacterController, fires OnTriggerEnter) → `MapManager.Travel(targetScene, spawnId)`.
`fired` guard against double-trigger during the async load.
**NEW `BossBanner.cs`** (runtime) — floating name + HP bar above a boss (world-space, billboarded, counter-
scaled), polls `Health` but only repaints on HP change (TrainingDummy discipline); hides while dead, restores
on revive.
**NEW `MobRoster.Boss`** — RO MVP archetype (Dragon sheet, ×2.3 size, 600 HP, def 8/mdef 6, atk 16, xp 400,
wide aggro/long leash, never flees). NOT in the weighted `All` pool — placed once by the tool.
**NEW `Editor/RegionBuilder.cs`** — `Tools ▸ unwritten ▸ Region ▸`:
- **Build Town Hub** → bakes `Assets/Scenes/Town.unity` (Ground_02 + Ground layer + Pathfinder3DConfig +
  SpawnPoints `Start`/`FromField` + a Portal3D → NatureStage @ `FromTown` + a visible warp pad). No mobs (safe
  zone). Registers in Build Settings.
- **Link Field → Town (open field map)** → non-destructively adds SpawnPoint `FromTown` + Portal3D → Town @
  `FromField` to the OPEN field scene (idempotent). Run with NatureStage open.
- **Spawn Field Boss (open field map)** → drops `MobRoster.Boss` (+ BossBanner, respawnDelay 120s, homeRadius
  10) into the open field map.

**TRAVEL CONTRACT:** Town `Start`(boot)/`FromField` ↔ Field `FromTown`. Town pad (cyan, +X) → field; field pad
(gold, −X) → town. **BOOT MAP → "Town"** (`SystemsSceneBuilder.FirstMap` + `GameRoot.firstMap`, were the stale
"NatureStage"/"NewTown").

**⚠️ DO (in order):** 1) **Region ▸ Build Town Hub**. 2) Open **NatureStage** → **Region ▸ Link Field → Town**.
3) (optional) NatureStage open → **Region ▸ Spawn Field Boss**. 4) **Setup Systems Scene** (picks up boot map =
Town). TEST: open **Town**, press Play → spawn at Start → walk into cyan pad → arrive in field next to gold pad
→ fight mobs + the Dragon boss (name + HP bar) → walk into gold pad → back in town. **COMMIT when ready (USER's job).**

**HOUSES PIPELINE (same session):** houses = 3D low-poly models (matches the 3D ground; characters stay 2D
billboards). Reuses the existing `KenneyPrefabBaker` (FBX → drop-ready prefab w/ shared colormap + MeshCollider).
Added **asset-agnostic** `Build Prefabs ▸ From Folder…` (pick ANY downloaded kit's FBX folder under Assets →
auto-detects colormap → bakes to `Assets/Prefabs/<folder>`) and **Region ▸ Drop Selected to Ground** (raycast-
snaps hand-dragged props onto the bumpy terrain). WORKFLOW: download a CC0 kit (Kenney Fantasy Town / Medieval /
City — FBX) → drop under `Assets/Art/…` → Build Prefabs ▸ From Folder… → drag houses into Town → Drop Selected
to Ground. ⚠️ Keep houses OFF the Ground layer (Default is fine) so the player-arrival raycast doesn't snap onto
roofs. Pairs with VIN-15 (skin props).

**QUATERNIUS MODULAR MEDIEVAL (user's actual download, `Assets/Art/Quaternius/FBX`):** it's a MODULAR kit (no
whole houses — walls/corners/roofs/doors/windows as separate FBX) and **vertex-colored** (no texture/atlas;
`materialImportMode 2`), so the colormap baker does NOT apply. User chose a **procedural assembler**. NEW
`Editor/HouseAssembler.cs` → `Tools ▸ unwritten ▸ Build Houses ▸ Medieval House Small/Medium/Large` — bounds-
DRIVEN (measures each piece's real AABB, aligns by edges, no hard-coded grid): builds a wall-ring facade +
corners + front door + a scaled roof (no interior floor — RO facades you walk around), keeps imported materials,
adds MeshColliders, saves to `Assets/Prefabs/Quaternius`. Consts at top (piece names, `HouseScale`,
`RoofOverhang`) for iteration. ⚠️ EXPECT a tuning pass: if houses render GRAY → add a vertex-color shader; wrong
size vs player → bump `HouseScale`; wall facing/gaps → tweak piece names+offsets, re-run (overwrites).

**NEXT (M2):** more field maps (3–5 total — same bakers) · NPCs + dialogue + a quest system · a story thread ·
minimap/POI. The field-boss is the MVP placeholder until an anthell-style dungeon later.

---
## ⏩ CHECKPOINT (2026-06-18) — Meteor multi-hit: PER-TICK yellow totals + overkill numbers + per-hit SFX
Branch `experiment/3d-world`, UNCOMMITTED. Reworked the RO multi-hit so the yellow combo total shows **once per
tick** (not one across the whole skill), per-hit numbers keep showing even after a mob dies mid-sequence, **one
meteor falls per tick**, and a **custom `hit.wav` plays on every hit beat**.

**PER-HIT SOUND (custom `hit.wav`).** `MultiHitBurst.Run` gained an optional `onHitBeat` callback — fires **once
per hit pulse** (a hit landed on ≥1 mob), NOT per-mob (so a pack doesn't stack N copies into mush) and NOT per
tick. `Player3DMeteor` got a `hitSfx` `SfxSet` field, passed as `() => Sfx.Play(hitSfx)`. NEW `SfxAssets.Custom(vol,
paths…)` loads clips from arbitrary `Assets/…` paths (outside the TomMusic pack); `Build25DSpike` wires
`meteor.hitSfx = SfxAssets.Custom(0.8f, "Assets/Audio/Customs/hit.wav")`.

**PER-METEOR FIREBALL whoosh (staggered) + hit-interval tuning.** `Player3DMeteor.hitInterval` 0.3 → 0.1 → **0.2**.
NEW `meteorSfx` `SfxSet` plays once per METEOR **on LAUNCH** — wired to randomized **Fireball 1/2/3** from `Spells`
(`SfxSet.Pick` randomizes). `CastAt` now starts a **`MeteorShower` coroutine** that spawns the meteors one at a
time, `meteorInterval` (1.2s) apart — so each meteor is its OWN fall animation with its OWN fireball whoosh
(replaced the old simultaneous staggered-HEIGHT spawn, which fired all 3 whooshes the same frame). Coroutine
survives `enabled=false` (class switch); impacts stay `meteorInterval` apart so the damage cadence is unchanged.
Impact `onTick` is explosion-FX-only (no boom). Added `using System.Collections;`.

**SCREEN SHAKE on each meteor impact.** NEW reusable shake in `CameraFollow3D` — static `ShakeMain(magnitude,
duration)` + `Main` singleton; the offset is applied in `LateUpdate` ON TOP of the smoothed follow (and undone
next frame so it never feeds back into `SmoothDamp`), camera-local screen-plane jitter with linear decay,
overlapping shakes take strongest+longest. Alloc-free + no-op when idle. `Player3DMeteor.DetonateTick` calls it
per meteor impact; tunable `shakeStrength` (0.3) + `shakeDuration` (0.18) fields. **Code-only, NO re-bake** (the
new float fields take their inline defaults on the baked player; only the audio CLIPS needed the re-bake).

⚠️ **RE-BAKE Setup Systems Scene** to put the clips + new `hitInterval` 0.2 onto the baked player (new serialized
fields default empty/old value until a fresh `AddComponent` on re-bake; tune volumes/`meteorInterval` on the component).

**`MultiHitBurst.cs`** — `totals[]`/`totalPopups[]` are now **reset at the start of EACH tick** (and the
popup released at the tick's end), so a 3-tick skill pops **3 separate rising yellow totals** per mob: each
appears on its tick's FIRST hit, accumulates over the tick's hits, then rises + fades when the tick ends.
(Before: one running total across all 15 hits, released only at the very end.)
**`DamagePopup.cs`** — `SpawnHeld`/`SetHeld`/`Release`: a held, live-updating number that sits + pulses while
it accumulates, then rises+fades on Release. `MaxHold` 8s failsafe.
**`Health.TakeMagicDamageOverkill`** — always pops a damage number, **even on an already-dead mob** (HP floors
at 0, death fires once). So the flurry's remaining ticks still show over a mob that died partway (RO overkill).
**`Player3DMeteor`** — now drops **ONE meteor PER TICK** (a 3-meteor shower). All meteors launch at once but
from **staggered heights** (higher = lands later, same fall speed) so impacts are ~`meteorInterval` (1.2s) apart
**without a coroutine** — each `Projectile3D` is self-contained (survives a mid-cast class switch). Each meteor's
`onArrive` → `DetonateTick` = a **1-tick** `MultiHitBurst.Run` (5 hits + its own yellow total + 1 explosion), so
the numbers land exactly when that meteor hits. Per-hit dmg = `damageMult / (ticks×hitsPerTick)` = `/15`, so the
shower deals the SAME total as before (visual split, not a buff; bump `damageMult` if numbers read small). New
fields: `meteorInterval` (1.2, gap between impacts) + `meteorScatter` (0.6, horizontal spread). Removed the now-
unused `tickInterval`. **3 ticks × 5 hits = 15** unchanged.

TEST: Mage → press **3** → click a pack → **3 meteors rain down one after another**; each impact pops its own
explosion + 5 purple per-hit numbers + a rising yellow total, and the numbers keep counting over mobs that die
mid-burst. Tune `meteorInterval` for how fast the shower falls.

---
## ⏩ CHECKPOINT (2026-06-18) — Meteor: yellow combo TOTAL + ground-target reticle (circle)
Branch `experiment/3d-world`, UNCOMMITTED. Two polish asks on the multi-hit Meteor — **all code-only** except
the bigger radius (see re-bake).

**1. RO-style yellow COMBO TOTAL.** `MultiHitBurst.Run`'s `onHit` now RETURNS the damage it dealt; the burst
sums it per mob and, when the ticks finish, pops a **big bright-yellow total** over each mob (1.7× size) — the
individual purple per-tick numbers still flash (exactly like RO: hit-flurry + combo number). `DamagePopup`
gained a `scaleMult` overload (`Spawn/SpawnText(..., float scaleMult)`) for the bigger number. Total shows over
kills too (the GameObject lingers during the death dissolve).

**2. GROUND-TARGET RETICLE.** NEW reusable **`GroundTargeter.cs`** — `Begin(player, radius, castRange, onConfirm)`
puts you in AIM mode: a **LineRenderer circle** (sized to the AoE) follows the cursor on the ground, clamped to
castRange. **Left-click = drop**, **Esc / re-press = cancel**. While `GroundTargeter.Active`,
`Player3DController.ReadInput` skips (the confirm click doesn't also move you). MP/cooldown are spent only on
CONFIRM (cancel = free). **`Player3DMeteor`** rewired: `Activate()` enters aim → `CastAt(point)` casts; **radius
3→4.5, castRange 9→11** (bigger circle). New flow: press **3** → circle on cursor → click ground to drop.

**⚠️ RE-BAKE: Setup Systems Scene** — ONLY to pick up the bigger `radius`/`castRange` defaults on the already-
baked Meteor (serialized at the old 3/9). The reticle, click-to-place, yellow total, and multi-hit all work
**code-only** (just a smaller circle until re-baked). TEST: Mage → press 3 → orange circle follows mouse →
click a pack → meteor drops, 3 purple ticks/mob + a yellow TOTAL, some stun. Esc cancels with no cost.

---
## ⏩ CHECKPOINT (2026-06-18) — RO-style MULTI-HIT + STATUS (stun) on Meteor
Branch `experiment/3d-world`, UNCOMMITTED. The "one cast, several damage ticks + per-hit proc" RO pattern,
built as reusable systems and applied to Meteor.

**NEW reusable `MultiHitBurst.cs`** — `MultiHitBurst.Run(center, radius, hits, interval, onHit)` snapshots the
mobs in a blast ONCE (death-safe, like the Warcry fix), then calls `onHit(mob, hitIndex)` for each living
target across `hits` waves spaced `interval` apart (numbers pop in sequence). Runs on its OWN GameObject, so it
survives even if the casting skill is disabled (class switch) mid-burst.
**NEW reusable `StatusFX.cs`** — a looping indicator that follows a unit + billboards for a duration
(generalized [[GuardShieldFX]]); self-destroys when the unit is destroyed.
**NEW reusable `MonsterAI3D.Stun(seconds)`** — freezes the mob (reuses the `Stagger` freeze = no move/attack)
+ shows a spinning **swirl over its head** (`FX/Magic/Spirit`, via StatusFX) + a "Stun!" popup (once, no spam
on re-stun). `stunSheet` assigned to ALL mobs in `BuildMonster`; null sheet = freeze+popup, no swirl.

**METEOR is now multi-hit + per-hit stun** — `Player3DMeteor.Detonate` → `MultiHitBurst.Run`. Defaults: **3
hits**, `hitInterval` 0.12s, each tick rolls **its own** magic damage (`damageMult/hits` = the total split, so
it's NOT a power buff) AND **its own stun proc** (`stunChance` 0.25, `stunSeconds` 1.5). 3×25% ≈ 58% to lock a
mob. All knobs on the `Player3DMeteor` component. This is the template for adding multi-hit/procs to other
skills (Fireball/Bash/… just call `MultiHitBurst.Run` or roll a status in their hit).

**⚠️ RE-BAKE:** **Populate Mobs (Nature)** ONLY (mobs get `stunSheet` for the swirl). Meteor's multi-hit +
stun-freeze + "Stun!" popup are **code-only** (new fields default; works without re-bake — just no swirl until
mobs are re-baked). TEST: P → Class ▸ Mage → press **3** on a pack → meteor falls, **3 damage numbers per mob
in sequence**, some mobs freeze with a head-swirl + "Stun!". **COMMIT the pile when ready (USER's job).**

---
## ⏩ CHECKPOINT (2026-06-18) — M1: MAGE (class #2) + projectile system + class toggle
Branch `experiment/3d-world`, UNCOMMITTED. The second class kit + the long-promised Flam projectile.

**NEW reusable `Projectile3D.cs`** — a traveling billboard projectile: flies from a point toward a Transform
(homes) or a fixed fallback point, fires an on-arrive callback (caller applies damage) + optional impact FX,
then self-destroys. Frames via SpriteSheetCache; created per shot like SlashEffect. Powers the Flam fireball
AND the Mage's Fireball/Meteor.

**FLAM MOB now lobs a VISIBLE fireball** — `MonsterAI3D` magic attack: if `projectileSheet` set (assigned to
magic mobs in `BuildMonster` = `FX/Projectile/Fireball`), spawn a Projectile3D at the player; the magic damage
lands ON ARRIVAL (was instant). Null sheet = old instant touch (fallback). Also added **`MonsterAI3D.Slow(factor,
secs)`** (chills move speed; applied each frame) for Frost Nova.

**MAGE KIT (chosen: Fireball · Frost Nova · Meteor)** — 3 new `IHotbarSkill` components mirroring the caster
template: **`Player3DFireball`** (1: homing projectile nuke, explosion impact), **`Player3DFrostNova`** (2:
PBAoE magic dmg + Slow, two-pass collect-then-apply à la the Warcry fix), **`Player3DMeteor`** (3: ground-
targeted — falling meteor IS the telegraph — AoE on impact; uses new `Player3DController.GroundUnderCursor`).
Bolt (Q) stays the universal basic (icon swapped to `Spell/OrbLight` so it's distinct from Mage Fireball).

**CLASS SYSTEM (minimal, real)** — `enum PlayerClass {Swordsman, Mage}` + `ClassState` (PlayerPrefs). Every
skill now implements **`IHotbarSkill.InClass(c)`** (Swordsman kit → Swordsman, Mage kit → Mage, Bolt → both).
`Player3DController` bootstraps BOTH kits and `ApplyClass(c)` **enables only the chosen class's components**
(disabled = no keypress + filtered off the bar) then calls `HotbarPanel.RebuildRequested`. `HotbarPanel` now
shows only ENABLED skills + has `Rebuild()`. **Switch live via the DevSettings (P) panel → "Class" ▸ "Mage
(off = Swordsman)"** → swaps kit + rebuilds hotbar, persists.

**⚠️ RE-BAKES:** **Setup Systems Scene** (player gets Mage components + FX/icons + Bolt icon) + **Populate Mobs
(Nature)** (Flam projectile). NOT Build UI Shell (HotbarPanel code-only). Without re-bake: Mage self-bootstraps
asset-less (text labels, instant spells), Flam stays instant — functional, no visuals. TEST: P → Class ▸ Mage →
bar shows Fireball/Frost/Meteor/Bolt; 1 = flying fireball→boom, 2 = frost ring slows mobs, 3 = click ground →
meteor falls; get hit by a Flam mob = incoming fireball. **COMMIT the pile when ready (USER's job).**

**NEXT (M1):** that's the combat-variety slice done. Remaining M1 → **M2** (one region: town + quests + dungeon).

---
## ⏩ CHECKPOINT (2026-06-18) — FIX: Warcry AoE skipped ~half its targets
Branch `experiment/3d-world`, UNCOMMITTED. **Code-only, no re-bake.** `Player3DWarcry.Activate` damaged mobs
while iterating `MonsterAI3D.Active` by index — but a one-shot kill removes the mob from `Active` mid-loop
(Health.Die → DeathDissolve disables MonsterAI3D → OnDisable → `Active.Remove`), shifting indices so `i++`
skipped the NEXT mob (≈ every other one on a one-shot AoE). FIX: two-pass — collect in-range mobs into a
reused static `targets` list FIRST, then damage/knockback from the snapshot (immune to mid-loop removal).
Now every mob in `radius` is hit. (If a tanky mob still survives, that's the min–max damage roll × `damageMult`,
not the bug — tune `damageMult`.) ⚠️ Pattern note: never mutate-by-kill while index-iterating a live registry.

---
## ⏩ CHECKPOINT (2026-06-18) — FIX: units stacking / hopping on each other's heads
Branch `experiment/3d-world`, UNCOMMITTED (user commits). Mobs were piling into one spot and the player could
stand on top of them. Root cause: **CharacterControllers don't keep each other apart** (so mobs converged) and
the **0.3 stepOffset let a CC climb onto another CC's capsule** (the "hopping on heads"). The 2D game already
solved this (Crowd layer + agent avoidance); the 3D port had dropped it. Ported both.

**FIX 1 — units pass through each other** (no climbing, no hard pile-ups): NEW `UnitCrowd.cs` puts player +
mobs on a shared **"Crowd" layer** and, once at runtime, `Physics.IgnoreLayerCollision(Crowd, Crowd, true)`.
Units still collide with terrain/props (other layers) — only unit↔unit collision is off. `MonsterAI3D.Awake`
and `Player3DController.Awake` call `UnitCrowd.Register(gameObject)`. The layer is CREATED at bake via the new
generalized `NatureStageBuilder.EnsureLayer(name)` (EnsureGroundLayer now wraps it), and assigned in
`Build25DSpike.BuildPlayer`/`BuildMonster`. Camera/Light cullingMask = Everything, so Crowd still renders/lit.
⚠️ **The player now WALKS THROUGH mobs** (RO-style) instead of being body-blocked — that's what guarantees you
can't stand on them. Melee/targeting are distance+raycast based, unaffected.

**FIX 2 — mobs fan out** (since they no longer block each other): ported the 2D `PathAgent`'s local avoidance
into **`PathAgent3D`** — `avoidRadius 0.8` / `avoidStrength 0.9` + `Steer()` (radial push-off + tangential
slide around whoever's dead ahead, breaks conga lines). One `OverlapSphereNonAlloc` per agent/frame (reused
buffer, alloc-free — sanctioned per PERFORMANCE.md). `MonsterAI3D.Start` sets `agent.avoidMask = UnitCrowd.Mask`
and `agent.avoidIgnore = player` (so mobs spread from each other but NOT from the player → melee still closes
in). Halted-but-stacked mobs also drift apart (Steer with zero desired). Knockback branch runs before Steer
(shove dominates).

**⚠️ RE-BAKES:** **Setup Systems Scene** (player → Crowd + register) **and** **Populate Mobs (Nature)** (mobs →
Crowd). Both create/assign the layer; without a re-bake the layer doesn't exist and it safely no-ops (old
behavior). TEST: gather a swarm → they ring/spread around you instead of stacking; you can't stand on a mob nor
they on each other; you walk through them. **COMMIT the pile when ready (USER's job).**

---
## ⏩ CHECKPOINT (2026-06-18) — M1: WARCRY (AoE knockback) — Swordsman now has 4 actives
Branch `experiment/3d-world`, UNCOMMITTED (user commits). A point-blank **AoE** that damages + knocks back +
staggers all nearby mobs.

**NEW `Player3DWarcry.cs`** (key **4**, `IHotbarSkill` slot 3 → on the hotbar; **Bolt bumped to slot 4**, so
bar = Bash(1)·Charge(2)·Guard(3)·**Warcry(4)**·Bolt(Q), 5 of 6 cells filled). No target — point-blank; can
fire into empty space. Defaults: MP 7, cooldown 10s, **radius 3.5**, damage **1.4× a swing to every mob in
range**. Scans `MonsterAI3D.Active` (registry, cached `.Health` — NO OverlapSphere), deals
`Health.TakeDamage(power, crit, -1)` (always-lands), then knocks each back + staggers. Self-bootstraps via
`Player3DController.Awake`.

**KNOCKBACK plumbing (reusable):** `PathAgent3D.Knockback(dir, speed, secs)` — a decelerating shove through
the CharacterController (so it still collides with props/terrain, never phases), overrides pathing for the
window. `MonsterAI3D.Knockback(worldFrom, speed, time, stagger)` — computes the away-dir, calls the agent's
shove + `Stagger` (so the brain doesn't fight the slide), reusing the cached agent. Distance ≈ ½·speed·time
(defaults 13/0.22 ≈ **1.4 units**).

**WIRED in `Build25DSpike.BuildPlayer`:** shockwave FX `FX/Magic/Aura` (sliced 24×5 via SlashEffect.Spawn) +
`warcrySfx` (Spell Impact 1-3 boom) + icon `Spell/Explosion`.

**⚠️ RE-BAKES (still the same two):** **Build UI Shell** (HotbarPanel) + **Setup Systems Scene** (gives the
player Warcry's FX/SFX/icon). Without Setup Systems Scene it still works on key 4 (text label, no shockwave/
boom/icon). TEST: gather a pack, press **4** → aura pulse + boom, everyone takes a hit and gets shoved ~1.4u
outward + briefly frozen, slot 4 sweeps its 10s cooldown. **COMMIT the pile when ready (USER's job).**

**NEXT (M1 remaining):** Mage template (class #2; also gives Flam a real projectile). Then **M2** (one region:
town + quests + dungeon).

---
## ⏩ CHECKPOINT (2026-06-18) — M1: GUARD (defensive Swordsman active) — kit complete
Branch `experiment/3d-world`, UNCOMMITTED (user commits). The **3rd Swordsman skill**, completing the kit.

**Mechanic = a timed damage-reduction STANCE** (not a target skill, not fake "stagger immunity" — the player
has no stagger to be immune to). `Character` gained `BeginGuard(secs, reduction)` + `GuardActive` +
`GuardRemaining` + a private `Guarded(dmg)` applied in BOTH `TakeDamage` and `TakeMagicDamage` (cuts
physical AND magic by `damageReduction`, min 1). Cleared on `Revive()`. Runtime-only (not saved).

**NEW `Player3DGuard.cs`** (key **3**, `IHotbarSkill` → auto-appears on the hotbar as slot 3). No target —
self-buff; fires whenever off-cooldown + affordable. Defaults: MP 4, cooldown 9s, **duration 3.5s,
reduction 0.6 (take 40%)** — all tunable. Self-bootstraps via `Player3DController.Awake` (works on key 3 with
no rebuild; assets null until Build Player).
**NEW `GuardShieldFX.cs`** — a looping shield bubble (`FX/Magic/Shield/SpriteSheetBlue.png`, sliced 24×6 via
SpriteSheetCache) that FOLLOWS the player (not parented → no billboard/breathe scale inheritance), shown for
the stance duration, billboarded, FxSlash band. Created lazily on first use.

**HOTBAR is now 4 actives** — reordered by key: **Bash(1)·Charge(2)·Guard(3)·Bolt(Q)** (Guard `SlotOrder` 2,
Bolt bumped to 3). 2 of the 6 baked cells stay empty.

**WIRED in `Build25DSpike.BuildPlayer`:** shieldSheet + `guardSfx` (Sword Blocked 1-3 clang) + icon
`Spell/DefenseUpgrade`.

**⚠️ RE-BAKES (same two as the hotbar):** **Build UI Shell** (HotbarPanel on the canvas — if not already
done) + **Setup Systems Scene** (gives the player Guard's assets + all skill icons). Without Setup Systems
Scene, Guard still works on key 3 but with no shield FX / clang / icon (text label "Guard"). TEST: stand in a
mob, press **3** → shield bubble + clang, incoming numbers drop ~60% for 3.5s, hotbar slot 3 sweeps its 9s
cooldown. **COMMIT the pile when ready (USER's job).**

**NEXT (M1 remaining):** Mage template (class #2; also gives Flam a real projectile). Then **M2** (one region:
town + quests + dungeon).

---
## ⏩ CHECKPOINT (2026-06-18) — M1: HOTBAR UI (actives + cooldowns) + skill icons
Branch `experiment/3d-world`, UNCOMMITTED (user commits). Built the **HUD hotbar** that makes the Swordsman
kit legible — the next M1 item after the class build below.

**NEW reusable extension point: `IHotbarSkill`** (`IHotbarSkill.cs`) — any player active that implements it
appears on the bar automatically, ordered by `SlotOrder`. Exposes label/key/icon/MP/affordable/cooldown
fraction+remaining/tint + `Activate()`. **Refactored `Player3DBash`/`Player3DCharge`/`Player3DCaster`** to
implement it: each gained a public `Activate()` (the keypress path AND the hotbar-click path both call it —
identical target/MP/cooldown rules) + a `float lastDuration` (so the sweep knows the % left) + a `public
Sprite icon`. Behavior on keypress is unchanged.

**NEW `HotbarPanel.cs`** (runtime, on GameUICanvas) — finds the `Player3DController`, collects its
`IHotbarSkill`s, and decorates the **first N of the 6 baked `HUD_Hotbar` cells** (built empty by
`UIShellBuilder.BuildHud`) with: icon (or text label fallback), hotkey glyph (top-left), MP cost
(bottom-right, reddens when unaffordable), a **radial cooldown sweep** (1×1 white sprite, Filled/Radial360)
+ a live countdown number, and a Button that fires the skill on click. Builds once (retries till player+holder
exist), then per-frame just sets fillAmount + repaints the second-counter when it ticks (alloc-light).
Currently shows **3 actives** (Bash·Charge·Bolt); the other 3 cells stay empty (room for the defensive active).

**ICONS (user found the Ninja "Skill Icon" pack** `Assets/Art/NinjaAdventure/Ui/Skill Icon/`): assigned at
bake in `Build25DSpike.BuildPlayer` via new `LoadSkillIcon(...)` — **Bash→Spell/Cut, Charge→Spell/Counter,
Bolt→Spell/Fireball** (24×24, were Sprite/**Multiple** → loader forces Single so `LoadAssetAtPath<Sprite>`
returns them). Swappable per-skill in the Inspector (`icon` field). Other Spell/Meteo/Job icons are there for
future skills.

**⚠️ RE-BAKES:** **Build UI Shell** (REQUIRED — bakes `HotbarPanel` onto `GameUICanvas.prefab`; without it
there's no hotbar). **Setup Systems Scene** (for the ICONS — re-runs `BuildPlayer` to assign them; without it
the bar still works but shows TEXT labels "Bash/Charge/Bolt"). TEST: Play → bottom-center bar shows 3 slots
with icons + keys 1/2/Q; use a skill (key or click) → radial sweep + countdown; drain MP → MP number reddens
& slot dims; off-cooldown → ready. **COMMIT the pile when ready (USER's job).**

**NEXT (M1 remaining):** defensive Swordsman active (guard/endure — will auto-appear on the bar as slot 4) ·
Mage template (gives Flam a real projectile). Then M2.

---
## ⏩ CHECKPOINT (2026-06-18) — ROADMAP REFRAME + M1: SAVE + ENEMY VARIETY + SWORDSMAN CLASS
Branch `experiment/3d-world`, UNCOMMITTED (user commits). Code-only; **player skills SELF-BOOTSTRAP (no rebuild)**.
⚠️ **Re-run `Populate Mobs (Nature)` once** for the new bestiary.

**ROADMAP REFRAMED → two-tier launch plan.** Doc renamed **"unwritten — Roadmap to Launch"**
(`q1732p46z2r8bsssth46fq955n88kveq`): **Tier 1** = Minimum Shippable single-player/co-op ARPG on Steam
(milestones **M1–M5**), **Tier 2** = full MMO (deferred — "a second whole game"). Honest distance: ~5–10% to the
MMO dream; months to a Tier-1 demo. **Deleted** the superseded "Unity 2D Dev Setup" doc. NEW issues
**VIN-16..20 = M1..M5** (VIN-16 = M1, in-progress), linked to the roadmap.

**M1 BUILT this session (code-only, on the existing player/scene):**
1. **SAVE/LOAD (progression slice 1).** NEW `SaveData.cs` / `SaveSystem.cs` (JSON → `persistentDataPath`) /
   `SaveManager.cs` (self-bootstraps via `[RuntimeInitializeOnLoadMethod]`; auto-load on launch, auto-save on
   level-up/respawn/quit/20s-dirty) / `Editor/SaveTools.cs` (Tools ▸ unwritten ▸ Save ▸ Delete/Open/Print).
   `Character.CaptureSave/RestoreSave` + `PassiveRanks.RankMap/LoadState`. Persists level/XP/HP/MP/6 primary
   stats+points/TRIO-RING ranks+banked+leader. ⚠️ **Equipment + bag NOT saved** (UI-layer → M3). Death/respawn
   was ALREADY built (`DeathScreen.cs`) — that listed M1 gap was a no-op.
2. **ENEMY VARIETY (bestiary).** NEW `Editor/Bestiary.cs` — 5 archetypes (Slime grunt · Bat swarmer · Kappa
   bruiser · Flam caster · Cyclope darter), each own sheet+stats+tint+size, weighted. `MonsterAI3D` gained
   **`preferredRange`** (caster kites), **`fleeHpFraction`** (skittish flee), **`Stagger(secs)`** (CC freeze) —
   all allocation-free. `Build25DSpike.BuildMonster` refactored archetype-driven (old `(tex,foot,bool)` overload
   KEPT for spike/dummy). `NatureMobs` rolls `Bestiary.WeightedPick()`. ⚠️ **RE-BAKE: Populate Mobs (Nature)**.
   Flam = "magic touch at range" (no visible projectile yet → Mage-template follow-up).
3. **BUG FIX (variety exposed it): attack thrash vs kiters.** Stand-vs-chase flipped every frame at the range
   edge → machine-gun footstep SFX + stuck attack pose + pacing. FIX: `Player3DAttacker.EngagedWith` range
   **hysteresis** (strict `InRange` still gates the hit) + `Player3DController.Footsteps` persistent cooldown.
   Flam retuned (preferredRange 5→3.2 = catchable).
4. **SWORDSMAN = CLASS #1.** Model doc **"Class System — Design"** (`q171yxv1de9zce12zhzrssr59188ws6t`): class =
   active-skill **kit + attack-type + stat affinity** ON TOP of universal stats/passives (NOT a stat lock). Skills
   = code components mirroring `Player3DCaster`. Built: **Bash** (`Player3DBash.cs`, key **1** — heavy ~2.6×
   always-landing hit, MP, staggers, gold number) + **Charge** (`Player3DCharge.cs`, key **2** — gap-closer dash
   that tracks fleeing kiters → ~2× staggering hit). NEW reusable hooks: `Player3DAttacker.Strike(...)`,
   `MonsterAI3D.Stagger`, `Player3DController.BeginDash(...)`. Both **self-bootstrap** via `Player3DController.Awake`
   (no Build Player rerun; BuildPlayer only sets SFX). Fixed a CS0136 (`dashMoving` rename) live.
5. **PERF CHECKUP: PASS.** Profiler Hierarchy = gameplay `UpdateScene` **~210 B/frame** (≈ baseline 0). The earlier
   PerfHud 4.2 MB/s was the pre-fix thrash, now gone. `PERF_LOG.md` entry added; new `PERFORMANCE.md` rule:
   hysteresis on per-frame boundary checks that gate a hot path.

**DOCS/ISSUES:** Roadmap `q1732...`, Class System `q171yxv1...`, Skill System `q17as16xh2gt1g058kemazjar588tvc8`.
**VIN-16** carries the M1 progress comments + links. Memory added: `tempo-docs-no-markdown-tables`
(⚠️ **Tempo doc GFM tables vanish on re-edit — use bullet lists**), `class-system-design`.

**VERIFIED in Play (user):** save persists across restart; bestiary varied & distinct; Bash(1)+Charge(2) work;
clean Console; perf clean.

**NEXT (M1 remaining):** a defensive Swordsman active (guard/endure) · **hotbar UI** (show the 3 actives +
cooldowns — AI's pick, makes the kit legible) · **Mage template** (also gives Flam a real projectile). Then **M2**
(one region: town + quests + a dungeon). **COMMIT the pile when ready (USER's job).**

---
## ⏩ CHECKPOINT (2026-06-18) — 120 FPS CAP + skill-draft steer animation
Branch `experiment/3d-world`, UNCOMMITTED.
- **FRAME RATE:** the game had NO cap → VSync-locked to monitor (60 Hz = 60 FPS). Now `GameSystems` sets
  **120 FPS, VSync OFF** globally via a `[RuntimeInitializeOnLoadMethod] BootFrameRate()` (works at boot, **no
  re-bake**). ⚠️ The "GameSystems" object never actually had the `GameSystems` COMPONENT — fixed in
  `SystemsSceneBuilder` (`AddComponent<GameSystems>()`); re-run **Setup Systems Scene** to get the runtime
  inspector tuner (`targetFps` + `lockToVSync`). 60 Hz monitors may TEAR at 120 (tick lockToVSync for no-tear).
  Verify via PerfHud **F3** (editor Play-mode FPS is throttled; true 120 = Dev Build).
- **SKILL-DRAFT STEER ANIMATION** (`SkillDraftPanel`): clipped carousel viewport; neighbor pick slides the card
  set L/R, leader/wildcard/opening = zoom/fade, new leader ring row pulses. Eased with **smootherstep** (gentle
  in+out), `SlideDur` 0.30s, `LayoutRebuilder.ForceRebuildLayoutImmediate` on the incoming set (no 1-frame
  flash), unscaled time, `animating` flag gates Render, ring rows update in-place. Code-only, **no re-bake**.

---
## ⏩ CHECKPOINT (2026-06-18) — RO HIT/FLEE ACCURACY SYSTEM — BUILT (code)
Branch `experiment/3d-world`, UNCOMMITTED. Code-only, **no re-bake**. Replaces the earlier flat-evade hack
(`Health.evadeChance` + `Character.BaseEvasion`/`Evasion01` — all REMOVED) with a real HIT-vs-FLEE roll.
- **NEW `Combat.cs`** (static): `HitChance01(atkHit, defFlee) = clamp((100 + atkHit − defFlee)/100, 0.05, 0.95)`;
  `Lands(atkHit, defFlee)`. The **[5%,95%] clamp guarantees both MISSes always show** (even a maxed attacker
  whiffs 5%; you can't fully dodge by FLEE alone). Magic ignores it entirely (MATK vs MDEF).
- **`Character.cs`**: `Hit = Level + DEX`, `Flee = (Level + AGI) × (1 + Evasion-passive%)`. `TakeDamage` gained an
  `(attackerAttack, attackerHit)` overload — rolls monster HIT vs player FLEE → **WHITE MISS** on a dodge
  (`attackerHit = −1` skips the roll = always lands, for legacy 2D callers).
- **`Health.cs`**: `evadeChance` → `int flee`. `TakeDamage(attack, crit, attackerHit)` overload rolls player HIT
  vs mob FLEE → **RED MISS** (crits bypass; `−1` skips). Old 1/2-arg overloads pass −1 (2D unaffected).
- **`MonsterAI3D.hit` (def 8)** passed on each bite; **`Player3DAttacker`** passes `Character.Hit`.
  `BuildMonster` sets mob `flee = 10`; **dummy `flee = 15`** (tunable; set Health▸Flee=0 for clean DPS).
  ⚠️ existing saved mobs get field defaults (mob flee **0** → still 5% red MISS via the ceiling; monster hit 8).
  Stat panels now show **HIT / FLEE** instead of EVA%. Re-run Spawn Training Dummy for the flee-15 dummy.
  ⚠️ **Bugfix (white MISS "not rendering"):** `Character.Avatar()` only searched for `PlayerController2D`, so
  in the 2.5D game it returned null and the `if (a != null)` guard silently dropped the white MISS / Guard /
  heal popups (monster *hit* numbers still showed — `MonsterAI3D` spawns those itself). `Avatar()` now also
  finds `Player3DController`. At base AGI the dodge is the 5% floor (95% hit cap); pump AGI/level to see more.

---
## ⏩ CHECKPOINT (2026-06-18) — RO-STYLE DAMAGE RANGE + MISS — BUILT (code)
Branch `experiment/3d-world`, UNCOMMITTED. Code-only, **no re-bake**.
- **Min–max damage** (`Character.cs`): hits now roll `Random.Range(Min, Max+1)` instead of fixed. `MaxAttack`=
  the existing `Attack`; `MinAttack` = `Attack × MinFraction(DEX)`; `MinFraction = clamp01(0.55 + stat×0.012)`
  so **DEX raises (and stabilises) min physical dmg** (~38 DEX → min==max). Magic mirrors it: `MinMagicAttack`
  driven by **INT**. A **crit ignores the spread → always max** ×CritMult. `RollDamage`/`RollMagicDamage` updated.
  Stat readouts (`StatPanel.cs`, `CharacterUIBinder.cs`) now show `ATK min-max` / `M.ATK min-max`.
  Monster outgoing dmg also varies: `MonsterAI3D.damageSpread` (def 0.25) → `attack×(1−rand(0,spread))`.
- **MISS rendering**: physical hits can now whiff. **Player→monster miss = RED "MISS"** (`Health.evadeChance`,
  default **0.08**, rolled in `Health.TakeDamage`; crits can't be evaded; magic can't be evaded).
  **Monster→player miss = WHITE "MISS"** (the AGI `Evasion01` dodge in `Character.TakeDamage`, popup text
  cyan "Dodge" → white "MISS"). `BuildMonster` sets mob evade 0.08 (= new field default, so saved NatureStage
  mobs evade too). ⚠️ **Visibility fix (after "miss not showing"):** AGI-only evasion was ~0.4% at base AGI, and
  the dummy had evade 0 — so neither MISS ever appeared. Added `Character.BaseEvasion = 0.05` (flat flee floor so
  WHITE miss shows when any mob hits you), and the **dummy now spawns with `evadeChance = 0.25`** so RED miss pops
  when you hit it (zero it in the inspector for clean DPS). **Must re-run Spawn Training Dummy** — the dummy
  already in the scene was saved with evade 0. WHITE miss still needs a real mob attacking you (Populate Mobs).

---
## ⏩ CHECKPOINT (2026-06-18) — COMBAT TEST RIG: training dummy + level knob — BUILT (code)
Branch `experiment/3d-world`, UNCOMMITTED. Two tuning tools for measuring damage/builds:
- **Training dummy** — `TrainingDummy.cs` (runtime: 10,000,000-HP marker, `health.Revive()` every 5 s,
  floating "current/max" HP label that billboards). Spawned by **Tools ▸ unwritten ▸ Spawn Training Dummy**
  (`Editor/TrainingDummyTool.cs`): reuses `Build25DSpike.BuildMonster` (so it's click-targetable + has a
  collider), then neuters it — 10M HP, defense/magicDefense 0, all `MonsterAI3D` senses/move/touch/attack/xp = 0,
  strips MobRespawn+DeathDissolve, slate-gray tint. **Tools ▸ unwritten ▸ Clear Training Dummy** removes it.
  No re-bake needed — just run the menu in whatever 2.5D scene is open, then Play and swing/cast at it.
- **Tunable level** — `Character.cs`: `Level` is now a `[SerializeField] int level` (was read-only auto-prop;
  `AddXP` writes `level++`). New `OnValidate` refills HP/MP + fires OnStats/Health/Mana events so dragging the
  level in the inspector AT RUNTIME recomputes MaxHP/Attack/MaxMP/… live. Skill % layer (Passives) and allocated
  stat points are separate layers applied on top, so a build stays valid at any dialed-in level. Code-only, no re-bake.
  `OnValidate` also calls `ReconcilePointsToLevel()` → tops up/trims the allocatable **StatPoints**
  (`startingStatPoints + statPointsPerLevel·(level−1) − spent`) and skill **BankedPoints** (`level − TotalRanks()`,
  via new `PassiveRanks.SetBankedPoints`), accounting for already-spent points so it's idempotent up/down.

---
## ⏩⏩⏩⏩⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-18) — PHASE G SKILL MVP: **BUILT (code)** — READ FIRST
Branch `experiment/3d-world`, UNCOMMITTED. The Trio-Ring passive draft MVP is now CODED (the design doc
`q17as16xh2gt1g058kemazjar588tvc8` is still source of truth). **Draft UX decision LOCKED = bankable points**
(doc §11 #4): each level banks 1 skill point; player spends it in the Skills (K) panel on a 4-card draft.

**NEW FILES (`unity/Assets/Scripts/`):**
- `SkillRing.cs` — `enum StatId` (10 stable identities) + static ring: clockwise `Ring[]`, neighbor lookup,
  `InTrio`, soft-cap `Percent(stat,ranks)=Cap·R/(R+K)` (Cap .80 global, **CritChance .50**), ShortName, TintFor.
- `PassiveRanks.cs` — plain class (NOT MonoBehaviour); ranks dict + `Leader`/`HasLeader` + `BankedPoints`
  (starts **1** = the lvl-1 free start). `Pick(stat)` = `ranks++ ; Leader=stat` (the stack/slide/recenter
  steering collapses to this ONE line — emergent from the window). `OnChanged` event.
- `DraftService.cs` — pure: `BuildDraft(passives,rng)` → opening(all 10, no wild) | `[left,leader,right]`+wildcard
  (uniform over non-Trio). Unit-test-friendly.
- `SkillDraftPanel.cs` — runtime UI on GameUICanvas; builds banked-points header + draft cards (gold STACK /
  purple WILD / region-tinted) + ring view (ranks/%s, leader ▶, Trio highlit) into the empty "SkillsPanel/Content";
  + a level-up toast off `OnLeveledUp`.
- `Editor/SkillSelfTest.cs` — **Tools ▸ unwritten ▸ Skills ▸** Print Ring+Soft-Cap Table / Simulate Draft Walk
  (proves the logic with NO Play mode).

**EDITED:** `Character.cs` — added `Passives` (owns PassiveRanks); `MaxHP/Attack/Defense` now `×(1+Percent)`
(only HP/Atk/Def have flat sources today — other 7 stats accrue ranks but are inert until those systems exist);
`AddXP` banks a point per level; `Passives.OnChanged`→`OnStatsChanged`+`OnHealthChanged`.
`Editor/UIShellBuilder.cs` — `AddComponent<SkillDraftPanel>()`.

**⚠️ RE-BAKE (only one):** **Tools ▸ unwritten ▸ Build UI Shell** (bakes SkillDraftPanel into the
`Resources/GameUICanvas.prefab`). Character changes are code-only — no systems re-bake. TEST: Play → press **K**
→ opening shows all 10 stats → pick one (leader set) → level up (kill mobs) = toast + banked point → K → 4-card
Trio+wildcard → picking center stacks / side slides / wild recenters; ATK/HP/DEF on the Character (C) panel rise.
Or skip Play: run the two **Skills** self-test menus and read the Console.

**ISSUES SYNCED (2026-06-18):** VIN-3/4/5/6 bodies REWRITTEN to the built slice + moved to stage **merged**;
VIN-1 epic → **implementation**. ⚠️ **Cross-artifact LINKS STILL NOT WIRED** — `link_items` / `get_linked_items`
are 500ing server-side again (same outage as 2026-06-14, retried several times 2026-06-18). RETRY next session:
link VIN-3/4/5/6 → VIN-1, and VIN-1 → design doc `q17as16xh2gt1g058kemazjar588tvc8`.

**STATS WIRED (2026-06-18, same session):** 7/10 ring stats now affect real combat — added **Evasion**
(dodge roll in `Character.TakeDamage`, cap .60, cyan "Dodge" popup + `OnEvaded`), **AttackSpeed**
(`AttackSpeedMult` shortens `Player3DAttacker` cooldown), **CritChance** (cap .50) + **CritDamage**
(`BaseCritMultiplier` 1.5 + %) via `Character.RollDamage(out crit)` → new `Health.TakeDamage(power, crit)`
gold-number overload. Caps live in `SkillRing.CapFor`. New stats shown on the Character (C) panel
(`CharacterUIBinder`). ⚠️ **MAtk/MP/MDef still INERT** — blocked on a magic/mana/actives layer (post-MVP §9);
not fake-wired. Same **Build UI Shell** re-bake covers the panel text; combat changes are code-only.

**MAGIC FOUNDATION BUILT (2026-06-18, same session) → ALL 10 STATS LIVE.** User chose "finish all the basic
stats now." Added to `Character`: **mana pool** (`CurrentMP`/`MaxMP` + regen `Update` + `SpendMana` +
`OnManaChanged`, refills on level/revive), **MagicAttack/MagicDefense** (flat bases + per-level), `RollMagicDamage`,
`TakeMagicDamage` (ignores Evasion). `Item` got `bonusMP/bonusMAtk/bonusMDef`. NEW **`Player3DCaster.cs`** =
basic magic bolt on key **Q** (costs `manaCost` MP, dmg=MagicAttack, FX=`FX/Magic/Spark`, targets cursor/nearest
within range) → makes MAtk+MP real. `Health` got `magicDefense` + `TakeMagicDamage`. `MonsterAI3D` got
`AttackType{Physical,Magic}`; ~**30% of mobs are now MYSTIC** (magic attackers, blue-tinted) so player **MDef**
matters. HUD: blue **MP bar** under HP (`UIShellBuilder`/`CharacterUIBinder`); C-panel shows MP/M.ATK/M.DEF/ASPD/
CRIT/EVA. Builder wiring: `Build25DSpike.BuildPlayer` adds the caster, `BuildMonster(tex,foot,mystic)` overload,
mystic in both spawners. ⚠️ This is a HARDCODED spell, NOT the actives-draft (still post-MVP §9).

**⚠️ RE-BAKES for this batch:** **Build UI Shell** (MP bar + skill panel) → **Setup Systems Scene** (player
caster + Character magic stats) → **Populate Mobs (Nature)** (mystic mobs). TEST: Q near a mob = bolt + MP drains
+ regens; blue mobs hit through M.DEF (purple numbers, no dodge); crit = gold numbers; stack EVA = "Dodge".

**STAT-DISTRIBUTION SYSTEM (RO-style) BUILT (2026-06-18, same session).** This is the doc's planned FLAT
"stat-point distribution" layer. `Character` now has 6 primary stats `enum PrimaryStat{STR,AGI,VIT,INT,DEX,LUK}`
(`int[] primary`, start 1 each), `StatPoints` (`startingStatPoints`=9 + `statPointsPerLevel`=3/level), `GetStat`,
`AddStat(s)` (spend 1 pt). Contributions FOLD INTO the flat layer of the getters (inside `Scale`, so passive %
still multiplies on top): STR→ATK, VIT→HP+DEF, INT→MP+M.ATK+M.DEF, AGI→ASPD+EVA, LUK→CRIT(+ATK/MATK),
DEX→new `CastSpeedMult`(+ATK). Tunable consts at top of Character (HpPerVit=8, MpPerInt=5, divisors for DEF/MDEF
etc.). `Player3DCaster` cooldown now ÷ `CastSpeedMult`. NEW **`StatPanel.cs`** (runtime, on GameUICanvas, added
in `UIShellBuilder`) builds the 6 stat rows + `[+]` buttons + "Stat Points" counter + a live derived-stats readout
into CharacterPanel/Content; it **disables the old static "Stats" text** and subscribes to OnStatsChanged +
**OnHealthChanged + OnManaChanged** — which also **FIXES the reported bug** (C-panel text was stale during MP
regen because it only refreshed on OnStatsChanged). ⚠️ Allocation is permanent (no respec yet); 1 pt per stat
(no RO escalating cost) — MVP simplification.

**⚠️ RE-BAKE for this batch:** just **Build UI Shell** (bakes StatPanel into the prefab). Character/caster changes
are code-only (new serialized fields take their defaults on recompile — no Setup Systems Scene needed). TEST:
Play → C panel shows STR..LUK with [+], "Stat Points: 9"; click + → stat rises, derived ATK/HP/etc. update live,
MP line updates while regenerating.

**STILL NEXT:** persistence/save (ranks + stats + MP/level are runtime-only); respec item; per-stat cap tuning +
ring-order playtest; actives (every-10th, post-MVP §9). Open questions in doc §11. **COMMIT when ready (USER's job).**

---
## ⏩⏩⏩⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-18) — PHASE G SKILL SYSTEM: REDESIGNED (Trio-Ring) — design
Branch `experiment/3d-world`, UNCOMMITTED. **PLANNING-ONLY session, no code** (user keeps planning & coding in
separate sessions). **The old 28-node graph draft (`src/skill-draft/`) is RETIRED — user called it "trash."
Do NOT port it.** Brand-new mechanic designed this session and written to the canonical design doc
**`q17as16xh2gt1g058kemazjar588tvc8`** ("Skill System — Trio-Ring Passive Draft (MVP Design)", linked to VIN-1).
Memory `skill-system-mechanical-model` is the index; the doc is the source of truth.

THE MODEL (locked): draft = **1 of 4 each level — 3 known (a "Trio") + 1 random wildcard**. MVP skills are all
**repeatable generic stat passives** (no content). The 10 stats sit on a fixed **ring**; a Trio = a stat + its
2 ring-neighbors = a sliding window centered on your **leader** (last pick). Pick center=stack, pick a
side=slide (new leader), pick wildcard=recenter. **Level 1 = all stats, no wildcard** (free start). Ring beats a
line (no dead ends; closes phys/magic poles). Effects = **% multipliers** (flat lives on stats/equips) with a
**soft-cap DR curve** `passive% = Cap·R/(R+K)` that rewards diversification. **Every 10th level = an ACTIVE
draft — designed but POST-MVP.**

NEXT (next session): rewrite issues VIN-3/4/5/6 (their bodies still describe the retired graph) to the new
slice — data ring → rank store → fold % into `Character` → pure draft service → 4-card popup off
`Character.OnLeveledUp` → ring view in the empty Skills (K) panel. Open decisions still in the doc §11.

---
## ⏩⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — COMBAT BUGFIXES: phantom damage + mob respawn
Branch `experiment/3d-world`, UNCOMMITTED.

**BUG 1 — "still taking damage, no mobs around."** `MonsterAI3D` touch-damage used PLANAR (XZ) distance, so a
mob that walked off a cliff/map-edge and fell BELOW the terrain (invisible, same XZ as the player) kept biting
forever. FIX: gate the bite on TRUE 3D distance (`Vector3.Distance`) — a mob far below can't reach you.
(A killed mob already can't bite: `DeathDissolve.Play` disables its `MonsterAI3D` synchronously.)

**BUG 2 — mobs don't respawn.** There was NO respawn system (`NatureMobs` is an editor one-shot). Since mobs
are built procedurally (no runtime prefab), added a RECYCLE-based respawner:
- NEW `MobRespawn.cs` — on `Health.Die`, after `DeathDissolve` finishes, hides the mob for `respawnDelay`
  (8s), then revives it at a fresh ground spot within `homeRadius` of its spawn, kept `playerClearRadius`
  (7s) from the player. Re-enables everything DeathDissolve switched off (AI/agent/CC/colliders/renderer).
- `Health.Revive()` (NEW) = full HP + base color; `Health.Die` now hands off to `MobRespawn.OnFadedOut` if
  present (else Destroy, unchanged for 2D/spike mobs).
- `DeathDissolve` captures original material/color on Awake + `ResetAfterRespawn()` to undo the fade.
- `Build25DSpike.BuildMonster` now adds `MobRespawn` to every mob.

**⚠️ RE-BAKE:** **Populate Mobs (Nature)** (bakes `MobRespawn` onto mobs + the 3D-touch fix is code-only).
Test: kill a mob → ~8s later it returns elsewhere; walk the map → no more phantom hits. Tune knobs on the
mob's `MobRespawn` (respawnDelay/homeRadius/playerClearRadius).

---
## ⏩⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — UI CLICK + HURT GRUNT + DAY/NIGHT AMBIENCE
Branch `experiment/3d-world`, still UNCOMMITTED. Extends the TomMusic SFX system (checkpoint below) with
the 3 requested sounds. All TomMusic WAV foley (no vocals in the pack).

**Built this session:**
- **UI click sounds** — NEW `UISounds.cs` (runtime) on `GameUICanvas`: `open`/`close`/`click` SfxSets +
  static `PlayOpen/PlayClose/PlayClick`; auto-wires every `Button` under the canvas to the click. Clips
  (TomMusic "Doors Gates and Chests"): open = Chest Open 1/2, close = Chest Close 1/2, click = Lock Unlock,
  assigned in `UIShellBuilder`. `PanelToggle` plays open/close on I/C/K toggle + Esc-close-all.
  `DeathScreen.OnRespawnClicked` calls `UISounds.PlayClick()` (its canvas is separate, so direct call).
- **Player hurt grunt** — `Character.hurtSfx` (NEW SfxSet), played in `Character.TakeDamage` on UNblocked
  damage (before OnDamaged). PLACEHOLDER = low-pitched "chop" (pitch 0.7–0.85) as a meaty body-hit; assigned
  in `Build25DSpike.BuildCharacter`. ⚠️ swap in a CC0 human grunt later (TomMusic has no vocals).
- **Day↔night CYCLE (light + ambience + clock).** Central NEW `TimeOfDay.cs` (singleton on GameSystems) =
  accelerated clock split EVENLY: `halfCycleSeconds` 3600 → **1h day + 1h night** (full = 7200s). `time01`
  0=sunrise, 0.25=noon, 0.5=sunset, 0.75=midnight. Exposes `Daylight01` = (cos((t−0.25)2π)+1)/2 and
  `Night01`. **It IS the live in-game tuner**: `paused` + `[Range] time01` slider to scrub, `testSpeed`
  fast-forward, `[ContextMenu]` Jump ▶ Sunrise/Noon/Sunset/Midnight, and a `_readout` string showing live
  phase/clock in the Inspector during Play.
  - **Light** — NEW `DayNightLight.cs` on the Systems Sun (added in `SystemsSceneBuilder`; `BuildLight()`
    now RETURNS the Light). Lerps sun color (warm↔blue) + intensity (1.1↔0.18) + sky pitch + scene
    `RenderSettings.ambientLight` (sets ambientMode=Flat) by `Daylight01`.
  - **Ambience** — `DayNightAmbience` REFACTORED to read `TimeOfDay.Instance.Night01` (falls back to its own
    `fallbackCycleSeconds` timer only if no clock). Forest Day↔Night still wired by `NatureStageBuilder`.

**⚠️ RE-BAKE for these:** **Build UI Shell** (UISounds + click clips into GameUICanvas prefab) → **Setup
Systems Scene** (Character.hurtSfx + DayNightLight on Sun + TimeOfDay clock + new UI prefab) → **Add Forest
Ambience (day/night)** with NatureStage open (or **New Nature Stage** rebuild). Test: open Bag/Char/Skills =
latch click; get hit by a mob = thud grunt + red flash; day/night = select **GameSystems** in the hierarchy
during Play, tick **TimeOfDay ▸ paused**, drag **time01** (or right-click ▸ Jump ▶ Midnight) → sun dims to
blue, ambient darkens, forest audio crossfades to night. Untick paused + raise testSpeed to watch it cycle.

---
## ⏩⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — SFX SYSTEM (TomMusic)
Branch `experiment/3d-world`, still UNCOMMITTED. Audio direction: **TomMusic "Free Fantasy 200 SFX"**
(realistic foley, royalty-free; the user rejected Kenney as too chiptune). RO-rip audio is OFF-limits
(copyright). Pack lives at `Assets/Audio/TomMusic/` (we use the **WAV** files).

**System built this session:**
- `Sfx.cs` — `SfxSet` (clip[] + volume + pitch range; `Pick()` = random, no immediate repeat) + `Sfx`
  (pooled one-shot player, 16 2D voices, `Sfx.Play(set)` static). Lives on Systems (`GameSystems`).
  RANDOM VARIATION is ~free (one Random + index); cost is voices, capped by the pool.
- `Editor/SfxAssets.cs` — `SfxAssets.Set(vol, folder, names…)` loads clips from
  `Assets/Audio/TomMusic/WAV Files/SFX/` and forces **Decompress On Load** import (right for short SFX).
- Equipment-driven audio: `Item` gained `attackSfx`/`hitSfx` (weapon) + `guardSfx`/`blockChance` (off-hand).
  Direct AudioClip refs on the SO (NOT an id→wav registry — the SO ref IS the link).
- Hooks: **attack** (swing) + **hit** (impact) in `Player3DAttacker` (equipped weapon overrides player
  defaults via `WeaponSfx`); **footsteps** in `Player3DController` (Dirt Walk, `stepInterval` 0.32s);
  **block** = new off-hand mechanic in `Character.TakeDamage` (rolls `off.blockChance`, on proc negates
  the hit, plays guardSfx/`blockSfx`, pops "Guard", fires `OnBlocked`). `MonsterAI3D` skips the dmg popup
  when blocked (dealt==0).
- Defaults assigned in `Build25DSpike` (player attack/hit/footstep + Character blockSfx = Sword sets);
  `ItemStarterSet` gives weapons Sword attack/hit, the "Wooden Guard" off-hand Sword Blocked + 0.25 block.

**MORE SFX added (same session):**
- **Mob death** — `DeathDissolve.deathSfx`, played on death (assigned "Spell Impact 1/2/3" = poof; TomMusic
  has NO creature vocals — swap in CC0 monster cries later). Set in `Build25DSpike.BuildMonster`.
- **Level-up sting** — `Character.levelUpSfx`, played in `AddXP` on level (assigned "Firebuff 1/2"). Set in
  `BuildCharacter`.
- **Forest ambience** — per-map looping `AudioSource` (TomMusic "BGS Loops/Forest Day", 2D, vol 0.22,
  Streaming import). `SfxAssets.Ambient()` loads it. Added by `NatureStageBuilder` on fresh builds AND via a
  NEW menu **Tools ▸ unwritten ▸ Add Forest Ambience** that injects it into the OPEN map WITHOUT rebuild
  (so you don't lose scatter/mobs). Survives Systems load (MapManager strips listeners, not AudioSources).

**⚠️ RE-BAKE for audio:** **Setup Systems Scene** (Sfx player + player/Character defaults incl. levelup) +
**Create Starter Items** (Item audio) + **Populate Mobs (Nature)** (mob deathSfx) + **Add Forest Ambience**
(open NatureStage, no rebuild). Test: walk=footsteps; hit mob=swing+impact; kill mob=poof; level=Firebuff;
forest hum in bg; equip Wooden Guard = ~25% Guard clang. NEXT possible: UI click sounds, player hurt grunt,
day/night ambience swap, more weapon-distinct sets (bow).

---
## ⏩⏩⏩⏩ CHECKPOINT (2026-06-17) — PHASE F death/respawn DONE (code)
Branch **`experiment/3d-world`**, still **UNCOMMITTED**. Phase F (the last foundation gap) is coded; user
verifies in Unity (press Play, let a mob kill you).

**WHAT CHANGED:**
- `Character.TakeDamage` NO LONGER instant-revives at 0 HP — it just fires `OnDied` and stays dead
  (`IsDead`). New `Character.Revive()` (full HP + `OnRevived` event) is the only way back.
- `DeathScreen.cs` (NEW, runtime) — persistent overlay on the Systems layer. Listens to `OnDied`, fades
  in a full-screen "YOU DIED" vignette, counts down `respawnDelay` (3s), then a **Respawn** button →
  `Character.Revive()` + teleport to the map's SpawnPoint. **Builds its own ScreenSpaceOverlay canvas
  in code** (sortingOrder 5000, no theme assets) — fully self-contained.
- `Player3DController.Update` freezes input/orders while `Character.Instance.IsDead` (corpse only gets
  gravity; can't walk a dead player).
- Respawn teleport reuses existing placement: `PersistentPlayer3D.Respawn()` (NEW public, = re-`PlaceInScene`)
  with a 2D fallback `PersistentPlayer.Respawn()` (NEW public). Both just re-run their SpawnPoint snap.
- Wired in: `SystemsSceneBuilder` now `gameSystems.AddComponent<DeathScreen>()`.

**DEATH ANIMATIONS (same session):**
- **Player** — `Player3DController` shows a defeated pose while `IsDead` (new `deadSheet` Texture2D field,
  sliced at runtime in Awake like `attackSheet`; from NinjaAdventure `SeparateAnim/Dead.png`, one 16×16
  frame). `Build25DSpike.BuildPlayer` assigns it (`LoadDeadSheet`). DeathScreen now waits `preFadeDelay`
  (0.5s) before fading in so the fall reads first.
- **Mobs** — `DeathDissolve.cs` (NEW, per user spec): NO shrink. On death → **smoke puff** (NinjaAdventure
  `FX/Smoke/Smoke/SpriteSheet.png`, 6×32px, via `SlashEffect.Spawn`), freeze the mob (disable
  AI/agent/CC/colliders), **remains lie `remainSeconds` (3s, tunable)**, then **fade `fadeSeconds` (1s)** →
  destroy. The fade swaps the renderer to `Sprites/Default` (alpha-blend) first, because `BillboardLit`
  is alpha-CLIPPED and can't fade. `Health.Die()` defers to `DeathDissolve` if present (else
  instant-destroy — 2D mobs unchanged). Added in `Build25DSpike.BuildMonster`.

**EXTRA ANIMS (this round):**
- **Mob attack tell** — `AttackLunge.cs` (NEW): squash-stretch "chomp" (scale punch only, never position,
  so it can't fight CC/pathing). `MonsterAI3D` calls `lunge.Punch()` on each bite. Added in BuildMonster.
- **Player hurt flash** — `PlayerHurtFlash.cs` (NEW): red tint for 0.12s on damage (red works under the
  cutout shader; white wouldn't). Driven by new `Character.OnDamaged` event. Added in BuildPlayer.
- **Idle breathing** — procedural squash-stretch loop (no idle art exists). In `BillboardAnimator3D`
  (mobs) + `Player3DController` (player), gated to standing-still; random per-instance phase desyncs the
  herd. Single scale owner per object: idle YIELDS to `AttackLunge.Punching`; DeathDissolve disables the
  mob animator so no fight during the corpse linger/fade; player resets to base when moving/attacking/dead.
  Knobs: `breathe`/`breatheAmount`(0.03)/`breatheSpeed`(2.5).

**⚠️ USER MUST RE-RUN `Setup Systems Scene`** (bakes DeathScreen + player `deadSheet` + `PlayerHurtFlash`
into `Systems.unity`) **AND `Populate Mobs (Nature)`** (so mobs get `DeathDissolve` + `AttackLunge` +
smoke). Then Play → mob bite = chomp + red flash; kill a mob = smoke → body lingers 3s → fades; die =
defeated pose → overlay → Respawn.

**NEXT:** verify in Unity → then Phase G skill-draft (the empty K panel) OR NPCs+dialogue. Also: COMMIT
the whole uncommitted pile when ready.

---
## ⏩⏩⏩ CHECKPOINT (2026-06-17, late) — gameplay + 2.5D Systems port
Branch **`experiment/3d-world`**, **UNCOMMITTED** (user is staying uncommitted for now; offer to commit on resume).
All work below is in the working tree; compile-clean by reference scan (user verifies in Unity = presses Play).

**WHAT'S NEW since the nature-direction commit (all this session):**
1. **Mobs + combat in the nature world.** `Populate Mobs (Nature)` / `Clear Mobs (Nature)` (`NatureMobs.cs`) —
   surface-snapped billboard mobs (`Build25DSpike.BuildMonster`), clear of spawn, area-density. The 2.5D
   combat loop was already wired: `Player3DController` click-to-attack (`MonsterUnderCursor`, A-attack-move,
   S-stop) → `Player3DAttacker` → `Health`; `MonsterAI3D` chases + bites via `Character`. `PathAgent3D` has
   gravity so mobs stick to hills.
2. **Player attack POSE** — `Player3DController` slices 4 directional poses at RUNTIME from a `Texture2D
   attackSheet` (NinjaAdventure SeparateAnim/Attack.png auto-slices are BROKEN, so we slice 16px cols
   ourselves; player is NinjaBlue2). Holds pose ~0.22s per swing.
3. **Target indicator** — gold down-arrow ported to 3D: `Player3DController` creates a `TargetIndicator` in
   Awake (+`Billboard`), Follows the attack target, clears on stop/kill/move.
4. **Slash FX** — `Player3DAttacker` spawns `SlashEffect` (SlashCurved sheet, 32px×4@22fps) on each landed
   hit; `SlashEffect.Spawn` now adds a `Billboard`.
5. **Damage numbers** — `DamagePopup` faces the camera each LateUpdate (no more skew when orbiting).
6. **⭐ 2.5D SYSTEMS PORT (the big one).** Player/camera/HUD/cursor are now PERSISTENT in `Systems.unity`,
   maps are CONTENT-ONLY. So player tweaks never require a map rebuild again.
   - `SystemsSceneBuilder` REWRITTEN → 2.5D: Sun + billboard player (+`PersistentPlayer3D`) + 3D follow cam
     + Character + **GameUICanvas** (full UI shell: HP bar + Bag(I)/Character(C)/Skills(K) panels via
     `PanelToggle`/`CharacterUIBinder`/`BagSeeder`, bound to `Character.Instance`) + `GameCursor3D` +
     `GameRoot` (firstMap="NatureStage"). Sets Build index 0 + playModeStartScene=Systems. (NOTE: Skills/K
     panel is an EMPTY placeholder — Phase G skill-draft still unbuilt.)
   - `NatureStageBuilder` REFACTORED → content-only map: terrain (Ground layer) + `Pathfinder3DConfig` +
     a `SpawnPoint` (id "Start") + an edit-mode Sun; registered in Build Settings. NO player/cam/HUD.
   - `PersistentPlayer3D.cs` (NEW) — 3D spawn: on map load, teleport to SpawnPoint, raycast-snap feet to
     terrain (precise via CC geometry → groundY+0.28), cancel order (`Player3DController.Halt`), aim
     `CameraFollow3D`. Reuses GameRoot/MapManager/SystemsPlayMode/SceneTravel UNCHANGED.
   - `MapManager.StripForeignGlobals` now also disables a map's Lights at Play (Systems owns the one light).
   - `BuildPlayer`/`BuildMonster` CC bumped to slopeLimit 45 / stepOffset 0.3 so units walk the hills.

**RUN FLOW (the new boot model):** Build Prefabs ▸ Low Poly Nature → **New Nature Stage** → **Scatter
Nature** → **Populate Mobs** → **Setup Systems Scene** → press **Play** (Systems boots, NatureStage loads
under it, player spawns at SpawnPoint). Re-running `Setup Systems Scene` is cheap (no content loss); the
map content is in NatureStage. `activeInputHandler: 2` (Both) so PanelToggle's old `Input` works.

**KNOWN/UNVERIFIED:** levitation fix + light-in-edit + slash + panels were JUST coded — user hadn't
confirmed the last round in-Unity before this checkpoint. If player still floats a hair, bump
`Build25DSpike.SpriteFoot` (0.1) up (sprite bottom padding). Editor-light: add a Directional Light to
NatureStage by hand (auto-disabled at Play) OR re-run New Nature Stage.

**NEXT options (user to pick):** verify this round → then Phase G skill-draft (fills the K panel) /
death-respawn (Phase F gap) / NPCs+dialogue+a clearing. Also: COMMIT the cleanup+gameplay+port when ready.

---
## ⏩⏩ DIRECTION FULLY COMMITTED: artist low-poly nature + billboard characters (2026-06-17)
Active branch **`experiment/3d-world`**. **The world is now built from Just Create Studio's "Stylized Low
Poly Nature" pack** (`unity/Assets/Art/LowPolySimpleNature_FBX/`, 25 FBX sharing ONE atlas
`SimpleNature_Texture_01.png`). The pack's **Ground_0x meshes are artist-sculpted terrain with baked-in
hills** — so we get elevation FOR FREE (no procedural terrain gen needed). Characters stay 2D billboards
(NinjaAdventure). Play `Assets/Scenes/NatureStage.unity`. **VERIFIED Play-confirmed: forest renders, player
walks the hills, paths around trees/rocks, walks through grass; clean Console.** User will add MORE free
packs from the same artist (drop each in `Assets/Art/`, add a sibling line in `KenneyPrefabBaker`).

**The nature pipeline = 3 one-click tools (all NEW this session):**
- `Build Prefabs ▸ Low Poly Nature` (`KenneyPrefabBaker.cs`) — bakes the 25 FBX → drop-ready prefabs at
  `Assets/Prefabs/LowPolyNature/` (shared atlas material + MeshCollider each). `BakeKit` now takes an
  optional `colormapPath` so any single-atlas pack works.
- `New Nature Stage (low-poly ground)` (`NatureStageBuilder.cs`) — builds `NatureStage.unity`: Ground_01 as
  the floor (Ground layer + collider + height-aware `Pathfinder3DConfig`) + the standard billboard rig
  (reuses `Build25DSpike` public helpers). Self-contained (owns `EnsureGroundLayer`).
- `Scatter Nature (fill map)` + `Clear Nature Scatter` (`NatureScatter.cs`) — area-based weighted scatter of
  trees/bushes/grass/flowers/rocks/mushrooms/stumps onto the terrain via raycast. Re-runnable (reseeds).
  Colliders per category: trees/stumps = trunk capsule (walk under canopy), rocks = box, decor (grass/
  flowers/mushrooms/bushes) = none. All scatter on Default layer (NOT Ground) so the height-ray ignores it.
  Obstacles = non-ground colliders (see `Pathfinder3DConfig`). Tunables = consts at top of NatureScatter.cs.

**BIG CLEANUP done this session — committed to nature, deleted the dead experiments.** Tools menu cut
~40 → 16. **Deleted:** all 2D-tilemap authoring (DepthSortTools, FloorRuleTileBuilder, TilemapTools,
GroundFillTool, MapBaker, BuildingBakeTools, MapBoundaryBuilder, NewMapSceneTool, PropTemplate ×2, Generate
Town) · Kenney 3D experiments (Town3DBaker, TerrainSandbox, Stage25DBuilder, Build25DElevationSpike,
HouseAssembler, KenneyPaletteRemap, PlayerBuilder, Terrain3DArt/Field) · the whole `Scripts/MapGen/` runtime
(town-generator brain) · `TerrainProjected.shader` · scenes NewTown/Town3D · art (Kenney FantasyTown +
Platformer kits, Tiles, Terrain3D, Palettes, baked Kenney prefabs). **Surgical edits:** `MonsterBuilder`
stripped to its shared combat+cursor helpers (still called by Run All / Setup Global Systems — the Systems
layer is KEPT per user); `Build25DSpike` kept all rig helpers, demo menu removed.
⚠️ **SCAR (don't repeat):** `rm -rf Shaders` nuked `BillboardLit.shader` too → player went magenta;
restored from git (same .meta/GUID). And the batch delete + recompile triggered a transient URP
`get_scriptableRenderer IndexOutOfRange` (renderer-cache desync) — cleared by reimporting UniversalRP.asset /
Unity restart, NOT a data bug.

**NEXT:** commit the cleanup on `experiment/3d-world` (pending), then content: more packs from the artist,
mob/NPC placement in the nature world, and folding combat/RPG into the nature stage. Spike doc:
`q1755ftddqpyy9qm7e357hdpf188sajr`.

---

## (Superseded) Kenney 3D maps + billboard characters (2026-06-17)
After spiking it, we **rejected the "2D sprite-props in a 3D
world" approach** (billboard trees/houses). The killer: a billboard building can't have honest collision —
a collider that rotates with the always-camera-facing art confuses players, and a static collider
mismatches the rotating art. Large/enterable structures force real 3D meshes.

**DECISION (final): the ENVIRONMENT (ground, buildings, trees, props, terrain) is built from Kenney 3D
mesh assets; only CHARACTERS (player / mobs / NPCs) stay 2D billboards** — the Ragnarok-Online model, and
what `Build25DSpike` already does (Kenney FBX props + billboard player). With 3D meshes, occlusion is the
depth buffer (billboards write depth via `BillboardLit`), so the old "camera-depth vs world-Y sprite-prop"
problem doesn't exist — that whole sub-system is gone.

**CLEANUP done this session — deleted the 2D-in-3D experiments:** `Town25DConverter.cs` (billboard town
from tilemaps), `Build25DTerraceDemo.cs` (2D-tile-skinned terraces), `CameraDepthManager.cs` +
`CameraDepthProp.cs` (static sprite-prop sorting — moot with 3D meshes), and scenes `NewTown25D.unity` +
`Terrace25D.unity`.

**KEPT (valid for the Kenney direction):** `Build25DSpike.cs` (Kenney-props + billboard rig — the
foundation; its helpers are now public, incl. `BuildPlayer(foot)` / `BuildMonsters(center,r)` overloads);
the height-aware `Pathfinder3D` + `Pathfinder3DConfig` (multi-level terrain pathing works on ANY 3D
ground, incl. Kenney elevation blocks); `Build25DElevationSpike.cs` (primitive plateau/ramp proof of it,
holds the `Ground`-layer helper); `CameraDepthSorter` + all billboard-character/combat code.

**Kenney 3D asset location:** existing kit = `unity/Assets/Art/KenneyFantasyTownKit/FBX format/`
(FBX models + `Textures/colormap.png`). Put each NEW downloaded pack in its OWN folder under
**`unity/Assets/Art/`** (e.g. `unity/Assets/Art/KenneyMiniDungeon/`), using the pack's **FBX format**
folder + its colormap texture. (`Build25DSpike` loads `.fbx` and re-applies the kit colormap material.)

**FLOW CHOSEN (2026-06-17): A — hand-place Kenney prefabs in a clean 2.5D scene.** New pack
`KenneyPlatformerKit` is in `unity/Assets/Art/KenneyPlatformerKit/` (153 FBX + `FBX format/Textures/colormap.png`).
Two new one-click tools built this session (both reuse `Build25DSpike` public helpers):
- `Tools ▸ unwritten ▸ Build Kenney Platformer Prefabs` (`KenneyPrefabBaker.cs`) — bakes every kit FBX
  (skips `character-*`, since chars are 2D billboards) into drop-ready prefabs at
  `Assets/Prefabs/KenneyPlatformer/` with the colormap material + a MeshCollider each, plus a shared
  `PlatformerKit.mat`. Re-runnable (overwrites in place).
- `Tools ▸ unwritten ▸ New 2.5D Stage (hand-place)` (`Stage25DBuilder.cs`) — builds clean
  `Assets/Scenes/Stage25D.unity`: boot rig only (3D follow cam, sun, flat grass ground, billboard
  player, HP/HUD, Dota cursor), NO scattered props/mobs. Drag the baked prefabs in by hand.

**AUTO-GENERATION (2026-06-17): reuse the 2D generator for 3D maps — Phases 1–3 BUILT.** Decision:
both kits + (eventual) elevation. The 2D layout brain (`TownGenerator.Generate(recipe)`→`TownLayout`)
is reused UNCHANGED; only the backend differs.
- Phase 1 — `KenneyPrefabBaker` generalized → menu **Build Prefabs ▸ {Platformer · Fantasy Town · Both Kits}**
  (→ `Assets/Prefabs/Kenney{Platformer,FantasyTown}/`). Old single menu name retired.
- House — `HouseAssembler` → **Build Prefabs ▸ Assemble House (Fantasy Town)** tiles modular wall/door/
  window/roof FBX into `Assets/Prefabs/KenneyFantasyTown/house-small.prefab`. VERIFIED good (door front,
  windows, eaves). Tuning consts at top of the file.
- Phase 2 — `Town3DTheme` + `Prop3D` (`MapGen/Town3DTheme.cs`): prefab palette mirroring the fields the
  generator reads. Tiny additive hook: `TownLayout.placed` (List<PlacedProp>) + a 2-line record in
  `TownGenerator.Stamp()` (2D path ignores it).
- Phase 3 — `Town3DBaker` → menu **Generate 3D Town**. Feeds the generator a synthetic MARKER TownTheme
  (sentinel tiles + synthesized PropTemplates from Prop3D, mapped back), runs it, then emits Kenney
  prefabs: ground tiles + roads + Fence boundary + one prefab per placed prop; ONE big Ground-layer
  floor box collider (perf) + per-prop box colliders; `Pathfinder3DConfig` height-aware. Auto-creates a
  default `Assets/MapGen/Town3DTheme_Default.asset` wired from baked prefabs (no manual setup). Output
  `Assets/Scenes/Town3D.unity`. FLAT for now.
- Phase 4 — elevation/slopes: TERRAIN SANDBOX WORKING (`TerrainSandbox.cs`, menu **Terrain Sandbox**).
  Merged, tiered, ramp-gated Kenney terrain, VERIFIED good + walkable:
  • merged lawn via the OVERLAP trick (blocks scaled ~1.3× so rounded shoulders fuse — memory
    `kenney-3d-terrain-overlap`); blocks visual-only, flat per-cell box collider on `Ground` layer.
  • quantised-Perlin heightmap; every tier edge is an IMPASSABLE cliff; the ONLY way up is generated
    grass RAMPS placed on a union-find spanning tree of tier regions (all areas reachable).
  • ramp = same block-grass, oversized + thick + ROTATED ABOUT ITS BOUNDS CENTRE (its pivot is off-base
    — rotating about pivot flung it into the air; centre-rotate fixed it). Player stepOffset 0.1 (cliffs
    are walls) + slopeLimit 45 (ramps walk); `Pathfinder3DConfig` maxStep = TierStep*0.75.
  • KNOWN MINOR BUG (deferred to a pathfinding-polish pass): when the player is flush against a cliff
    right by a ramp mouth, PathAgent3D steers straight into the wall (walks in place) instead of backing
    off to the ramp. Not structural.
  NOT YET: fold the terrain into `Generate 3D Town` (houses/roads/props at terrain height).
- FLAT+SCENIC town: BUILT. `Town3DBaker` rings the flat walkable town with tiered Kenney hills
  (`EmitScenicBorder`) as the natural boundary (no fence); `Town3DTheme.scenicBorder` toggles it.
  Billboards (player/mobs/NPCs) scaled to `Build25DSpike.BillboardScale = 0.7` (30% smaller vs the 3D
  map; only the 2.5D scenes, NOT the 2D game).
- TERRAIN TEXTURING: WORKS (2026-06-17). 2D pixel-art ground tiles painted on the merged 3D terrain via
  world-projection shader `Unwritten/TerrainProjected` (canonical URP unlit: GetVertexPositionInputs +
  instancing macros) + `Terrain3DArt.cs` (grass/dirt tiles extracted to `Art/Terrain3D/`, point+repeat).
  ⚠️ **GOTCHA that cost ~6 round-trips:** the shader renders in the GAME (3D renderer, Play mode) but
  NOT in the Scene view (which uses the project's 2D Renderer2D, default renderer index 0) — so terrain
  looks INVISIBLE while editing and only shows on **Play**. Stock URP/Lit/Unlit DO draw under the 2D
  renderer, which made diagnosis misleading. **Always verify 3D-shader work by pressing Play, not Scene
  view.** 2nd gotcha: the `Art/` import postprocessor forces `wrapMode=Clamp`, so tiles didn't repeat
  (detail only at world origin) — fixed two ways: shader wraps UVs with `frac()`, and the postprocessor
  now exempts `Art/Terrain3D/` → Repeat. Shader is now LIT: gentle Lambert + RECEIVES soft main-light
  shadows (player/props/cliffs darken the ground) + a ShadowCaster pass (cliffs self-shadow). Tune on
  `Art/Terrain3D/Terrain3D.mat`: `_Tiles` (grass density, default 4), `_ShadeFloor`, `_Ambient`.
  TERRAIN MILESTONE COMPLETE: generated, walkable, ramp-gated, textured, lit/shadowed 2.5D terrain+town.

**NEXT (user step):** Unity → **Build Prefabs ▸ Both Kits**, then **Assemble House (Fantasy Town)**, then
**Generate 3D Town**; press Play + screenshot. Watch for: house footprint vs grass cell-size match,
road look, prop overlap. Prior spike doc: `q1755ftddqpyy9qm7e357hdpf188sajr`.
---

**Last updated:** 2026-06-16 · **Status:** 🟢 foundation A–F built; Phase G is next. **Repo: the Unity game was relocated INTO `unity/` of this workspace (committed `98b796c`); old `D:\Unity3d\unwritten` is now just an archive.**

> **Map Generator (VIN-13) — BUILT & WORKING (2026-06-16).** Editor tool bakes *permanent* town scenes from a `MapRecipe` (params + seed) — NOT runtime/roguelike. `Tools ▸ unwritten ▸ Generate Town`. Generic `PropTemplate` objects (houses/trees/wells, placement Lot/OpenGround/PlazaCenter/Roadside); auto-tiled terrain; gate Portals + spawns. **Per-prop collision is authorable** (`PropTemplate.collisionMode` Auto/Custom/None, **half-cell** resolution, edited via the Inspector grid). **Everything is PRE-BAKED at generate time** (static foot-sorted props w/ fixed sort orders + plain run-merged static box colliders) — **no on-Play generation** (perf rule: memory `performance-first-prebake`). Hand-painted maps → `Bake Map (props + collision)`. Spec doc `q178ppzhynajt660nadeh6ras988n88a`; **workflow how-to** doc `q171zzcsyy34n52cga9v1zn95d88sn9g`; detail in memories `map-generator-tool` + `ysort-depth`. **Live build checkpoint =** `WAKE_UP.md` **(project root) — on resume, read it first.** Safe to `/clear`: on-disk code + notes are the source of truth.

> Cheap-resume file. Canonical status lives in the **"unwritten — Development Roadmap"** doc (Tempo Docs); this is the quick pointer. On "resume": read this, then the roadmap doc, then the on-disk files in the Unity project — **now at `unity/Assets/Scripts/` inside THIS repo** (relocated + committed 2026-06-16 as `98b796c`; edits show in Tempo's diff). The standalone `D:\Unity3d\unwritten` / GitHub `unwritten-unity` are just the archive now.

---

## Where we are

The playable foundation is essentially complete (Phases A–F). Most recent work (2026-06-14) **reworked movement & combat into Dota-style controls**:

- `PlayerCommander` (new) — order brain: **right-click** move / attack (enemy) / follow (`FriendlyUnit`); **A** then click = attack-click (enemy = attack, ground = attack-move); **S** = stop & cancel everything (incl. un-landed damage) + `onStop` hook for future skill-casts.
- `PlayerController2D` is now a click-to-move **service** (`MoveTo`/`MoveToward`/`Halt`); **WASD removed**.
- `PlayerAttacker` is now a swing **executor** with a `windupTime` — damage lands at the end of the swing, so **S** cancels it.
- `GameCursor` (new) drives a **hardware cursor** from the **Kenney Cursor Pack** (`Assets/Art/KenneyCursorPack`, Outline) — pointer / sword / crosshair. Replaces the old UI `CombatCursor` (deleted).
- Also new: `ClickMarker` (click ripple) and `FriendlyUnit` — the **reusable allegiance marker** for friendlies (non-attackable + right-click-to-follow); it's the seam a future mercenary/companion reuses. Wired by `Setup Mouse Combat`. (The temporary `Add Test Ally` menu was removed — follow is code-complete but only testable once a real `FriendlyUnit` exists in a scene.)

(Earlier predecessor work had reworked combat into mouse-target click-to-attack with `SlashEffect` / `CharacterAnimator2D.PlayAttack` / `TargetIndicator` — those FX still apply.)

| Phase | State |
| --- | --- |
| A Engine/hello-world | ✅ done |
| B Character & tiled world | ✅ done |
| C Towns/portals/dialogue | ✅ systems done (town content hand-painted) |
| D Monsters & combat (mouse-target) | ✅ done |
| E UI shell (HUD + panels + drag-equip) | ✅ done |
| F RPG numbers (`Character`) | 🟢 core done — **only the death/respawn screen is missing** |
| G Skill-draft (signature) | ⬜ **next** |
| H Multiplayer | ⬜ deferred |

## Immediate next

**Phase G — the graph-based level-up skill draft.** Port the web prototype in `src/skill-draft/` (`graph.ts`, `SkillCard`, `SkillTree`, `SkillDraftPage`) to Unity: skill graph as ScriptableObjects → `DraftService` (2 guaranteed + 1 wildcard, no-repeat, class reveal) → 3-card draft UI → tree view. Hang it off `Character.OnLeveledUp` and the **Skills** panel (K). Tracked by issues **VIN-1** (epic) + **VIN-3/4/5/6**.

Small loose end if preferred first: Phase F **death/respawn screen** (at 0 HP you currently instant-revive at full HP; `Character.OnDied` already fires).

## Issue board (synced 2026-06-14)

- Done: **VIN-2** (setup), **VIN-7** (player+XP loop), **VIN-8** (Phase B), **VIN-10** (Phase D), **VIN-11** (Phase E).
- In progress: **VIN-9** (Phase C), **VIN-12** (Phase F).
- Planned (Phase G): **VIN-1** epic + **VIN-3/4/5**; **VIN-6** (tree view) in backlog.
- ⚠️ **Cross-artifact links are NOT wired yet** — `link_items` was down (server 500s) during the 2026-06-14 sync. Retry: link VIN-3/4/5/6 → VIN-1, and link VIN-1 + the new milestone issues → the roadmap doc.

## Gotchas / decisions

- Scripts are authored in the Unity project at **`unity/Assets/Scripts/` inside this repo** (the whole game was relocated here 2026-06-16 — one git repo, edits show in Tempo's diff). The standalone `D:\Unity3d\unwritten` + GitHub `unwritten-unity` are the archive. (A *separate* 2026-06-14 `unity/` script-mirror was deleted back then; this deliberate full relocation supersedes that note.)
- PPU 16 for all art; world content is hand-painted by the user (don't auto-generate).
- **Depth/z-index + player drift ✅ DONE (2026-06-14):** depth uses **per-object feet-Y sorting** (`YDepthSorter` on every entity) + fixed order **BANDS** for tilemaps/FX, all in `DepthSortRuntime` (auto-applied on Play, no menu needed). Layer stack: Ground/Decor (behind) → ENTITIES (per-object) → Overhead → FX. **Obstacle tilemaps auto-bake into grouped foot-sorted props at Play** (`DepthSortRuntime`, runtime + non-destructive) — just paint trees/houses on the Obstacles tilemap, no convert menu. Multi-tile props group into one foot-sorted unit (`YDepthSorter` GROUP mode); trees get a trunk base collider (walk under leaves), "building/house"-named tilemaps get full colliders. **Create Decor + Overhead Layers** adds two non-collider decoration tilemaps. The earlier transparency-axis-only attempt **flickered** on same-row ties and is retired. Full detail + gotchas (16-bit sortingOrder, stacked-tile caveat) in \[\[ysort-depth\]\]. Player **drift fixed** separately by turning OFF crowd-avoidance on the player (`PlayerController2D.Start` → `agent.avoidRadius = 0`). See \[\[pathfinding-grid-astar\]\].
- Pathfinding ✅ DONE (2026-06-14): grid A\* over the physics world (`Pathfinder.cs`, static, no package/scene-object) + `PathAgent.cs` (attachable mover, the "smart movement" component) for every moving unit. `PlayerController2D` delegates to a sibling `PathAgent` (auto-added) so `PlayerCommander` is unchanged; `MonsterAI` is now a pure brain issuing `PathAgent` orders. **Wall rule:** a non-trigger collider blocks unless its Rigidbody2D is *Dynamic* (so painted Tilemap/Composite obstacle colliders — backed by a *Static* body — block correctly; the early `attachedRigidbody==null` check was the "mobs stuck at the tree" bug, now fixed). Open ground = straight line (A\* only when blocked). **Units don't hard-block A**\* (you must be able to reach a chased/attacked unit); PathAgent instead does **soft local avoidance** (`Separation` + `avoidIgnore` = current target) so units flow around each other Dota-style instead of stacking/shoving. Existing scene player auto-gains routing on Play; re-run **Setup Mouse Combat** (or rebuild monster prefabs) to make the component Inspector-visible. **⚠️ Critical fix:** `PathAgent.Awake` sets `rb.sleepMode = NeverSleep` — a Dynamic body moved only by `MovePosition` falls asleep and then IGNORES `MovePosition`, which froze every unit mid-route (the whole "mobs stuck" saga; normal-looking body state). Validated working: monsters move/chase/route around obstacles. Remaining polish only: crowd-jam when mobs mob the player (avoidance under-spreads).