---
name: import-weapon-models
description: ALWAYS use — no exceptions, a single weapon included — for ANY work on a weapon's 3D model — importing a weapon pack (downloaded or already imported), turning a 2D weapon sprite sheet (a GPT drawing) into a 3D weapon or an ornate one into a Trellis model, writing the GPT prompt for such a sheet, adding one model, replacing or swapping one weapon's model, or fixing how a weapon sits in the hand ("put these weapons in the hands", "convert this sword sheet", "swap the axe model", "the bow faces the wrong way"). Covers the backup, moving the pack, the pink-material fix, the two-view sprite sheet and its converter, working out where each model is gripped, building the weapon prefabs with their hand position and keeping where they rest on the body when put away, checking them in private pictures in real poses and from the game camera, and pointing the weapon items at them.
---

# Importing 3D weapon models into the hands

Everything here runs through the live editor link (`eval_file` with a script in the scratchpad). The user does
nothing but get the pack onto the machine. First proven on 2026-10-08 with a low-poly medieval pack
(`0_Raw/TossedSalads/LowPoly-MedievalWeapons`): every hand-weapon shape built, checked in pictures, play-tested by
the user as "all looks good".

A weapon can also start as a 2D drawing instead of a pack: a two-view sprite sheet turned into a model by a
converter. That road is **A weapon from a 2D sprite sheet** (`sprite-sheet-road.md` beside this file); its S1–S5 take the place of steps 2–6, except step 5b (the put-away place), which every road runs.
An ornate weapon whose parts sit at different depths goes through Trellis instead — **An ornate weapon made with
Trellis** (`trellis-road.md` beside this file).

## What is already there

`1_Authored/Prefabs/Weapons/` holds only the weapons made with Trellis, which every weapon item uses since
2026-10-10 (the user had the folder cleared of everything else that day): each named `<weapon type>_<Kashira>` (`sword_veyra`, `wand_seraphine`, …), a `<name>.prefab` with its own
`Materials/<name>.mat`, its model and texture in `2_Generated/WeaponModels/<name>/`. Weapons made from sprite sheets
by the converter keep theirs in `2_Generated/Weapons/<name>/` (no prefab uses one today). The first low-poly
pack's raw files stay in `0_Raw/TossedSalads/LowPoly-MedievalWeapons/`; its 41 prefabs and the sheet weapons were
removed on 2026-10-10, copies in `backups/weapons-folder-cleanup-2026-10-10/`. List what is there with a glob of the folder and read
`equipModel` off each item rather than trusting a list here.

**Swapping one weapon to another shape that is already in the folder** is step 7 alone (point the item at the
prefab) plus a picture of that one weapon (step 6). Every other step is for shapes that are not there yet.

## How the game mounts a weapon (read these before changing anything)

- A weapon item (`3_Data/Resources/Items/<Name>.asset`) points at a prefab through `equipModel`.
- The prefab carries a `WeaponGrip`: which hand, plus position / rotation / scale **local to the hand bone**.
  `WeaponSocket` parents the prefab under the bone `HandSocketResolver.Find` returns and copies those values.
- The only runtime reader of `equipModel` is `WeaponSocket` (re-grep `equipModel` to confirm before you rely on it).
  Creatures carrying the same item show the same model; a thrown weapon's flying copy is the model in the hand.
- An item with no `equipModel` shows an empty hand — that is how the Fist is drawn (bare hands).
- The same `WeaponGrip` also holds **where the weapon rests on the body while put away** (combat and peace mode,
  2026-10-09): six settings — `stowAuthored` (is a place set), `stowBone`, `stowPosition`, `stowEuler`, and for the
  other copy of a pair weapon `stowPairPosition`, `stowPairEuler` — local to that body bone. `WeaponSocket` moves
  each copy there when the server says the weapon is away; **a weapon whose place is not set is hidden while put
  away.** Building a prefab from scratch writes all six as "not set", so every build must keep or carry them (5b).
- The working references ("seeds") are two saved grips, in the hand bone's space, from the pack prefabs every
  weapon was first fitted to (`sword_1_blue`, `bow_1_blue`, removed 2026-10-10 — the copies in the backup above hold
  the same values). **Sword (right hand):** fist point `G = (0.011, 0.18756053, 0.12156433)`, turn
  `(345.37842, 86.71383, 282.81427)`, scale `0.8735323` on the raw pack mesh `sword_1`. **Bow (left hand):**
  position `(-0.024, 0.226, -0.005)`, turn `(358.83505, 51.418995, 283.19495)`, scale `0.8735323` on `bow_1`. The
  animation skill's scripts use the same fist point.

## The steps

