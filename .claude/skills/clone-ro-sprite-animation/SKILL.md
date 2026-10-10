---
name: clone-ro-sprite-animation
description: ALWAYS use when making or changing a character ANIMATION — an attack, a skill release, a stance, any body motion for the player or a creature. Our standard is to clone the matching Ragnarok Online sprite animation onto our 3D characters by hand-posing them frame by frame. Covers finding the sprite sheet, reading two views to get each limb's 3D direction, the build script that poses the default character and saves a humanoid clip, pointing the held weapon along the swing, the sprite-over-ours picture that proves the match, the sample videos for the user, and the chibi-body limits.
---

# Cloning a Ragnarok Online sprite animation onto our 3D characters

**The standard (set by the user on 2026-10-09):** every new character animation takes the matching Ragnarok Online
sprite animation as its reference and is built by posing our default character in each sprite frame. First proven on
the Katar attack (`Assets/1_Authored/Art/Characters/Animations/AttackKatar.anim`), which the user judged "much better"
than Kimodo (NVIDIA text-to-motion, installed at `D:\kimodo`). Kimodo text prompts never landed a short, exact move;
keep it only for long, flowing full-body motion, and fit its result to our body afterwards.

Everything runs through the live editor link (`eval_file`). This skill's folder holds the proven scripts
(`point-weapon-build.cs` + `point-weapon-gen.py`, `render-video.cs` + `make-videos.py`) and every key table made so far
(`keys/`); filled copies go in the scratchpad. Nothing here touches a scene.

## 1. Get the reference

- The Spriters Resource lists every class sheet: `https://www.spriters-resource.com/pc_computer/ragnarokonline/`
  (e.g. male Assassin = asset `141282`). The asset page names the image URL, of the form
  `https://www.spriters-resource.com/media/assets/138/141282.png?updated=...` — download it with `curl -A "Mozilla/5.0"`
  into its own new folder in the scratchpad.
- Look at the whole sheet with Read, then crop and enlarge (`ffmpeg -vf crop=W:H:X:Y,scale=...:flags=neighbor`) the
  rows that hold the motion. **Ask the user which rows they mean if there is any doubt** — a sheet holds several
  directions of several actions, and the user's pick settles it in one message.
- A sheet shows 5 of RO's 8 directions; the other 3 are its mirror. So the sheet's left/right hand order can be the
  mirror of what the user describes. If the user names the order ("right first"), build that and mirror the
  comparison picture (step 4); say so in the report.

## 2. Read the motion in 3D — always from TWO views

One view is not enough: on 2026-10-09 a slash read from the front view alone came out aimed at the camera and vanished
into the body. Take two rows of the same action (e.g. front-left and back) and work out each limb's direction:

- Fix a world frame: camera looks north (into the screen), screen-right = east, toward the camera = south. Name each
  row's facing (front-left row = facing south-west, back row = facing north-west).
- For each key frame, a limb drawn toward screen-right AND lower in the front-left view, and toward screen-left in the
  back view, is the character's LEFT arm out to its left side (south-east in one, south-west in the other). Check every
  limb against both rows before writing it down.
