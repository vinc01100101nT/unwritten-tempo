// DaggerSheathe = the spin (DaggerFlourish, played as built) -> eased down to the handover pose (DaggerPutAwayPoseHandover:
// hand before the hip, blade slanted down and back) -> held a moment -> eased to standing with the arm hanging (the LAST
// pose of __C__). Joint by joint, 60 poses per second. Build keys/dagger_flourish.json and keys/dagger_putaway_handover.json
// first (point-weapon-gen.py ... 1 1), run with eval_file, then reverse-clip.cs for DaggerUnsheathe. The handover frame
// (60 per second) is printed: it goes into the stance library's sheathe frame; the take-out's is the length minus it.
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
const string FLOURISH = "DaggerFlourish", POSE = "DaggerPutAwayPoseHandover", C = "__C__";
const int DOWN = 14, HOLD = 4, HANG = 16;   // frames (60 per second) to reach the pose, to hold it, to hang the arm
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name; }
var cf = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + FLOURISH + ".anim"); var cp = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + POSE + ".anim"); var cc = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + C + ".anim");
if (cf == null || cp == null || cc == null) return "MISSING";
int F = Mathf.RoundToInt(cf.length * 60f), HAND = F + DOWN, HOLDEND = HAND + HOLD, END = HOLDEND + HANG;
float V(AnimationClip c, EditorCurveBinding b, float t) { var cv = AnimationUtility.GetEditorCurve(c, b); return cv == null ? float.NaN : cv.Evaluate(t); }
string[] q = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
Quaternion Q(AnimationClip c, float t) { var r = new float[4]; for (int k = 0; k < 4; k++) r[k] = V(c, EditorCurveBinding.FloatCurve("", typeof(Animator), q[k]), t); return new Quaternion(r[0], r[1], r[2], r[3]).normalized; }
var qf = Q(cf, cf.length); var qp = Q(cp, 0f); var qc = Q(cc, cc.length);
AnimationCurve Smooth(AnimationCurve c) { for (int j = 0; j < c.length; j++) { AnimationUtility.SetKeyLeftTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); } return c; }
var clip = new AnimationClip { frameRate = 60 }; int n = 0;
foreach (var b in AnimationUtility.GetCurveBindings(cf)) {
  if (b.type != typeof(Animator)) continue;
  int qi = System.Array.IndexOf(q, b.propertyName);
  float fEnd = V(cf, b, cf.length), p0 = V(cp, b, 0f), c0 = V(cc, b, cc.length);
  if (float.IsNaN(p0) || float.IsNaN(c0)) return "a pose lacks " + b.propertyName;
  var cv = new AnimationCurve();
  for (int f = 0; f <= END; f++) {
    float v;
    if (f <= F) v = V(cf, b, f / 60f);
    else {
      Quaternion r; float a, z, w;
      if (f <= HAND) { w = Mathf.SmoothStep(0f, 1f, (f - F) / (float)DOWN); a = fEnd; z = p0; r = Quaternion.Slerp(qf, qp, w); }
      else if (f <= HOLDEND) { w = 1f; a = p0; z = p0; r = qp; }
      else { w = Mathf.SmoothStep(0f, 1f, (f - HOLDEND) / (float)HANG); a = p0; z = c0; r = Quaternion.Slerp(qp, qc, w); }
      v = qi >= 0 ? (qi == 0 ? r.x : qi == 1 ? r.y : qi == 2 ? r.z : r.w) : Mathf.Lerp(a, z, w);
    }
    cv.AddKey(f / 60f, v);
  }
  AnimationUtility.SetEditorCurve(clip, b, Smooth(cv)); n++;
}
AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(cc));
var outPath = DIR + "DaggerSheathe.anim"; var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = "DaggerSheathe"; EditorUtility.SetDirty(existing); } else AssetDatabase.CreateAsset(clip, outPath);
AssetDatabase.SaveAssets();
var log = "DaggerSheathe: " + n + " curves, " + (END + 1) + " poses (" + (END / 60f).ToString("F3") + " s); the spin ends at " + F + ", the handover pose is reached at " + HAND + " and held to " + HOLDEND + " (60 per second); the take-out's handover is " + (END - HAND);
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "; " + sc.name + " dirty=" + sc.isDirty; }
return log;
