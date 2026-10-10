var log = new System.Text.StringBuilder();
const int FPS = 60;
// THE WRISTS TURN SMOOTHLY: the three joints that turn a held weapon without moving the hand — the forearm's twist and
// the wrist's two bends — may change at most this between two stored poses (1 = a joint's whole natural reach), and
// the solver prefers the answer nearest the last frame for them and for the upper arm's twist. Solved one frame at a
// time with no memory, a wrist could flip from one end of its range to the other in a sixtieth of a second — the user
// saw the axe glitch. The shoulder and elbow are never held back: they carry the hand along its path.
const float ARM_STEP = 0.25f;
bool WRITE = __WRITE__;     // false = build in memory and compare with the saved clip; true = save
bool AIM = __AIM__;         // false = the plain build (no weapon pointing) — a clip with nothing in the hand
const string DIR = "Assets/1_Authored/Art/Characters/Animations/";
// name, weapon item, foot lifts on the first step, second hand on the same weapon, where that second hand grips
// (share of body height along the weapon from the first hand: + toward the tip, - toward the butt), and whether the
// weapon is a rifle stand-in held at its trigger grip in the right hand (no rifle model exists yet).
// loop = a standing idle that repeats (its first and last keys must match); smooth = ease in AND out between keys
// (a breath), instead of the default fast start and soft stop of a strike. steadyArms = the no-shake rule below is
// applied to EVERY arm joint (shoulder, upper arm, elbow), not only the wrists.
// hug = the weapon is LAID AGAINST the body in this clip (a resting pose on the back): the capsule estimate below may not
// push it off — capsules are wider than the real back (0.10–0.125 of body height against a measured 0.065), so every
// weapon handed over behind the body floated a hand's width off it. The report's count on the real mesh still shows
// any point that ends up inside.
var clips = new (string name, string item, bool stepLift, bool twoHand, float secondGrip, bool rifle, bool loop, bool smooth, bool steadyArms, float wristStep, bool hug, float[][] keys)[] {
__CLIPS__
};
const float RIFLE_STOCK = 0.12f, RIFLE_BARREL = 0.5f;   // stand-in rifle: stock behind and barrel ahead of the trigger grip, share of body height
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
AnimationClip idle = null;
foreach (var o in AssetDatabase.LoadAllAssetsAtPath(DIR + "Idle.fbx")) if (o is AnimationClip c && !c.name.StartsWith("__preview")) { idle = c; break; }
float H = CharacterSpec.Active.bodyHeight;
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
var idleBinds = AnimationUtility.GetCurveBindings(idle);
var wristR = new System.Collections.Generic.List<int>(); var wristL = new System.Collections.Generic.List<int>();
var twistR = new System.Collections.Generic.List<int>(); var twistL = new System.Collections.Generic.List<int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) {
  var mn = HumanTrait.MuscleName[m];
  foreach (var side in new[] { "Right", "Left" }) {
    if (mn == side + " Forearm Twist In-Out" || mn == side + " Hand Down-Up" || mn == side + " Hand In-Out") (side == "Right" ? wristR : wristL).Add(m);
    if (mn == side + " Arm Twist In-Out") (side == "Right" ? twistR : twistL).Add(m);
  }
}
var armMusR = new System.Collections.Generic.List<int>(); var armMusL = new System.Collections.Generic.List<int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) {
  var mn = HumanTrait.MuscleName[m];
  foreach (var side in new[] { "Right", "Left" })
    if (mn.StartsWith(side + " Arm") || mn.StartsWith(side + " Forearm") || mn.StartsWith(side + " Hand") || mn.StartsWith(side + " Shoulder")) (side == "Right" ? armMusR : armMusL).Add(m);
}
// The head's turn for a key's "look": the neck and the head share it; a muscle at 1 is LOOK_RANGE degrees of turn
// (the humanoid's default limit), and LOOK_SIGN makes a positive look turn the face to the character's left.
var lookMus = new System.Collections.Generic.List<int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) if (HumanTrait.MuscleName[m] == "Neck Turn Left-Right" || HumanTrait.MuscleName[m] == "Head Turn Left-Right") lookMus.Add(m);
const float LOOK_RANGE = 40f, LOOK_SIGN = -1f;   // measured 2026-10-10: a raw +1 turns the face to the right
Vector3 V(float[] k, int i) => new Vector3(k[i], k[i + 1], k[i + 2]);
// The two hand bones are mirror images across their local x (measured 2026-10-09).
Vector3 Mx(Vector3 v) => new Vector3(-v.x, v.y, v.z);
// Where the fist closes, in the right hand bone's space, and the turn a sword held there takes: the grip of the pack
// sword every weapon was first fitted to (sword_1_blue, removed 2026-10-10 - these are its saved values).
var FIST_GRIP = new Vector3(0.011f, 0.18756053f, 0.12156433f);
var SWORD_TURN = new Vector3(345.37842f, 86.71383f, 282.81427f);
// The striking side of each model in its own mesh space; -x unless listed. Trellis turns some models round, so these
// were carried from the model each one replaced (2026-10-10): the axe's edge on +z, the bow's front on +x. The
// scythe's blade is on -x like the rest (its shaft sits at x +0.16, the blade reaches x -0.28, measured on the mesh).
var EDGE = new System.Collections.Generic.Dictionary<string, Vector3> {
  { "axe_varkhaz", Vector3.forward }, { "bow_celestia", Vector3.right } };

