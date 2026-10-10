// A put-away NOT WIRED YET, checked pose by pose: plays each clip on the default body with the item's model in the hand
// until the hand lets go and reports the most of the weapon inside the body, its lowest point against the ground and
// the far end's biggest move in one pose (a flip shows as a jump of 60% of body height or more). A wired weapon is
// checked with check-clip-fit.cs, which reads its saved row. Nothing is saved.
// Fill __JOBS__: (item, put-away clip, the frame the hand lets go at 60 per second, spin start frame, spin end frame,
// turns, lift, turns about its own middle) — e.g. ("Spear", "SpearSheathe", 36, 8, 23, 1f, 0.3f, true).
var log = new System.Text.StringBuilder();
var jobs = new (string item, string clip, int hand60, int spinFrom, int spinTo, float turns, float lift, bool mid)[] { __JOBS__ };
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
float H = CharacterSpec.Active.bodyHeight;
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
var FIST = new Vector3(0.011f, 0.18756053f, 0.12156433f);
foreach (var job in jobs) {
  var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + job.clip + ".anim"); if (clip == null) { log.AppendLine(job.clip + " MISSING"); continue; }
  var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave; GameObject w = null; Mesh bm = null;
  try {
    rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar;
    var hph = new HumanPoseHandler(avatar, rig.transform);
    var rest = new HumanPose(); hph.GetHumanPose(ref rest);
    var binds = AnimationUtility.GetCurveBindings(clip); var curves = new AnimationCurve[binds.Length]; for (int b = 0; b < binds.Length; b++) curves[b] = AnimationUtility.GetEditorCurve(clip, binds[b]);
    var item = AssetDatabase.LoadAssetAtPath<Item>("Assets/3_Data/Resources/Items/" + job.item + ".asset"); var wp = item.equipModel; var grip = wp.GetComponent<WeaponGrip>();
    bool left = grip.hand == WeaponHand.Left; var hand = HandSocketResolver.Find(anim, grip.hand);
    w = (GameObject)UnityEngine.Object.Instantiate(wp); w.hideFlags = HideFlags.HideAndDontSave; w.transform.SetParent(hand, false); w.transform.localScale = grip.localScale;
    var mf = w.GetComponentInChildren<MeshFilter>(); var mb = mf.sharedMesh.bounds; var vs = mf.sharedMesh.vertices;
    var smr = rig.GetComponentInChildren<SkinnedMeshRenderer>(); bm = new Mesh { hideFlags = HideFlags.HideAndDontSave }; var bwt = smr.sharedMesh.boneWeights;
    var armIdx = new System.Collections.Generic.HashSet<int>();
    foreach (var hbb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder }) { var tb = anim.GetBoneTransform(hbb); if (tb != null) armIdx.Add(System.Array.IndexOf(smr.bones, tb)); }
    var pivot = left ? new Vector3(-FIST.x, FIST.y, FIST.z) : FIST; var gq = Quaternion.Euler(grip.localEuler);
    // a weapon that turns about its own middle slides through the fist exactly as the game's hand does it
    var slide = job.mid ? WeaponSocket.MiddleSlide(grip.localPosition, grip.localEuler, grip.localScale, left, wp.transform.InverseTransformPoint(wp.GetComponentInChildren<MeshFilter>().transform.TransformPoint(mb.center))) : Vector3.zero;
    int total = Mathf.RoundToInt(clip.length * 60f); float worstIn = 0f, low = float.MaxValue, maxStep = 0f; int worstAt = 0, lowAt = 0, stepAt = 0; var parts = new System.Collections.Generic.Dictionary<string, int>();
    var prevEnd = Vector3.zero; string overFrames = "";
    for (int f = 0; f <= job.hand60; f++) {
      var hp = rest; hp.muscles = (float[])rest.muscles.Clone(); float t = f / 60f; float rx = 0, ry = rest.bodyPosition.y, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
      for (int b = 0; b < binds.Length; b++) {
        if (binds[b].type != typeof(Animator)) continue; float v = curves[b].Evaluate(t);
        switch (binds[b].propertyName) { case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue; case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue; }
        var nm = binds[b].propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " "); if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
      }
      hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized; hph.SetHumanPose(ref hp);
      var lp = grip.localPosition; var lr = gq;
      if (job.turns != 0f && f > job.spinFrom && f < job.spinTo) {
        float share = (f - job.spinFrom) / (float)(job.spinTo - job.spinFrom);
        WeaponSocket.SpunOverFist(grip, share, job.turns, job.lift, slide, out lp, out lr);
      }
      w.transform.localPosition = lp; w.transform.localRotation = lr;
      smr.BakeMesh(bm, true); var bv = bm.vertices; var bn = bm.normals;
      int ins = 0; float lw = float.MaxValue;
      for (int vi = 0; vi < vs.Length; vi += 2) {
        var p = mf.transform.TransformPoint(vs[vi]); lw = Mathf.Min(lw, p.y - rig.transform.position.y);
        float best = float.MaxValue; int bj = -1;
        for (int j = 0; j < bv.Length; j += 2) { if (armIdx.Contains(bwt[j].boneIndex0)) continue; float d = (smr.transform.TransformPoint(bv[j]) - p).sqrMagnitude; if (d < best) { best = d; bj = j; } }
        if (bj >= 0 && Mathf.Sqrt(best) < 0.08f * H && Vector3.Dot(p - smr.transform.TransformPoint(bv[bj]), smr.transform.TransformDirection(bn[bj])) < 0f) { ins++; var bn2 = smr.bones[bwt[bj].boneIndex0].name.Replace("mixamorig:", ""); parts[bn2] = parts.TryGetValue(bn2, out var c0) ? c0 + 1 : 1; }
      }
      float pct = 200f * ins / vs.Length; if (pct > worstIn) { worstIn = pct; worstAt = f; } if (pct > 0.5f) overFrames += f + " "; if (lw < low) { low = lw; lowAt = f; }
      var gM = mf.transform.InverseTransformPoint(anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).TransformPoint(pivot));
      var far = mf.transform.TransformPoint(new Vector3(gM.x, mb.max.y - gM.y >= gM.y - mb.min.y ? mb.max.y : mb.min.y, gM.z));
      if (f > 0) { float st = (far - prevEnd).magnitude / H; if (st > maxStep) { maxStep = st; stepAt = f; } }
      prevEnd = far;
    }
    log.AppendLine(job.clip + " [" + job.item + "] " + (total + 1) + " poses, in hand to " + job.hand60 + ": most inside the body " + worstIn.ToString("F1") + "% at " + worstAt + " (over 0.5% at: " + overFrames + "), lowest point " + (low / H * 100f).ToString("F1") + "% at " + lowAt +
      ", far end's biggest move in one pose " + (maxStep * 100f).ToString("F0") + "% at " + stepAt + " | " + string.Join(" ", System.Linq.Enumerable.Select(parts, kv => kv.Key + "=" + kv.Value)));
    hph.Dispose();
  } catch (System.Exception e) { log.AppendLine(job.clip + " ERR " + e); }
  finally { if (w) UnityEngine.Object.DestroyImmediate(w); if (bm) UnityEngine.Object.DestroyImmediate(bm); UnityEngine.Object.DestroyImmediate(rig); }
}
return log.ToString();
