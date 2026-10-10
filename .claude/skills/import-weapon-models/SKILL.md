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
converter. That road is **A weapon from a 2D sprite sheet** below; its S1–S5 take the place of steps 2–6, except step 5b (the put-away place), which every road runs.
An ornate weapon whose parts sit at different depths goes through Trellis instead — **An ornate weapon made with
Trellis** below.

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

First done on 2026-10-09 for the four Kashira weapons. Steps 1, 5b and 7–9 above still apply; S1–S5 take the place of
the rest of steps 2–6.

**S1. The sheet — two GPT messages, never one.** Asking GPT to design a weapon AND draw strict upright views in one
message gives stiff, symmetric, cup-shaped results (the Fist failed four times that way). So:
- **Design first:** a 3/4 hero view, four clearly different options side by side, the theme, "NOT symmetric", and a
  reference picture attached when the user names another game's style. The user picks one.
- **Then the sheet**, in the same chat: "Redraw option N exactly — same shapes, details, colours — as TWO
  orthographic views side by side, left to right: FRONT view (the broad face) and SIDE view (turned a quarter turn,
  showing its thickness). Same scale, upright, tip at the TOP and handle end at the BOTTOM on the same pixel rows in
  both views. Flat solid magenta #FF00FF background edge to edge, no shadow, no text, crisp pixel art, one solid
  connected object, nothing wire-thin (no strings, chains, ribbons)." A rifle is drawn barrel-up, a bow upright and
  stringless.
- **Never ask for a third, top-down view.** GPT draws it wrong (in one sheet it was a small copy of the hilt), and
  the converter carves the WHOLE weapon to that third picture's outline.
- The user saves the image file itself — a screenshot of the chat carries its corner buttons in as extra pieces.

**S2. Convert** — `python3 tools/sprite-sheet-to-weapon.py <sheet.png> --name <name>` writes
`2_Generated/Weapons/<name>/<name>.obj` and `.png` (length 1 along +Y, origin at the centre of the bottom end). The
import rule already makes that texture point-filtered and uncompressed.
- The output must say `views: 2`. The FIRST view is read as the front: when GPT swaps them (the sniper's profile
  came out on the right), swap the two halves of the picture in a scratch copy and convert that.
- Two views are lofted with rounded edges (2026-10-10): every row of the front view is an eight-cornered
  cross-section, as thick as the side view at that row only deep inside the front view's outline and rounding down
  to one block at it, so wide parts (blades, discs) end in a thin rounded edge and narrow ones (grips) come out
  round; the shading is smoothed across the curve. `--square` rounds only the outer two blocks, for parts that must
  stay blocky (the hammer head, the bow, the sniper). A surface may stray two blocks from the drawing so that many
  rows share one face.
- **The texture has 4×4 colours per block** (`--texels`), taken only from pixels 3 inside the drawing's outline and
  filled outward from there. One colour per block smeared a 40-block wand head into mush; the outline pixels put a
  dark and magenta rim on any surface that strays past the drawing. 13 weapons ≈ 7.3 MB of textures, fixed by the
  number of weapon models, not by the number of players.
- **The side view is only a thickness per row**, so an ornate head whose side view is filled by ornaments (the
  wand's clouds) comes out as a rounded lens as thick as those ornaments, never with its parts separated in depth.
- **Triangle budget, measured:** the character body is 2312 triangles and the pack's weapons 58–146. Rounded
  lofts: sword 412, greatsword 700, katar 626, wand 984, axe 920 (a pair is twice that), scythe 1138, shuriken
  1382. Ten corners per slice and a one-block fit cost up to 2359; stacked blocks 1622 to 5118 — never go back.
- **Check the shape against the sheet before Unity:** render the model straight from the front and the side beside
  the sheet (a small numpy rasteriser over the `.obj` and its `.png` is enough) — that comparison is what shows a
  head turned to slabs or a dark rim.
- Unity's model import mirrors X (measured: the sniper's scope, drawn on the right, came out at mesh −X).

