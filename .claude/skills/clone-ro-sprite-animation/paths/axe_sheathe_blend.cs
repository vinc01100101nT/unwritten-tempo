// Run from the clone-ro-sprite-animation skill: build the three pose tables keys/axe_putaway_*.json first (write 1),
// fill __C__ with AxeSheathe (its last pose is the hanging-arms end), run with eval_file, then reverse-clip.cs for
// AxeUnsheathe.
// AxeSheathe made by blending whole poses joint by joint (60 poses per second): the combat idle -> hands out beside the
// hips, axes up and out -> hands just behind the hips, axes tipped back -> hands behind the hips with the axes crossing
// the back (the handover, held a moment) -> standing with the arms hanging. The first four are joined by one smooth
// curve through all of them (equal spacing), so the hands never stop between them; every pose is one the body can take.
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
var chain = new[] { "IdleAxe", "AxePutAwayPoseSide", "AxePutAwayPoseBack", "AxePutAwayPoseHandover" };   // at frames 0, 8, 16, 24
const int STEP = 8, HAND = 24, HOLD = 28, END = 44;
const string C = "__C__";   // a clip whose LAST pose is the hanging-arms end
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name; }
var clips = new AnimationClip[chain.Length];
for (int i = 0; i < chain.Length; i++) { clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + chain[i] + ".anim"); if (clips[i] == null) return "MISSING " + chain[i]; }
var cc = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + C + ".anim"); if (cc == null) return "MISSING " + C;
float V(AnimationClip c, EditorCurveBinding b, float t) { var cv = AnimationUtility.GetEditorCurve(c, b); return cv == null ? float.NaN : cv.Evaluate(t); }
string[] q = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
Quaternion Q(AnimationClip c, float t) { var r = new float[4]; for (int k = 0; k < 4; k++) r[k] = V(c, EditorCurveBinding.FloatCurve("", typeof(Animator), q[k]), t); return new Quaternion(r[0], r[1], r[2], r[3]).normalized; }
var qs = new Quaternion[chain.Length]; for (int i = 0; i < chain.Length; i++) qs[i] = Q(clips[i], 0f);
var qc = Q(cc, cc.length);
float CR(float p0, float p1, float p2, float p3, float t) => 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
AnimationCurve Smooth(AnimationCurve c) { for (int j = 0; j < c.length; j++) { AnimationUtility.SetKeyLeftTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); } return c; }
var clip = new AnimationClip { frameRate = 60 }; int n = 0;
foreach (var b in AnimationUtility.GetCurveBindings(clips[0])) {
  if (b.type != typeof(Animator)) continue;
  int qi = System.Array.IndexOf(q, b.propertyName);
  var vals = new float[chain.Length]; for (int i = 0; i < chain.Length; i++) { vals[i] = V(clips[i], b, 0f); if (float.IsNaN(vals[i])) return chain[i] + " lacks " + b.propertyName; }
  float c0 = V(cc, b, cc.length); if (float.IsNaN(c0)) return C + " lacks " + b.propertyName;
  var cv = new AnimationCurve();
  for (int f = 0; f <= END; f++) {
    float v;
    if (f <= HAND) {
      int seg = Mathf.Min(f / STEP, chain.Length - 2); float t = (f - seg * STEP) / (float)STEP;
      if (qi >= 0) { var r = Quaternion.Slerp(qs[seg], qs[seg + 1], t); v = qi == 0 ? r.x : qi == 1 ? r.y : qi == 2 ? r.z : r.w; }
      else v = CR(vals[Mathf.Max(seg - 1, 0)], vals[seg], vals[seg + 1], vals[Mathf.Min(seg + 2, chain.Length - 1)], t);
    } else if (f <= HOLD) { v = qi >= 0 ? (qi == 0 ? qs[3].x : qi == 1 ? qs[3].y : qi == 2 ? qs[3].z : qs[3].w) : vals[chain.Length - 1]; }
    else {
      float w = Mathf.SmoothStep(0f, 1f, (f - HOLD) / (float)(END - HOLD));
      if (qi >= 0) { var r = Quaternion.Slerp(qs[3], qc, w); v = qi == 0 ? r.x : qi == 1 ? r.y : qi == 2 ? r.z : r.w; }
      else v = Mathf.Lerp(vals[chain.Length - 1], c0, w);
    }
    cv.AddKey(f / 60f, v);
  }
  AnimationUtility.SetEditorCurve(clip, b, Smooth(cv)); n++;
}
AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(cc));
var outPath = DIR + "AxeSheathe.anim"; var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = "AxeSheathe"; EditorUtility.SetDirty(existing); } else AssetDatabase.CreateAsset(clip, outPath);
AssetDatabase.SaveAssets();
var log = "AxeSheathe blended through " + string.Join(" > ", chain) + ": " + n + " curves, " + (END + 1) + " poses";
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "; " + sc.name + " dirty=" + sc.isDirty; }
return log;