**0. Contract first.** Three bullets per the project rule: which items get which models, what stays (the weapon
types the pack does not cover keep their current model), and the check (pictures next to the sword and bow the items use today, then the
user's play-test). Picking a shape/colour per weapon is a judgement call: decide, and label it as yours.

**1. Back up before touching anything.** Copy every prefab the weapon items currently use, plus everything it
depends on (`AssetDatabase.GetDependencies(path, true)`, project files only), plus every weapon item `.asset`,
each WITH its `.meta`, to `backups/<what>-<date>/` at the repo root. It must be OUTSIDE `unity/Assets`, or Unity
loads the copies as duplicate assets with clashing IDs. Count the copied files and say the count.

**2. Find the pack and move it to `0_Raw`.** Downloaded-but-not-imported packs sit in
`%APPDATA%/Unity/Asset Store-5.x/<publisher>/…/*.unitypackage` (import with `AssetDatabase.ImportPackage(path, false)`).
An imported one lands at `Assets/<Publisher>/`. Move it with `AssetDatabase.MoveAsset("Assets/<Publisher>",
"Assets/0_Raw/<Publisher>")` — that keeps every ID. Never edit anything inside it afterwards.
Getting the pack onto the machine needs the user's Unity account: **Window ▸ Package Management ▸ My Assets ▸
Download** (that menu path was read from Unity 6000.4 — re-read it with `Unsupported.GetSubmenus("Window")`).

**3. Measure the pack.** List every mesh with its bounds; read each pack material's `shader.name`. A pack made
for older Unity uses `Standard`, which draws **pink** here — every new weapon gets a URP `Lit` material made by
you in `1_Authored/Prefabs/Weapons/Materials/`, using the pack's texture (one material per colour, shared by all
weapons). Read the textures' size and compression too and check them against `unity/Assets/ASSETS.md`.

**4. Find where each model is gripped — measure, never assume.** Run the profile script below on the seed meshes
and the pack meshes. It prints the width of each mesh in 24 slices along its length (`y`): the handle is the
narrow run of slices, the guard or head is the wide jump. Establish, for the pack:
- which axis is the length (both packs so far: `+Y`, pommel at the low end), and
- where the mesh's own centre point sits. In the 2026-10-08 pack it sat **on the handle for every weapon**
  (middle of the handle for swords, near the butt for spears, the middle for staffs and bows), so the centre point
  WAS the grip point. A different pack may not do this — if not, compute the handle's middle from the profile.