**S3. Find the grip point in the front view's rows.** Load the converter as a module and print its `resample` mask
of the front view row by row (row 0 = bottom, 160 rows), with the side view's mask beside it. The handle is the
narrow run between pommel and guard: grip `y = (first row + last row + 1) / 2 / 160`; `x` and `z` are the run's
centre minus half the front width and half the side depth, `/160`, and `x` then has its sign flipped (the mirror
above). Measured examples: sword handle rows 10–38 → y 24.5/160; greatsword rows 15–36 → 26/160; the katar at the
midpoint between its two crossbars; the sniper on the stock's narrow wrist just behind the trigger guard.

**S4. Build the prefab against the model the item uses today** (the seed for that weapon type, not always the
sword): rotation = that prefab's `WeaponGrip` rotation; its grip point in the hand = its `localPosition` (a pack
model's centre point is its grip); scale `k` = (its mesh length × its scale) ÷ the new mesh length; position =
`seed.localPosition − R · (k · grip)`. Material: URP Lit with the generated texture, smoothness 0.5, metallic 0;
set the model's material import to None. Name the prefab and material after the sheet's `--name`. Then step 5b:
a rebuilt sheet weapon keeps its put-away place, and a new one carries the place of the model the item uses today.
- **A rifle** follows the hold the Sniper attack was built for (the animation skill's rifle branch): right hand;
  barrel (+Y) along the hand from wrist to knuckles — in a T-pose, hand position minus lower-arm position, in the
  hand's own space; the gun's top (the scope side) toward the thumb — the seed sword's +Y in hand space, made square
  to the barrel; fist at the seed sword's grip point; length 0.62 of body height (the stand-in was stock 0.12,
  barrel 0.5).
- **The hand-scale trap:** `CharacterSpec.Active.bodyHeight` is in world units, but a `WeaponGrip` scale is in the
  HAND BONE's space, and that bone's scale was 0.1035 on a body scaled to 0.327. A length in body heights must be
  divided by the hand's scale; the first sniper skipped that and came out ten times too small.

**S5. Pictures — also from the game camera.** Besides the picture script, render the in-game body (for example
`Casual_Bald.fbx`, not only the default model) from the game camera's angle: `CameraFollow3D`'s defaults put it 10
up and 4 back, a steep look down; render it from four sides. From up there a weapon whose broad face points sideways
shows only its edge — the Kashira katar, held like the dagger, came out as a thin sliver the size of the old dagger
in those pictures (2026-10-09), and the user had reported it "missing". A pair weapon
(`WeaponTypes.IsDualWield`) gets its second copy exactly as `WeaponSocket` mounts it: position x negated, rotation
`(x, −y, −z, w)`, scale x negated.

