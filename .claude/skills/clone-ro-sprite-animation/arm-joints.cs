// The arm joints of poses that are to be blended, side by side: each clip's shoulder, arm (down-up, front-back, twist),
// forearm (stretch, twist) and hand (down-up, in-out) muscles at a time. Two poses whose forearm twist or hand bends sit
// at opposite ends flip the weapon through the body when blended — pick another roll of the weapon for one of them.
// Fill __JOBS__ (("SwordPutAwayPoseMouth", 0f), ...; seconds, -1 = the last pose) and __SIDE__ (Right or Left).
var log = new System.Text.StringBuilder();
var jobs = new (string clip, float t)[] { __JOBS__ };
string side = "__SIDE__";
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
string[] names = { "Shoulder Down-Up", "Shoulder Front-Back", "Arm Down-Up", "Arm Front-Back", "Arm Twist In-Out", "Forearm Stretch", "Forearm Twist In-Out", "Hand Down-Up", "Hand In-Out" };
log.AppendLine("clip: ShDU ShFB ArmDU ArmFB ArmTw FoStr FoTw HandDU HandIO");
foreach (var job in jobs) {
  var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + job.clip + ".anim"); if (clip == null) { log.AppendLine(job.clip + " MISSING"); continue; }
  var sb = new System.Text.StringBuilder(job.clip + "@" + job.t.ToString("F2") + ":");
  foreach (var n in names) {
    float v = float.NaN;
    foreach (var b in AnimationUtility.GetCurveBindings(clip)) { var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " "); if (nm == side + " " + n) { v = AnimationUtility.GetEditorCurve(clip, b).Evaluate(job.t < 0 ? clip.length : job.t); break; } }
    sb.Append(" " + v.ToString("F2"));
  }
  log.AppendLine(sb.ToString());
}
return log.ToString();
