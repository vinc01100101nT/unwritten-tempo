// A put-away made by blending whole poses joint by joint (60 poses per second) — the general form of the per-weapon
// blend scripts beside this file. Each stop names the clip its pose is read from (a still pose built from a two-key
// table, or any pose of a keyed clip), the time in that clip (seconds; -1 = its last pose), the frame of the new clip
// it lands on, and whether the move stops there (true) or flows through it (false). Between stops every joint follows
// one smooth curve; at a stop it comes to rest, so the same pose listed twice is a hold. The last stop is normally the
// hanging-arms end (the last pose of a saved put-away such as AxeSheathe).
// Fill __OUT__ and __CHAIN__ — e.g. ("SpearHoldUpright", 0f, 0, true), ("SpearPutAwayPoseOut", 0f, 8, true), ... —
// run with eval_file, then reverse-clip.cs for the take-out.
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
const string OUT = "__OUT__";
var chain = new (string clip, float at, int frame, bool stop)[] { __CHAIN__ };
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name; }
int K = chain.Length; if (K < 2) return "a chain needs two stops";
var clips = new AnimationClip[K]; var ts = new float[K];
for (int i = 0; i < K; i++) {
  clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + chain[i].clip + ".anim"); if (clips[i] == null) return "MISSING " + chain[i].clip;
  ts[i] = chain[i].at < 0f ? clips[i].length : chain[i].at;
  if (i > 0 && chain[i].frame <= chain[i - 1].frame) return "frames must rise: " + chain[i].clip;
}
int END = chain[K - 1].frame;
float V(AnimationClip c, EditorCurveBinding b, float t) { var cv = AnimationUtility.GetEditorCurve(c, b); return cv == null ? float.NaN : cv.Evaluate(t); }
string[] q = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
Quaternion Q(AnimationClip c, float t) { var r = new float[4]; for (int k = 0; k < 4; k++) r[k] = V(c, EditorCurveBinding.FloatCurve("", typeof(Animator), q[k]), t); return new Quaternion(r[0], r[1], r[2], r[3]).normalized; }
var qs = new Quaternion[K]; for (int i = 0; i < K; i++) qs[i] = Q(clips[i], ts[i]);
bool Stop(int i) => i <= 0 || i >= K - 1 || chain[i].stop;
// one curve through the stops: at a stop the slope is 0, elsewhere it is the slope between the two neighbours
float Hermite(float p0, float p1, float m0, float m1, float t) { float t2 = t * t, t3 = t2 * t; return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1; }
AnimationCurve Smooth(AnimationCurve c) { for (int j = 0; j < c.length; j++) { AnimationUtility.SetKeyLeftTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); } return c; }
var clip = new AnimationClip { frameRate = 60 }; int n = 0;
foreach (var b in AnimationUtility.GetCurveBindings(clips[0])) {
  if (b.type != typeof(Animator)) continue;
  int qi = System.Array.IndexOf(q, b.propertyName);
  var vals = new float[K]; for (int i = 0; i < K; i++) { vals[i] = V(clips[i], b, ts[i]); if (float.IsNaN(vals[i])) return chain[i].clip + " lacks " + b.propertyName; }
  float Slope(int i) => Stop(i) ? 0f : (vals[i + 1] - vals[i - 1]) / (chain[i + 1].frame - chain[i - 1].frame);
  var cv = new AnimationCurve();
  for (int f = 0; f <= END; f++) {
    int s = 0; while (s < K - 2 && f > chain[s + 1].frame) s++;
    float len = chain[s + 1].frame - chain[s].frame, t = Mathf.Clamp01((f - chain[s].frame) / len);
    float v;
    if (qi >= 0) {
      float w = Hermite(0f, 1f, Stop(s) ? 0f : 1f, Stop(s + 1) ? 0f : 1f, t);
      var r = Quaternion.Slerp(qs[s], qs[s + 1], w); v = qi == 0 ? r.x : qi == 1 ? r.y : qi == 2 ? r.z : r.w;
    } else v = Hermite(vals[s], vals[s + 1], Slope(s) * len, Slope(s + 1) * len, t);
    cv.AddKey(f / 60f, v);
  }
  AnimationUtility.SetEditorCurve(clip, b, Smooth(cv)); n++;
}
// the last stop's settings (the body's movement baked into the pose), but this clip's own length: copied whole, a
// shorter clip kept the other's stop time and played a held tail the game would scrub as part of the put-away
var settings = AnimationUtility.GetAnimationClipSettings(clips[K - 1]); settings.startTime = 0f; settings.stopTime = END / 60f; settings.loopTime = false;
AnimationUtility.SetAnimationClipSettings(clip, settings);
var outPath = DIR + OUT + ".anim"; var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = OUT; EditorUtility.SetDirty(existing); } else AssetDatabase.CreateAsset(clip, outPath);
AssetDatabase.SaveAssets();
var log = OUT + ": " + n + " curves, " + (END + 1) + " poses (" + (END / 60f).ToString("F3") + " s) through";
for (int i = 0; i < K; i++) log += " " + chain[i].clip + "@" + chain[i].frame + (chain[i].stop ? "." : "");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "; " + sc.name + " dirty=" + sc.isDirty; }
return log;
