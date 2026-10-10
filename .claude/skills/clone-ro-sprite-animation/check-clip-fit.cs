// Does a weapon's CURRENT model still fit its SAVED clips? Plays each clip with the item's model in the hand (a pair
// weapon in both, the second mirrored the way the game mounts it) on the default body and reports, per clip, at 30
// frames per second: how much of the weapon is inside the body, how far its lowest point is from the ground, and —
// for a put-away or take-out clip — how far each copy jumps and turns when it moves between the hand and its saved
// resting place at the handover. Run it after ANY change to a weapon's model, size or hand fit. Nothing is saved.
// Fill: __ITEM__ (item asset name), __CLIPS__ ("AttackAxe", "IdleAxe", ...), __HANDOVERS__ (("AxeSheathe", 24, 24, true),
// ("AxeUnsheathe", 20, 20, false), ... = clip, the main hand's handover frame and the other hand's at 60 per second,
// and whether the weapon goes TO the body there; leave empty for none). In such a clip a copy is counted only while
// it is in its hand — after a put-away's handover (before a take-out's) the game shows it on the body.
var log = new System.Text.StringBuilder();
const string ITEM = "__ITEM__";
var clips = new string[] { __CLIPS__ };
var handovers = new (string clip, int main60, int pair60, bool toBody)[] { __HANDOVERS__ };
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
float H = CharacterSpec.Active.bodyHeight;
var item = AssetDatabase.LoadAssetAtPath<Item>("Assets/3_Data/Resources/Items/" + ITEM + ".asset"); var grip = item.equipModel.GetComponent<WeaponGrip>();
bool pair = WeaponTypes.IsDualWield(item.weaponType);
var spinRow = StanceCatalog.Weapon(item.weaponType);
AnimationClip FindClip(string n) {
  foreach (var c in CharacterSpec.Active.animatorController.animationClips) if (c != null && c.name == n) return c;
  return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/" + n + ".anim");
}
void Pose(HumanPoseHandler hph, AnimationClip clip, EditorCurveBinding[] binds, AnimationCurve[] curves, float t, bool inPlace) {
  var hp = new HumanPose(); hph.GetHumanPose(ref hp);
  float rx = 0, ry = hp.bodyPosition.y, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
  for (int b = 0; b < binds.Length; b++) {
    if (binds[b].type != typeof(Animator)) continue;
    float v = curves[b].Evaluate(t);
    switch (binds[b].propertyName) { case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue; case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue; }
    var nm = binds[b].propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
    if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
  }
  hp.bodyPosition = inPlace ? new Vector3(0, ry, 0) : new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized; hph.SetHumanPose(ref hp);
}
var spinSheathe = spinRow != null && !string.IsNullOrEmpty(spinRow.sheatheClip) ? FindClip(spinRow.sheatheClip) : null;
foreach (var name in clips) {
  var clip = FindClip(name); if (clip == null) { log.AppendLine(name + " MISSING"); continue; }
  var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
  var bodyMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
  try {
    rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar;
    var hph = new HumanPoseHandler(avatar, rig.transform);
    var hand = HandSocketResolver.Find(anim, grip.hand);
    var otherHand = HandSocketResolver.Find(anim, grip.hand == WeaponHand.Left ? WeaponHand.Right : WeaponHand.Left);
    var w = (GameObject)UnityEngine.Object.Instantiate(item.equipModel); w.hideFlags = HideFlags.HideAndDontSave;
    var gr = Quaternion.Euler(grip.localEuler); var grP = new Quaternion(gr.x, -gr.y, -gr.z, gr.w);
    var gpP = new Vector3(-grip.localPosition.x, grip.localPosition.y, grip.localPosition.z); var gsP = new Vector3(-grip.localScale.x, grip.localScale.y, grip.localScale.z);
    w.transform.SetParent(hand, false); w.transform.localPosition = grip.localPosition; w.transform.localRotation = gr; w.transform.localScale = grip.localScale;
    var mf = w.GetComponentInChildren<MeshFilter>(); var verts = mf.sharedMesh.vertices;
    var meshes = new System.Collections.Generic.List<Transform> { mf.transform };
    if (pair) {
      var w2 = (GameObject)UnityEngine.Object.Instantiate(item.equipModel); w2.hideFlags = HideFlags.HideAndDontSave; w2.transform.SetParent(otherHand, false);
      w2.transform.localPosition = gpP; w2.transform.localRotation = grP; w2.transform.localScale = gsP;
      meshes.Add(w2.GetComponentInChildren<MeshFilter>().transform);
    }
    var smr = rig.GetComponentInChildren<SkinnedMeshRenderer>(); var bw8 = smr.sharedMesh.boneWeights;
    var armIdx = new System.Collections.Generic.HashSet<int>();   // a held weapon lies against its own arm: the arms' points are left out
    foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftShoulder, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightShoulder }) { var t0 = anim.GetBoneTransform(hb); if (t0 != null) armIdx.Add(System.Array.IndexOf(smr.bones, t0)); }
    int step = Mathf.Max(1, verts.Length / 500);
    var binds = AnimationUtility.GetCurveBindings(clip); var curves = new AnimationCurve[binds.Length];
    for (int b = 0; b < binds.Length; b++) curves[b] = AnimationUtility.GetEditorCurve(clip, binds[b]);
    int frames = Mathf.RoundToInt(clip.length * 30f); float groundY = float.MaxValue;
    float worstIn = 0, lowest = 99; int inAt = 0, lowAt = 0, framesIn = 0, framesUnder = 0;
    var parts = new System.Collections.Generic.Dictionary<string, int>();
    bool hasHo = false; (string clip, int main60, int pair60, bool toBody) mine = default;
    foreach (var ho in handovers) if (ho.clip == name) { hasHo = true; mine = ho; }
    bool InHand(int copy, int fi) { if (!hasHo) return true; float at = (copy == 0 ? mine.main60 : mine.pair60) / 60f, t = fi / 30f; return mine.toBody ? t < at : t >= at; }
    for (int fi = 0; fi <= frames; fi++) {
      Pose(hph, clip, binds, curves, Mathf.Min(fi / 30f, clip.length), true);
      // a weapon spun during its put-away (its stance library row's spin settings) sits where the game draws it
      if (spinRow != null && spinSheathe != null && (name == spinRow.sheatheClip || name == spinRow.unsheatheClip)
          && WeaponSocket.SpinWindow(spinRow, spinSheathe.length * spinSheathe.frameRate, out var sFrom, out var sTo)) {
        float prog = clip.length > 0f ? Mathf.Min(fi / 30f, clip.length) / clip.length : 1f;
        float share = WeaponDrawMath.SpinWindowShare(name == spinRow.sheatheClip ? prog : 1f - prog, sFrom, sTo);
        WeaponSocket.SpunOverFist(grip, share, spinRow.spinTurns, spinRow.spinLift, out var sp, out var sr);
        w.transform.localPosition = sp; w.transform.localRotation = sr;
      }
      float feet = Mathf.Min(anim.GetBoneTransform(HumanBodyBones.LeftToes).position.y, anim.GetBoneTransform(HumanBodyBones.RightToes).position.y) - 0.01f * H;
      groundY = Mathf.Min(groundY, feet);   // the ground: the lowest the toes have been (1% for the sole)
      smr.BakeMesh(bodyMesh, true);
      var bv = bodyMesh.vertices; var bn = bodyMesh.normals; var bt = smr.transform;
      var bwp = new System.Collections.Generic.List<Vector3>(); var bnp = new System.Collections.Generic.List<Vector3>(); var bbi = new System.Collections.Generic.List<int>();
      for (int q = 0; q < bv.Length; q++) { if (armIdx.Contains(bw8[q].boneIndex0)) continue; bwp.Add(bt.TransformPoint(bv[q])); bnp.Add(bt.TransformDirection(bn[q])); bbi.Add(bw8[q].boneIndex0); }
      int n = 0, tot = 0; float low = 99;
      for (int mi2 = 0; mi2 < meshes.Count; mi2++) { if (!InHand(mi2, fi)) continue; var mt = meshes[mi2]; for (int vi = 0; vi < verts.Length; vi += step) {
        var p = mt.TransformPoint(verts[vi]); float best = float.MaxValue; int bj = -1; tot++; low = Mathf.Min(low, (p.y - groundY) / H);
        for (int q = 0; q < bwp.Count; q++) { float d = (bwp[q] - p).sqrMagnitude; if (d < best) { best = d; bj = q; } }
        if (bj >= 0 && Mathf.Sqrt(best) < 0.08f * H && Vector3.Dot(p - bwp[bj], bnp[bj]) < 0f) { n++; var pn = smr.bones[bbi[bj]].name.Replace("mixamorig:", ""); parts[pn] = parts.TryGetValue(pn, out var c0) ? c0 + 1 : 1; }
      } }
      if (tot == 0) continue;   // no copy in a hand in this frame
      float ins = n / (float)tot;
      if (ins > worstIn) { worstIn = ins; inAt = fi; } if (ins > 0.005f) framesIn++; if (low < lowest) { lowest = low; lowAt = fi; } if (low < -0.01f) framesUnder++;
    }
    log.AppendLine(name + " (" + (frames + 1) + " frames, " + item.equipModel.name + "): inside the body worst " + (worstIn * 100f).ToString("F1") + "% of the weapon's points at frame " + inAt + ", over 0.5% in " + framesIn + " frames (" + string.Join(" ", System.Linq.Enumerable.Select(parts, kv => kv.Key + "=" + kv.Value)) + "); lowest point " + (lowest * 100f).ToString("F0") + "% of body height from the ground at frame " + lowAt + ", under it in " + framesUnder + " frames");
    foreach (var ho in handovers) {
      if (ho.clip != name) continue;
      if (!grip.stowAuthored) { log.AppendLine("  " + name + ": the model has no resting place saved"); continue; }
      foreach (var (frame60, isPair) in pair ? new[] { (ho.main60, false), (ho.pair60, true) } : new[] { (ho.main60, false) }) {
        Pose(hph, clip, binds, curves, frame60 / 60f, false);
        var hnd = isPair ? otherHand : hand; var bone = anim.GetBoneTransform(grip.stowBone);
        var inHandPos = hnd.TransformPoint(isPair ? gpP : grip.localPosition); var inHandRot = hnd.rotation * (isPair ? grP : gr);
        var restPos = bone.TransformPoint(isPair ? grip.stowPairPosition : grip.stowPosition); var restRot = bone.rotation * Quaternion.Euler(isPair ? grip.stowPairEuler : grip.stowEuler);
        log.AppendLine("  " + name + (isPair ? " other copy" : " main copy") + " at " + frame60 + "/60: jumps " + ((inHandPos - restPos).magnitude / H * 100f).ToString("F1") + "% of body height and turns " + Quaternion.Angle(inHandRot, restRot).ToString("F0") + " deg at the handover");
      }
    }
    hph.Dispose();
  } catch (System.Exception e) { log.AppendLine(name + " ERR " + e); }
  finally { UnityEngine.Object.DestroyImmediate(rig); UnityEngine.Object.DestroyImmediate(bodyMesh); }
}
return log.ToString();
