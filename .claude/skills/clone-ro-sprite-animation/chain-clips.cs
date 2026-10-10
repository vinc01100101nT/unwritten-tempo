// Plays saved clips one after another as ONE new clip (60 poses per second), each join eased over its own number of poses: every
// joint blends from where the last clip ended to where the next one starts (smooth in and out), the body's turn by
// slerp. The clips used are only read, never changed — a locked attack can be a segment. The curves written are the
// LAST segment's (the body joints); every segment must carry all of them, or the run stops and names the gap.
// Fill: __OUT__ (the new clip's name), __SEGMENTS__ (("AttackSword2H", 0f, -1f, 0), ("GreatswordPutAwayCore", 0f, -1f, 20) =
// clip, from second, to second (-1 = its end), poses of easing before it (ignored for the first)). Size each join by
// what it must cover: the run prints the biggest joint change at each join. Then run with eval_file. It prints
// where each segment starts in the new clip, so a handover frame of the last segment can be moved by that offset.
// First used 2026-10-10 for the Greatsword put-away: two swings of its locked attack, then the move to the back.
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
const string OUT = "__OUT__";
var segs = new (string clip, float from, float to, int join)[] { __SEGMENTS__ };
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name; }
AnimationClip Find(string n) {
  var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + n + ".anim"); if (c != null) return c;
  foreach (var x in CharacterSpec.Active.animatorController.animationClips) if (x != null && x.name == n) return x;
  return null;
}
var clips = new AnimationClip[segs.Length];
for (int s = 0; s < segs.Length; s++) { clips[s] = Find(segs[s].clip); if (clips[s] == null) return "MISSING " + segs[s].clip; }
var last = clips[segs.Length - 1];
var binds = System.Array.FindAll(AnimationUtility.GetCurveBindings(last), b => b.type == typeof(Animator));
string[] qn = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
float V(AnimationClip c, EditorCurveBinding b, float t) { var cv = AnimationUtility.GetEditorCurve(c, b); return cv == null ? float.NaN : cv.Evaluate(t); }
Quaternion Q(AnimationClip c, float t) { var r = new float[4]; for (int k = 0; k < 4; k++) r[k] = V(c, EditorCurveBinding.FloatCurve("", typeof(Animator), qn[k]), t); return new Quaternion(r[0], r[1], r[2], r[3]).normalized; }
foreach (var c in clips) foreach (var b in binds) if (float.IsNaN(V(c, b, 0f))) return c.name + " lacks " + b.propertyName;
// the timeline: (segment, its own time) for every pose; a join is (-1, blend share) between two segments
var poses = new System.Collections.Generic.List<(int seg, float t, float w)>(); var starts = new int[segs.Length];
for (int s = 0; s < segs.Length; s++) {
  float a0 = segs[s].from, a1 = segs[s].to < 0f ? clips[s].length : segs[s].to;
  if (s > 0) for (int j = 1; j <= segs[s].join; j++) poses.Add((-s, 0f, Mathf.SmoothStep(0f, 1f, j / (float)(segs[s].join + 1))));
  starts[s] = poses.Count;
  int n = Mathf.RoundToInt((a1 - a0) * 60f);
  for (int f = 0; f <= n; f++) poses.Add((s, a0 + f / 60f, 0f));
}
float End(int s) => segs[s].to < 0f ? clips[s].length : segs[s].to;
AnimationCurve Smooth(AnimationCurve c) { for (int j = 0; j < c.length; j++) { AnimationUtility.SetKeyLeftTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); } return c; }
var clip = new AnimationClip { frameRate = 60 };
foreach (var b in binds) {
  int qi = System.Array.IndexOf(qn, b.propertyName);
  var cv = new AnimationCurve();
  for (int p = 0; p < poses.Count; p++) {
    var (seg, t, w) = poses[p]; float v;
    if (seg >= 0) v = qi >= 0 ? (qi == 0 ? Q(clips[seg], t).x : qi == 1 ? Q(clips[seg], t).y : qi == 2 ? Q(clips[seg], t).z : Q(clips[seg], t).w) : V(clips[seg], b, t);
    else {
      int s1 = -seg, s0 = s1 - 1;
      if (qi >= 0) { var r = Quaternion.Slerp(Q(clips[s0], End(s0)), Q(clips[s1], segs[s1].from), w); v = qi == 0 ? r.x : qi == 1 ? r.y : qi == 2 ? r.z : r.w; }
      else v = Mathf.Lerp(V(clips[s0], b, End(s0)), V(clips[s1], b, segs[s1].from), w);
    }
    cv.AddKey(p / 60f, v);
  }
  AnimationUtility.SetEditorCurve(clip, b, Smooth(cv));
}
AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(last));
var outPath = DIR + OUT + ".anim"; var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = OUT; EditorUtility.SetDirty(existing); } else AssetDatabase.CreateAsset(clip, outPath);
AssetDatabase.SaveAssets();
var log = OUT + ": " + binds.Length + " curves, " + poses.Count + " poses (" + ((poses.Count - 1) / 60f).ToString("F3") + " s); segments start at pose";
for (int s = 0; s < segs.Length; s++) log += " " + segs[s].clip + "=" + starts[s];
// how far each joint jumps at the joins, as a check: the biggest change of any joint between two poses there
for (int s = 1; s < segs.Length; s++) { float worst = 0f; foreach (var b in binds) { if (System.Array.IndexOf(qn, b.propertyName) >= 0) continue; float d = Mathf.Abs(V(clips[s - 1], b, End(s - 1)) - V(clips[s], b, segs[s].from)); worst = Mathf.Max(worst, d); } log += "; join " + s + " spans a joint change of " + worst.ToString("F2") + " over " + segs[s].join + " poses"; }
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) { var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "; " + sc.name + " dirty=" + sc.isDirty; }
return log;
