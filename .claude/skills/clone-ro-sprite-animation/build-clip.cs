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
