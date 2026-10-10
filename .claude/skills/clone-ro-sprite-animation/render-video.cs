// Renders each attack clip, frame by frame at 30 per second, from two cameras (front-left and front-right, 35° down)
// into <FRAMES>\<ClipName>\000.png… Run with eval_file after replacing <FRAMES>; then make-videos.py joins the frames.
// Nothing is saved to the project: every object is HideAndDontSave and destroyed at the end.
var log = new System.Text.StringBuilder();
var outRoot = @"<FRAMES>\";
var jobs = new[] {   // clip name, weapon item it is shown holding
  ("AttackOneHandSword", "Sword"), ("AttackAxe", "Axe"), ("AttackMace", "Warhammer"), ("AttackSpear", "Spear"),
  ("AttackKnuckle", "Fist"), ("AttackDaggerStab", "Dagger"), ("AttackKatar", "Katar"), ("AttackShurikenSwing", "Shuriken"),
  ("AttackSniper", "Sniper"), ("AttackStaffSwing", "Staff"), ("AttackWand", "Wand"), ("AttackScytheSweep", "Scythe") };
var only = new System.Collections.Generic.HashSet<string>(new string[] { });   // e.g. "AttackSpear"; empty = all
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
  var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/" + job.Item1 + ".anim");
  if (clip == null) { log.AppendLine(job.Item1 + " MISSING"); continue; }
  var dir = outRoot + job.Item1 + "\\"; System.IO.Directory.CreateDirectory(dir);
  foreach (var f in System.IO.Directory.GetFiles(dir)) System.IO.File.Delete(f);
  GameObject weaponPrefab = null; WeaponGrip grip = null;
  var item = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/3_Data/Resources/Items/" + job.Item2 + ".asset");
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
    if (job.Item1 == "AttackSniper" && weaponPrefab == null) {
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
      }
    }
    pru.AddSingleGO(rig); pru.AddSingleGO(proxy);
    var hph = new HumanPoseHandler(avatar, rig.transform);
    pru.camera.orthographic = true; pru.camera.orthographicSize = H * ZOOM; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 100f;
    pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.82f, 0.8f, 0.75f);
    pru.lights[0].intensity = 1.5f; pru.lights[0].transform.rotation = Quaternion.Euler(40, 160, 0); pru.lights[1].intensity = 0.5f;
    pru.ambientColor = new Color(0.75f, 0.75f, 0.75f);
    var sheet = new Texture2D(S * yaws.Length, S, TextureFormat.RGB24, false);
    float worstGap = 0f;
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
      hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized;
      hph.SetHumanPose(ref hp);
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
    log.AppendLine(job.Item1 + " frames=" + frames + " weapon=" + (weaponPrefab ? weaponPrefab.name : "none") + " worst hand-to-body gap=" + (worstGap * 100f).ToString("F1") + "% of body height");
  } catch (System.Exception e) { log.AppendLine(job.Item1 + " ERR " + e); }
  finally {
    pru.Cleanup();
    if (rig) UnityEngine.Object.DestroyImmediate(rig); if (proxy) UnityEngine.Object.DestroyImmediate(proxy); if (baked) UnityEngine.Object.DestroyImmediate(baked);
    foreach (var tm in tmpMats) UnityEngine.Object.DestroyImmediate(tm);
  }
}
return log.ToString();
