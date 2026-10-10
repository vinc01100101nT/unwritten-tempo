// Where the held weapon is, relative to the hips, at the put-away clip's handover frame — the resting place that makes
// the handover seamless. Measured on the default body and the Knight.
var log = new System.Text.StringBuilder();
const string CLIP = "<Weapon>Sheathe"; const string ITEM = "<Weapon item>"; const int HANDOVER60 = 0;   // the hand's handover frame, at 60 per second
const int PAIR_HANDOVER60 = 0;   // a pair weapon's other copy: its hand's handover frame (prints stowPair*)
var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/" + CLIP + ".anim");
var item = AssetDatabase.LoadAssetAtPath<Item>("Assets/3_Data/Resources/Items/" + ITEM + ".asset");
var grip = item.equipModel.GetComponent<WeaponGrip>();
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
float H = CharacterSpec.Active.bodyHeight;
foreach (var modelPath in new[] { Player3DVisualBuilder.DefaultModel, Player3DVisualBuilder.RiggedDir + "/Knight_Male.fbx" }) {
  var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
  Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
  var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
  try {
    rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar;
    var hph = new HumanPoseHandler(avatar, rig.transform);
    var restHp = new HumanPose(); hph.GetHumanPose(ref restHp);
    // A pair weapon's other copy sits in the other hand mirrored, exactly as the game's hand mounts it (WeaponSocket).
    bool pair = WeaponTypes.IsDualWield(item.weaponType);
    foreach (var (frame60, isPair) in pair ? new[] { (HANDOVER60, false), (PAIR_HANDOVER60, true) } : new[] { (HANDOVER60, false) }) {
      var hp = restHp;
      float t = frame60 / 60f; float rx = 0, ry = restHp.bodyPosition.y, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
      foreach (var b in AnimationUtility.GetCurveBindings(clip)) {
        float v = AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
        switch (b.propertyName) { case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue; case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue; }
        var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
        if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
      }
      hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized; hph.SetHumanPose(ref hp);
      var hand = HandSocketResolver.Find(anim, isPair ? (grip.hand == WeaponHand.Left ? WeaponHand.Right : WeaponHand.Left) : grip.hand);
      var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
      var gp = grip.localPosition; var gr = Quaternion.Euler(grip.localEuler);
      if (isPair) { gp.x = -gp.x; gr = new Quaternion(gr.x, -gr.y, -gr.z, gr.w); }
      var wpos = hand.TransformPoint(gp);
      var wrot = hand.rotation * gr;
      var lp = hips.InverseTransformPoint(wpos); var lr = Quaternion.Inverse(hips.rotation) * wrot;
      log.AppendLine(System.IO.Path.GetFileNameWithoutExtension(modelPath) + (isPair ? " stowPairPos=" : " stowPos=") + lp.ToString("F5") + (isPair ? " stowPairEul=" : " stowEul=") + lr.eulerAngles.ToString("F3") + " hand/hips scale=" + (hand.lossyScale.x / hips.lossyScale.x).ToString("F4"));
    }
    hph.Dispose();
  } finally { UnityEngine.Object.DestroyImmediate(rig); }
}
return log.ToString();
