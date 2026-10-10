// How smoothly a held weapon moves through a saved clip: at 60 poses per second on the default body, with the item's
// model mounted as the game mounts it, the far end's travel per pose, where its speed jumps, how often its path
// changes the way it curves (seen from the front), how many arm joints turn back on themselves, and whether the clip
// ends where another clip starts. The pass mark for a flowing move: curve changes only where the shape has them, 0 arm
// reversals, no sudden change of speed. Nothing is saved.
// Fill: __CLIP__, __ITEM__ (item asset name), __REF__ (a clip whose FIRST pose the end is compared with, or the clip
// itself), __FROM__ / __TO__ (poses to judge, 0 and -1 for all — the part before a put-away's handover).
var log = new System.Text.StringBuilder();
const string CLIP = "__CLIP__", ITEM = "__ITEM__", REF = "__REF__"; int FROM = __FROM__, TO = __TO__;
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
float H = CharacterSpec.Active.bodyHeight;
var item = AssetDatabase.LoadAssetAtPath<Item>("Assets/3_Data/Resources/Items/" + ITEM + ".asset"); var grip = item.equipModel.GetComponent<WeaponGrip>();
AnimationClip Find(string n) { var c = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/" + n + ".anim"); if (c != null) return c; foreach (var x in CharacterSpec.Active.animatorController.animationClips) if (x != null && x.name == n) return x; return null; }
var clip = Find(CLIP); var refClip = Find(REF); if (clip == null || refClip == null) return "MISSING";
var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
try {
  rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
  var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar;
  var hph = new HumanPoseHandler(avatar, rig.transform);
  var hand = HandSocketResolver.Find(anim, grip.hand);
  var w = (GameObject)UnityEngine.Object.Instantiate(item.equipModel); w.hideFlags = HideFlags.HideAndDontSave; w.transform.SetParent(hand, false);
  w.transform.localPosition = grip.localPosition; w.transform.localRotation = Quaternion.Euler(grip.localEuler); w.transform.localScale = grip.localScale;
  var mf = w.GetComponentInChildren<MeshFilter>(); var mb = mf.sharedMesh.bounds;
  // the far end: the end of the model's length (y) further from the fist
  var fistM = mf.transform.InverseTransformPoint(hand.TransformPoint(new Vector3(0.011f, 0.18756053f, 0.12156433f)));
  var tipM = new Vector3(0, mb.max.y - fistM.y >= fistM.y - mb.min.y ? mb.max.y : mb.min.y, 0);
  float[] Pose(AnimationClip c, float t) {
    var hp = new HumanPose(); hph.GetHumanPose(ref hp);
    float ry = hp.bodyPosition.y, qx = 0, qy = 0, qz = 0, qw = 1;
    foreach (var b in AnimationUtility.GetCurveBindings(c)) {
      if (b.type != typeof(Animator)) continue;
      float v = AnimationUtility.GetEditorCurve(c, b).Evaluate(t);
      switch (b.propertyName) { case "RootT.x": case "RootT.z": continue; case "RootT.y": ry = v; continue; case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue; }
      var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
      if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
    }
    hp.bodyPosition = new Vector3(0, ry, 0); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized; hph.SetHumanPose(ref hp);
    return (float[])hp.muscles.Clone();
  }
  int N = Mathf.RoundToInt(clip.length * 60f); if (TO < 0 || TO > N) TO = N;
  var tips = new Vector3[N + 1]; var mus = new float[N + 1][];
  for (int f = 0; f <= N; f++) { mus[f] = Pose(clip, f / 60f); var p = mf.transform.TransformPoint(tipM) - rig.transform.position; tips[f] = new Vector3(Vector3.Dot(p, rig.transform.right), p.y, Vector3.Dot(p, rig.transform.forward)) / H; }
  var sp = new float[N + 1]; for (int f = 1; f <= N; f++) sp[f] = (tips[f] - tips[f - 1]).magnitude;
  var seq = new System.Text.StringBuilder(); float maxSp = 0; int maxAt = 0, jumpAt = 0; float worstJump = 0;
  for (int f = FROM + 1; f <= TO; f++) {
    seq.Append((sp[f] * 100f).ToString("F0")).Append(' '); if (sp[f] > maxSp) { maxSp = sp[f]; maxAt = f; }
    if (f > FROM + 1) { float hi = Mathf.Max(sp[f], sp[f - 1]); if (hi > 0.03f) { float j = Mathf.Abs(sp[f] - sp[f - 1]) / hi; if (j > worstJump) { worstJump = j; jumpAt = f; } } }
  }
  // the way the path curves, seen from the front (x right, y up): the sign of the turn between two steps
  int flips = 0; float last = 0; var flipAt = new System.Text.StringBuilder();
  for (int f = FROM + 2; f <= TO; f++) {
    var v0 = new Vector2(tips[f - 1].x - tips[f - 2].x, tips[f - 1].y - tips[f - 2].y); var v1 = new Vector2(tips[f].x - tips[f - 1].x, tips[f].y - tips[f - 1].y);
    if (v0.magnitude < 0.004f || v1.magnitude < 0.004f) continue;
    float turn = Mathf.Atan2(v0.x * v1.y - v0.y * v1.x, Vector2.Dot(v0, v1)) * Mathf.Rad2Deg;
    if (Mathf.Abs(turn) < 2f) continue;
    if (last != 0 && Mathf.Sign(turn) != Mathf.Sign(last)) { flips++; flipAt.Append(f).Append(' '); }
    last = turn;
  }
  int rev = 0; var revW = new System.Text.StringBuilder();
  for (int m = 0; m < HumanTrait.MuscleCount; m++) {
    var nm = HumanTrait.MuscleName[m]; if (!(nm.Contains("Arm") || nm.Contains("Forearm") || nm.Contains("Hand ") || nm.Contains("Shoulder")) || nm.Contains("Stretched") || nm.Contains("Spread")) continue;
    for (int f = FROM + 1; f < TO; f++) { float d1 = mus[f][m] - mus[f - 1][m], d2 = mus[f + 1][m] - mus[f][m]; if (d1 * d2 < 0f && Mathf.Abs(mus[f][m] - (mus[f - 1][m] + mus[f + 1][m]) * 0.5f) > 0.03f) { rev++; if (revW.Length < 200) revW.Append(nm + "@" + f + " "); } }
  }
  var r0 = Pose(refClip, 0f); float endDiff = 0; string endWhere = "";
  for (int m = 0; m < HumanTrait.MuscleCount; m++) { var nm = HumanTrait.MuscleName[m]; if (!(nm.Contains("Arm") || nm.Contains("Forearm") || nm.Contains("Hand ") || nm.Contains("Shoulder"))) continue; float d = Mathf.Abs(mus[N][m] - r0[m]); if (d > endDiff) { endDiff = d; endWhere = nm; } }
  log.AppendLine(CLIP + ": " + (N + 1) + " poses (" + clip.length.ToString("F3") + " s), judged " + FROM + ".." + TO + ". Far end's travel per pose (% of body height): " + seq);
  log.AppendLine("  fastest " + (maxSp * 100f).ToString("F0") + "% per pose at pose " + maxAt + "; biggest change of speed between two neighbouring poses " + (worstJump * 100f).ToString("F0") + "% at pose " + jumpAt + "; the path changes the way it curves " + flips + " times (at poses " + flipAt + "); arm joint reversals " + rev + (rev > 0 ? " (" + revW + ")" : ""));
  log.AppendLine("  far end spans right " + System.Linq.Enumerable.Min(tips, t => t.x).ToString("F2") + ".." + System.Linq.Enumerable.Max(tips, t => t.x).ToString("F2") + ", up " + System.Linq.Enumerable.Min(tips, t => t.y).ToString("F2") + ".." + System.Linq.Enumerable.Max(tips, t => t.y).ToString("F2") + " body heights; last pose vs " + REF + "'s first: biggest arm joint difference " + endDiff.ToString("F2") + " (" + endWhere + ")");
  hph.Dispose();
} finally { UnityEngine.Object.DestroyImmediate(rig); }
return log.ToString();
