# Handoff — combat / peace mode, and every weapon's stance set (idle, running arms, put-away)

**Task:** weapons drawn (combat) or put away on the body (peace) for players, other players and creatures, with peace
bonuses; then give each weapon the katar's four stance pieces — combat idle, running arms, a resting place on the
body, put-away and take-out clips — videos to the user first, wired in after their yes on each weapon. No Tempo issue/doc id. Design
doc: `docs/2-systems/combat-and-peace-mode.md`.

**Status:** IN PROGRESS.
- Combat/peace mode: BUILT, partly play-tested by the user (they tested the katar in game on 2026-10-10 and
  reported only the running-arms bug, now fixed). Not every path has been played — don't trust "it works" beyond
  the katar's draw/run/idle.
- **LOCKED by the user on 2026-10-10: the Axe, the Katar and the Dagger, each as a whole set** (attack, idle, running
  arms, put-away, take-out, hold, resting place). Never rework or offer to rework them (memory
  `locked-attack-animations`).
- Katar: DONE and in game (idle, crossed-guard running arms, X on the lower back, put-away/take-out). Re-seated in the
  fist on 2026-10-10 (the user: "looks misaligned"): the middle of its grip frame now sits on the fist's middle (it sat
  5.4% of the model's width off to one side and slid 2.9% of body height up the frame); resting places carried.
- Every weapon's standing idle shares ONE breath, the dagger's (user, 2026-10-10): an idle key table holds one key and
  `point-weapon-gen.py` adds the breath. `IdleKatar` and `IdleAxe` were re-saved with it; `IdleDagger` rebuilt identical.
- Dagger: DONE and in game. Its put-away (user, 2026-10-10): the arm rises straight out to the right and holds, the
  dagger makes 2 turns flat over the fist (drawn by the game, not by the clip), and it is left at the hip slanted
  BACKWARD; frames sheathe R 64 / unsheathe R 20 (of 84). Not yet play-tested.
- Axe: DONE and in game since 2026-10-10 (model doubled, whole set re-fitted, stance row and resting places wired,
  controller rebuilt and read back). Not yet play-tested.
- Greatsword: DONE and in game since 2026-10-10 (low idle and running hold from its attack's first frames, a simple
  put-away to level across the back and take-out, weight 133 = 0.998 s each way). Its attack stays LOCKED. Not yet
  play-tested.
