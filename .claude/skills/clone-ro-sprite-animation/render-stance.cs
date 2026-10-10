// Renders standing stances, travel gears with a weapon's carry arms laid over them, and put-away / take-out clips
// (the weapon moving between the hand and its resting place on the hips at the handover). Fill __JOBS__ with
// ("<weapon>_<idle|run|sheathe|unsheathe>", clip, weapon item, arms clip or null), and the put-away placeholders;
// then make-stance-videos.py joins the frames. Nothing is saved to the project.
// Renders each attack clip, frame by frame at 30 per second, from two cameras (front-left and front-right, 35° down)
// into C:\Users\Admin\AppData\Local\Temp\claude\d--tempo-projects-roguelike-mmo\15abc913-e20c-4fe2-9959-f8d92b3ebb60\scratchpad\frames\<ClipName>\000.png… Run with eval_file after replacing C:\Users\Admin\AppData\Local\Temp\claude\d--tempo-projects-roguelike-mmo\15abc913-e20c-4fe2-9959-f8d92b3ebb60\scratchpad\frames; then make-videos.py joins the frames.
// Nothing is saved to the project: every object is HideAndDontSave and destroyed at the end.
var log = new System.Text.StringBuilder();
var outRoot = @"<FRAMES>\";
var jobs = new (string, string, string, string)[] {   // output name, clip, weapon item, arms clip laid over it (or null)
__JOBS__ };
var only = new System.Collections.Generic.HashSet<string>(new string[] { });
AnimationClip FindClip(string n) {
  foreach (var c in CharacterSpec.Active.animatorController.animationClips) if (c != null && c.name == n) return c;
  return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/" + n + ".anim");
}
// put-away clips: the time the weapon changes place, whether it goes TO the body there, and its resting place on the hips
var stowJobs = new System.Collections.Generic.Dictionary<string, (float at, bool toBody)> { __STOWJOBS__ };   // e.g. { "DaggerSheathe", (24f / 60f, true) }
var STOW_POS = __STOWPOS__; var STOW_EUL = __STOWEUL__;   // the resting place on the hips: measure-handover.cs prints it
// a pair weapon's other copy: its own hand's handover time per put-away clip, and its resting place (stowPair*)
var pairStowAt = new System.Collections.Generic.Dictionary<string, float> { __PAIRSTOWAT__ };   // e.g. { "AxeSheathe", 24f / 60f }
var STOW_PAIR_POS = __STOWPAIRPOS__; var STOW_PAIR_EUL = __STOWPAIREUL__;
// a weapon whose stance library row is not saved yet: the spin its put-away video is drawn with, by weapon item —
// e.g. { "Spear", new WeaponStanceBinding { sheatheClip = "SpearSheathe", unsheatheClip = "SpearUnsheathe", spinStartFrame = 8, spinEndFrame = 23, spinTurns = 1f, spinLift = 0.3f, spinAboutMiddle = true } }
var spinRows = new System.Collections.Generic.Dictionary<string, WeaponStanceBinding> { __SPINROWS__ };
// clips drawn MIRRORED, left and right swapped, as a controller state with "mirror" plays them (the bow's attack since
// the bow moved to the right hand, 2026-10-11): each "Left …" muscle takes its "Right …" partner's value, the middle's
// side-bend and turn muscles change sign, and the body's sideways travel and turn are reflected
var mirrorClips = new System.Collections.Generic.HashSet<string> { __MIRRORCLIPS__ };
float[] Mirrored(float[] mus) {
  var o = (float[])mus.Clone();
  var byName = new System.Collections.Generic.Dictionary<string, int>(); for (int m = 0; m < HumanTrait.MuscleName.Length; m++) byName[HumanTrait.MuscleName[m]] = m;
  for (int m = 0; m < mus.Length; m++) {
    var n = HumanTrait.MuscleName[m];
    if (n.StartsWith("Left ") && byName.TryGetValue("Right " + n.Substring(5), out var r)) { o[m] = mus[r]; o[r] = mus[m]; }
    else if (!n.StartsWith("Right ") && (n.Contains("Left-Right"))) o[m] = -mus[m];
  }
  return o;
}
bool IsArm(string n) => (n.StartsWith("Left ") || n.StartsWith("Right ")) && (n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Forearm") || n.Contains("Hand") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"));   // e.g. "AttackSpear"; empty = all
const float ZOOM = 1.0f;   // orthographic size as a share of body height: 1.0 for videos, 0.55 to inspect a weapon's angle
// The default body's texture is plain dark grey, so the videos wear a dressed fighter.
string modelPath = Player3DVisualBuilder.RiggedDir + "/Knight_Male.fbx";
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
float H = CharacterSpec.Active.bodyHeight;
const int S = 400;
float[] yaws = { -45f, 45f };
foreach (var job in jobs) {
  if (only.Count > 0 && !only.Contains(job.Item1)) continue;
  var clip = FindClip(job.Item2);
  if (clip == null) { log.AppendLine(job.Item2 + " MISSING"); continue; }
  // "CarryDagger:Right" lays only that arm over the clip — a weapon whose carry pose holds one arm (its binding's
  // carryArms); the other arm keeps the clip's own swing, as the game's one-arm carry layer does.
  string armsName = job.Item4, armsSide = null;
  if (armsName != null && armsName.Contains(":")) { armsSide = armsName.Substring(armsName.IndexOf(':') + 1); armsName = armsName.Substring(0, armsName.IndexOf(':')); }
  var arms = armsName != null ? FindClip(armsName) : null;
  var armBinds = arms != null ? AnimationUtility.GetCurveBindings(arms) : new EditorCurveBinding[0];
  var dir = outRoot + job.Item1 + "\\"; System.IO.Directory.CreateDirectory(dir);
  foreach (var f in System.IO.Directory.GetFiles(dir)) System.IO.File.Delete(f);
  GameObject weaponPrefab = null; WeaponGrip grip = null; GameObject heldW = null, heldW2 = null; Transform heldHand = null, heldHand2 = null;
  var item = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/3_Data/Resources/Items/" + job.Item3 + ".asset");
  if (item != null) { var p = new SerializedObject(item).FindProperty("equipModel"); if (p != null && p.objectReferenceValue != null) { weaponPrefab = p.objectReferenceValue as GameObject; if (weaponPrefab) grip = weaponPrefab.GetComponent<WeaponGrip>(); } }
  var binds = AnimationUtility.GetCurveBindings(clip);
  var curves = new AnimationCurve[binds.Length];
  for (int b = 0; b < binds.Length; b++) curves[b] = AnimationUtility.GetEditorCurve(clip, binds[b]);
  int frames = Mathf.RoundToInt(clip.length * 30f) + 1;
  var pru = new PreviewRenderUtility(); GameObject rig = null, proxy = null; Mesh baked = null;
  var tmpMats = new System.Collections.Generic.List<Material>();
  try {
    rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
    rig.transform.localScale = Vector3.one * (H / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar; anim.applyRootMotion = false;
    var smr = rig.GetComponentInChildren<SkinnedMeshRenderer>();
    // The preview does not re-skin the body between renders (the arms froze in the first videos), so the body is drawn
    // from a mesh baked every frame, with a plain lit material: the game's character shader draws black here.
    var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
    bodyMat.SetTexture("_BaseMap", smr.sharedMaterial.GetTexture("_BaseMap")); bodyMat.SetColor("_BaseColor", Color.white); bodyMat.SetFloat("_Smoothness", 0.1f);
    tmpMats.Add(bodyMat); smr.enabled = false;
    baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
    proxy = new GameObject("BodyProxy") { hideFlags = HideFlags.HideAndDontSave };
    proxy.AddComponent<MeshFilter>().sharedMesh = baked; proxy.AddComponent<MeshRenderer>().sharedMaterial = bodyMat;
    if (job.Item3 == "Sniper" && weaponPrefab == null) {
      // No rifle model: a plain dark stand-in, held at its trigger grip in the right hand the way the
      // animation was built — barrel along the hand from wrist to knuckles, stock 0.12 and barrel 0.5 of body height.
      var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
      var pivot = new GameObject("RifleStandIn") { hideFlags = HideFlags.HideAndDontSave };
      pivot.transform.SetParent(rh, false);
      pivot.transform.localPosition = new Vector3(0.011f, 0.18756053f, 0.12156433f);   // where the fist closes in the hand bone's space
      var ls = rh.lossyScale; pivot.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
      var gunMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave }; gunMat.SetColor("_BaseColor", new Color(0.18f, 0.18f, 0.2f)); tmpMats.Add(gunMat);
      void Part(Vector3 centre, Vector3 size) {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.hideFlags = HideFlags.HideAndDontSave; UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(pivot.transform, false); c.transform.localPosition = centre * H; c.transform.localScale = size * H; c.GetComponent<Renderer>().sharedMaterial = gunMat;
      }
      Part(new Vector3(0, 0.19f, 0), new Vector3(0.05f, 0.62f, 0.05f));   // stock to muzzle
      Part(new Vector3(0, -0.07f, 0.035f), new Vector3(0.06f, 0.12f, 0.09f));   // the stock's body
    } else if (weaponPrefab != null && grip != null) {
      var hand = HandSocketResolver.Find(anim, grip.hand);
      var w = (GameObject)UnityEngine.Object.Instantiate(weaponPrefab); w.hideFlags = HideFlags.HideAndDontSave;
      w.transform.SetParent(hand, false);
      w.transform.localPosition = grip.localPosition; w.transform.localRotation = Quaternion.Euler(grip.localEuler); w.transform.localScale = grip.localScale;
      heldW = w; heldHand = hand;
      // A pair weapon fills the other hand too, mirrored exactly the way the game's hand mounts it.
      var itemDef = item as Item;
      if (itemDef != null && WeaponTypes.IsDualWield(itemDef.weaponType)) {
        var other = HandSocketResolver.Find(anim, grip.hand == WeaponHand.Left ? WeaponHand.Right : WeaponHand.Left);
        var w2 = (GameObject)UnityEngine.Object.Instantiate(weaponPrefab); w2.hideFlags = HideFlags.HideAndDontSave;
        w2.transform.SetParent(other, false);
        var r = Quaternion.Euler(grip.localEuler);
        w2.transform.localPosition = new Vector3(-grip.localPosition.x, grip.localPosition.y, grip.localPosition.z);
        w2.transform.localRotation = new Quaternion(r.x, -r.y, -r.z, r.w);
        w2.transform.localScale = new Vector3(-grip.localScale.x, grip.localScale.y, grip.localScale.z);
        heldW2 = w2; heldHand2 = other;
      }
    }
    pru.AddSingleGO(rig); pru.AddSingleGO(proxy);
    var hph = new HumanPoseHandler(avatar, rig.transform);
    pru.camera.orthographic = true; pru.camera.orthographicSize = H * ZOOM; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 100f;
    pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.82f, 0.8f, 0.75f);
    pru.lights[0].intensity = 1.5f; pru.lights[0].transform.rotation = Quaternion.Euler(40, 160, 0); pru.lights[1].intensity = 0.5f;
    pru.ambientColor = new Color(0.75f, 0.75f, 0.75f);
    var sheet = new Texture2D(S * yaws.Length, S, TextureFormat.RGB24, false);
    float worstGap = 0f, minElbowR = 180f, minElbowL = 180f;
    for (int fi = 0; fi < frames; fi++) {
      float t = Mathf.Min(fi / 30f, clip.length);
      var hp = new HumanPose(); hph.GetHumanPose(ref hp);
      float rx = 0, ry = 0, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
      for (int b = 0; b < binds.Length; b++) {
        if (binds[b].type != typeof(Animator)) continue;
        float v = curves[b].Evaluate(t);
        switch (binds[b].propertyName) {
          case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue;
          case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue;
        }
        var nm = binds[b].propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
        if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
      }
      foreach (var ab in armBinds) {
        if (ab.type != typeof(Animator)) continue;
        var an = ab.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
        if (IsArm(an) && (armsSide == null || an.StartsWith(armsSide + " ")) && idx.TryGetValue(an, out var ai)) hp.muscles[ai] = AnimationUtility.GetEditorCurve(arms, ab).Evaluate(0f);
      }
      if (arms != null) { rx = 0f; rz = 0f; }   // a travel gear runs in place, as in game
      if (mirrorClips.Contains(job.Item2)) { hp.muscles = Mirrored(hp.muscles); rx = -rx; qy = -qy; qz = -qz; }
      hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized;
      hph.SetHumanPose(ref hp);
      if (heldW != null && stowJobs.TryGetValue(job.Item2, out var sj)) {
        bool onBody = sj.toBody ? t >= sj.at : t < sj.at;
        if (onBody) { heldW.transform.SetParent(anim.GetBoneTransform(HumanBodyBones.Hips), false); heldW.transform.localPosition = STOW_POS; heldW.transform.localRotation = Quaternion.Euler(STOW_EUL); }
        else { heldW.transform.SetParent(heldHand, false); heldW.transform.localPosition = grip.localPosition; heldW.transform.localRotation = Quaternion.Euler(grip.localEuler); }
        heldW.transform.localScale = grip.localScale;
        if (heldW2 != null && pairStowAt.TryGetValue(job.Item2, out var pAt)) {
          bool pOnBody = sj.toBody ? t >= pAt : t < pAt;
          var t2 = heldW2.transform;
          if (pOnBody) { t2.SetParent(anim.GetBoneTransform(HumanBodyBones.Hips), false); t2.localPosition = STOW_PAIR_POS; t2.localRotation = Quaternion.Euler(STOW_PAIR_EUL); }
          else { var r = Quaternion.Euler(grip.localEuler); t2.SetParent(heldHand2, false); t2.localPosition = new Vector3(-grip.localPosition.x, grip.localPosition.y, grip.localPosition.z); t2.localRotation = new Quaternion(r.x, -r.y, -r.z, r.w); }
          t2.localScale = new Vector3(-grip.localScale.x, grip.localScale.y, grip.localScale.z);
        }
      }
      // a weapon spun during its put-away (its stance library row's spin settings) is drawn the way the game draws it
      var spinRow = spinRows.TryGetValue(job.Item3, out var spinOver) ? spinOver : item is Item spinItem ? StanceCatalog.Weapon(spinItem.weaponType) : null;
      if (heldW != null && grip != null && spinRow != null && heldW.transform.parent == heldHand && (job.Item2 == spinRow.sheatheClip || job.Item2 == spinRow.unsheatheClip)) {
        var sheathe = FindClip(spinRow.sheatheClip);
        if (sheathe != null && WeaponSocket.SpinWindow(spinRow, sheathe.length * sheathe.frameRate, out var sFrom, out var sTo)) {
          float prog = clip.length > 0f ? t / clip.length : 1f;
          float share = WeaponDrawMath.SpinWindowShare(job.Item2 == spinRow.sheatheClip ? prog : 1f - prog, sFrom, sTo);
          // a weapon that turns about its own middle slides through the fist the way the game's hand does it
          var slide = Vector3.zero;
          if (spinRow.spinAboutMiddle) { var meshF = weaponPrefab.GetComponentInChildren<MeshFilter>(); slide = WeaponSocket.MiddleSlide(grip.localPosition, grip.localEuler, grip.localScale, grip.hand == WeaponHand.Left, weaponPrefab.transform.InverseTransformPoint(meshF.transform.TransformPoint(meshF.sharedMesh.bounds.center))); }
          WeaponSocket.SpunOverFist(grip, share, spinRow.spinTurns, spinRow.spinLift, slide, out var sp, out var sr);
          heldW.transform.localPosition = sp; heldW.transform.localRotation = sr;
        }
      }
      // what the picture shows, measured: each elbow's bend (180 = straight), the smallest over the clip
      float ElbowAt(HumanBodyBones u, HumanBodyBones l, HumanBodyBones h) { var e = anim.GetBoneTransform(l).position; return Vector3.Angle(anim.GetBoneTransform(u).position - e, anim.GetBoneTransform(h).position - e); }
      minElbowR = Mathf.Min(minElbowR, ElbowAt(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand));
      minElbowL = Mathf.Min(minElbowL, ElbowAt(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand));
      smr.BakeMesh(baked, true);
      proxy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
      proxy.transform.localScale = smr.transform.lossyScale;
      // proof the drawn body follows the bones: the hand bone's distance to the nearest drawn point (~2% = the glove)
      var hb = anim.GetBoneTransform(HumanBodyBones.RightHand).position; float best = float.MaxValue;
      foreach (var v in baked.vertices) best = Mathf.Min(best, (proxy.transform.TransformPoint(v) - hb).sqrMagnitude);
      worstGap = Mathf.Max(worstGap, Mathf.Sqrt(best) / H);
      var target = rig.transform.position + Vector3.up * H * 0.5f + rig.transform.forward * H * 0.1f;
      for (int vi = 0; vi < yaws.Length; vi++) {
        float y = yaws[vi] * Mathf.Deg2Rad, pch = 35f * Mathf.Deg2Rad;
        Vector3 flat = rig.transform.forward * Mathf.Cos(y) + rig.transform.right * Mathf.Sin(y);
        pru.camera.transform.position = target + (flat * Mathf.Cos(pch) + Vector3.up * Mathf.Sin(pch)) * H * 5f;
        pru.camera.transform.LookAt(target);
        pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
        var tex = pru.EndStaticPreview();
        if (fi == 0 && vi == 0) { UnityEngine.Object.DestroyImmediate(tex); pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true); tex = pru.EndStaticPreview(); }
        var rtx = RenderTexture.GetTemporary(S, S, 0); Graphics.Blit(tex, rtx);
        var prev = RenderTexture.active; RenderTexture.active = rtx;
        sheet.ReadPixels(new Rect(0, 0, S, S), vi * S, 0);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rtx); UnityEngine.Object.DestroyImmediate(tex);
      }
      sheet.Apply();
      System.IO.File.WriteAllBytes(dir + fi.ToString("000") + ".png", sheet.EncodeToPNG());
    }
    UnityEngine.Object.DestroyImmediate(sheet); hph.Dispose();
    log.AppendLine(job.Item1 + " frames=" + frames + " weapon=" + (weaponPrefab ? weaponPrefab.name : "none") + " worst hand-to-body gap=" + (worstGap * 100f).ToString("F1") + "% of body height; most bent elbow, right " + minElbowR.ToString("F0") + " deg, left " + minElbowL.ToString("F0") + " deg");
  } catch (System.Exception e) { log.AppendLine(job.Item1 + " ERR " + e); }
  finally {
    pru.Cleanup();
    if (rig) UnityEngine.Object.DestroyImmediate(rig); if (proxy) UnityEngine.Object.DestroyImmediate(proxy); if (baked) UnityEngine.Object.DestroyImmediate(baked);
    foreach (var tm in tmpMats) UnityEngine.Object.DestroyImmediate(tm);
  }
}
return log.ToString();
