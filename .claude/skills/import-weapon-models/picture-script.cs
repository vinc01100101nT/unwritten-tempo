var outDir = @"<SCRATCHPAD>\";
var W = "Assets/1_Authored/Prefabs/Weapons/";
var sheets = new[] {      // prefab names; keep a reference row: the sword and bow the items use today
  new[]{"sword_veyra","<new prefab>","<new prefab>"},
  new[]{"bow_celestia","<new bow prefab>"} };
var log = new System.Text.StringBuilder();
string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var ctrl = CharacterSpec.Active.animatorController;
AnimationClip Clip(string n) { foreach (var c in ctrl.animationClips) if (c.name == n) return c; return null; }
const int S = 320;
for (int si = 0; si < sheets.Length; si++) {
  var rows = sheets[si];
  var sheet = new Texture2D(S * 3, S * rows.Length, TextureFormat.RGBA32, false);
  for (int ri = 0; ri < rows.Length; ri++) {
    var wp = AssetDatabase.LoadAssetAtPath<GameObject>(W + rows[ri] + ".prefab");
    var grip = wp.GetComponent<WeaponGrip>();
    bool bow = rows[ri].ToLower().Contains("bow");
    var poses = new[]{ ("Idle2", 0.3f, false), (bow ? "AttackBow" : "AttackSword2H", 0.45f, false), ("Idle2", 0.3f, true) };
    for (int ci = 0; ci < 3; ci++) {
      var pru = new PreviewRenderUtility();
      GameObject rig = null;
      try {
        rig = (GameObject)UnityEngine.Object.Instantiate(model);
        rig.hideFlags = HideFlags.HideAndDontSave;
        float h = Character3DBody.MeasureHeight(rig);
        rig.transform.localScale = Vector3.one * (CharacterSpec.Active.bodyHeight / h);
        var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>();
        anim.avatar = avatar; anim.applyRootMotion = false;
        var hand = HandSocketResolver.Find(anim, grip.hand);
        var w = (GameObject)UnityEngine.Object.Instantiate(wp);
        w.hideFlags = HideFlags.HideAndDontSave;
        w.transform.SetParent(hand, false);
        w.transform.localPosition = grip.localPosition; w.transform.localRotation = Quaternion.Euler(grip.localEuler); w.transform.localScale = grip.localScale;
        pru.AddSingleGO(rig);
        var clip = Clip(poses[ci].Item1);
        var hph = new HumanPoseHandler(avatar, rig.transform);
        var hp = new HumanPose(); hph.GetHumanPose(ref hp);
        var idx = new System.Collections.Generic.Dictionary<string, int>();
        for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
        float t = clip.length * poses[ci].Item2;
        foreach (var b in AnimationUtility.GetCurveBindings(clip)) {
          if (b.type != typeof(Animator)) continue;
          var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
          if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
        }
        hph.SetHumanPose(ref hp); hph.Dispose();
        var bounds = new Bounds(rig.transform.position + Vector3.up * CharacterSpec.Active.bodyHeight * 0.5f, Vector3.zero);
        foreach (var rr in rig.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(rr.bounds);
        pru.camera.fieldOfView = 30; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 100f;
        pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
        pru.lights[0].intensity = 1.4f; pru.lights[0].transform.rotation = Quaternion.Euler(35, 150, 0);
        pru.lights[1].intensity = 0.8f;
        pru.ambientColor = new Color(0.6f, 0.6f, 0.6f);
        Vector3 dir = (rig.transform.forward + rig.transform.right * 0.7f + Vector3.up * 0.25f).normalized;
        Vector3 target; float dist;
        if (poses[ci].Item3) { target = hand.position; dist = CharacterSpec.Active.bodyHeight * 0.9f; }
        else { target = bounds.center; dist = bounds.extents.magnitude / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.05f; }
        pru.camera.transform.position = target + dir * dist;
        pru.camera.transform.LookAt(target);
        pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
        UnityEngine.Object.DestroyImmediate(pru.EndStaticPreview());          // first render can be empty
        pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
        var tex = pru.EndStaticPreview();
        var rt = RenderTexture.GetTemporary(S, S, 0); Graphics.Blit(tex, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        sheet.ReadPixels(new Rect(0, 0, S, S), ci * S, (rows.Length - 1 - ri) * S);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(tex);
      } catch (System.Exception e) { log.AppendLine(rows[ri] + " c" + ci + " ERR " + e.Message); }
      finally { pru.Cleanup(); if (rig) UnityEngine.Object.DestroyImmediate(rig); }
    }
  }
  sheet.Apply();
  System.IO.File.WriteAllBytes(outDir + "sheet" + si + ".png", sheet.EncodeToPNG());
  UnityEngine.Object.DestroyImmediate(sheet);
}
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log.AppendLine("after " + sc.name + " dirty=" + sc.isDirty);}
return log.ToString();
