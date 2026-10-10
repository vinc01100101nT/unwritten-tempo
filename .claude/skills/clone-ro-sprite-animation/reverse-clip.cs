// The take-out clip is the put-away clip played backwards, curve by curve — the same poses, so the same wrists.
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
var pairs = new[] { ("<Weapon>Sheathe", "<Weapon>Unsheathe") };
var log = new System.Text.StringBuilder();
foreach (var (from, to) in pairs) {
  var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + from + ".anim");
  if (src == null) { log.AppendLine(from + " MISSING"); continue; }
  float len = src.length;
  var clip = new AnimationClip { frameRate = src.frameRate };
  foreach (var b in AnimationUtility.GetCurveBindings(src)) {
    var c = AnimationUtility.GetEditorCurve(src, b);
    var r = new AnimationCurve();
    for (int i = c.length - 1; i >= 0; i--) { var k = c[i]; r.AddKey(new Keyframe(len - k.time, k.value, -k.outTangent, -k.inTangent)); }
    AnimationUtility.SetEditorCurve(clip, b, r);
  }
  AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(src));
  var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + to + ".anim");
  if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = to; EditorUtility.SetDirty(existing); }
  else AssetDatabase.CreateAsset(clip, DIR + to + ".anim");
  log.AppendLine(to + " = " + from + " reversed, " + len.ToString("F3") + " s");
}
AssetDatabase.SaveAssets();
return log.ToString();