foreach (var cl in clips) {
  // a table's "wristStep" lets its wrists turn further per pose (a fast swing whose weapon must keep up with the arm)
  float armStep = cl.wristStep > 0f ? cl.wristStep : ARM_STEP;
  var rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
  var temps = new System.Collections.Generic.List<GameObject>();
  try {
  rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
  var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar; anim.applyRootMotion = false;
  var hph = new HumanPoseHandler(avatar, rig.transform);
  Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
  Vector3 W(float x, float y, float z) => rig.transform.position + rig.transform.right * x * H + Vector3.up * y * H + rig.transform.forward * z * H;
  Vector3 Dir(Vector3 d) => (rig.transform.right * d.x + Vector3.up * d.y + rig.transform.forward * d.z).normalized;
  Vector3 ToChar(Vector3 w) => new Vector3(Vector3.Dot(w, rig.transform.right), w.y, Vector3.Dot(w, rig.transform.forward));
  Vector3 Local(Vector3 w) { var d = w - rig.transform.position; return new Vector3(Vector3.Dot(d, rig.transform.right), d.y, Vector3.Dot(d, rig.transform.forward)) / H; }
  void PoseIdle() {
    var hp = new HumanPose(); hph.GetHumanPose(ref hp); float ry = hp.bodyPosition.y;
    foreach (var b in idleBinds) {
      if (b.type != typeof(Animator)) continue;
      float v = AnimationUtility.GetEditorCurve(idle, b).Evaluate(0f);
      if (b.propertyName == "RootT.y") { ry = v; continue; }
      var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
      if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
    }
    hp.bodyPosition = new Vector3(0, ry, 0); hp.bodyRotation = Quaternion.identity;
    hph.SetHumanPose(ref hp);
  }
  void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 poleHint, bool keepWorldEnd) {
    var keepW = c.rotation; var keepL = c.localRotation;
    float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
    Vector3 at = target - a.position; float d = Mathf.Clamp(at.magnitude, 1e-5f, (la + lb) * 0.999f);
    Vector3 dir = at.normalized;
    Vector3 bend = poleHint - Vector3.Dot(poleHint, dir) * dir;
    if (bend.sqrMagnitude < 1e-10f) bend = b.position - a.position - Vector3.Dot(b.position - a.position, dir) * dir;
    bend.Normalize();
    float A = Mathf.Acos(Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1f, 1f));
    Vector3 wantB = a.position + dir * Mathf.Cos(A) * la + bend * Mathf.Sin(A) * la;
    a.rotation = Quaternion.FromToRotation(b.position - a.position, wantB - a.position) * a.rotation;
    b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
    if (keepWorldEnd) c.rotation = keepW; else c.localRotation = keepL;
  }
  float ArmLen(HumanBodyBones u, HumanBodyBones l, HumanBodyBones h) => (B(l).position - B(u).position).magnitude + (B(h).position - B(l).position).magnitude;
  Transform HandOf(bool left) => B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
  Transform UpperOf(bool left) => B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
  Transform LowerOf(bool left) => B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);

  // ── The weapon's own axes, in the wielding hand's space. Every model in the pack is built along y with its head,
  // edge or bow-front on -x, read off pictures of each mesh on 2026-10-09 (EDGE lists the exceptions).
  Vector3 tipH = Vector3.zero, edgeH = Vector3.zero, gripH = Vector3.zero; float buttLen = 0f, tipLen = 0f; bool hasWeapon = false;
  var item = AssetDatabase.LoadAssetAtPath<Item>("Assets/3_Data/Resources/Items/" + cl.item + ".asset");
  var wp = item != null ? item.equipModel : null;
  var grip = wp != null ? wp.GetComponent<WeaponGrip>() : null;
  bool LEFT = !cl.rifle && grip != null && grip.hand == WeaponHand.Left;
  // A pair weapon: the same model in the other hand, mirrored — the one list the game's hand reads too.
  bool PAIR = !cl.rifle && item != null && WeaponTypes.IsDualWield(item.weaponType);
  var mainHand = HandOf(LEFT);
  var meshPts = new System.Collections.Generic.List<(Transform t, Vector3[] v)>();   // weapon points, for the body check
  if (AIM && cl.rifle) {
    // Held like a rifle at its trigger grip: the barrel runs along the hand's length, from wrist to knuckles, and the
    // gun's top is the thumb side — the way a sword's blade leaves the fist.
    var tmpPose = new HumanPose(); hph.GetHumanPose(ref tmpPose); for (int m = 0; m < tmpPose.muscles.Length; m++) tmpPose.muscles[m] = 0f; hph.SetHumanPose(ref tmpPose);
    tipH = mainHand.InverseTransformDirection(mainHand.position - LowerOf(LEFT).position).normalized;
    var thumb = (Quaternion.Euler(SWORD_TURN) * Vector3.up).normalized;
    edgeH = (thumb - Vector3.Dot(thumb, tipH) * tipH).normalized;
    gripH = FIST_GRIP;
    buttLen = RIFLE_STOCK * H; tipLen = RIFLE_BARREL * H;
    hasWeapon = true;
    log.AppendLine(cl.name + ": rifle stand-in, right hand, barrel(hand)=" + tipH.ToString("F2") + " top(hand)=" + edgeH.ToString("F2"));
  } else if (AIM && wp != null && grip != null) {
    var hand = HandSocketResolver.Find(anim, grip.hand);
    var weapon = (GameObject)UnityEngine.Object.Instantiate(wp); weapon.hideFlags = HideFlags.HideAndDontSave; temps.Add(weapon);
    weapon.transform.SetParent(hand, false);
    weapon.transform.localPosition = grip.localPosition; weapon.transform.localRotation = Quaternion.Euler(grip.localEuler); weapon.transform.localScale = grip.localScale;
    var mf = weapon.GetComponentInChildren<MeshFilter>();
    var mb = mf.sharedMesh.bounds;
    // Where the fist closes: every pack weapon is gripped at the same spot in the hand as the seed sword, wherever along
    // its own length that falls (the staff and wand are held a third of the way up, not at their middle).
    gripH = FIST_GRIP;
    var gM = mf.transform.InverseTransformPoint(mainHand.TransformPoint(gripH));
    var tipM = mb.max.y - gM.y >= gM.y - mb.min.y ? Vector3.up : Vector3.down;   // the tip is whichever end reaches further from the grip
    tipH = mainHand.InverseTransformDirection(mf.transform.TransformDirection(tipM)).normalized;
    edgeH = mainHand.InverseTransformDirection(mf.transform.TransformDirection(EDGE.TryGetValue(wp.name, out var edgeM) ? edgeM : Vector3.left)).normalized;
    buttLen = (mf.transform.TransformPoint(new Vector3(0, tipM.y > 0 ? mb.min.y : mb.max.y, 0)) - mainHand.TransformPoint(gripH)).magnitude;
    tipLen = (mf.transform.TransformPoint(new Vector3(0, tipM.y > 0 ? mb.max.y : mb.min.y, 0)) - mainHand.TransformPoint(gripH)).magnitude;
    meshPts.Add((mf.transform, mf.sharedMesh.vertices));
    if (PAIR) {
      var other = HandSocketResolver.Find(anim, LEFT ? WeaponHand.Right : WeaponHand.Left);
      var w2 = (GameObject)UnityEngine.Object.Instantiate(wp); w2.hideFlags = HideFlags.HideAndDontSave; temps.Add(w2);
      w2.transform.SetParent(other, false);
      var r = Quaternion.Euler(grip.localEuler);
      w2.transform.localPosition = Mx(grip.localPosition); w2.transform.localRotation = new Quaternion(r.x, -r.y, -r.z, r.w);
      w2.transform.localScale = new Vector3(-grip.localScale.x, grip.localScale.y, grip.localScale.z);
      var mf2 = w2.GetComponentInChildren<MeshFilter>(); meshPts.Add((mf2.transform, mf2.sharedMesh.vertices));
    }
    hasWeapon = true;
    log.AppendLine(cl.name + ": weapon " + wp.name + (LEFT ? " LEFT hand" : " right hand") + (PAIR ? " + the same in the other hand" : "") + " tip(hand)=" + tipH.ToString("F2") + " edge(hand)=" + edgeH.ToString("F2"));
  }

  PoseIdle();
  var iRF = Local(B(HumanBodyBones.RightFoot).position); var iLF = Local(B(HumanBodyBones.LeftFoot).position);
  float groundY = Mathf.Min(B(HumanBodyBones.RightFoot).position.y, B(HumanBodyBones.LeftFoot).position.y);
  float armR = ArmLen(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
  float armL = ArmLen(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
  var iRV = B(HumanBodyBones.RightHand).position - B(HumanBodyBones.RightUpperArm).position; var iLV = B(HumanBodyBones.LeftHand).position - B(HumanBodyBones.LeftUpperArm).position;
  var iRD = ToChar(iRV).normalized; float iRR = iRV.magnitude / armR; var iLD = ToChar(iLV).normalized; float iLR = iLV.magnitude / armL;
  float EaseOut(float t) => 1f - Mathf.Pow(1f - t, 3f);
  var keys = cl.keys; int K = keys.Length;
  Vector3 Perp(Vector3 v, Vector3 n) { var p = v - Vector3.Dot(v, n) * n; return p.sqrMagnitude > 1e-6f ? p.normalized : Vector3.zero; }

  // ── Each key's weapon direction (character space), for the hand on one side. The tip continues that arm unless the
  // table says otherwise (rw/re for the main hand, lw/le for the pair's other hand; an idle key may carry one too). The
  // striking side faces along the WHOLE cut: the travel of the biggest swing in the clip decides it once, so the blade
  // never rolls in the air between keys (the user, 2026-10-09: "consistent sharp edge facing where it will slash").
  (Vector3[] tips, Vector3[] edges) Plan(bool leftHand, bool mainOne) {
    int tipAt = mainOne ? 20 : 28, edgeAt = mainOne ? 24 : 32, armAt = leftHand ? 16 : 12;
    var hb = HandOf(leftHand);
    var tL = mainOne ? tipH : Mx(tipH); var eL = mainOne ? edgeH : Mx(edgeH);
    var iTip = ToChar(hb.TransformDirection(tL)).normalized; var iEdge = ToChar(hb.TransformDirection(eL)).normalized;
    var tips = new Vector3[K]; var edges = new Vector3[K];
    for (int k = 0; k < K; k++) {
      bool isIdle = keys[k][1] > 0.5f;
      tips[k] = keys[k][tipAt] > 0.5f ? V(keys[k], tipAt + 1).normalized : isIdle ? iTip : V(keys[k], armAt).normalized;
    }
    int strike = -1; float big = 0f;
    for (int k = 0; k + 1 < K; k++) { if (keys[k][1] > 0.5f && keys[k][tipAt] < 0.5f) continue; float ang = Vector3.Angle(tips[k], tips[k + 1]); if (ang > big) { big = ang; strike = k; } }
    var travel = strike >= 0 && big >= 20f ? tips[strike + 1] - tips[strike] : Vector3.zero;
    for (int k = 0; k < K; k++) {
      bool isIdle = keys[k][1] > 0.5f;
      Vector3 e;
      if (keys[k][edgeAt] > 0.5f) e = Perp(V(keys[k], edgeAt + 1).normalized, tips[k]);
      else if (isIdle && keys[k][tipAt] < 0.5f) e = iEdge;
      else if (travel != Vector3.zero) e = Perp(travel, tips[k]);
      else e = k > 0 ? Perp(edges[k - 1], tips[k]) : Perp(iEdge, tips[k]);
      if (e == Vector3.zero) e = Perp(Vector3.up, tips[k]);
      if (e == Vector3.zero) e = Perp(Vector3.forward, tips[k]);
      edges[k] = e;
    }
    return (tips, edges);
  }
  (Vector3[] tips, Vector3[] edges) planMain = hasWeapon ? Plan(LEFT, true) : default;
  (Vector3[] tips, Vector3[] edges) planPair = hasWeapon && PAIR ? Plan(!LEFT, false) : default;

  // ── The body as capsules, each sized from the character's own mesh: a bone's points are measured from its segment at
  // the idle pose and the 95th-percentile distance is its radius (the 80th left the head and thighs too thin). The weapon may not pass through the torso, the head
  // or the legs; the arms are left out, since a held weapon lies against its own arm.
  var bodyS = rig.GetComponentInChildren<SkinnedMeshRenderer>();
  var capDefs = new[] { (HumanBodyBones.Hips, HumanBodyBones.Spine), (HumanBodyBones.Spine, HumanBodyBones.Chest), (HumanBodyBones.Chest, HumanBodyBones.Neck),
                        (HumanBodyBones.Neck, HumanBodyBones.Head), (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) };
  var capR = new float[capDefs.Length]; var headC = Vector3.zero; float headR = 0f;
  {
    PoseIdle();
    var bm = new Mesh(); bodyS.BakeMesh(bm, true); var vv = bm.vertices; var bwts = bodyS.sharedMesh.boneWeights;
    float Seg(Vector3 p, Vector3 a0, Vector3 b0) { var ab = b0 - a0; float u = Mathf.Clamp01(Vector3.Dot(p - a0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f)); return (p - (a0 + ab * u)).magnitude; }
    float P95(System.Collections.Generic.List<float> d) { if (d.Count == 0) return 0f; d.Sort(); return d[(int)(d.Count * 0.95f)]; }
    for (int ci = 0; ci < capDefs.Length; ci++) {
      var ta = B(capDefs[ci].Item1); var tb = B(capDefs[ci].Item2); if (ta == null || tb == null) continue;
      var ds = new System.Collections.Generic.List<float>();
      for (int j = 0; j < vv.Length; j++) { var bone = bodyS.bones[bwts[j].boneIndex0]; if (bone == ta || (capDefs[ci].Item1 == HumanBodyBones.Spine && bone.name.Contains("Spine1"))) ds.Add(Seg(bodyS.transform.TransformPoint(vv[j]), ta.position, tb.position)); }
      capR[ci] = P95(ds);
    }
    var head = B(HumanBodyBones.Head); var hs = new System.Collections.Generic.List<Vector3>();
    for (int j = 0; j < vv.Length; j++) if (bodyS.bones[bwts[j].boneIndex0] == head) hs.Add(bodyS.transform.TransformPoint(vv[j]));
    var cen = Vector3.zero; foreach (var q in hs) cen += q; cen /= Mathf.Max(1, hs.Count);
    var hd = new System.Collections.Generic.List<float>(); foreach (var q in hs) hd.Add((q - cen).magnitude);
    headC = head.InverseTransformPoint(cen); headR = P95(hd);
    UnityEngine.Object.DestroyImmediate(bm);
    log.AppendLine(cl.name + ": body capsule radii (share of body height) " + string.Join(" ", System.Array.ConvertAll(capR, r => (r / H).ToString("F3"))) + " head " + (headR / H).ToString("F3"));
  }
  // How deep the weapon's shaft (butt to tip, 13 points) sinks into the body, summed, as a share of body height.
  float Sink(Transform hand, Vector3 tLocal, Vector3 gLocal) {
    var g = hand.TransformPoint(gLocal); var dir = hand.TransformDirection(tLocal).normalized; float sum = 0f;
    for (int si = 0; si <= 12; si++) {
      float s0 = Mathf.Lerp(-buttLen, tipLen, si / 12f); var p = g + dir * s0;
      for (int ci = 0; ci < capDefs.Length; ci++) {
        if (capR[ci] <= 0f) continue;
        var a0 = B(capDefs[ci].Item1).position; var b0 = B(capDefs[ci].Item2).position; var ab = b0 - a0;
        float u = Mathf.Clamp01(Vector3.Dot(p - a0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
        sum += Mathf.Max(0f, capR[ci] - (p - (a0 + ab * u)).magnitude);
      }
      sum += Mathf.Max(0f, headR - (p - B(HumanBodyBones.Head).TransformPoint(headC)).magnitude);
    }
    return sum / H;
  }

  // ── Turn one hand so its weapon points along (wTip, wEdge), swinging the elbow around the shoulder-to-wrist line
  // (the wrist stays put) and keeping every arm joint within its natural range; the elbow may not fold forward-and-in.
  // The elbow's swing around the shoulder-to-wrist line chosen last frame, per arm (999 = none yet). Each frame may only
  // swing it 20 degrees further: the swing never moves the wrist, so this costs the hand's path nothing, and without it
  // the elbow could jump to the far side between two frames (the upper arm's twist flipped by more than its whole reach).
  int prevSwR = 999, prevSwL = 999;
  (float tipErr, float edgeErr, bool bad, float sink) Aim(ref HumanPose hp, bool leftHand, Vector3 tLocal, Vector3 eLocal, Vector3 gLocal, Vector3 wTip, Vector3 wEdge, Vector3 fwd, Vector3 right, float[] prev, bool elbowSet) {
    var hb = HandOf(leftHand); var ua = UpperOf(leftHand); var la = LowerOf(leftHand); var mus = leftHand ? armMusL : armMusR;
    var Fh = Quaternion.LookRotation(tLocal, eLocal);
    var basePose = hp; var baseMus = (float[])hp.muscles.Clone();
    float bestScore = float.MaxValue; float[] bestMus = null; float bTip = 0, bEdge = 0, bSink = 0; bool bBad = false; var bPos = hp.bodyPosition; var bRot = hp.bodyRotation; int bSw = 0;
    var side1 = Vector3.Cross(wTip, Mathf.Abs(Vector3.Dot(wTip, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
    void Try(Vector3 cTip) {
      var cEdge = wEdge - Vector3.Dot(wEdge, cTip) * cTip; if (cEdge.sqrMagnitude < 1e-6f) cEdge = side1;
      var Fw = Quaternion.LookRotation(cTip, cEdge);
      int prevSw = leftHand ? prevSwL : prevSwR;
      int swLo = prevSw == 999 ? -90 : Mathf.Max(-90, prevSw - 20), swHi = prevSw == 999 ? 90 : Mathf.Min(90, prevSw + 20);
      // A key that says where this elbow goes ("rp" / "lp") keeps it there: no swing to chase the aim.
      if (elbowSet) { swLo = 0; swHi = 0; }
      for (int sw = swLo; sw <= swHi; sw += 10) {
        basePose.muscles = (float[])baseMus.Clone(); hph.SetHumanPose(ref basePose);
        var axisW = (hb.position - ua.position).normalized;
        ua.rotation = Quaternion.AngleAxis(sw, axisW) * ua.rotation;
        hb.rotation = Fw * Quaternion.Inverse(Fh);
        var trial = new HumanPose(); hph.GetHumanPose(ref trial);
        foreach (var am in mus) trial.muscles[am] = Mathf.Clamp(trial.muscles[am], -1f, 1f);
        float jump = 0f;
        if (prev != null) {
          foreach (var am in leftHand ? wristL : wristR) { trial.muscles[am] = Mathf.Clamp(trial.muscles[am], prev[am] - armStep, prev[am] + armStep); jump += Mathf.Abs(trial.muscles[am] - prev[am]); }
          foreach (var am in leftHand ? twistL : twistR) jump += Mathf.Abs(trial.muscles[am] - prev[am]);
        }
        hph.SetHumanPose(ref trial);
        float eTip = Vector3.Angle(hb.TransformDirection(tLocal), wTip), eEdge = Vector3.Angle(hb.TransformDirection(eLocal), wEdge);
        var sh = ua.position; var el = la.position; var wr = hb.position; var line = (wr - sh).normalized;
        var bend = (el - sh) - Vector3.Dot(el - sh, line) * line; bend = bend.sqrMagnitude > 1e-10f ? bend.normalized : Vector3.zero;
        bool bad = Vector3.Dot(bend, fwd) > 0.3f && Vector3.Dot(bend, leftHand ? right : -right) > 0.3f;
        float sink = cl.hug ? 0f : Sink(hb, tLocal, gLocal);
        float score = eTip + 0.4f * eEdge + (bad ? 1000f : 0f) + Mathf.Abs(sw) * 0.05f + sink * 2000f + jump * 20f + (prevSw == 999 ? 0f : Mathf.Abs(sw - prevSw) * 0.1f);
        if (score < bestScore) { bestScore = score; bestMus = (float[])trial.muscles.Clone(); bTip = eTip; bEdge = eEdge; bBad = bad; bSink = sink; bPos = trial.bodyPosition; bRot = trial.bodyRotation; bSw = sw; }
      }
    }
    Try(wTip);
    // Only when the planned direction sinks the weapon into the body: tilt it up to 90 degrees off the plan, 8 ways.
    if (bSink > 0f)
      foreach (float tilt in new[] { 15f, 30f, 45f, 60f, 75f, 90f })
        for (int dI = 0; dI < 8; dI++) {
          var axis = Quaternion.AngleAxis(dI * 45f, wTip) * side1;
          Try((Quaternion.AngleAxis(tilt, axis) * wTip).normalized);
        }
    hp.muscles = bestMus; hp.bodyPosition = bPos; hp.bodyRotation = bRot; hph.SetHumanPose(ref hp);
    if (leftHand) prevSwL = bSw; else prevSwR = bSw;
    return (bTip, bEdge, bBad, bSink);
  }

  // ── The body check: weapon points inside the character. Each weapon point is tested against the nearest point of
  // the body's own mesh (baked this frame) — on the inner side of its surface means inside.
  var bodySmr = rig.GetComponentInChildren<SkinnedMeshRenderer>(); var bodyMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
  // A held weapon lies against its own arm, so the arms' points are left out: this counts the torso, the head and the legs.
  var handIdx = new System.Collections.Generic.HashSet<int>();
  foreach (var arm in new[] { false, true }) foreach (var t0 in new[] { HandOf(arm), LowerOf(arm), UpperOf(arm), B(arm ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder) }) if (t0 != null) handIdx.Add(System.Array.IndexOf(bodySmr.bones, t0));
  var bodyBw = bodySmr.sharedMesh.boneWeights;
  var insideBones = new System.Collections.Generic.Dictionary<string, int>();
  int Inside(bool tally) {
    if (meshPts.Count == 0) return 0;
    bodySmr.BakeMesh(bodyMesh, true);
    var bv = bodyMesh.vertices; var bn = bodyMesh.normals; var bt = bodySmr.transform;
    var bw = new Vector3[bv.Length]; var bnw = new Vector3[bv.Length];
    for (int j = 0; j < bv.Length; j++) { bw[j] = bt.TransformPoint(bv[j]); bnw[j] = bt.TransformDirection(bn[j]); }
    int n = 0;
    foreach (var (t, vs) in meshPts)
      foreach (var v in vs) {
        var p = t.TransformPoint(v); float best = float.MaxValue; int bj = -1;
        for (int j = 0; j < bw.Length; j++) { if (handIdx.Contains(bodyBw[j].boneIndex0)) continue; float d = (bw[j] - p).sqrMagnitude; if (d < best) { best = d; bj = j; } }
        if (bj >= 0 && Mathf.Sqrt(best) < 0.08f * H && Vector3.Dot(p - bw[bj], bnw[bj]) < 0f) {
          n++;
          if (tally) { var bn2 = bodySmr.bones[bodyBw[bj].boneIndex0].name.Replace("mixamorig:", ""); insideBones[bn2] = insideBones.TryGetValue(bn2, out var c0) ? c0 + 1 : 1; }
        }
      }
    return n;
  }

  // ── THE FEET: one stays rooted, the other steps (the user, 2026-10-09: "body moving forward + one foot rooted on
  // the ground + another foot stepping forward"). The rooted foot is the one the table moves forward least — it stays
  // exactly where it stood for the whole clip, so it can never slide; the body travels over it. The other foot lifts
  // whenever it moves instead of gliding.
  float FwdTravel(int at, Vector3 idleFoot) { float m = 0f; for (int k = 0; k < K; k++) if (keys[k][1] < 0.5f) m = Mathf.Max(m, V(keys[k], at).z - idleFoot.z); return m; }
  bool rootLeft = FwdTravel(9, iLF) <= FwdTravel(6, iRF);
  Vector3 rootedAt0 = Vector3.zero; float worstRootDrift = 0f;

  int N = (int)keys[K - 1][0] * FPS / 30 + 1;
  var musc = new float[N, HumanTrait.MuscleCount]; var bpos = new Vector3[N]; var brot = new Quaternion[N];
  float[] prevMus = null; float worstArmStep = 0f, worstWristStep = 0f; string worstArmWhere = "";
  var wantMain = new Vector3[N]; var wantPair = new Vector3[N];   // where each weapon was meant to point, per stored pose
  float worstTip = 0, sumTip = 0, worstEdge = 0, worstWrist = 0, worstGrip2 = 0, worstGrip2Ang = 0, worstPairTip = 0;
  int badElbow = 0, badElbow2 = 0, worstInside = 0, worstInsideAt = -1, framesInside = 0;
  for (int i = 0; i < N; i++) {
    float fi = i * 30f / FPS;
    int k = 0; while (k < K - 2 && fi > keys[k + 1][0]) k++;
    // "pass" keys: the motion flows THROUGH them instead of stopping at each one (user, 2026-10-10: the warhammer
    // "teleported" from overhead to the hit). From the key before a run of pass keys to the key after it, time is
    // warped once — accelerating all the way (u²), the weapon fastest at the run's last key, where it stops dead (a
    // hit) — and every segment inside is crossed at that one speed curve, so it never slows at a pass key.
    bool inRun = false;
    {
      int rs = k, re = k + 1;
      while (rs > 0 && keys[rs].Length > 45 && keys[rs][45] > 0.5f) rs--;
      while (re < K - 1 && keys[re].Length > 45 && keys[re][45] > 0.5f) re++;
      if (re - rs > 1 && fi >= keys[rs][0] && fi <= keys[re][0]) {
        float U = Mathf.Clamp01((fi - keys[rs][0]) / (keys[re][0] - keys[rs][0]));
        fi = keys[rs][0] + U * U * (keys[re][0] - keys[rs][0]);
        k = rs; while (k < re - 1 && fi > keys[k + 1][0]) k++;
        inRun = true;
      }
    }
    var A = keys[k]; var Bk = keys[k + 1];
    float u = Mathf.Clamp01((fi - A[0]) / (Bk[0] - A[0]));
    float t = inRun ? u : cl.smooth ? Mathf.SmoothStep(0f, 1f, u) : EaseOut(u);
    bool aI = A[1] > 0.5f, bI = Bk[1] > 0.5f;
    var aRF = aI ? iRF : V(A, 6); var aLF = aI ? iLF : V(A, 9); var bRF = bI ? iRF : V(Bk, 6); var bLF = bI ? iLF : V(Bk, 9);
    var aRD = aI ? iRD : V(A, 12).normalized; var aLD = aI ? iLD : V(A, 16).normalized; float aRR = aI ? iRR : A[15]; float aLR = aI ? iLR : A[19];
    if (rootLeft) { aLF = iLF; bLF = iLF; } else { aRF = iRF; bRF = iRF; }
    var rf = Vector3.Lerp(aRF, bRF, t); var lf = Vector3.Lerp(aLF, bLF, t);
    // A foot that moves along the ground this segment is lifted on the way — a step, never a glide. The lift grows
    // with the distance, up to 5% of body height.
    float Lift(Vector3 from, Vector3 to) { float d = new Vector2(to.x - from.x, to.z - from.z).magnitude; return d > 0.005f ? Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.05f, d * 0.5f) : 0f; }
    if (cl.stepLift) { rf.y += Lift(aRF, bRF); lf.y += Lift(aLF, bLF); }
    var rd = Vector3.Slerp(aRD, V(Bk, 12).normalized, t); var ld = Vector3.Slerp(aLD, V(Bk, 16).normalized, t);
    float rr = Mathf.Lerp(aRR, Bk[15], t), lr = Mathf.Lerp(aLR, Bk[19], t);
    float hipF = Mathf.Lerp(A[2], Bk[2], t), hipD = Mathf.Lerp(A[3], Bk[3], t), lean = Mathf.Lerp(A[4], Bk[4], t), yaw = Mathf.Lerp(A[5], Bk[5], t);
    PoseIdle();
    var hips = B(HumanBodyBones.Hips);
    hips.position += rig.transform.forward * hipF * H - Vector3.up * hipD * H;
    hips.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(lean, rig.transform.right) * hips.rotation;
    var fwd = rig.transform.forward; var right = rig.transform.right;
    TwoBone(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), W(rf.x, 0, rf.z) + Vector3.up * (groundY - rig.transform.position.y + rf.y * H), fwd, true);
    TwoBone(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), W(lf.x, 0, lf.z) + Vector3.up * (groundY - rig.transform.position.y + lf.y * H), fwd, true);
    // "twist": the torso turned this many degrees to the character's RIGHT over the hips (negative = left), shared by
    // the spine's joints, while the hips and legs stay as they are — a turn that still shows when the clip plays on the
    // upper body only, as a put-away does in game. Arm and weapon directions stay in the character's own frame.
    float twist = Mathf.Lerp(A.Length > 46 ? A[46] : 0f, Bk.Length > 46 ? Bk[46] : 0f, t);
    if (twist != 0f) {
      var spineJoints = new System.Collections.Generic.List<Transform>();
      foreach (var sb in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }) { var sj = B(sb); if (sj != null) spineJoints.Add(sj); }
      foreach (var sj in spineJoints) sj.rotation = Quaternion.AngleAxis(twist / spineJoints.Count, Vector3.up) * sj.rotation;
    }
    var rT = B(HumanBodyBones.RightUpperArm).position + Dir(rd) * rr * armR;
    var lT = B(HumanBodyBones.LeftUpperArm).position + Dir(ld) * lr * armL;
    // Where each elbow bends: down and back by default; a key's "rp" / "lp" says where instead (character frame), eased
    // between keys like everything else — a tucked elbow by the waist is a pose, not something the solver should pick.
    Vector3 Pole(float[] key, int at, Vector3 def) => key[at] > 0.5f ? Dir(V(key, at + 1)) : def;
    var rPoleDef = -Vector3.up - fwd * 0.5f + right * 0.3f; var lPoleDef = -Vector3.up - fwd * 0.5f - right * 0.3f;
    TwoBone(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), rT, Vector3.Slerp(Pole(A, 36, rPoleDef), Pole(Bk, 36, rPoleDef), t), false);
    TwoBone(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), lT, Vector3.Slerp(Pole(A, 40, lPoleDef), Pole(Bk, 40, lPoleDef), t), false);
    var hp = new HumanPose(); hph.GetHumanPose(ref hp);
    if (hasWeapon) {
      Quaternion Want((Vector3[] tips, Vector3[] edges) p) => Quaternion.Slerp(Quaternion.LookRotation(p.tips[k], p.edges[k]), Quaternion.LookRotation(p.tips[k + 1], p.edges[k + 1]), t);
      var qc = Want(planMain); wantMain[i] = Dir(qc * Vector3.forward);
      bool ElbowSet(bool left) => A[left ? 40 : 36] > 0.5f || Bk[left ? 40 : 36] > 0.5f;
      var r1 = Aim(ref hp, LEFT, tipH, edgeH, gripH, Dir(qc * Vector3.forward), Dir(qc * Vector3.up), fwd, right, prevMus, ElbowSet(LEFT));
      worstWrist = Mathf.Max(worstWrist, (mainHand.position - (LEFT ? lT : rT)).magnitude / H);
      worstTip = Mathf.Max(worstTip, r1.tipErr); sumTip += r1.tipErr; worstEdge = Mathf.Max(worstEdge, r1.edgeErr); if (r1.bad) badElbow++;
      if (PAIR) {
        var q2 = Want(planPair); wantPair[i] = Dir(q2 * Vector3.forward);
        var r2 = Aim(ref hp, !LEFT, Mx(tipH), Mx(edgeH), Mx(gripH), Dir(q2 * Vector3.forward), Dir(q2 * Vector3.up), fwd, right, prevMus, ElbowSet(!LEFT));
        worstPairTip = Mathf.Max(worstPairTip, r2.tipErr); if (r2.bad) badElbow2++;
      } else if (cl.twoHand) {
        // ── The other hand grips the same weapon, thumb toward the tip like the first; its roll around the shaft is
        // searched — whichever roll the arm reaches within its natural range, elbow not folded forward-and-in, wins.
        bool oL = !LEFT; var oHand = HandOf(oL); var oUa = UpperOf(oL); var oLa = LowerOf(oL); var oMus = oL ? armMusL : armMusR;
        var shaft = mainHand.TransformDirection(tipH).normalized;
        var g1 = mainHand.TransformPoint(gripH);
        float along = cl.secondGrip >= 0f ? cl.secondGrip * H : -Mathf.Min(-cl.secondGrip * H, buttLen * 0.8f);
        var g2 = g1 + shaft * along;
        var tip2 = Mx(tipH); var grip2 = Mx(gripH);
        var start = hp; var startMus = (float[])hp.muscles.Clone();
        float bestS = float.MaxValue; float[] bestM = null; float bGap = 0, bAng = 0; bool bBad2 = false; Vector3 bPos = hp.bodyPosition; Quaternion bRot = hp.bodyRotation;
        foreach (var poleSide in new[] { 0.3f, 1.0f })
        for (int roll = 0; roll < 360; roll += 15) {
          start.muscles = (float[])startMus.Clone(); hph.SetHumanPose(ref start);
          var q0 = Quaternion.FromToRotation(oHand.rotation * tip2, shaft) * oHand.rotation;
          var q = Quaternion.AngleAxis(roll, shaft) * q0;
          var wristT = g2 - q * Vector3.Scale(grip2, oHand.lossyScale);
          TwoBone(oUa, oLa, oHand, wristT, -Vector3.up - fwd * 0.5f + (oL ? -right : right) * poleSide, false);
          oHand.rotation = q;
          var tr = new HumanPose(); hph.GetHumanPose(ref tr);
          foreach (var am in oMus) tr.muscles[am] = Mathf.Clamp(tr.muscles[am], -1f, 1f);
          float jump2 = 0f;
          if (prevMus != null) {
            foreach (var am in oL ? wristL : wristR) { tr.muscles[am] = Mathf.Clamp(tr.muscles[am], prevMus[am] - armStep, prevMus[am] + armStep); jump2 += Mathf.Abs(tr.muscles[am] - prevMus[am]); }
            foreach (var am in oL ? twistL : twistR) jump2 += Mathf.Abs(tr.muscles[am] - prevMus[am]);
          }
          hph.SetHumanPose(ref tr);
          float gap = (oHand.TransformPoint(grip2) - g2).magnitude / H, ang = Vector3.Angle(oHand.rotation * tip2, shaft);
          var sh = oUa.position; var el = oLa.position; var wr = oHand.position; var line = (wr - sh).normalized;
          var bend = (el - sh) - Vector3.Dot(el - sh, line) * line; bend = bend.sqrMagnitude > 1e-10f ? bend.normalized : Vector3.zero;
          bool bad = Vector3.Dot(bend, fwd) > 0.3f && Vector3.Dot(bend, oL ? right : -right) > 0.3f;
          float sc = gap * 300f + ang * 0.2f + (bad ? 1000f : 0f) + jump2 * 20f;
          if (sc < bestS) { bestS = sc; bestM = (float[])tr.muscles.Clone(); bGap = gap; bAng = ang; bBad2 = bad; bPos = tr.bodyPosition; bRot = tr.bodyRotation; }
        }
        hp.muscles = bestM; hp.bodyPosition = bPos; hp.bodyRotation = bRot; hph.SetHumanPose(ref hp);
        worstGrip2 = Mathf.Max(worstGrip2, bGap); worstGrip2Ang = Mathf.Max(worstGrip2Ang, bAng); if (bBad2) badElbow2++;
      }
      if (i % 2 == 0) { int ins = Inside(true); if (ins > 0) framesInside++; if (ins > worstInside) { worstInside = ins; worstInsideAt = i / 2; } }
    }
    for (int m = 0; m < HumanTrait.MuscleCount; m++) musc[i, m] = hp.muscles[m];
    // "look": the head (neck and head halves alike) turned this many degrees to the character's left, on top of whatever
    // the body does — a body turned side-on keeps its eyes on the enemy (user, 2026-10-10, the warhammer's wind-up).
    float look = Mathf.Lerp(A.Length > 44 ? A[44] : 0f, Bk.Length > 44 ? Bk[44] : 0f, t);
    if (look != 0f) foreach (var lm in lookMus) musc[i, lm] = Mathf.Clamp(musc[i, lm] + LOOK_SIGN * look / (2f * LOOK_RANGE), -1f, 1f);
    if (prevMus != null) {
      foreach (var am in armMusR) { float d = Mathf.Abs(hp.muscles[am] - prevMus[am]); if (d > worstArmStep) { worstArmStep = d; worstArmWhere = HumanTrait.MuscleName[am] + " at stored pose " + i; } }
      foreach (var am in armMusL) { float d = Mathf.Abs(hp.muscles[am] - prevMus[am]); if (d > worstArmStep) { worstArmStep = d; worstArmWhere = HumanTrait.MuscleName[am] + " at stored pose " + i; } }
      foreach (var am in wristR) worstWristStep = Mathf.Max(worstWristStep, Mathf.Abs(hp.muscles[am] - prevMus[am]));
      foreach (var am in wristL) worstWristStep = Mathf.Max(worstWristStep, Mathf.Abs(hp.muscles[am] - prevMus[am]));
    }
    prevMus = (float[])hp.muscles.Clone();
    bpos[i] = hp.bodyPosition; brot[i] = hp.bodyRotation;
    // Proof the rooted foot stays put: where it ends up from the stored pose, against where it stood in frame 0.
    hph.SetHumanPose(ref hp);
    var rootedNow = B(rootLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position;
    if (i == 0) rootedAt0 = rootedNow; else worstRootDrift = Mathf.Max(worstRootDrift, (rootedNow - rootedAt0).magnitude / H);
  }
  UnityEngine.Object.DestroyImmediate(bodyMesh);
  // ── THE WRISTS DO NOT SHAKE (the user, 2026-10-09: "why the left hand is shaking"). Each pose is solved on its own,
  // and where the planned weapon direction sits at the edge of what the wrist can reach, two neighbouring answers
  // score almost the same, so the wrist alternated between them — the katar's left wrist reversed 41 times in 47
  // poses, both as back-and-forth and as single-pose spikes a quarter of the joint's reach high. A pose where a wrist
  // joint turns back AND sits more than SHAKE off the line between its two neighbours is a shake, never a motion (the
  // turn at the end of a real swing is eased, so it sits on that line), and is smoothed toward its neighbours until no
  // shake is left. Only the three wrist joints are touched: they turn the weapon and never move the hand off its path.
  // A table with "steadyArms" holds every arm joint to the same rule (the user, 2026-10-10, on the warhammer: "the
  // hands are shaking"): with two hands on one handle the shoulders and elbows alternated between two answers too —
  // 66 reversals over the arms' joints in 41 poses, the upper arm jumping 0.12 of its reach and back while the hammer
  // was held still overhead. That moves the hands a little off their planned path, which the run reports below as
  // the second hand's gap to the handle.
  const float SHAKE = 0.03f;
  bool Shake(int m, int j) { float d1 = musc[j, m] - musc[j - 1, m], d2 = musc[j + 1, m] - musc[j, m]; return d1 * d2 < 0f && Mathf.Abs(musc[j, m] - (musc[j - 1, m] + musc[j + 1, m]) * 0.5f) > SHAKE; }
  int Shakes(int m) { int n = 0; for (int j = 1; j < N - 1; j++) if (Shake(m, j)) n++; return n; }
  var muscRaw = (float[,])musc.Clone();
  int zigBefore = 0, zigAfter = 0;
  foreach (var am in cl.steadyArms ? System.Linq.Enumerable.Concat(armMusR, armMusL) : System.Linq.Enumerable.Concat(wristR, wristL)) {
    zigBefore += Shakes(am);
    for (int pass = 0; pass < 500; pass++) {
      var next = new float[N]; bool changed = false;
      for (int j = 0; j < N; j++) {
        next[j] = musc[j, am];
        if (j > 0 && j < N - 1 && Shake(am, j)) { next[j] = (musc[j - 1, am] + 2f * musc[j, am] + musc[j + 1, am]) * 0.25f; changed = true; }
      }
      for (int j = 0; j < N; j++) musc[j, am] = next[j];
      if (!changed) break;
    }
    zigAfter += Shakes(am);
  }
  // steadyArms, second half: a hand following a handle also WOBBLES — it drifts one way for a few poses and back
  // (the warhammer's left wrist moved 0.1 of its reach back and forth over five poses), which no single pose shows.
  // Every arm joint is eased over its two neighbours (1-2-1), STEADY_PASSES times; the first and last poses stay.
  const int STEADY_PASSES = 2;
  if (cl.steadyArms) foreach (var am in System.Linq.Enumerable.Concat(armMusR, armMusL))
    for (int pass = 0; pass < STEADY_PASSES; pass++) {
      var next = new float[N];
      for (int j = 0; j < N; j++) next[j] = j > 0 && j < N - 1 ? (musc[j - 1, am] + 2f * musc[j, am] + musc[j + 1, am]) * 0.25f : musc[j, am];
      for (int j = 0; j < N; j++) musc[j, am] = next[j];
    }
  // What the smoothing cost: how far each weapon now points from its plan, pose by pose — and, with two hands on one
  // weapon, how far the second hand's grip point now is from its place on the handle (before -> after).
  float smoothTip = 0f, smoothPairTip = 0f, gap2Raw = 0f, gap2Smooth = 0f;
  if (hasWeapon) for (int j = 0; j < N; j++) for (int raw = 0; raw < 2; raw++) {
    var src = raw == 1 ? muscRaw : musc;
    var hpj = new HumanPose { muscles = new float[HumanTrait.MuscleCount], bodyPosition = bpos[j], bodyRotation = brot[j] };
    for (int m = 0; m < HumanTrait.MuscleCount; m++) hpj.muscles[m] = src[j, m];
    hph.SetHumanPose(ref hpj);
    if (cl.twoHand && !PAIR) {
      float along2 = cl.secondGrip >= 0f ? cl.secondGrip * H : -Mathf.Min(-cl.secondGrip * H, buttLen * 0.8f);
      var want2 = mainHand.TransformPoint(gripH) + mainHand.TransformDirection(tipH).normalized * along2;
      float g = (HandOf(!LEFT).TransformPoint(Mx(gripH)) - want2).magnitude / H;
      if (raw == 1) gap2Raw = Mathf.Max(gap2Raw, g); else gap2Smooth = Mathf.Max(gap2Smooth, g);
    }
    if (raw == 1) continue;
    smoothTip = Mathf.Max(smoothTip, Vector3.Angle(mainHand.TransformDirection(tipH), wantMain[j]));
    if (PAIR) smoothPairTip = Mathf.Max(smoothPairTip, Vector3.Angle(HandOf(!LEFT).TransformDirection(Mx(tipH)), wantPair[j]));
  }
  var clip = new AnimationClip { frameRate = FPS };
  float dt = 1f / FPS;
  AnimationCurve Smooth(AnimationCurve c) { for (int j = 0; j < c.length; j++) { AnimationUtility.SetKeyLeftTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, j, AnimationUtility.TangentMode.ClampedAuto); } return c; }
  foreach (var b in idleBinds) {
    if (b.type != typeof(Animator)) continue;
    var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
    if (!idx.TryGetValue(nm, out var mi)) continue;
    var cv = new AnimationCurve(); for (int i = 0; i < N; i++) cv.AddKey(i * dt, musc[i, mi]);
    AnimationUtility.SetEditorCurve(clip, b, Smooth(cv));
  }
  string[] tn = { "RootT.x", "RootT.y", "RootT.z" }; string[] qn = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
  for (int q = 0; q < 3; q++) { var cv = new AnimationCurve(); for (int i = 0; i < N; i++) cv.AddKey(i * dt, bpos[i][q]); AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), tn[q]), Smooth(cv)); }
  for (int q = 0; q < 4; q++) { var cv = new AnimationCurve(); for (int i = 0; i < N; i++) cv.AddKey(i * dt, brot[i][q]); AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), qn[q]), Smooth(cv)); }
  // THE BODY'S MOVEMENT IS PART OF THE POSE. The game's characters never apply root motion, so a clip that leaves the
  // body's travel, height or turn as root motion has them thrown away in game — the body stays put and a planted foot
  // drags backward (2026-10-09). Every clip the game ships bakes all three in, as the imported ones do.
  var settings = AnimationUtility.GetAnimationClipSettings(clip);
  settings.loopTime = cl.loop;
  settings.loopBlendOrientation = true; settings.loopBlendPositionY = true; settings.loopBlendPositionXZ = true;
  settings.keepOriginalOrientation = true; settings.keepOriginalPositionY = true; settings.keepOriginalPositionXZ = true;
  settings.heightFromFeet = false;
  AnimationUtility.SetAnimationClipSettings(clip, settings);
  var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIR + cl.name + ".anim");
  float maxDiff = 0; string worstCurve = "";
  if (existing != null) foreach (var b in AnimationUtility.GetCurveBindings(clip)) {
    var ca = AnimationUtility.GetEditorCurve(clip, b); var cb = AnimationUtility.GetEditorCurve(existing, b);
    if (cb == null) { worstCurve = b.propertyName + " missing in saved"; maxDiff = 999; continue; }
    for (int i = 0; i < N; i++) { float d = Mathf.Abs(ca.Evaluate(i * dt) - cb.Evaluate(i * dt)); if (d > maxDiff) { maxDiff = d; worstCurve = b.propertyName + "@" + i; } }
  }
  log.AppendLine(cl.name + ": biggest wrist change between two frames " + worstWristStep.ToString("F2") + " (limit " + armStep + "); biggest of any arm joint " + worstArmStep.ToString("F2") + " (" + worstArmWhere + ")");
  log.AppendLine(cl.name + ": " + (cl.steadyArms ? "arm joint" : "wrist") + " shakes " + zigBefore + " -> " + zigAfter + (hasWeapon ? "; after smoothing, tip error worst " + smoothTip.ToString("F0") + "°" + (PAIR ? ", other hand's weapon " + smoothPairTip.ToString("F0") + "°" : "") + (cl.twoHand && !PAIR ? ", second hand's gap to its place on the handle worst " + (gap2Raw * 100f).ToString("F1") + "% -> " + (gap2Smooth * 100f).ToString("F1") + "% of body height" : "") : ""));
  log.AppendLine(cl.name + ": rooted foot " + (rootLeft ? "left" : "right") + ", it moves at most " + (worstRootDrift * 100f).ToString("F2") + "% of body height");
  log.AppendLine(cl.name + ": frames=" + N + " diff vs saved=" + maxDiff.ToString("F4") + " (" + worstCurve + ")" +
    (hasWeapon ? " tip error worst=" + worstTip.ToString("F0") + "° mean=" + (sumTip / N).ToString("F0") + "° edge worst=" + worstEdge.ToString("F0") + "° bad elbows=" + badElbow +
                 " wrist moved " + (worstWrist * 100f).ToString("F1") + "% of body height" +
                 (PAIR ? " | other hand's weapon: tip error worst " + worstPairTip.ToString("F0") + "°, wrong-way elbows " + badElbow2 : "") +
                 (cl.twoHand && !PAIR ? " | second hand: worst gap to the grip " + (worstGrip2 * 100f).ToString("F1") + "% of body height, worst angle " + worstGrip2Ang.ToString("F0") + "°, wrong-way elbows " + badElbow2 : "") +
                 (meshPts.Count > 0 ? " | weapon points inside the body: worst " + worstInside + (worstInsideAt >= 0 ? " at frame " + worstInsideAt : "") + ", in " + framesInside + " of " + (N + 1) / 2 + " frames, by body part over all frames: " + string.Join(" ", System.Linq.Enumerable.Select(insideBones, kv => kv.Key + "=" + kv.Value)) : "") : ""));
  if (WRITE) {
    if (existing != null) { EditorUtility.CopySerialized(clip, existing); existing.name = cl.name; EditorUtility.SetDirty(existing); }
    else AssetDatabase.CreateAsset(clip, DIR + cl.name + ".anim");
  } else UnityEngine.Object.DestroyImmediate(clip);
  hph.Dispose();
  } catch (System.Exception e) { log.AppendLine(cl.name + " ERR " + e); }
  finally { foreach (var g in temps) if (g) UnityEngine.Object.DestroyImmediate(g); UnityEngine.Object.DestroyImmediate(rig); }
}
if (WRITE) AssetDatabase.SaveAssets();
return log.ToString();
