string modelPath = Player3DVisualBuilder.DefaultModel;
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
Avatar avatar = null; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) if (o is Avatar a) { avatar = a; break; }
var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/1_Authored/Art/Characters/Animations/<ClipName>.anim");
var outPath = @"<SCRATCHPAD>\ours.png";
int[] frames = { 0, 3, 5, 9, 12, 15, 18, 23 };   // our frame for each sprite frame
float[] yaws = { 45f, 135f };
const int S = 180;
var sheet = new Texture2D(S * frames.Length, S * yaws.Length, TextureFormat.RGBA32, false);
var idx = new System.Collections.Generic.Dictionary<string, int>();
for (int m = 0; m < HumanTrait.MuscleName.Length; m++) idx[HumanTrait.MuscleName[m]] = m;
var binds = AnimationUtility.GetCurveBindings(clip);
for (int vi = 0; vi < yaws.Length; vi++)
for (int ci = 0; ci < frames.Length; ci++) {
  var pru = new PreviewRenderUtility(); GameObject rig = null;
  try {
    rig = (GameObject)UnityEngine.Object.Instantiate(model); rig.hideFlags = HideFlags.HideAndDontSave;
    rig.transform.localScale = Vector3.one * (CharacterSpec.Active.bodyHeight / Character3DBody.MeasureHeight(rig));
    var anim = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>(); anim.avatar = avatar; anim.applyRootMotion = false;
    pru.AddSingleGO(rig);
    var hph = new HumanPoseHandler(avatar, rig.transform); var hp = new HumanPose(); hph.GetHumanPose(ref hp);
    float t = frames[ci] / 30f; float rx = 0, ry = 0, rz = 0, qx = 0, qy = 0, qz = 0, qw = 1;
    foreach (var b in binds) {
      float v = AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
      switch (b.propertyName) {
        case "RootT.x": rx = v; continue; case "RootT.y": ry = v; continue; case "RootT.z": rz = v; continue;
        case "RootQ.x": qx = v; continue; case "RootQ.y": qy = v; continue; case "RootQ.z": qz = v; continue; case "RootQ.w": qw = v; continue;
      }
      var nm = b.propertyName.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace(".", " ");
      if (idx.TryGetValue(nm, out var mi)) hp.muscles[mi] = v;
    }
    hp.bodyPosition = new Vector3(rx, ry, rz); hp.bodyRotation = new Quaternion(qx, qy, qz, qw).normalized;
    hph.SetHumanPose(ref hp); hph.Dispose();
    float H = CharacterSpec.Active.bodyHeight;
    var target = rig.transform.position + Vector3.up * H * 0.5f + rig.transform.forward * H * 0.06f;
    pru.camera.orthographic = true; pru.camera.orthographicSize = H * 0.62f; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 100f;
    pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.82f, 0.8f, 0.75f);
    pru.lights[0].intensity = 1.6f; pru.lights[0].transform.rotation = Quaternion.Euler(40, 160, 0); pru.lights[1].intensity = 1.0f;
    pru.ambientColor = new Color(0.9f, 0.9f, 0.9f);
    float y = yaws[vi] * Mathf.Deg2Rad, p = 35f * Mathf.Deg2Rad;
    Vector3 flat = rig.transform.forward * Mathf.Cos(y) + rig.transform.right * Mathf.Sin(y);
    pru.camera.transform.position = target + (flat * Mathf.Cos(p) + Vector3.up * Mathf.Sin(p)) * H * 5f;
    pru.camera.transform.LookAt(target);
    pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true); UnityEngine.Object.DestroyImmediate(pru.EndStaticPreview());
    pru.BeginStaticPreview(new Rect(0, 0, S, S)); pru.Render(true);
    var tex = pru.EndStaticPreview();
    var rtx = RenderTexture.GetTemporary(S, S, 0); Graphics.Blit(tex, rtx);
    var prev = RenderTexture.active; RenderTexture.active = rtx;
    sheet.ReadPixels(new Rect(0, 0, S, S), ci * S, (yaws.Length - 1 - vi) * S);
    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rtx); UnityEngine.Object.DestroyImmediate(tex);
  } finally { pru.Cleanup(); if (rig) UnityEngine.Object.DestroyImmediate(rig); }
}
sheet.Apply(); System.IO.File.WriteAllBytes(outPath, sheet.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(sheet);
return "done";