- Write each key frame as: hips forward/down (share of body height), forward lean (deg), torso turn (deg), each foot's
  ground position, and each arm as a DIRECTION from its own shoulder (x = right, y = up, z = forward) plus a REACH
  (share of the arm's full length). **Reach sets the elbow's bend, and it bends fast:** measured on the default
  body (2026-10-10), 0.97 leaves the elbow at 151° — visibly bent — and 1.0 gives 175°, a straight arm. When the
  user asks for a straight arm, key 1.0 and measure the elbow angle in the built clip before saying it is straight
  (the 0.97 dagger arm was reported "straight" unmeasured, and the user's video frame showed it bent).

## 3. The chibi limits — measured on the default player, 2026-10-08

As shares of body height: shoulders sit 10.2% from the body's centre, the head is 15.7% wide on each side, a fully
raised wrist reaches 87.5%, the head's top is at 99%. So **any arm raised straight up is hidden behind the head from
the front**. Raise arms up AND out to the side (direction like `(-0.7, 0.7, 0)`), never straight up. Arms held at
chest height or out to the sides are always visible.

**The arm that is not attacking stays in a guard in front of the chest** — direction like `(0.3, -0.35, 0.75)` from its
shoulder at reach `0.5` (mirror x for the right arm). Never aim it behind the body: on 2026-10-09 the Dagger and Fist
free arms aimed backward came out with the elbow folded the wrong way, which the user saw as a broken arm. Check with
an elbow pass: at every stored pose, a bent elbow should point down, back or outward — never forward-and-inward.

## 4. Build the clip — the build script

Every key table made so far is saved in `keys/` beside this file, one JSON per weapon (the hand-read poses of the 12
attack clips — read them before making a new one; `f` frame, `hipF`/`hipD`/`lean`/`yaw`, `rf`/`lf` feet, `rd`/`rr` and
`ld`/`lr` arms, `rw`/`re` and `lw`/`le` weapon directions; at the top `twoHand`, `secondGrip`, `rifle` — see 4b). A new attack gets its own JSON there.
Fill the `keys` table from step 2 (frame numbers counted at 30 per second; the Katar's keys sit at 0/5/9/15/18/23, 0.77 s),
set `DST`, run with `timeout: 120000`. It poses the default character from the `Idle` clip, moves the hips, plants
the feet with a two-bone solve, aims each wrist by direction + reach from its shoulder (so arms stay full length and
visible), eases each segment out (fast start, soft stop — how a sprite move snaps), and records every frame as
humanoid muscles plus body position and rotation. Re-running overwrites the same asset (its ID is kept). It does NOT
turn the hand, so it is only for moves with nothing in the hand — an attack holding a weapon goes through step 4b.

```csharp
var log = new System.Text.StringBuilder();
const string DST = "Assets/1_Authored/Art/Characters/Animations/<ClipName>.anim";
const string NAME = "<ClipName>";
const int FPS = 60;   // poses stored per second; the key table's frame numbers stay at 30 per second
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
AnimationClip idle = null;
foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/1_Authored/Art/Characters/Animations/Idle.fbx")) if (o is AnimationClip c && !c.name.StartsWith("__preview")) { idle = c; break; }
float H = CharacterSpec.Active.bodyHeight;
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar; anim.applyRootMotion = false;
var hph = new HumanPoseHandler(avatar, rig.transform);
Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
Vector3 W(float x, float y, float z) => rig.transform.position + rig.transform.right * x * H + Vector3.up * y * H + rig.transform.forward * z * H;
void PoseIdle() {
  var hp = new HumanPose(); hph.GetHumanPose(ref hp); float ry = hp.bodyPosition.y;
  foreach (var b in AnimationUtility.GetCurveBindings(idle)) {
    if (b.type != typeof(Animator)) continue;
    float v = AnimationUtility.GetEditorCurve(idle, b).Evaluate(0f);
    if (b.propertyName == "RootT.y") { ry = v; continue; }
    var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
    if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
  }
  hp.bodyPosition = new Vector3(0, ry, 0); hp.bodyRotation = Quaternion.identity;
  hph.SetHumanPose(ref hp);
}
void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 poleHint, bool keepWorldEnd) {
  var keepW = c.rotation; var keepL = c.localRotation;
  float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
  Vector3 at = target - a.position; float d = Mathf.Clamp(at.magnitude, 1e-5f, (la + lb) * 0.999f);
  Vector3 dir = at.normalized;
  Vector3 bend = poleHint - Vector3.Dot(poleHint, dir) * dir;
  if (bend.sqrMagnitude < 1e-10f) bend = b.position - a.position - Vector3.Dot(b.position - a.position, dir) * dir;
  bend.Normalize();
  float A = Mathf.Acos(Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1f, 1f));
  Vector3 wantB = a.position + dir * Mathf.Cos(A) * la + bend * Mathf.Sin(A) * la;
  a.rotation = Quaternion.FromToRotation(b.position - a.position, wantB - a.position) * a.rotation;
  b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
  if (keepWorldEnd) c.rotation = keepW; else c.localRotation = keepL;
}
// One row per key frame. idle = the standing idle pose. Units: body height; x right, y up, z forward.
// rd/ld: shoulder→wrist direction; rr/lr: share of full arm length. These are the Katar attack's keys.
var keys = new[] {
  new { f = 0,  idle = true,  hipF = 0f,    hipD = 0f,    lean = 0f,  yaw = 0f,   rf = Vector3.zero,                 lf = Vector3.zero,                   rd = Vector3.zero,                    rr = 0f,    ld = Vector3.zero,                      lr = 0f },
  new { f = 5,  idle = false, hipF = 0.05f, hipD = 0.07f, lean = 22f, yaw = -20f, rf = new Vector3(0.12f, 0, 0.18f), lf = new Vector3(-0.14f, 0, -0.08f), rd = new Vector3(-0.6f, -0.3f, 0.7f),  rr = 0.6f,  ld = new Vector3(0.5f, -0.4f, 0.7f),    lr = 0.55f },
  new { f = 9,  idle = false, hipF = 0.09f, hipD = 0.10f, lean = 38f, yaw = 10f,  rf = new Vector3(0.16f, 0, 0.22f), lf = new Vector3(-0.16f, 0, -0.10f), rd = new Vector3(1f, -0.05f, 0.1f),    rr = 0.97f, ld = new Vector3(0.4f, -0.4f, 0.7f),    lr = 0.55f },
  new { f = 15, idle = false, hipF = 0.09f, hipD = 0.10f, lean = 36f, yaw = 12f,  rf = new Vector3(0.16f, 0, 0.22f), lf = new Vector3(-0.16f, 0, -0.10f), rd = new Vector3(1f, -0.05f, 0f),      rr = 0.97f, ld = new Vector3(0.4f, -0.4f, 0.7f),    lr = 0.55f },
  new { f = 18, idle = false, hipF = 0.09f, hipD = 0.09f, lean = 15f, yaw = -15f, rf = new Vector3(0.16f, 0, 0.22f), lf = new Vector3(-0.16f, 0, -0.10f), rd = new Vector3(1f, 0.05f, 0f),       rr = 0.97f, ld = new Vector3(-0.7f, 0.7f, 0.05f),   lr = 0.97f },
  new { f = 23, idle = false, hipF = 0.09f, hipD = 0.09f, lean = 14f, yaw = -16f, rf = new Vector3(0.16f, 0, 0.22f), lf = new Vector3(-0.16f, 0, -0.10f), rd = new Vector3(1f, 0.05f, 0f),       rr = 0.97f, ld = new Vector3(-0.65f, 0.75f, 0.05f), lr = 0.97f },
};
Vector3 Local(Vector3 w) { var d = w - rig.transform.position; return new Vector3(Vector3.Dot(d, rig.transform.right), d.y, Vector3.Dot(d, rig.transform.forward)) / H; }
Vector3 Dir(Vector3 d) => (rig.transform.right * d.x + Vector3.up * d.y + rig.transform.forward * d.z).normalized;
Vector3 ToChar(Vector3 w) => new Vector3(Vector3.Dot(w, rig.transform.right), w.y, Vector3.Dot(w, rig.transform.forward));
float ArmLen(HumanBodyBones u, HumanBodyBones l, HumanBodyBones h) => (B(l).position - B(u).position).magnitude + (B(h).position - B(l).position).magnitude;
PoseIdle();
var iRF = Local(B(HumanBodyBones.RightFoot).position); var iLF = Local(B(HumanBodyBones.LeftFoot).position);
float groundY = Mathf.Min(B(HumanBodyBones.RightFoot).position.y, B(HumanBodyBones.LeftFoot).position.y);
float armR = ArmLen(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
float armL = ArmLen(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
var iRV = B(HumanBodyBones.RightHand).position - B(HumanBodyBones.RightUpperArm).position; var iLV = B(HumanBodyBones.LeftHand).position - B(HumanBodyBones.LeftUpperArm).position;
var iRD = ToChar(iRV).normalized; float iRR = iRV.magnitude / armR; var iLD = ToChar(iLV).normalized; float iLR = iLV.magnitude / armL;
float EaseOut(float t) => 1f - Mathf.Pow(1f - t, 3f);
int N = keys[keys.Length - 1].f * FPS / 30 + 1;
var musc = new float[N, HumanTrait.MuscleCount]; var bpos = new Vector3[N]; var brot = new Quaternion[N];
float worstHand = 0;
for (int i = 0; i < N; i++) {
  float fi = i * 30f / FPS;   // where this stored pose falls, in the key table's 30-per-second frames
  int k = 0; while (k < keys.Length - 2 && fi > keys[k + 1].f) k++;
  var A = keys[k]; var Bk = keys[k + 1];
  float t = EaseOut(Mathf.Clamp01((fi - A.f) / (float)(Bk.f - A.f)));
  var aRF = A.idle ? iRF : A.rf; var aLF = A.idle ? iLF : A.lf; var bRF = Bk.idle ? iRF : Bk.rf; var bLF = Bk.idle ? iLF : Bk.lf;
  var aRD = A.idle ? iRD : A.rd.normalized; var aLD = A.idle ? iLD : A.ld.normalized; float aRR = A.idle ? iRR : A.rr; float aLR = A.idle ? iLR : A.lr;
  var rf = Vector3.Lerp(aRF, bRF, t); var lf = Vector3.Lerp(aLF, bLF, t);
  if (k == 0) rf.y += Mathf.Sin(t * Mathf.PI) * 0.05f;   // a foot stepping out lifts on the way (edit per motion)
  var rd = Vector3.Slerp(aRD, Bk.rd.normalized, t); var ld = Vector3.Slerp(aLD, Bk.ld.normalized, t);
  float rr = Mathf.Lerp(aRR, Bk.rr, t), lr = Mathf.Lerp(aLR, Bk.lr, t);
  float hipF = Mathf.Lerp(A.hipF, Bk.hipF, t), hipD = Mathf.Lerp(A.hipD, Bk.hipD, t), lean = Mathf.Lerp(A.lean, Bk.lean, t), yaw = Mathf.Lerp(A.yaw, Bk.yaw, t);
  PoseIdle();
  var hips = B(HumanBodyBones.Hips);
  hips.position += rig.transform.forward * hipF * H - Vector3.up * hipD * H;
  hips.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(lean, rig.transform.right) * hips.rotation;
  var fwd = rig.transform.forward; var right = rig.transform.right;
  TwoBone(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), W(rf.x, 0, rf.z) + Vector3.up * (groundY - rig.transform.position.y + rf.y * H), fwd, true);
  TwoBone(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), W(lf.x, 0, lf.z) + Vector3.up * (groundY - rig.transform.position.y + lf.y * H), fwd, true);
  var rT = B(HumanBodyBones.RightUpperArm).position + Dir(rd) * rr * armR;
  var lT = B(HumanBodyBones.LeftUpperArm).position + Dir(ld) * lr * armL;
  TwoBone(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), rT, -Vector3.up - fwd * 0.5f + right * 0.3f, false);
  TwoBone(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), lT, -Vector3.up - fwd * 0.5f - right * 0.3f, false);
  worstHand = Mathf.Max(worstHand, Mathf.Max((B(HumanBodyBones.RightHand).position - rT).magnitude, (B(HumanBodyBones.LeftHand).position - lT).magnitude));
  var hp = new HumanPose(); hph.GetHumanPose(ref hp);
  for (int m = 0; m < HumanTrait.MuscleCount; m++) musc[i, m] = hp.muscles[m];
  bpos[i] = hp.bodyPosition; brot[i] = hp.bodyRotation;
}
var clip = new AnimationClip { frameRate = FPS };
float dt = 1f / FPS;
// No curve may overshoot a stored pose: between two equal poses it stays flat, even right before a big jump.
AnimationCurve Smooth(AnimationCurve c) { for (int j = 0; j < c.length; j++) { AnimationUtility.SetKeyLeftTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); } return c; }
foreach (var b in AnimationUtility.GetCurveBindings(idle)) {
  if (b.type != typeof(Animator)) continue;
  var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
  if (!idx.TryGetValue(nm, out var mi)) continue;
  var cv = new AnimationCurve(); for (int i = 0; i < N; i++) cv.AddKey(i * dt, musc[i, mi]);
  AnimationUtility.SetEditorCurve(clip, b, Smooth(cv));
}
string[] tn = { "RootT.x", "RootT.y", "RootT.z" }; string[] qn = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
for (int q = 0; q < 3; q++) { var cv = new AnimationCurve(); for (int i = 0; i < N; i++) cv.AddKey(i * dt, bpos[i][q]); AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), tn[q]), Smooth(cv)); }
for (int q = 0; q < 4; q++) { var cv = new AnimationCurve(); for (int i = 0; i < N; i++) cv.AddKey(i * dt, brot[i][q]); AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), qn[q]), Smooth(cv)); }
// THE BODY'S MOVEMENT IS PART OF THE POSE. The game's characters never apply root motion, so a clip that leaves the
// body's travel, height or turn as root motion has them thrown away in game — the body stays put and a planted foot
// drags backward (2026-10-09). Every clip the game ships bakes all three in, as the imported ones do.
var settings = AnimationUtility.GetAnimationClipSettings(clip);
settings.loopBlendOrientation = true; settings.loopBlendPositionY = true; settings.loopBlendPositionXZ = true;
settings.keepOriginalOrientation = true; settings.keepOriginalPositionY = true; settings.keepOriginalPositionXZ = true;
settings.heightFromFeet = false;
AnimationUtility.SetAnimationClipSettings(clip, settings);
var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DST);
if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = NAME; EditorUtility.SetDirty(existing); }
else AssetDatabase.CreateAsset(clip, DST);
AssetDatabase.SaveAssets();
hph.Dispose(); UnityEngine.Object.DestroyImmediate(rig);
return "frames=" + N + " worst wrist miss=" + (worstHand / H * 100f).ToString("F1") + "% of body height";
```

A worst wrist miss above 0% means a target is out of reach: lower that key's reach or bring the direction in.

## 4b. Point the weapon — every attack that holds one

The script above places the wrist but never turns the hand, so the weapon points wherever the resting wrist aims —
on 2026-10-09 the user saw a spear thrust with the spear pointing back at the ground. Weapon attacks are built with
`point-weapon-build.cs` (in this skill's folder), filled from `keys/` by `point-weapon-gen.py`
(`python point-weapon-gen.py <out .cs> <write 0|1> <aim 0|1> [weapon ...]`, then `eval_file` the out file with
`timeout: 600000`).

The weapon is NOT cloned from the sprite: the Swordsman sheet the sword attack was compared against draws the body only,
with no sword in it. Its direction comes from the rule below, and the user judges it on the videos (step 5).

- **Weapon direction per key:** the tip continues the arm's direction (`rd`, or `ld` for a left-hand weapon) unless the
  key carries `"rw": [x, y, z]` — a thrust keeps the tip on the target the whole way (spear, dagger). An idle key may
  carry one too, so the weapon starts the attack from a set direction (the spear starts level, the staff upright).
- **The edge never rolls in the air:** the striking side (axe edge, blade edge) faces along the WHOLE cut — the travel
  of the clip's biggest swing decides it once, for every key, unless a key carries `"re"`. The first version faced the
  edge along each segment separately, so a raised sword rolled in mid-air; the user: "it must be consistent sharp edge
  facing where it will slash".
- **Weapon axes:** every weapon model lies along y. The fist closes at `FIST_GRIP` (the hand-bone point every weapon
  is fitted to), wherever along the model that falls; the tip is the end that reaches further from it. The striking
  side (edge, head, bow front) is on -x unless the model is listed in `EDGE` at the top of the build script with its
  own direction. **The Trellis models (2026-10-10) do not all keep -x:** the axe's edge is on +z and the bow's front
  on +x, carried from the models they replaced; the scythe's blade is on -x like the rest. Before building any key
  table for a new model, render the mesh alone from -z (screen right = +x) and look, and confirm it on the vertices
  (where the shaft's middle sits against the furthest point on each side); a model listed on the wrong side gets
  every `re` aimed at its back, and the dry run still reports a clean 0° — or, where the wrist cannot turn that far,
  an edge error near 150° (the scythe's, while it was listed on +x).
- **Where the fist holds a long stick is the weapon prefab's grip** (`import-weapon-models` skill), not the animation:
  the staff and the wand are held a third of the way up from their plain end (user, 2026-10-09). Held at the middle,
  their lower half went through the legs in the attack's first frame.
- **The weapon may not pass through the body.** The body is capsules sized from the character's own mesh (each bone's
  95th-percentile distance — the 80th left the head and thighs too thin), plus a head sphere; the arms are left out,
  since a held weapon lies against its own arm. When the planned direction sinks the shaft (butt to tip) into a capsule,
  the solver tilts the weapon up to 90° off the plan, 8 ways, and takes the least-sunk direction closest to the plan. The
  report then counts, from the real baked mesh, weapon points inside the torso, head or legs per frame and by body part —
  the honest check, since capsules are only an estimate. **The count belongs to the model the clip was built with:**
  when a weapon's model, size or hold changes, play every SAVED clip with the new model and count again —
  `check-clip-fit.cs` does it (body overlap per frame, lowest point against the ground, and each handover's jump;
  include `Idle2`, `Walk` and `Run2`, where the weapon hangs in the hand) (the capsules can also
  talk a rebuild out of a pose that is clean on the real mesh — the katar idle and put-away, 2026-10-10). Measured
  with the Trellis models on 2026-10-10, as a share of the weapon's points: sword, dagger, warhammer, spear, katar and
  wand none; staff 0.9% in one frame; the axe 3.7%, over 0.5% in 17 of 25 frames (its model was doubled in size that
  day and the attack not rebuilt); shuriken 6.8% and the rifle 7.3% in the first frame (the hanging
  arm), the scythe 0.3% there.
- **A weapon longer than the reach needs its swing in the right plane, not just tilt.** The staff is 1.3 times body
  height; held a third of the way up, its butt reaches 0.44 of body height behind the fist, into the chest whenever the
  tip points away. Its swing stays in an upright plane to the right of the body: raised behind the shoulder, brought
  down level forward (keys in `keys/staff.json`). No tilt could fix a swing across the front. The wand
  (`wand_seraphine`, 1.17 times body height, butt 0.39) takes the same right arm and wand directions under its own
  body, feet, left arm and timing (`keys/wand.json`): its raise-and-chop had the lower end in the torso in 13 of 21
  frames. Both start with the stick upright in the hanging hand, which puts its lower end 13% of body height under
  the ground in the clip's first frame and 3–7% in the next one or two.
- **Two hands on one weapon:** `"twoHand": true` at the top of a key table (Warhammer and Spear, by the user's call on
  2026-10-09) puts the other hand on the same weapon every frame, thumb toward the tip; that arm's own keys are ignored.
  `"secondGrip"` is where, as a share of body height along the weapon from the first fist: negative toward the butt
  (default -0.11, fists side by side), positive toward the tip (a rifle's front hand). The two hand bones are mirror
  images across their local x. The solver tries every roll around the shaft and reports the second hand's worst gap.
  **Chibi limit:** both hands reach one weapon only in front of the body. A raise over one shoulder or straight overhead
  left the second hand 10–25% of body height off the handle, and a spear wind-up beside the hip 16–20% off. So a
  two-handed wind-up stays in front: the Warhammer upright before the chest (arm `[-0.1, 0.45, 0.9]` at reach 0.6, weapon
  `[0.1, 0.95, -0.3]`), the spear drawn back before the right hip (arm `[0.25, -0.4, 0.5]` at reach 0.75, second hand
  -0.06). A spear's rear hand belongs near the butt: with both fists low between the legs its butt went through the hips.
  **Read the second hand's gap per frame, not its worst:** it is the hand coming over from the resting pose (the
  Warhammer's 7.0% is frame 0; by frame 3 it is 0.5%), and the reported worst hides whether it ever arrives. A weapon
  that continues the arm at the end of a slam ends under the ground (the Warhammer's head does, by 14% of body height).
- **⛔ The WHOLE sets of the Axe, the Katar and the Dagger are LOCKED by the user since 2026-10-10** ("lock these and
  put into the game with complete animations"): attack, idle, running arms, put-away, take-out, the hold and the
  resting place. Same rule as the next line: measure freely, never rework, never offer to.
- **⛔ The Greatsword's attack, the Bow's attack and the Shuriken throw are LOCKED by the user — never build a key
  table, a new clip or a "rework" for them, and never offer one.** The greatsword and the bow keep the imported
  clips (`AttackSword2H`, `AttackBow`). The two changes the user made themselves, and nothing more: the shuriken
  throw's wind-up keeps the hand farther from the head (2026-10-11, `keys/shuriken.json`), and the bow's attack plays
  MIRRORED since the bow moved to the right hand (2026-10-11, the stance entry's `mirrored`). Measuring them is fine; changing them needs the user to lift the lock in so many words. (2026-10-10:
  the greatsword's blade measured inside the body in 11 of 39 frames, a rework was offered among three other fixes,
  the user said "go fix them all", and a new `AttackGreatsword` was built. The user: "i instructed you that we will
  keep the existing greatsword attack as itself, why did you create a new one?" The lock was written in memory the
  whole time; it was never looked up before the offer was made.)
- **A pair weapon (`WeaponTypes.IsDualWield`, today Katar and Axe)** is the same model in both hands — the game's hand
  mounts the mirror copy itself (`WeaponSocket`). The build reads that same list and aims the other hand's copy from that
  arm's keys (`ld`), with `"lw"` / `"le"` to override its direction.
- **A rifle (`"rifle": true`)** is held in the right hand at its trigger grip: the barrel runs along the hand from wrist
  to knuckles, its top is the thumb side, and the stock reaches 0.12 and the barrel 0.5 of body height from the grip.
  The Sniper uses it with `"twoHand"` and `"secondGrip": 0.1` (the left hand on the barrel, 2–3% of body height off it
  through the aim). The build aims that stand-in, not the item's model; `sniper_vael`, the rifle the Sniper item holds
  since 2026-10-10, sits in the hand exactly as the stand-in does (barrel `(0, 1, 0)` and top `(0, 0, -1)` in the hand
  bone's space, measured), and the videos draw the real rifle.
- **The body's movement is baked into the pose** — both build scripts set it when they save. The game's characters
  never apply root motion, so a clip that leaves the body's travel, height or turn as root motion has them thrown
  away in game: on 2026-10-09 every attack's back foot slid in Play while the videos looked perfect, because the
  videos apply the body movement themselves. The asset check fails a character `.anim` that does not bake all three.
- **The wrists turn smoothly** (user, 2026-10-09: "the axe swings are glitched"). Solved one frame at a time, a
  wrist could flip from one end of its range to the other in a sixtieth of a second, and the elbow could jump to the
  far side of the arm. Now the forearm's twist and the wrist's two bends change at most 0.25 of their reach between
  stored poses, and the elbow's swing around the shoulder-to-wrist line at most 20 degrees per frame — neither moves
  the hand off its path, and the elbow prefers the swing nearest last frame's. **The wrists do not shake** (user,
  2026-10-09: "why the left hand is shaking"): where the planned direction sat at the edge of the wrist's reach, two
  neighbouring answers scored the same and the wrist alternated between them — the katar's left wrist reversed 41
  times in 47 poses. After solving, every pose where a wrist joint turns back AND sits more than 0.03 off the line
  between its two neighbours is smoothed toward them until none is left (a real swing's turn is eased, so it sits on
  that line and is kept). The run reports "wrist shakes before -> after" (must end at 0) and how far each weapon then
  points from its plan. The biggest change of any arm joint is reported too; the elbow straightening in the first
  frame out of the resting pose is the approved motion, not a flip. **A swing that must FLOW through several keys** (over the top and down): mark the middle keys `"pass": true` — from the key before the run to the key after it, time is warped once (accelerating, fastest at the run's last key, where it stops dead: a hit) instead of easing to a stop at every key; give the table `"wristStep"` (e.g. 1.0, per pose, default 0.25) if the weapon lags the arm, which shows as the head slowing mid-swing. Measure the weapon head's travel per pose: the warhammer's swing went from one 89%-of-body-height jump (the user: "dont make the hammer teleport") to 20–33% per pose. Keep the hit at the shared hit timing (`CharacterSpec.hitTiming`, 0.70 of the clip). **`"look"` on a key** (degrees, + = to the character's left) turns the neck and head on top of the body's `yaw`, so a body turned side-on keeps its eyes on the enemy (the warhammer's wind-up: `yaw` 70, `look` 70; measured face 3° off straight ahead). **Two hands on one weapon: `"steadyArms": true`**
  (user, 2026-10-10, on the warhammer: "the hands are shaking"). With the second hand chasing the handle, the
  shoulders and elbows alternated too — 66 direction reversals over the arm joints in 41 poses — which the wrist-only
  pass never touched. The flag runs the same pass on every arm joint and then eases each one over its neighbours
  twice (1-2-1); the run reports "arm joint shakes" and the second hand's gap to the handle before -> after (the
  warhammer: 66 -> 22 reversals, gap 4.7% -> 4.5% of body height). Measure a clip's hands with a per-pose turn
  readout before and after, never by eye.
  **A weapon that drifts or lurches in a held pose is aimed at a direction the arm cannot reach — smoothing does not
  cure it** (2026-10-10: `steadyArms` removed every reversal and the warhammer still shook; the user saw it twice).
  Read the build's "tip error mean": the axe's is 3°, the warhammer's was 25° (38° held overhead), so every pose
  settled on a different nearest answer. Fix the PLAN: read where the weapon really points in the built clip at each
  held key (tip and face, character frame) and write those as `rw` / `re` on those keys. The warhammer went to 3° mean
  and its turn now slows to a stop once and speeds up once. A test that tells which limit binds: rebuild in memory
  with the face's share of the score at 0, then with the wrist step lifted — if neither drops the error, it is reach.
- **A weapon's carry pose** (user, 2026-10-09: "idle walk run and other movement per weapon") is ONE still arm pose,
  played on the controller's arms-and-fingers layer over the shared Idle/Walk/Run/Sprint legs, so strides and
  footsteps never change. Ragnarok Online only draws the weapon in attacks and the battle-ready stance, so that
  stance is the reference. Build it with the same script: a key table `keys/<weapon>_carry.json` whose clip is
  `Carry<Weapon>`, two identical non-idle keys (`"stepLift": false`), mapped to its item in `point-weapon-gen.py`.
  Then pick the clip on the weapon's row under "Carry pose by weapon" in the Stance Composer and Save + Apply to 3D.
  The katar's is `CarryKatarGuard` (key table `keys/katar_carryguard.json`): the arms of its combat idle held over
  the run — left arm laid flat across with its blade pointing to the character's right, right arm low, the blades
  crossing.
- **A weapon's standing idle** is a whole-body loop (`"loop": true`, `"smooth": true`) whose table holds ONE key, the
  stance, in `keys/<weapon>_idle.json`, clip `Idle<Weapon>`, picked on the weapon's row in the Stance Composer. The
  breath is not in the table: `point-weapon-gen.py` adds the one every weapon shares (user, 2026-10-10, the dagger's)
  — halfway through a 2.2 s loop the hips rise 3.2% of body height and the torso straightens 4°. A looping table
  with more than one key is refused.
  To put an elbow somewhere exact (user, 2026-10-09: "right elbow slightly close to the right waist"), work the arm
  out from the MEASURED shoulder of that pose: elbow = shoulder + upper arm × its direction, fist = elbow + forearm ×
  the blade direction; key `rd`/`rr` from shoulder to fist, `"rp"` (or `"lp"`) = the elbow's side of the
  shoulder-to-fist line — a key with `rp`/`lp` keeps that elbow where it is instead of swinging it to chase the aim.
  A standing pose has no swing for the edge to face, so give `"re"`/`"le"` too: try the four directions square to the
  blade in one dry run (`write 0`, several tables at once — a table named `<weapon>_<anything>` maps to its weapon)
  and keep the one with 0° tip and edge error. The katar idle's right arm came out exact that way on the fourth try
  after three guessed ones missed by 15–68°.
- **A move that carries the hands somewhere specific (a sheathe to the lower back, 2026-10-09) is keyed every
  2 frames along a hand PATH, not as a few poses.** Between sparse keys the builder swings each arm around its
  shoulder, which carried both hands 10% of body height outside the body and lifted the elbows to shoulder height
  — "arms out like wings", the opposite of "passing the waist". Read the shoulders and hands at the few key poses
  from the built clip, ease the hands along a low path in plain position (and the blade through waypoints, never a
  half-turn in one step, which slerps through the body), work `rd`/`rr` from the eased shoulder to each point, and
  hold one `rp`/`lp` for the whole move. Also: the builder's `"idle": true` is the Idle.fbx pose (fists up), not the
  game's relaxed Idle2 — end a move on explicit hanging-arm keys.
- **A katar is held by its crossbars** (user, 2026-10-09): the fist closes around the two bars inside the frame, the
  side bars lie along the forearm and the blade points straight out past the knuckles — so the blade CONTINUES THE
  FOREARM and no wrist turn can aim it elsewhere. Any key where a forearm points across the body sends that blade
  through the torso: the guard arm is held forward (`[0.1, -0.35, 0.95]` at reach 0.75), never across the chest.
- **One foot rooted, the other steps** (user, 2026-10-09: the back foot slid backward in every attack). The foot the
  table moves forward least — the left, in all 12 attacks — is pinned to where it stood for the whole clip, whatever
  its keys say, and the body travels over it; the other foot lifts on every move (up to 5% of body height, more for a
  longer move) instead of gliding. The run reports how far the rooted foot moves (at most 0.2% of body height on the
  default body). Checked on other bodies by playing the clip on each: Knight and Casual at most 0.4%, the Ninja body
  1.1–2.8% (its legs are proportioned differently).
- **Natural joint range only:** the arm muscles are held to -1…1. Without that, the solver points every weapon exactly
  by twisting the wrist to about 3, which looks like a broken arm. The solver also swings the elbow around the
  shoulder-to-wrist line, which keeps the wrist in place, and refuses any elbow that points forward and inward.
- **Run order:** before editing any key table, run `write 0, aim 1` — every clip must report a difference of 0.0000
  from the saved clip, which proves the tables still match what is saved. Then edit, run `write 0, aim 1` to read the
  errors, then `write 1, aim 1`. To compare several versions of one table side by side, generate one run holding each
  version as its own entry — they all build in memory and report against the same saved clip.
- **Check it in a zoomed picture** of the key frames from two sides: `render-video.cs` with `ZOOM = 0.55f`–`0.7f` — the
  normal framing is too small to see which way a head or blade faces. Remember the 35° camera: a weapon held level and
  pointing forward reads as pointing DOWN-forward on screen.

## 4c. A weapon's whole stance set — combat idle, running arms, put-away and take-out

Agreed with the user on 2026-10-10: every weapon gets the katar's four pieces, videos first, wired in only after the
user's yes on each weapon. The Katar, Dagger, Axe and Greatsword were made one at a time; the other ten (Sword, Staff,
Wand, Spear, Warhammer, Scythe, Bow, Sniper, Shuriken, Fist) in one run at the user's word — all but the Bow and the
Sniper in the game since 2026-10-11 — and their tables are all
written by ONE script, `paths/stance_sets.py` (`python paths/stance_sets.py <folder>` rewrites their `keys/` tables
and writes one filled put-away blend script per weapon into the folder; it prints each handover frame and spin). The
Fist has no model and no put-away clip: an idle and running arms only. Trial tables go in a scratch folder, never in
`keys/`: `KEYS_DIR=<folder> python point-weapon-gen.py …` reads them from there.

1. **Combat idle** `keys/<weapon>_idle.json` → `Idle<Weapon>` (one key, `"loop"`, `"smooth"`; the shared breath is
   added by the generator). The
   reference is the class's RO **battle-ready** frames: on the Thief sheet (asset 141352) they are the first three
   frames of the top-right group (front-left view) and of the row under it (back view) — wide stance, knees bent,
   lean forward, weapon arm forward at chest height, the other arm low. The sprites carry no weapon. Find `re` with
   one dry run of four tables (edge up / down / out / in) and keep a 0° one.
2. **Running arms** `keys/<weapon>_carry.json` → `Carry<Weapon>`: the idle's first key twice (`f` 0 and 4),
   `"stepLift": false`.
3. **Put-away** `<Weapon>Sheathe`. **First test that the hand can reach the resting direction at all:** a static
   two-key table per candidate (wrist spot, elbow side, blade direction), built as a trial clip, then READ where the
   blade really points in it (the plan error alone does not say which way it is off). In this grip the blade is
   square to the forearm, on the thumb side, so a hand by the hip turns it forward, out or in easily and BACK only
   with the hand before the hip and the elbow back: the dagger's back-slant (blade `(0.24, -0.44, -0.87)`) came from
   wrist `[0.15, 0.40, 0.08]`, elbow side `[0, -0.4, -0.9]`; 30 other tries were 38–113° off. A shaft standing
   straight up the back is out of reach for any hand position (the doubled axe: 25–70° off); crossing inward to the
   other shoulder is reachable (an X).
   **Blend only between poses whose ARM JOINTS agree — compare them before blending** (2026-10-10, paid for on the
   sword, the sniper and the warhammer). Two still poses can each be reached exactly and still sit at opposite ends
   of the forearm's twist or the wrist's bend; blended joint by joint, the weapon then swings through the head or the
   ground half way (the sword: 27% of it in the head, its tip 20% of body height under the ground; the rifle: 56%
   under). Read each pose's arm muscles with `arm-joints.cs` (shoulder, arm down-up / front-back / twist, forearm stretch /
   twist, hand down-up / in-out) and, where two neighbours disagree, scan every ROLL of the weapon about its own
   length (`re` turned in 12 steps — a sword's or a rifle's roll does not matter to the eye) and keep the roll whose
   joints match the neighbour's. If no roll matches, the two poses are not joined by any motion of that hold: pick
   another resting place. A keyed table does not rescue it — solved pose by pose it walks into a dead end (the sword's
   slide left its line by 49 degrees). After EVERY blend, play the clip pose by pose with the item's model and read
   the most inside the body, the lowest point and the far end's biggest move in one pose (`check-put-away.cs`).
   **A weapon's reach below the fist decides its path:** the Warhammer's handle (0.64 of body height below the fist)
   cannot stand upright behind the hip or be carried round low (ground, legs) — it goes up beside the shoulder and
   over; a weapon as long as the body is tall (the bow, the rifle) is swung round the OUTSIDE of the right side,
   level, before it goes behind — swung straight there, or over the shoulder, it went through the hips or 30–57% of
   body height into the ground. Widest and cleanest is the arm straight out to the side first, as the Staff's and
   Wand's put-aways do (the user's fix for the bow, 2026-10-11: nothing of the bow enters the body that way). Each middle pose is picked from a scan by `arm-joints.cs`: the one whose joints sit
   between its two neighbours'.
   **A hand cannot press a weapon onto the back — it is between them** (2026-10-11, the user: "dont float it away").
   Scans of 600+ holds found none that leaves a spear, scythe or bow touching the back while the fist still grips it;
   with the shaft on the palm side the weapon goes INTO the body, and the chibi head sticks out 0.1 of body height
   further back than the torso, so a pole behind the head stays ~11% off the middle of the back. A table's `"hug":
   true` stops the build pushing a resting pose off the body (its capsules are wider than the real back: 0.10–0.125 of
   body height against a measured 0.065). Where the hand still cannot get closer, the resting place is the hand's
   place moved straight onto the back, and the weapon makes that small move at the handover — say how far.
   **A weapon moved to the other hand** (the bow, 2026-10-11, the user: "make the bow stance right handed"): its grip
   is mirrored across the hand's x (as a pair weapon's second copy) and turned half round its own length so its scale
   stays positive — for a thin model the same picture as a mirror; its attack clip is played mirrored by the stance
   entry's `mirrored` setting (`render-stance.cs` draws `__MIRRORCLIPS__` the same way); its key tables are the old
   ones mirrored (x negated, the two arms' keys swapped, `yaw`/`look`/`twist` negated).
   **What the game does for a stance set (2026-10-11):** a row's `putAwayMovesUpperBody` plays its put-away on the
   "DrawUpper" layer (arms, body and head) instead of arms only — needed by any table using `"twist"`;
   `spinAboutMiddle` makes the hand slide the weapon to its middle while it spins (`WeaponSocket.MiddleSlide`, which
   `render-stance.cs` and `check-put-away.cs` call too); `handoverVfx` plays an effect where the weapon leaves the
   hand and where it lands at the handover.
   `paths/pose_chain_blend.cs` is the general blend (stops = pose clip, time in it, frame, stops-there; one smooth
   curve between stops; it gives the clip its OWN length — copying another clip's settings whole left a 34-frame
   put-away with a 44-frame stop time). **A put-away is built as long as the game plays it** (0.2 s + 0.006 s per
   point of weight: weight 90 = 44 frames at 60 per second, weight 60 = 34), so its video shows the game's speed.
   **A turn of the torso that must show in game** is a key's `"twist"` (degrees to the character's right, shared by
   the spine joints over still hips; pair it with `"look"` to keep the face forward) — `yaw` turns the hips, which a
   put-away never plays. The sword's put-away uses it, so its stance row plays it on the "DrawUpper" layer
   (`putAwayMovesUpperBody`).
   **Then build it by BLENDING still poses joint by joint, not with a keyed hand path**, whenever the blade must turn
   a long way (front to back, up to across): the key-table solver turns the wrist one pose at a time and loses it —
   tip errors up to 130° and 130 wrist reversals on the axe. A blend of poses the body can really take cannot flip a
   wrist. `paths/axe_sheathe_blend.cs` runs one smooth curve through four poses (idle → hands out beside the hips →
   just behind them → the handover pose) and on to the hanging arm; `paths/dagger_sheathe_blend.cs` appends a keyed
   move (the arm held out for the dagger's spin) to a pose blend. Add a middle pose where the weapon sweeps through the body: the axes still
   cross the hips for 3 frames just before their handover. A short move that keeps the blade's direction (the katar)
   can still use a keyed hand path every 2 frames.
   **A weapon turning over in the hand is never keyed** — the wrist cannot turn a blade all the way round (the
   dagger's keyed roll was 84° off when the blade should point down). The clip only HOLDS the arm; the weapon's
   stance library row (`spinStartFrame`, `spinEndFrame` of the put-away clip, `spinTurns`, `spinLift`) makes the game
   lift the main copy off the fist and turn it about the hand bone's x through the fist point. So the held pose
   decides the plane of the spin: measure the hand's x in the built clip (palm up = a level spin, the best seen from
   the game camera) and give enough lift for the blade to pass over the forearm (dagger: 0.25 hand units). The
   take-out turns it back by itself. `render-stance.cs` and `check-clip-fit.cs` draw the spin from that row.
   **A weapon far longer than the arm cannot spin about the fist** (the Spear and the Scythe, 2026-10-10): in all 36
   holds measured its far end went through the head or the ground. It turns about its own MIDDLE — slid through the
   fist while it turns — in an upright wheel beside the raised right arm (`*PutAwayPoseWheel`, lift 0.3: nothing
   inside the body, lowest point 3.6% / 5.1% of body height above the ground). The stance row's `spinAboutMiddle`
   tells the game's hand to do it; `render-stance.cs` (from the row, or `__SPINROWS__` for a row not saved yet) and
   `check-put-away.cs` call the game's own `WeaponSocket.MiddleSlide`.
   **A put-away and a take-out play on the ARMS ONLY in game** (the Draw layer's mask), over the shared idle or the
   run, and the weapon's own standing idle fades out the moment the move starts. So build every pose of them on the
   STANDING body (lean 0, hips 0) — a pose solved on a leaning idle puts the weapon somewhere else in game than in
   the video. A leaning idle's hold is carried onto the standing body by turning its arm, blade, edge and elbow
   directions back by the idle's lean and turn (`keys/greatsword_hold_upright.json`). The body does not move with
   the arms, so a big flourish with a heavy weapon looks static (the user, on the greatsword's figure-8: "only arms
   is moving while the great sword is too heavy") — keep put-aways simple. The game scrubs the whole clip over the
   weapon's draw time (0.2 s + 0.006 s per point of weight, the same for taking out and putting away), so a clip
   longer than that plays faster — say the in-game seconds in the report; a weapon that needs longer gets more
   WEIGHT (the user's ruling: the greatsword weighs 133 for 1 s). Measure a move's flow with `tip-path.cs` (the far
   end's travel per pose, where the path changes the way it curves, arm joint reversals) — a pause shows as the
   travel dropping toward 0.
4. **Take-out** is the put-away clip played backwards, curve by curve (`reverse-clip.cs`) — never a reversed key
   table: built forward from reversed keys, the solver turned the blade the other way round and put it in the thigh.
   Its handover frame is the clip's length minus the put-away one.
5. **The resting place** is wherever the weapon IS at the put-away's handover frame, relative to the hips:
   `measure-handover.cs` prints it, so the weapon does not jump when the hand lets go. A pair weapon's other copy
   gets its own (`PAIR_HANDOVER60` → `stowPairPos` / `stowPairEul`, mirrored the way the game's hand mounts it).
   It is written to the weapon's grip only after the user approves the videos (the weapon skill's step 5b), with the
   binding's clips and frames.
   **A weapon no hand carries to its place** (the Staff and the Wand: the arm rises to the right and the weapon moves
   to the back by itself, with the game's Blink Step effect when wired) gets a DESIGNED place from `place-on-body.cs`
   (the model's middle at a point of the body, its length along a direction, its thin side away from the body).
   **A resting place designed first (the katar's X on the lower back) is the other way round: the hand must deliver
   to it.** Measure the jump at each hand's handover — how far the model moves and how far it turns between its
   in-hand pose and the saved place — and fix the HAND, never the place: moving the place to a hand that arrives
   wrong leaves the weapon resting wrong. The right katar jumped 12.2% of body height and 52° (2026-10-10), nearly
   all of it a roll about the blade: its keys carried no edge (`re`), so the solver chose one. With the edge and
   blade set to the resting directions at keys 11–15 and the wrist moved 85% of the way to where the resting place
   needs it, it is 0.8% and 1°; at 92% the wrist ran out of bend and the miss came back at 5.9% and 12°. The left
   katar's 1.6% and 8° was left as it was (every variant tried was worse). The resting directions are read in the
   CLIP's pose at that frame, never in the standing idle: the right katar's saved place points `(-0.77, -0.63, -0.09)`
   at its handover frame and `(-0.82, -0.57, -0.01)` standing in Idle2 (that one measured on the Knight body).
   The saved `KatarSheathe` is a splice, not its table built whole — read the note in `keys/katar_sheathe.json`.
6. **Videos** (`render-stance.cs` + `make-stance-videos.py`): `<weapon>_idle`, `<weapon>_run` (the run with the
   carry arms laid over it, in place), `<weapon>_sheathe`, `<weapon>_unsheathe` (the weapon moved onto the hips at
   the handover; a pair's other copy at its own handover, from `pairStowAt` and `STOW_PAIR_*`). A job named
   `<weapon>_<kind>` lands in `D:\kimodo\out\ro_refs\videos\<weapon>\<weapon>_<kind>.mp4`; an imported attack with no
   key table (the bow's) is rendered here too, as `<weapon>_attack`. Pull frames back out of each video and compare them before sending. The script prints each
   elbow's bend from the very poses it draws; quote THAT number, never one measured on the clip alone. (2026-10-10:
   its arm filter matched "Arm" but not "Forearm", so the run videos kept the run's own 90° elbow under a carry pose
   measured straight on the clip; the user saw the bend twice before the filter was found.)

## 5. Prove the match — the comparison picture

Render our clip at the sprite's frame times from the sprite's cameras and stack the sprite rows over ours. Cameras:
35° down; yaw (degrees, + = toward the character's right) **-45 for a front-left row, -135 for a back row** — or +45 /
+135 with every frame flipped (`hflip`) when the hands were mirrored in step 1. Look at the picture yourself with Read
and compare frame by frame BEFORE showing the user anything; fix keys and re-run until each frame matches.

```csharp
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/<ClipName>.anim");
var outPath = @"<SCRATCHPAD>\ours.png";
int[] frames = { 0, 3, 5, 9, 12, 15, 18, 23 };   // our frame for each sprite frame
float[] yaws = { 45f, 135f };
const int S = 180;
var sheet = new Texture2D(S * frames.Length, S * yaws.Length, TextureFormat.RGBA32, false);
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
var binds = AnimationUtility.GetCurveBindings(clip);
for (int vi = 0; vi < yaws.Length; vi++)
for (int ci = 0; ci < frames.Length; ci++) {
  var pru = new PreviewRenderUtility(); GameObject rig = null;
  try {
    rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
    rig.transform.localScale = Vector3.one * (CharacterSpec.Active.bodyHeight / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar; anim.applyRootMotion = false;
    pru.AddSingleGO(rig);
    var hph = new HumanPoseHandler(avatar, rig.transform); var hp = new HumanPose(); hph.GetHumanPose(ref hp);
    float t = frames[ci] / 30f; float rx = 0, ry = 0, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
    foreach (var b in binds) {
      float v = AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
      switch (b.propertyName) {
        case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue;
        case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue;
      }
      var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
      if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
    }
    hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized;
    hph.SetHumanPose(ref hp); hph.Dispose();
    float H = CharacterSpec.Active.bodyHeight;
    var target = rig.transform.position + Vector3.up * H * 0.5f + rig.transform.forward * H * 0.06f;
    pru.camera.orthographic = true; pru.camera.orthographicSize = H * 0.62f; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 100f;
    pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.82f, 0.8f, 0.75f);
    pru.lights[0].intensity = 1.6f; pru.lights[0].transform.rotation = Quaternion.Euler(40, 160, 0); pru.lights[1].intensity = 1.0f;
    pru.ambientColor = new Color(0.9f, 0.9f, 0.9f);
    float y = yaws[vi] * Mathf.Deg2Rad, p = 35f * Mathf.Deg2Rad;
    Vector3 flat = rig.transform.forward * Mathf.Cos(y) + rig.transform.right * Mathf.Sin(y);
    pru.camera.transform.position = target + (flat * Mathf.Cos(p) + Vector3.up * Mathf.Sin(p)) * H * 5f;
    pru.camera.transform.LookAt(target);
    pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true); UnityEngine.Object.DestroyImmediate(pru.EndStaticPreview());
    pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
    var tex = pru.EndStaticPreview();
    var rtx = RenderTexture.GetTemporary(S, S, 0); Graphics.Blit(tex, rtx);
    var prev = RenderTexture.active; RenderTexture.active = rtx;
    sheet.ReadPixels(new Rect(0, 0, S, S), ci * S, (yaws.Length - 1 - vi) * S);
    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rtx); UnityEngine.Object.DestroyImmediate(tex);
  } finally { pru.Cleanup(); if (rig) UnityEngine.Object.DestroyImmediate(rig); }
}
sheet.Apply(); System.IO.File.WriteAllBytes(outPath, sheet.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(sheet);
return "done";
```

Stack it under the sprite rows (each 180 px frame flipped when mirrored), from a Python helper:
`[1:v]crop=180:180:{i*180}:{r*180},hflip[o{r}{i}]` per frame, `hstack=8` per row, then
`[spriteRowA][ours0][spriteRowB][ours1]vstack=4`.

**The user approves from videos, not pictures.** `render-video.cs` (replace `<FRAMES>` with a scratchpad folder; `only`
picks clips) draws each clip at 30 frames per second from front-left and front-right, 35° down, holding the weapon item's
real model — a pair weapon in both hands, mirrored as the game mounts it (a plain stand-in rifle only when the Sniper
item has no model); `python make-videos.py <frames dir> D:\kimodo\out\ro_refs\videos` joins them into
`<weapon>\<weapon>_attack.mp4`, one folder per weapon: three swings at real speed, then one at a third of the speed.
**That folder holds only videos of the models the items use today** (user, 2026-10-10): when a weapon's model
changes, render every clip of that weapon again and delete its old videos — never leave one showing a model the game
no longer has. Both were proven on 2026-10-09 (they reproduce the
delivered spear video exactly). What they handle, each learned the hard way that day:

- **The preview does NOT re-skin the body between renders** — the weapon (an ordinary child of the hand bone) moved,
  the body kept a stale pose, and the user saw "the hands don't move". So the body is baked every frame
  (`smr.BakeMesh(mesh, true)`, drawn at the body renderer's position, rotation and `lossyScale`), and the script prints
  the hand bone's gap to the nearest drawn point — a steady ~2% of body height is the glove; more means stale.
- **The default body is plain dark grey** (its texture really is), and the game's character shader draws black in the
  preview, so videos wear `Knight_Male` with a plain lit material carrying its own texture.
- **Look at the MOTION before sending**: pull several frames back out of the finished video and compare them — one frame
  per clip passed the frozen-arms videos.

## 6. Report and clean up

Report with the comparison picture first, then the videos (saved under `D:\kimodo\out\ro_refs\videos\<weapon>\`, never opened
on the user's screen), the wrist miss, the weapon errors from 4b, any mirroring, and what is not wired yet. The clip is unused until a stance points at
it — wiring it into the game is a separate, agreed change. Delete the scratchpad scripts, frames and the downloaded
sheet in the same reply.

**Every sentence about how the body looks is measured on the video, not on the clip.** "Straight", "bent",
"raised", "behind the hip", "crossed": take the number from the poses the video script drew (it prints each
elbow's bend), or pull that frame back out of the finished video and look at it, before writing the sentence. A
number measured on the clip describes a file the user never sees — the video tool lays poses over other clips and
can drop parts of them. If the user's eyes disagree with your number, measure the video, never re-defend the clip.

**A clip that REPLACES one already in game is not finished until the game plays it.** Building and approving the new
clip changes nothing by itself: the weapon's row in the stance library still names the old one. In the same change,
point the row at the new clip, rebuild the controller, then read the rebuilt controller's states on that layer and
confirm the new clip is listed and the old one is gone. Say both names in the report. (2026-10-10: `CarryKatarGuard`
was built and checked on 2026-10-09, but the katar's row still named `CarryKatar`, so the user's play-test showed the
old parallel blades while running.)