For the seed sword (`sword_1` in the pack's model file), the handle's middle is its centre point, `(0, 0, 0)`.

**5. Build the prefabs** with the build script below. The maths, per weapon:
- **rotation** = the seed's rotation (same length axis, same flat side). Bows: the bow seed's rotation, and check
  whether the new bow needs a half turn around its length (`BOW_FLIP`) — a bow whose string sits on the other side
  of its centre point does; only a picture next to the seed bow shows it.
- **position** = the seed's handle point carried into hand space: `seed.pos + seed.rot * (seed.scale ⊙ seedHandle)`,
  minus `rot * (k ⊙ packGripPoint)` (zero when the pack's centre point is the grip).
- **scale** = ONE number for the whole pack: `seed sword length ÷ pack sword length`. That keeps the pack's own size
  differences (dagger ≈ half a sword, spear ≈ 1.75 swords). Say in the report how the new bow compares to the seed bow.
- **Name** each prefab after the pack's own prefab name without its `pfb_` prefix (`sword_1_blue`) — never invent one.

**A weapon NOT held like a sword handle (a katar, held by crossbars inside a frame) is placed on the middle of the
fist, never on the seed's handle point** — that point only has to lie somewhere along the sword's handle line, and
it sits 15% of the fist's width off to one side (2026-10-09, the katar's bars ended up beside the fist). The fist's
middle, measured from the hand's own mesh on the default and Knight bodies, is `(0.0045, 0.2135, -0.024)` in the
hand bone's space; the hand's +y points from the wrist out past the knuckles (the forearm sits at -y), and the
seed sword's handle runs along z, across the palm. The katar: blade along +y, flat side facing x, crossbars along z
(`localEuler (0, 90, 0)`).

**Do NOT use the Weapon Grip Tuner's batch "Copy Grip" button for a mixed pack:** it stretches every weapon to the
seed's length, so a dagger comes out as long as a spear.

**5b. Keep or carry the put-away place — every build, before step 6.** Read the put-away settings of every prefab
the build will overwrite BEFORE building, and of the model each item uses today.
- **Re-saving a prefab whose model and grip point did not change** (a material fix, a re-run of the build): the build
  script below copies all six settings from the old prefab onto the new one, the "set" flag included. Copying only
  the position and turn leaves it "not set", and the weapon vanishes while put away.
- **A new model replacing the one an item uses** (a new katar for the Katar), or a re-fit that moved the grip point
  or the scale: run the carry-over script below. It keeps the old place's bone and turn and moves the position so the
  new weapon's grip point rests exactly where the old one's did — both copies of a pair, the second with its
  mirrored scale. It assumes what the hand fitting already assumes: length along +Y, flat side the same way.
- **The old model has no place set** (today every weapon but the katar): nothing to carry. The new weapon stays
  "not set" and is hidden while put away; choosing a spot for it is a separate job, agreed with the user.
- **Picture before saving.** A longer or bulkier model can put its blade into the body or the floor even with its
  grip point in the old spot. Run the carry-over script with `WRITE = false`, render the body standing in its idle
  with the weapon on that bone at those values (from behind, from the side and from the game camera), look at it
  yourself, then show the user. Write only after their yes.

**6. Check in pictures before attaching anything.** Run the picture script below. It poses the real player model
(standing, mid-attack, close-up of the hand) holding each new weapon, beside the sword and bow the items use today (held at the seed grips), and writes
contact sheets to the scratchpad. **Look at them yourself with Read; never open them on the user's screen.** Compare
each row with those reference rows: handle inside the fist, guard or head on the far side of the fist, same direction
as the reference. Fix and re-run until they match.

**7. Point the items at the new prefabs** (SerializedObject on each item, `ApplyModifiedPropertiesWithoutUndo`,
`SetDirty`, `SaveAssets`), then read each item back and `git diff` the item files. Re-saving an old item also drops
fields the code no longer declares and writes new fields at their defaults — grep `Item.cs` for each dropped name
to prove it is dead, and say so in the report.

**8. Prove nothing else moved.** Every script checks that no open scene is modified before and after (`isDirty`).
Then run `tools/check-unity-code.sh`, `tools/check-asset-rules.py`, `tools/check-editor-writes.py`, and read the
WHOLE console (`level: log`, a generous tail). Delete the scratchpad scripts and pictures in the same reply.

**9. Report:** a table of item → model → hand → put-away place (kept, carried over, or not set — hidden while put
away); the weapon types still on the old models; whether any server needs a
rebuild (it did not on 2026-10-08: only the hand mounting reads the model, and `MapServerLink.CharacterBuild` is a
fixed string — re-check both); where the backup is.

## A weapon from a 2D sprite sheet

The whole road is `sprite-sheet-road.md` beside this file — read it whole before taking it.

## An ornate weapon made with Trellis

The whole road is `trellis-road.md` beside this file — read it whole before taking it.

## Traps in the editor link's script runner

- **No `using` lines** — `UnityEngine` and `UnityEditor` are already imported, and a `using` line fails to compile.
- `Object` is ambiguous: write `UnityEngine.Object`.
- Extension methods need their static class spelled out (there is no `using` to bring them in).
- The default timeout is 5000 ms; pass `timeout: 60000` or more for building and rendering.
- Editing a `.cs` file in the scratchpad with `sed` is refused by the performance hook; use the Edit tool.

## Posing a character outside Play — the only way that worked

`Animator.Play` + `Animator.Update(0)` and a `PlayableGraph` with `Evaluate()` both leave the model in its T-pose
inside a preview scene (the hand bone did not move a millimetre). Set the joints directly instead:
`HumanPoseHandler.GetHumanPose`, overwrite `muscles[i]` with each humanoid clip curve evaluated at the wanted time
(curve name → `HumanTrait.MuscleName`, after turning `LeftHand.` / `RightHand.` into `Left ` / `Right ` and `.` into
a space), then `SetHumanPose`. All 95 muscles matched on the player controller's clips. The first render of a
fresh preview can come back empty — render once, throw it away, render again.

**Muscles alone are only the joint bends.** For a still hand close-up that is enough; for any full-body pose or
video, also set `hp.bodyPosition` from the clip's `RootT.x/y/z` curves and `hp.bodyRotation` from `RootQ.x/y/z/w`
— they carry how low the hips sit, the whole-body lean and any step. Skipping them (2026-10-08, a slam made with
Kimodo) left the hips at standing height while the knees bent, so the feet floated up to 14% of body height and
the step forward vanished; the user saw it in the video before I did.

## Profile script

The script is `profile-script.cs` beside this file.

Each column is one slice, low end first: `width across / width through`, `--` = no vertex in that slice (low-poly
meshes leave many empty). Slice of the centre point = `(0 - ymin) / (ymax - ymin) × 24`.

## Build script

The script is `build-script.cs` beside this file.

## Put-away carry-over script (5b)

Run with `WRITE = false` first: it prints the new place for the picture. Rerun with `WRITE = true` after the user's
yes. The scale is the grip's own: `WeaponSocket` keeps the weapon's world size on the body bone, and hand and hips
measured the same scale on the default and all three Knight bodies (2026-10-09), so the grip scale is the size there.

The script is `carry-over-script.cs` beside this file.

Then read the new prefab's file back and diff it: the six put-away lines must hold the printed values and nothing
else may have changed.

`packGrip` stays zero when the new pack's centre point is the grip point (step 4); the bow line assumes the same
for bows. A sword-shaped list (`picks`) can hold any melee shape — the pack's own size differences carry through `k`.

## Picture script

The script is `picture-script.cs` beside this file.

Columns: standing, mid-attack, close-up of the hand (that close-up is framed from behind the wrist; for a clear view
of the handle in the fist, set every muscle to 0 instead — the T-pose close-ups are the easiest to judge). The model
draws black in these pictures because of the preview lighting; only the weapon and the hand matter here.