- Warhammer 3x, Sniper 2x, Shuriken 2x, Greatsword 1.5x (user, 2026-10-10; done by a sub-agent): only their hand
  scale changed, fist on the same spot; their saved clips were NOT re-fitted (see What's left, item 8).
- IN THE GAME since 2026-10-11 (the user: "for the weapons i dont mention ... ship them in-game"): Sword, Staff,
  Wand, Spear, Scythe, Warhammer, Shuriken, Fist — stance rows, resting places on their grips, the controller rebuilt
  and read back (layer "DrawUpper" holds the Sword's put-away). New game settings on a stance row, all built then:
  `putAwayMovesUpperBody` (Sword), `spinAboutMiddle` (Spear, Scythe), `handoverVfx` = Blink Step (Staff, Wand). The
  Staff's attack is now two-handed, the Warhammer's re-timed to look heavy, the Shuriken throw's wind-up keeps the
  hand off the head — each built over the shipped attack clip. NOT PLAY-TESTED.
- Bow: RIGHT-HANDED since 2026-10-11 (the user: "the right hand must hold the bow's handle ... the left hand must hold
  the bow's string ... the attack animation for bow should be flipped"): its grip was mirrored into the right hand
  and `AttackBow` plays mirrored — both already in the game. Its idle and run (bow low and level in the middle of the
  body, its face tilted 20 degrees down, left hand on the string) and its put-away (right arm straight out to the
  side, then behind the hip to the lower back) are VIDEOS ONLY, waiting on the user. Its row is not written and its
  grip's resting place is "not set" (hidden while put away) until then; the numbers are in "What's left", item 2.
- Sniper: IN THE GAME since 2026-10-11 (the user: "ship the sniper in-game") — diagonal on the back, stock up at the
  right shoulder, muzzle down at the left hip. NOT PLAY-TESTED.
- Draw buffs (one passive per weapon, trigger "When you draw your weapon"): mechanism built, the 14 passives NOT
  authored — waiting on the user to name them.

## What's done

**Combat / peace mode (code):**
- `WeaponDraw.cs` (phases Sheathed/Drawing/Drawn/Sheathing, one pending request, `DrawSaid` wire struct),
  `WeaponDrawMath.cs` (all numbers: draw time 0.2 s + 0.006 s × weight, quiet spell 5 s, regen ×2, notice ×0.5,
  sprint margin 1.3, ordered-move exemption, noticed-distance weighting), pinned in `tools/game-math-tests/Checks.cs`.
- `WeaponDrawClient.cs` (owner side: asks, one held weapon-skill press replayed on Drawn, dropped on sheathe or map
  change).
- `StatusController` owns `Draw`, ticks it server-side, fires `SkillTrigger.OnWeaponDrawn` (15 s spacing), doubles
  natural regen via `WeaponDrawMath.RegenPerSecond`; `NetPlayer` / `NetMob` push `DrawSaid` SyncVars; server refuses
  swings/weapon casts while not drawn (`GameNetwork.OnHitRequest`, `RunCastRequest`), puts the weapon away on walk,
  sprint report, or 3 measured sprint-speed steps (`PutAwayIfSprintingDrawn`, exempt during server-ordered moves).
- Input: X toggles (`GameKeys.DrawToggle`); attack orders draw first (`Player3DController.PursueAttackTarget`,
  `Player3DAttacker.TryAttack`); weapon-skill presses held (`SkillRunner.Activate`); sprint only while away.
- Creatures: `MonsterAI3D` draws while chasing, swings/weapon skills wait for drawn, prey picked by noticed
  distance (`PlayerUnits.NearestTo`, `Sides.*Nearest*`), chase starts inside `NoticeSqr()`.
- Presentation: `Character3DPose` layers — 1 Carry (both arms), 2 Stand (whole body), 3 Draw (arms, scrubbed by
  `DrawTime`), 4 CarryRight, 5 CarryLeft; all shown only while Drawn. `WeaponSocket.ShowDraw` moves each copy between
  hand and resting bone at its hand's handover frame; a weapon with no resting place is hidden while put away.
- Data shape: `WeaponGrip` stow fields (six), `WeaponStanceBinding` (idle, carry, `carryArms` Both/Right/Left,
  sheathe/unsheathe clips, four handover frames); Stance Composer shows the `carryArms` picker.

**Per-weapon data in game:** stance library rows Katar (Both), Axe (Both, frames 24 / 24 / 20 / 20) and Dagger (Right,
frames sheathe R 64 / unsheathe R 20, spin frames 14–48, 2 turns, lift 0.25);
`katar_nysara` and `dagger_velmora` — the Trellis models the Katar and Dagger items use since 2026-10-10 — have
resting places on the Hips, carried from the models they replaced so each grip point rests where the old one did.
The dagger keeps the re-fit the user asked for (fist at the pommel end of the handle, 30% bigger): same length and
the fist at the same 0.136 of it, measured. The old models' copies: `backups/weapons-folder-cleanup-2026-10-10/`.

**Axe (doubled, re-fitted and wired 2026-10-10):** `axe_varkhaz` is 109% of body height long (scale
3.522463, position (-0.067319535, 0.615939, -0.4752968); old values in `backups/axe-size-2x-2026-10-10/`).
- Attack `AttackAxe` (in game, `keys/axe.json`): each axe chops in an upright plane on its own side, right strike at
  frame 17, left at 22 — no point of either axe inside the body; a handle end 1% under the ground in frame 0.
- Idle / running arms (`IdleAxe`, `CarryAxe`): unchanged, clean at the new size.
- Put-away `AxeSheathe` (take-out = reversed): the hands swing out beside the hips, then behind them, and leave the
  axes crossed in an X on the back, heads up behind the shoulders. Built by BLENDING still poses joint by joint
  (`paths/axe_sheathe_blend.cs`, poses `keys/axe_putaway_{side,back,handover}.json`) — the key-table solver could not
  turn the wrists that far (tip errors up to 130°). Handover frames (60/s) sheathe R 24 / L 24, unsheathe R 20 / L 20.
  Open: for 3 of 23 frames just before the handover the turning axes cross the hips and thighs (up to 9.4% of their
  points). Resting places on `axe_varkhaz` (Hips): `stowPosition (-0.34315, 0.27171, -0.77285)`, `stowEuler (353.097,
  197.215, 302.717)`, `stowPairPosition (0.40956, 0.15334, -0.74475)`, `stowPairEuler (0.994, 159.149, 72.287)`. The
  files as they were before the wiring: `backups/axe-wire-2026-10-10/`.
- Why the back is an X: this grip puts the blade square to the forearm, so a hand at the lower back cannot stand a
  shaft straight up (25–70° off in every reach test, the axes splayed out like wings); crossing inward was reachable.
- Videos `videos\axe\axe_{attack,idle,run,sheathe,unsheathe,run_put_away}.mp4`, all re-rendered.

**Dagger put-away (live 2026-10-10):** `DaggerSheathe` = `DaggerFlourish` (`paths/dagger_sheathe.py`: the arm
straight out to the right, held from frame 12 to 50 with the blade forward and the palm up, 26° off level) blended by
`paths/dagger_sheathe_blend.cs` into `DaggerPutAwayPoseHandover` (hand before the hip, elbow back, blade (0.24, -0.44,
-0.87) — 63° back from straight down) and the hanging arm; 1.4 s. The handover jumps 0.0% / 0°. Straight back-down
at the hip is out of reach (38–113° off in 30 reach tests).
**The spin is drawn by the game, not by the clip** (the wrist cannot turn a blade all the way round): the weapon's
stance row has `spinStartFrame`, `spinEndFrame`, `spinTurns`, `spinLift`; `WeaponSocket` lifts the main copy off the
fist along the hand bone's x and turns it about that x through the fist point, eased by `WeaponDrawMath.SpinTurnShare`
/ `SpinLiftShare` (pinned in the game-maths checks); the take-out asks where the put-away would be, so it turns back.
`render-stance.cs` and `check-clip-fit.cs` draw the same spin from the same row. Dagger: frames 14–48, 2 turns, lift
0.25 (hand-bone units, 8% of body height — the blade passes over the forearm). Measured: in 1 of 43 frames 4.2% of the
dagger's points touch the chest as the blade points inward. Server: nothing to rebuild — only `WeaponSocket` and
`Character3DPose` read a weapon's stance row. Earlier versions: `backups/dagger-sheathe-2026-10-10/` (forward-slanted
put-away) and `backups/dagger-spin-2026-10-10/` (the wrist-roll version).

**Greatsword stance set (built 2026-10-10, unwired; second version after the user's notes "lowered … like the
initial frames of attack" and "sheathe should be horizontal, at the back"; numbers from the sub-agent):**
`IdleGreatsword` / `CarryGreatsword` copy the locked attack's first frame (blade forward-right 4° below level, both
hands low at the right hip; lean 26° with the hips forward stands in for the attack's bent chest; idle hips 1.8% lower
so the breath does not lift the foot; running blade tilted 2° up so it stays off the ground). Built vs the attack's
frame 0: right wrist 1.8% of body height off, left 4.4%, blade 0°. Known, not raised on purpose: the pommel sits in
the left thigh and hips — idle 4.1% of the sword in every frame, running up to 9.0% (default) / 11.0% (Knight); the
attack's own frame 0 is 2.6%. `GreatswordSheathe` (1.067 s, handover R 40) / `GreatswordUnsheathe` (reversed, R 24):
a pose blend (`paths/greatsword_sheathe_blend.cs`) from `GreatswordHoldUpright` — the idle's arms on the STANDING
body, because in game a put-away plays on the arms only over the shared idle or the run — through
`GreatswordPutAwayPose{Side,Back,Diagonal,Slant,Handover}`;
ends with the sword level across the upper back, hilt at the right shoulder blade, tip left, flat facing back turned
28° down. Resting place to write on the yes (Hips, default body): `stowPosition (-0.82022, 0.92847, -0.79970)`,
`stowEuler (28.202, 182.600, 279.408)`. Resting on the back: 0.0% inside body and arms over Idle2 / Walk / Run2 /
Sprint on the default body; Knight helmet 1.2% in Run2 frames 4–5. Handover jump 0.0% / 0° default, 2.5% / 2.9° Knight.
Key tables `keys/greatsword_*.json`; videos `videos\greatsword\greatsword_{idle,run,sheathe,unsheathe}.mp4` (three
views). **In game:** stance row (idle, carry, `carryArms` Both, the clips, frames sheathe R 40 / unsheathe R 24), the
resting place plus `stowAuthored` on `greatsword_mordane`, the controller rebuilt and read back, and the item's weight
90 -> 133 so the unchanged formula (0.2 s + 0.006 s × weight, the same for taking out and putting away) gives 0.998 s;
its bag weight is 133 too. Files as they were before: `backups/greatsword-simple-2026-10-10/`.
Tried and removed at the user's word the same day: a flourish before the put-away (two swings of the attack, then a
figure-8) — on the arms-only layer the body stayed still under a heavy sword ("it looks so static"), and a put-away
time that followed the animation's length ("the sheathe and unsheathe should have the same duration").

**Warhammer handle (2026-10-10, user: thin it to fit the hands, then "too thin … 2x", then thick everywhere except
inside the fist):** the prefab uses `2_Generated/WeaponModels/warhammer_tharos/warhammer_tharos_thin_handle.asset`, a
copy of the model (the original is unreferenced): handle 0.0154–0.0166 world across between pommel and head, 0.0084
inside the fist (the fist spans mesh y −0.1175 to −0.0610 on the default, Casual_Bald and Knight bodies), a short
taper each side; the whole mesh shifted 0.011 so the handle's centre line runs through the fist (it ran along the
hand's outer edge). 2,042 triangles (six rings of points added for the taper). The grip values are untouched. An even
2x handle pushed through the hand's skin by up to 0.006. Backups: `backups/hammer-handle-2026-10-10/`.
**Warhammer attack shake (user, 2026-10-10: "the hands are shaking"):** `keys/warhammer.json` has `"steadyArms": true`
(a new build-script option, see the skill): 66 direction reversals over the arm joints -> 22 — which did NOT cure it.
The real cause (measured): the planned hammer directions were out of the arm's reach (aim error 25° mean, 38° held
overhead), so the hammer drifted pose by pose. The overhead and strike-end keys now carry the directions the arm
reaches: aim error 3° mean, hammer turn per pose rises and falls once each. In game. Then (user: "make the end flat surface of the hammer actually face the ground, flat … hard-hit the ground"): the
strike keys hold the shaft level and forward with the head's flat end face straight down (3° at the hit, 0° held),
the face landing on the ground (lowest point 2–3% of body height into it; was 70%). Backup of the previous version:
`backups/hammer-flat-2026-10-10/`.
Then the wind-up (user: a bigger "bwelo" from behind, side-on, head on the enemy, hammer "held low near the ground behind
the legs"): body turned 70° (right shoulder back), head turned back 70° with the new key field `look`, the hammer
swung past the right hip to low behind the legs (lowest point at the ground), over the top, down flat; impact frame
unchanged (key 14 of 20). Known: the left hand comes off the handle by up to 10–17% of body height during the wind-up
(the left arm cannot reach the handle behind the right hip with the body side-on; less turn or a further second grip
did not help). Previous version in `backups/hammer-windup-2026-10-10/`.
Then the swing (user: "make more frames from swing to hit … dont make the hammer teleport"): the clip is now 30 key
frames (1.0 s at 30/s), hit at key 21 (still 0.70 of the swing); the swing from the held wind-up over the top to the hit
uses the new `pass` keys and `wristStep` 1.0 (see the skill): the head moves 20–33% of body height per pose all the
way (was one jump of 89%); face 1° from flat at the hit, lowest point on the ground. Previous version in
`backups/hammer-swing-frames-2026-10-10/`.
Old clip and scripts in `backups/hammer-attack-2026-10-10/`.

**Tools and skill (all in `.claude/skills/clone-ro-sprite-animation/`):** section 4c is the per-weapon recipe;
`render-stance.cs` + `make-stance-videos.py` (idle / run with one-arm overlay `"Clip:Right"` / put-away with the
handover; prints each elbow's bend from the drawn poses), `measure-handover.cs` (the resting place = where the
weapon is at the put-away handover frame), `reverse-clip.cs` (take-out = put-away played backwards),
`paths/axe_sheathe_blend.cs` and `paths/dagger_sheathe_blend.cs` (put-aways built by blending still poses),
`paths/dagger_sheathe.py` (the dagger spin and its handover pose), `check-clip-fit.cs` (does a weapon's current
model still fit its saved clips: body overlap, ground, handover jumps — run it after any model, size or hold change),
key tables `keys/<weapon>_*.json`. The katar's put-away is a keyed hand path (`keys/katar_sheathe.json`, a splice).
Videos: `D:\kimodo\out\ro_refs\videos\<weapon>\<weapon>_{attack,idle,run,sheathe,unsheathe}.mp4` — one folder per
weapon, 27 clips, every one rendered 2026-10-10 with the Trellis models (the user had all older videos removed; the
two joiner scripts write into those folders).

## Uncommitted working-tree changes (measured 2026-10-10 03:32)

248 changed or new paths, most of them NOT this task's — other sessions' work sits in the same tree (the
Trellis weapons and the removal of the sprite-sheet weapons and medieval-pack prefabs on 2026-10-10, item assets, UI changes, the
`DirtySceneProbe`, tools). Nothing is committed. This task's own files: all of `unity/Assets/Scripts/WeaponDraw*.cs`,
`WeaponSocket.cs`, `WeaponGrip.cs`, `Character3DPose.cs`, `StanceLibrary.cs`, `StanceCatalog.cs`, `StatusController.cs`,
`NetPlayer.cs`, `NetMob.cs`, `GameNetwork.cs`, `Player3DController.cs`, `Player3DAttacker.cs`, `SkillRunner.cs`,
`Character.cs`, `MonsterAI3D.cs`, `MobShadow3DVisual.cs`, `PlayerUnits.cs`, `Side.cs`, `GameKeys.cs`, `Item.cs`
(weight tooltip), `ICombatUnit.cs`, `Health.cs`, `UnitStates.cs`, `SkillDef.cs`, `SkillTooltip.cs`,
`Editor/Character3DControllerBuilder.cs`, `Editor/StanceComposerWindow.cs`, `Editor/SkillComposerWindow.cs`; the
animation clips `IdleKatar`, `CarryKatarGuard`, `KatarSheathe/Unsheathe`, `IdleDagger`, `CarryDagger`,
`DaggerSheathe/Unsheathe`, `IdleAxe`, `CarryAxe`, `AxeSheathe/Unsheathe`;
`StanceLibrary.asset`, `HumanoidPlayerController.controller`, the resting places now on `katar_nysara.prefab`,
`dagger_velmora.prefab` and `axe_varkhaz.prefab`; `WeaponDrawMath.cs` and `tools/game-math-tests/Checks.cs` (the spin); docs `combat-and-peace-mode.md`, wiki `combat.json`, `unified-unit-stats.md`, `docs/README.md`;
`tools/client-math-baseline.txt`. Several of these files also carry other sessions' edits — never revert one whole.
`unity/Assets/Scenes/Systems.unity` is modified on disk by others; the open scene was clean after every script here.

## What's left

1. **Shuriken through the head during its throw — fixed and shipped 2026-10-11** (the wind-up keeps the hand farther
   from the head; the shuriken is held at its middle — HARD RULE). Measured: more than 0.5% of it in the body in poses
   0–3 of 41 only (legs and hips, as the arm leaves its hanging start); nothing in the head or neck.
2. **Wire the Bow when the user approves its videos** (stance row; resting place on the grip, Hips;
   rebuild the controller with `Player3DVisualBuilder.BuildController()` and read its states back). Default body,
   frames at 60 per second, put-away / take-out:
   - Bow — Both (bow hand and string hand) — 21 / 13 of 34 — pos (0.10883, 0.22273, -0.34672) eul (11.284, 191.315,
     271.304). Flat across the lower back in the RIGHT hand, front straight down (8 degrees to the right), 3–4% of body
     height off the back. On the way the right arm goes straight out to the side with the bow level, then behind the
     hip: nothing of it enters the body; its far end dips 0.9% of body height under the ground for one pose.
   Shipped rows, for reference (default body; written to the grips 2026-10-11): Sword 35/9, Staff 24/20, Wand 24/20,
   Spear 36/8, Scythe 36/8, Warhammer 30/14, Shuriken 20/24, Sniper 20/14 (diagonal on the back; up to 5% of it passes
   the hips and back in poses 4–12 of the put-away; it never touches the ground). The Spear's and Scythe's resting places are the hand's
   place moved 6% / 5% of body height onto the back of the head (they still sit 11% off the middle of the back: the
   chibi head sticks out further back than the torso), so at the handover they move that far straight in.
3. Ask the user for the 14 draw-buff passive names; then author them with the data-author agent (values table in
   the earlier conversation: three channels per weapon from `WeaponMasteryStats.Raises`, 4 s, mastery-learned).
4. The user's play-test of the dagger (draw with X, run, walk/sprint puts it on the hip, take it back out).
5. Open since before this task: the dagger ATTACK clip's key table no longer reproduces its saved clip (dry run
   differs by 0.36). With `dagger_velmora` no point of the dagger is inside the body in any frame. Not touched.