Then step 7 (point the item, only on the user's go) and steps 8–9.

## An ornate weapon made with Trellis

When a weapon's head has parts at different depths (a wand's sun and clouds, a staff's crown), the converter above
cannot build it: make it with the `image-to-3d` skill instead, from the sheet's front view. First done 2026-10-10
for the wand, then for all 13 weapon types the same day (1,410–1,484 triangles each). Steps 1, 5b and 7–9 still
apply; this replaces the rest.

- **Make it:** `python3 tools/image-to-3d.py <front view.png> --name <name>` → `2_Generated/WeaponModels/<name>/`.
  Judge it with `tools/render-model-views.py` and show the user before anything else.
- **Import:** texture compressed, bilinear, mip-mapped, `maxTextureSize` 1024, not readable (painted, not pixel
  art); the model's material import set to None; a URP Lit material in `Materials/<name>.mat` with that texture.
- **Fit:** against the model the item uses today, like S4 — same rotation, same length (`k` = its length ÷ the new
  mesh's `bounds.size.y`), and the fist on the SAME FRACTION of the length as the old model's held point (the old
  held point is the sword's fist point carried into the old mesh: `R⁻¹ · (G − old.localPosition) / old.scale`).
  The new grip's x and z are the middle of the new mesh's vertices within 1.5% of the length of that height — the
  shaft there. Trellis's model is centred, not bottom-anchored, so measure from `bounds.min.y`.
- **Check which way Trellis turned it.** It does not keep the drawing's facing: on 2026-10-10 the axe came out a
  quarter turn round its length and the bow and scythe a half turn. Compare where the head (or the string) points in
  hand space against the old model, and add the turn about the length before saving.
- **A handle thicker than the fist** (the warhammer after it was tripled, 2026-10-10: 0.032 across against the
  sword grip's 0.008, in world units on the default body): never edit the model file. Copy the mesh into a new asset
  beside it (`<name>_thin_handle.asset`), move every point of the handle's run toward that slice's centre line until
  it is as wide as the sword's grip at its fist, with a short smooth band at each end, and point the prefab's mesh at
  the copy. First check the handle's centre line runs THROUGH the fist point — the hammer's ran along the hand's
  outer edge, hidden by its thickness, and the mesh had to be shifted 0.011. The bodies have no finger bones: the
  hand is a closed mitten, so "fits" means the handle runs inside it.
- **⛔ A shuriken is ALWAYS held at its middle, never by a blade** (user, 2026-10-10: "all handles should always be
  at the middle, not on the blades"). A clearance fix may turn, tilt or resize it — never slide the hold onto a blade.
- **A weapon not held by a handle** (katar, shuriken) goes on the fist's middle, as in step 5; the shuriken is held
  at the star's centre and turned so the star clears the leg at idle — measure that with the body's vertices.
- **Name it `<weapon type>_<Kashira>`** (`sword_veyra`, `wand_seraphine`), the Kashira from the story canon's table,
  and point the item at it only after the user's yes on pictures in the hand and from the game camera (S5).
  Renaming later: `AssetDatabase.MoveAsset` for the prefab, material, model folder and files (IDs stay), then the
  mesh name inside the OBJ (the `o` / `g` lines), re-import, and put the renamed mesh back on the prefab.

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

```csharp
var r = new System.Text.StringBuilder();
var meshes = new System.Collections.Generic.List<UnityEngine.Mesh>();
// SEED mesh: sword_1, inside the pack that is already in the game
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/0_Raw/TossedSalads/LowPoly-MedievalWeapons/Meshes/lowPoly_medievalWeapons.fbx")) if (o is UnityEngine.Mesh m0 && m0.name == "sword_1") meshes.Add(m0);
// NEW PACK meshes
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("<PACK FBX PATH>")) if (o is UnityEngine.Mesh m2) meshes.Add(m2);
const int N = 24;
foreach (var m in meshes) {
  var v = m.vertices; var b = m.bounds;
  var mnx = new float[N]; var mxx = new float[N]; var mnz = new float[N]; var mxz = new float[N]; var cnt = new int[N];
  for (int i=0;i<N;i++){mnx[i]=mnz[i]=1e9f;mxx[i]=mxz[i]=-1e9f;}
  foreach (var p in v) { int s = UnityEngine.Mathf.Clamp((int)((p.y - b.min.y) / b.size.y * N), 0, N-1); cnt[s]++; mnx[s]=UnityEngine.Mathf.Min(mnx[s],p.x); mxx[s]=UnityEngine.Mathf.Max(mxx[s],p.x); mnz[s]=UnityEngine.Mathf.Min(mnz[s],p.z); mxz[s]=UnityEngine.Mathf.Max(mxz[s],p.z); }
  r.Append(m.name + " ymin " + b.min.y.ToString("F3") + " ymax " + b.max.y.ToString("F3") + " | ");
  for (int i=0;i<N;i++) r.Append(cnt[i]==0 ? "-- " : ((mxx[i]-mnx[i]).ToString("F2") + "/" + (mxz[i]-mnz[i]).ToString("F2") + " "));
  r.AppendLine();
}
return r.ToString();
```

Each column is one slice, low end first: `width across / width through`, `--` = no vertex in that slice (low-poly
meshes leave many empty). Slice of the centre point = `(0 - ymin) / (ymax - ymin) × 24`.

## Build script

```csharp
const string W = "Assets/1_Authored/Prefabs/Weapons/";   // where the new prefabs go
const string OUT = W;                                      // where the new prefabs + material are written
const string FBX = "<PACK FBX PATH>";
const string TEX = "<PACK TEXTURE PATH>";
bool BOW_FLIP = false;                      // decided from the pictures, per pack
var seedHandle = Vector3.zero;              // seed sword handle middle in its own mesh space, from the profile
var packGrip = Vector3.zero;                // the NEW pack's grip point in its own mesh space, from the profile
const string PACK_SWORD = "<new pack's sword mesh name>";   // sets the one scale for the whole pack
var picks = new[]{"<new pack mesh name>", "..."};
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name;}
var log = new System.Text.StringBuilder();
var matPath = OUT + "Materials/<material name>.mat";
var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
if (mat == null) {
  mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
  mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TEX));
  mat.SetFloat("_Smoothness", 0.5f); mat.SetFloat("_Metallic", 0f);     // the seed sword's material values
  AssetDatabase.CreateAsset(mat, matPath);
}
var meshes = new System.Collections.Generic.Dictionary<string, Mesh>();
foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FBX)) if (o is Mesh m) meshes[m.name] = m;
// the seed sword: its saved grip (see "How the game mounts a weapon") on the raw pack mesh sword_1
Mesh seedMesh = null;
foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/0_Raw/TossedSalads/LowPoly-MedievalWeapons/Meshes/lowPoly_medievalWeapons.fbx")) if (o is Mesh m0 && m0.name == "sword_1") seedMesh = m0;
var seedPos = new Vector3(0.011f, 0.18756053f, 0.12156433f); var seedScale = Vector3.one * 0.8735323f;
var Rs = Quaternion.Euler(345.37842f, 86.71383f, 282.81427f);
float seedLen = seedMesh.bounds.size.y * seedScale.y;
var G = seedPos + Rs * Vector3.Scale(seedScale, seedHandle);
float k = seedLen / meshes[PACK_SWORD].bounds.size.y;
G -= Rs * (packGrip * k);
var bowPos = new Vector3(-0.024f, 0.226f, -0.005f);
var Rb = Quaternion.Euler(358.83505f, 51.418995f, 283.19495f) * (BOW_FLIP ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
foreach (var name in picks) {
  var path = OUT + name + "_<colour>.prefab";
  var oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
  var oldGrip = oldPrefab != null ? oldPrefab.GetComponent<WeaponGrip>() : null;   // read BEFORE it is overwritten
  var root = new GameObject(name + "_<colour>");
  root.AddComponent<MeshFilter>().sharedMesh = meshes[name];
  root.AddComponent<MeshRenderer>().sharedMaterial = mat;
  var g = root.AddComponent<WeaponGrip>();
  if (name.StartsWith("bow")) { g.hand = WeaponHand.Left; g.localEuler = Rb.eulerAngles; g.localPosition = bowPos; }
  else { g.hand = WeaponHand.Right; g.localEuler = Rs.eulerAngles; g.localPosition = G; }
  g.localScale = Vector3.one * k;
  if (oldGrip != null) {   // 5b: keep where it rests on the body — all six, the "set" flag included
    g.stowAuthored = oldGrip.stowAuthored; g.stowBone = oldGrip.stowBone;
    g.stowPosition = oldGrip.stowPosition; g.stowEuler = oldGrip.stowEuler;
    g.stowPairPosition = oldGrip.stowPairPosition; g.stowPairEuler = oldGrip.stowPairEuler;
    log.AppendLine("  put-away kept: set=" + oldGrip.stowAuthored);
  }
  PrefabUtility.SaveAsPrefabAsset(root, path);      // same path again keeps the prefab's ID
  UnityEngine.Object.DestroyImmediate(root);
  log.AppendLine(path);
}
AssetDatabase.SaveAssets();
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log.AppendLine("after " + sc.name + " dirty=" + sc.isDirty);}
return log.ToString();
```

## Put-away carry-over script (5b)

Run with `WRITE = false` first: it prints the new place for the picture. Rerun with `WRITE = true` after the user's
yes. The scale is the grip's own: `WeaponSocket` keeps the weapon's world size on the body bone, and hand and hips
measured the same scale on the default and all three Knight bodies (2026-10-09), so the grip scale is the size there.

```csharp
bool WRITE = false;
const string W = "Assets/1_Authored/Prefabs/Weapons/";
const string OLD = W + "<model the item uses today>.prefab";
const string NEW = W + "<its replacement>.prefab";
var oldGripPoint = Vector3.zero;   // OLD model's grip point in its own mesh space (step 4 / S3)
var newGripPoint = Vector3.zero;   // NEW model's grip point in its own mesh space
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name;}
var o = AssetDatabase.LoadAssetAtPath<GameObject>(OLD).GetComponent<WeaponGrip>();
var nPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NEW); var n = nPrefab.GetComponent<WeaponGrip>();
if (!o.stowAuthored) return "OLD has no put-away place: NEW stays not set (hidden while put away) - its spot is a separate job";
// Keep the turn; move the position so NEW's grip point lands where OLD's sat. A pair's other copy is mirrored (-x).
Vector3 Mirror(Vector3 s) => new Vector3(-s.x, s.y, s.z);
Vector3 Shift(Vector3 pos, Vector3 euler, Vector3 sOld, Vector3 sNew) {
  var R = Quaternion.Euler(euler);
  return pos + R * Vector3.Scale(sOld, oldGripPoint) - R * Vector3.Scale(sNew, newGripPoint);
}
var pos = Shift(o.stowPosition, o.stowEuler, o.localScale, n.localScale);
var pairPos = Shift(o.stowPairPosition, o.stowPairEuler, Mirror(o.localScale), Mirror(n.localScale));
var log = "bone " + o.stowBone + "  main " + pos.ToString("F5") + " " + o.stowEuler.ToString("F2")
        + "  pair " + pairPos.ToString("F5") + " " + o.stowPairEuler.ToString("F2");
if (!WRITE) return "DRY RUN " + log;
var so = new SerializedObject(n);
so.FindProperty("stowAuthored").boolValue = true;
so.FindProperty("stowBone").intValue = (int)o.stowBone;
so.FindProperty("stowPosition").vector3Value = pos;
so.FindProperty("stowEuler").vector3Value = o.stowEuler;
so.FindProperty("stowPairPosition").vector3Value = pairPos;
so.FindProperty("stowPairEuler").vector3Value = o.stowPairEuler;
so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(nPrefab); AssetDatabase.SaveAssets();
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "  after " + sc.name + " dirty=" + sc.isDirty;}
return "WROTE " + log;
```

Then read the new prefab's file back and diff it: the six put-away lines must hold the printed values and nothing
else may have changed.

`packGrip` stays zero when the new pack's centre point is the grip point (step 4); the bow line assumes the same
for bows. A sword-shaped list (`picks`) can hold any melee shape — the pack's own size differences carry through `k`.

## Picture script

```csharp
var outDir = @"<SCRATCHPAD>\";
var W = "Assets/1_Authored/Prefabs/Weapons/";
var sheets = new[] {      // prefab names; keep a reference row: the sword and bow the items use today
  new[]{"sword_veyra","<new prefab>","<new prefab>"},
  new[]{"bow_celestia","<new bow prefab>"} };
var log = new System.Text.StringBuilder();
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var ctrl = CharacterSpec.Active.animatorController;
AnimationClip Clip(string n) { foreach (var c in ctrl.animationClips) if (c.name == n) return c; return null; }
const int S = 320;
for (int si = 0; si < sheets.Length; si++) {
  var rows = sheets[si];
  var sheet = new Texture2D(S * 3, S * rows.Length, TextureFormat.RGBA32, false);
  for (int ri = 0; ri < rows.Length; ri++) {
    var wp = AssetDatabase.LoadAssetAtPath<GameObject>(W + rows[ri] + ".prefab");
    var grip = wp.GetComponent<WeaponGrip>();
    bool bow = rows[ri].ToLower().Contains("bow");
    var poses = new[]{ ("Idle2", 0.3f, false), (bow ? "AttackBow" : "AttackSword2H", 0.45f, false), ("Idle2", 0.3f, true) };
    for (int ci = 0; ci < 3; ci++) {
      var pru = new PreviewRenderUtility();
      GameObject rig = null;
      try {
        rig = (GameObject)UnityEngine.Object.Instantiate(model);
        rig.hideFlags = HideFlags.HideAndDontSave;
        float h = Character3DBody.MeasureHeight(rig);
        rig.transform.localScale = Vector3.one * (CharacterSpec.Active.bodyHeight / h);
        var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>();
        anim.avatar = avatar; anim.applyRootMotion = false;
        var hand = HandSocketResolver.Find(anim, grip.hand);
        var w = (GameObject)UnityEngine.Object.Instantiate(wp);
        w.hideFlags = HideFlags.HideAndDontSave;
        w.transform.SetParent(hand, false);
        w.transform.localPosition = grip.localPosition; w.transform.localRotation = Quaternion.Euler(grip.localEuler); w.transform.localScale = grip.localScale;
        pru.AddSingleGO(rig);
        var clip = Clip(poses[ci].Item1);
        var hph = new HumanPoseHandler(avatar, rig.transform);
        var hp = new HumanPose(); hph.GetHumanPose(ref hp);
        var idx = new System.Collections.Generic.Dictionary<string, int>();
        for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
        float t = clip.length * poses[ci].Item2;
        foreach (var b in AnimationUtility.GetCurveBindings(clip)) {
          if (b.type != typeof(Animator)) continue;
          var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
          if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
        }
        hph.SetHumanPose(ref hp); hph.Dispose();
        var bounds = new Bounds(rig.transform.position + Vector3.up * CharacterSpec.Active.bodyHeight * 0.5f, Vector3.zero);
        foreach (var rr in rig.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(rr.bounds);
        pru.camera.fieldOfView = 30; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 100f;
        pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
        pru.lights[0].intensity = 1.4f; pru.lights[0].transform.rotation = Quaternion.Euler(35, 150, 0);
        pru.lights[1].intensity = 0.8f;
        pru.ambientColor = new Color(0.6f, 0.6f, 0.6f);
        Vector3 dir = (rig.transform.forward + rig.transform.right * 0.7f + Vector3.up * 0.25f).normalized;
        Vector3 target; float dist;
        if (poses[ci].Item3) { target = hand.position; dist = CharacterSpec.Active.bodyHeight * 0.9f; }
        else { target = bounds.center; dist = bounds.extents.magnitude / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.05f; }
        pru.camera.transform.position = target + dir * dist;
        pru.camera.transform.LookAt(target);
        pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
        UnityEngine.Object.DestroyImmediate(pru.EndStaticPreview());          // first render can be empty
        pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
        var tex = pru.EndStaticPreview();
        var rt = RenderTexture.GetTemporary(S, S, 0); Graphics.Blit(tex, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        sheet.ReadPixels(new Rect(0, 0, S, S), ci * S, (rows.Length - 1 - ri) * S);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(tex);
      } catch (System.Exception e) { log.AppendLine(rows[ri] + " c" + ci + " ERR " + e.Message); }
      finally { pru.Cleanup(); if (rig) UnityEngine.Object.DestroyImmediate(rig); }
    }
  }
  sheet.Apply();
  System.IO.File.WriteAllBytes(outDir + "sheet" + si + ".png", sheet.EncodeToPNG());
  UnityEngine.Object.DestroyImmediate(sheet);
}
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log.AppendLine("after " + sc.name + " dirty=" + sc.isDirty);}
return log.ToString();
```

Columns: standing, mid-attack, close-up of the hand (that close-up is framed from behind the wrist; for a clear view
of the handle in the fist, set every muscle to 0 instead — the T-pose close-ups are the easiest to judge). The model
draws black in these pictures because of the preview lighting; only the weapon and the hand matter here.
