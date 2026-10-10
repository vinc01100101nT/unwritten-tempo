// A resting place DESIGNED on the body, for a weapon no hand carries there (the staff and the wand move to the back by
// themselves): the model's middle is put at a point of the body, its long axis along a direction and its thin side
// toward the body, in the pose of a clip — and the place is printed relative to the hips, as the weapon's grip saves it
// (stowPosition / stowEuler), for the default body and the Knight the videos wear. A weapon a hand DOES carry gets
// its place from measure-handover.cs instead. Nothing is saved.
// Fill: __ITEM__, __CLIP__ and __FRAME60__ (the body's pose), __MIDDLE__ (the point the model's middle sits at, share
// of body height, character frame: x right, y up, z forward), __TIP__ (the direction its tip points), __OUT__ (the
// direction its thin side faces — away from the body).
var log = new System.Text.StringBuilder();
const string ITEM = "__ITEM__", CLIP = "__CLIP__"; const int FRAME60 = __FRAME60__;
var MIDDLE = new Vector3(__MIDDLE__); var TIP = new Vector3(__TIP__).normalized; var OUT = new Vector3(__OUT__).normalized;
var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/" + CLIP + ".anim");
var item = AssetDatabase.LoadAssetAtPath<Item>("Assets/3_Data/Resources/Items/" + ITEM + ".asset");
if (clip == null || item == null || item.equipModel == null) return "MISSING";
var grip = item.equipModel.GetComponent<WeaponGrip>();
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
float H = CharacterSpec.Active.bodyHeight;
foreach (var modelPath in new[] { Player3DVisualBuilder.DefaultModel, Player3DVisualBuilder.RiggedDir + "/Knight_Male.fbx" }) {
  var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
  Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
  var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave; GameObject w = null;
  try {
    rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar;
    var hph = new HumanPoseHandler(avatar, rig.transform); var hp = new HumanPose(); hph.GetHumanPose(ref hp);
    float t = FRAME60 / 60f; float rx = 0, ry = hp.bodyPosition.y, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
    foreach (var b in AnimationUtility.GetCurveBindings(clip)) {
      if (b.type != typeof(Animator)) continue;
      float v = AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
      switch (b.propertyName) { case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue; case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue; }
      var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
      if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
    }
    hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized; hph.SetHumanPose(ref hp); hph.Dispose();
    var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var hand = HandSocketResolver.Find(anim, grip.hand);
    // which end of the model is the tip: the one further from where the fist holds it, as the build script reads it
    w = (GameObject)UnityEngine.Object.Instantiate(item.equipModel); w.hideFlags = HideFlags.HideAndDontSave;
    w.transform.SetParent(hand, false); w.transform.localPosition = grip.localPosition; w.transform.localRotation = Quaternion.Euler(grip.localEuler); w.transform.localScale = grip.localScale;
    var mf = w.GetComponentInChildren<MeshFilter>(); var mb = mf.sharedMesh.bounds;
    var fist = anim.GetBoneTransform(grip.hand == WeaponHand.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).TransformPoint(grip.hand == WeaponHand.Left ? new Vector3(-0.011f, 0.18756053f, 0.12156433f) : new Vector3(0.011f, 0.18756053f, 0.12156433f));
    var gM = mf.transform.InverseTransformPoint(fist); bool tipUp = mb.max.y - gM.y >= gM.y - mb.min.y;
    // on the hips: the model's long axis along TIP, its thin side (the mesh's z) along OUT, its middle at MIDDLE
    w.transform.SetParent(hips, false); w.transform.localScale = grip.localScale;
    var wantTip = (rig.transform.right * TIP.x + Vector3.up * TIP.y + rig.transform.forward * TIP.z).normalized;
    var wantOut = (rig.transform.right * OUT.x + Vector3.up * OUT.y + rig.transform.forward * OUT.z); wantOut = (wantOut - Vector3.Dot(wantOut, wantTip) * wantTip).normalized;
    var meshRot = Quaternion.LookRotation(wantOut, tipUp ? wantTip : -wantTip);   // the mesh's z along OUT, its y along the long axis
    w.transform.rotation = meshRot * Quaternion.Inverse(Quaternion.Inverse(w.transform.rotation) * mf.transform.rotation);
    var wantMid = rig.transform.position + (rig.transform.right * MIDDLE.x + Vector3.up * MIDDLE.y + rig.transform.forward * MIDDLE.z) * H;
    w.transform.position += wantMid - mf.transform.TransformPoint(mb.center);
    float low = float.MaxValue, high = float.MinValue; foreach (var v in mf.sharedMesh.vertices) { float y = (mf.transform.TransformPoint(v).y - rig.transform.position.y) / H; low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
    log.AppendLine(System.IO.Path.GetFileNameWithoutExtension(modelPath) + " stowPos=" + w.transform.localPosition.ToString("F5") + " stowEul=" + w.transform.localEulerAngles.ToString("F3") + " lowest " + low.ToString("F3") + " highest " + high.ToString("F3") + " of body height; hand/hips scale=" + (hand.lossyScale.x / hips.lossyScale.x).ToString("F4"));
  } finally { if (w) UnityEngine.Object.DestroyImmediate(w); UnityEngine.Object.DestroyImmediate(rig); }
}
return log.ToString();