6. **The Greatsword attack is LOCKED by the user (2026-10-09, repeated 2026-10-10): it keeps the imported
   `AttackSword2H`. Never rework it and never offer to.** Measured with `greatsword_mordane`, for the record only:
   the blade is inside the left thigh / head in 11 of 39 frames (up to 13.9% of its points), the second hand is 5–16%
   of body height off the handle in every frame, the tip goes 6% under the ground. A replacement was built on
   2026-10-10 against that lock and taken back out of the project the same day; the user did not ask for it and it
   was never in game (its files: `backups/alignment-fixes-2026-10-10/unrequested-greatsword-attack/`). The Bow's
   attack and the Shuriken throw are locked the same way.
7. **Saved clips against the Trellis models (measured 2026-10-10 by playing every saved clip with its item's model).**
   Aligned at the sizes of that morning: sword, dagger, spear, staff, katar, bow, scythe (its blade side leads the
   sweep by ~25°); the warhammer, sniper, shuriken and greatsword were resized later that day (item 8), the axe
   doubled and re-fitted (Axe paragraph). Fixed that day:
   - **Wand:** `AttackWand` rebuilt (`keys/wand.json`: the staff's right arm and swing under the wand's own body and
     timing) — no point of the wand inside the body in any frame (was 13 of 21 frames). In game; the old clip is in
     `backups/alignment-fixes-2026-10-10/`.
   - **Katar put-away:** the right katar now moves 0.8% of body height and turns 1° at its handover (was 12.2% and
     52°); the left is as it was, 1.6% / 8°. After the katar's re-seat in the fist they measure 0.9% / 1° and 1.2% /
     8°, with the resting places carried to the new hold. `KatarSheathe` is a splice (see the
     note in `keys/katar_sheathe.json`), `KatarUnsheathe` its reverse; the old clips are in the same backup.
   - **`EDGE`** no longer lists the scythe (its blade is on -x like the rest).
   Still open, nobody asked for them: the staff's and the wand's lower end is 13% of body height under the ground in their
   attack's first frame; the katar attack's blades go 19% of body height under the ground in frames 1–3; a rebuild no longer reproduces the
   saved clip for the katar idle (0.62), katar attack (2.00), scythe (0.74) and shuriken (1.82) — run the dry run and
   read its errors before any re-save.
8. **The resized weapons do not fit their saved clips** (measured 2026-10-10 with `check-clip-fit.cs`; nothing re-fitted,
   the user asked only for the sizes): the Warhammer's head ends its attack 73% of body height under the ground and
   stays 24% under it all through the standing idle; the Sniper is 69% under the ground in idle and walk with up to
   11.7% of it inside the body; the Shuriken is 10.9% inside the head and chest at the start of its attack; the
   Greatsword (attack LOCKED) is 15% inside the legs and hips in the run. The Sniper's hand sits correctly on the
   stock's narrow wrist; its scope tube sits about 45° round the barrel — that is how the model was made.


## Dead ends (proven)

- **Reversed key table for take-out:** building the take-out forward from reversed keys made the solver roll the
  blade the other way and put it in the thigh (30° off, 150 points inside). Use `reverse-clip.cs` on the built
  put-away clip instead — exact.
- **Dagger straight down at the hip:** unreachable with a sword-style grip (62° off; down-back 75°). Forward-and-down
  `[0, -0.5, 0.85]` reached exactly. Test reachability with static two-key tables before any path.
- **"Straight arm" at reach 0.97** is a 151° elbow; 1.0 gives 175°.
- **Video overlay filter** matched "Arm" but not "Forearm" — fixed; the video now prints elbow angles. Measure the
  video, never only the clip.
- Heredocs with quotes inside fail in this shell; write Python edit scripts with the Write tool and run them.

## Next single step

The user's verdict on the Bow's videos; wire it on a yes (What's left, item 2). Their play-test of everything
shipped — the axe, katar, dagger, greatsword and the nine weapons of 2026-10-11 — is still to come.

## How to resume

```text
Read handoffs/weapon-stances-and-combat-mode.md and the clone-ro-sprite-animation skill (section 4c), then continue:
wire in the Bow if I approved its videos (What's left, item 2), or fix what I sent back.
```
