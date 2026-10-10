// Run from the clone-ro-sprite-animation skill: build the pose tables keys/greatsword_putaway_*.json and
// keys/greatsword_hold_upright.json first (write 1), fill __C__ with a clip whose LAST pose is the hanging-arms end
// (AxeSheathe), run with eval_file, then reverse-clip.cs for GreatswordUnsheathe.
// GreatswordSheathe made by blending whole poses joint by joint (60 poses per second): the hold (GreatswordHoldUpright —
// the idle's arms, two hands low at the right hip, on the STANDING body, because in game a put-away plays on the arms
// only, over the shared idle or the run) -> right hand out beside the hip, blade up and out (the left hand lets go) -> hand just
// behind the hip, blade upright behind the right shoulder -> the blade tipping over to the left behind the back
// (Diagonal, Slant) -> the sword lying level across the upper back, tip to the left, hilt by the right shoulder blade
// (the handover, held a moment) -> standing with the arms hanging.
// The elbow stays behind the body the whole way. A level pose with the elbow out to the side was tried as the handover
// (the flat of the blade faces straight back there), but its wrist sits at the far end of its range from these poses:
// blending to it swung the blade straight down, 100% of body height into the ground, two frames before the handover
// (2026-10-10). The poses up to the handover are joined by one smooth curve through all of them (equal spacing), so the
// hand never stops between them; every pose is one the body can take (found with reach scans of the wrist's natural
// range, 2026-10-10).
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
var chain = new[] { "GreatswordHoldUpright", "GreatswordPutAwayPoseSide", "GreatswordPutAwayPoseBack", "GreatswordPutAwayPoseDiagonal", "GreatswordPutAwayPoseSlant", "GreatswordPutAwayPoseHandover" };   // at frames 0, 8, 16, 24, 32, 40
const int STEP = 8, HAND = 40, HOLD = 46, END = 64;
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
    } else if (f <= HOLD) { var qe = qs[chain.Length - 1]; v = qi >= 0 ? (qi == 0 ? qe.x : qi == 1 ? qe.y : qi == 2 ? qe.z : qe.w) : vals[chain.Length - 1]; }
    else {
      float w = Mathf.SmoothStep(0f, 1f, (f - HOLD) / (float)(END - HOLD));
      if (qi >= 0) { var r = Quaternion.Slerp(qs[chain.Length - 1], qc, w); v = qi == 0 ? r.x : qi == 1 ? r.y : qi == 2 ? r.z : r.w; }
      else v = Mathf.Lerp(vals[chain.Length - 1], c0, w);
    }
    cv.AddKey(f / 60f, v);
  }
  AnimationUtility.SetEditorCurve(clip, b, Smooth(cv)); n++;
}
AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(cc));
var outPath = DIR + "GreatswordSheathe.anim"; var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = "GreatswordSheathe"; EditorUtility.SetDirty(existing); } else AssetDatabase.CreateAsset(clip, outPath);
AssetDatabase.SaveAssets();
var log = "GreatswordSheathe blended through " + string.Join(" > ", chain) + ": " + n + " curves, " + (END + 1) + " poses";
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "; " + sc.name + " dirty=" + sc.isDirty; }
return log;
