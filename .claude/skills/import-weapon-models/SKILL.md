---
name: import-weapon-models
description: ALWAYS use — no exceptions, a single weapon included — for ANY work on a weapon's 3D model — importing a weapon pack (downloaded or already imported), adding one model, replacing or swapping one weapon's model, or fixing how a weapon sits in the hand ("put these weapons in the hands", "swap the axe model", "the bow faces the wrong way"). Covers the backup, moving the pack, the pink-material fix, working out where each model is gripped, building the weapon prefabs with their hand position, checking them in private pictures in real poses, and pointing the weapon items at them.
---

# Importing 3D weapon models into the hands

Everything here runs through the live editor link (`eval_file` with a script in the scratchpad). The user does
nothing but get the pack onto the machine. First proven on 2026-10-08 with a low-poly medieval pack
(`0_Raw/TossedSalads/LowPoly-MedievalWeapons`): every hand-weapon shape built, checked in pictures, play-tested by
the user as "all looks good".

## What is already there

`1_Authored/Prefabs/Weapons/` holds ONLY that pack's weapons, every hand-weapon shape in blue (the user tints
weapons in game, so one colour is enough): `axe_1..5`, `bow_1`, `bow_2`, `bow_1_1`, `bow_2_1` (string drawn),
`bowcross_1`, `dagger_1..5`, `hammer_1..5`, `longsword_1..5`, `mace_1..4`, `spear_1..5`, `staff_1..2`, `sword_1..5`,
each `<name>_blue.prefab` with its grip set, plus one material in `Materials/`. The pack's shields, quiver, arrows
and weapon rack were deliberately not built. `bowcross_1` is held by its stock like a sword and no weapon uses it
yet — it needs its own grip before one does.

**Swapping one weapon to another shape that is already in the folder** is step 7 alone (point the item at the
prefab) plus a picture of that one weapon (step 6). Every other step is for shapes that are not there yet.

## How the game mounts a weapon (read these before changing anything)

- A weapon item (`3_Data/Resources/Items/<Name>.asset`) points at a prefab through `equipModel`.
- The prefab carries a `WeaponGrip`: which hand, plus position / rotation / scale **local to the hand bone**.
  `WeaponSocket` parents the prefab under the bone `HandSocketResolver.Find` returns and copies those values.
- The only runtime reader of `equipModel` is `WeaponSocket` (re-grep `equipModel` to confirm before you rely on it).
  Creatures carrying the same item show the same model; a thrown weapon's flying copy is the model in the hand.
- An item with no `equipModel` shows an empty hand — that is how the Fist is drawn (bare hands).
- The working references ("seeds"): `1_Authored/Prefabs/Weapons/sword_1_blue.prefab` (right hand) and
  `bow_1_blue.prefab` (**left** hand). Re-read their `WeaponGrip` values every time; never trust numbers in this file.

## The steps

**0. Contract first.** Three bullets per the project rule: which items get which models, what stays (the weapon
types the pack does not cover keep their current model), and the check (pictures next to the seeds, then the
user's play-test). Picking a shape/colour per weapon is a judgement call: decide, and label it as yours.

**1. Back up before touching anything.** Copy every prefab the weapon items currently use, plus everything it
depends on (`AssetDatabase.GetDependencies(path, true)`, project files only), plus every weapon item `.asset`,
each WITH its `.meta`, to `backups/<what>-<date>/` at the repo root. It must be OUTSIDE `unity/Assets`, or Unity
loads the copies as duplicate assets with clashing IDs. Count the copied files and say the count.

**2. Find the pack and move it to `0_Raw`.** Downloaded-but-not-imported packs sit in
`%APPDATA%/Unity/Asset Store-5.x/<publisher>/…/*.unitypackage` (import with `AssetDatabase.ImportPackage(path, false)`).
An imported one lands at `Assets/<Publisher>/`. Move it with `AssetDatabase.MoveAsset("Assets/<Publisher>",
"Assets/0_Raw/<Publisher>")` — that keeps every ID. Never edit anything inside it afterwards.
Getting the pack onto the machine needs the user's Unity account: **Window ▸ Package Management ▸ My Assets ▸
Download** (that menu path was read from Unity 6000.4 — re-read it with `Unsupported.GetSubmenus("Window")`).

**3. Measure the pack.** List every mesh with its bounds; read each pack material's `shader.name`. A pack made
for older Unity uses `Standard`, which draws **pink** here — every new weapon gets a URP `Lit` material made by
you in `1_Authored/Prefabs/Weapons/Materials/`, using the pack's texture (one material per colour, shared by all
weapons). Read the textures' size and compression too and check them against `unity/Assets/ASSETS.md`.

**4. Find where each model is gripped — measure, never assume.** Run the profile script below on the seed meshes
and the pack meshes. It prints the width of each mesh in 24 slices along its length (`y`): the handle is the
narrow run of slices, the guard or head is the wide jump. Establish, for the pack:
- which axis is the length (both packs so far: `+Y`, pommel at the low end), and
- where the mesh's own centre point sits. In the 2026-10-08 pack it sat **on the handle for every weapon**
  (middle of the handle for swords, near the butt for spears, the middle for staffs and bows), so the centre point
  WAS the grip point. A different pack may not do this — if not, compute the handle's middle from the profile.

For the seed sword (`sword_1` in the pack's model file), the handle's middle is its centre point, `(0, 0, 0)`.

**5. Build the prefabs** with the build script below. The maths, per weapon:
- **rotation** = the seed's rotation (same length axis, same flat side). Bows: the bow seed's rotation, and check
  whether the new bow needs a half turn around its length (`BOW_FLIP`) — a bow whose string sits on the other side
  of its centre point does; only a picture next to the seed bow shows it.
- **position** = the seed's handle point carried into hand space: `seed.pos + seed.rot * (seed.scale ⊙ seedHandle)`,
  minus `rot * (k ⊙ packGripPoint)` (zero when the pack's centre point is the grip).
- **scale** = ONE number for the whole pack: `seed sword length ÷ pack sword length`. That keeps the pack's own size
  differences (dagger ≈ half a sword, spear ≈ 1.75 swords). Say in the report how the new bow compares to the seed bow.
- **Name** each prefab after the pack's own prefab name without its `pfb_` prefix (`sword_1_blue`) — never invent one.

**Do NOT use the Weapon Grip Tuner's batch "Copy Grip" button for a mixed pack:** it stretches every weapon to the
seed's length, so a dagger comes out as long as a spear.

**6. Check in pictures before attaching anything.** Run the picture script below. It poses the real player model
(standing, mid-attack, close-up of the hand) holding each new weapon, beside the two seeds, and writes contact
sheets to the scratchpad. **Look at them yourself with Read; never open them on the user's screen.** Compare each
row with the seed rows: handle inside the fist, guard or head on the far side of the fist, same direction as the
seed. Fix and re-run until they match.

**7. Point the items at the new prefabs** (SerializedObject on each item, `ApplyModifiedPropertiesWithoutUndo`,
`SetDirty`, `SaveAssets`), then read each item back and `git diff` the item files. Re-saving an old item also drops
fields the code no longer declares and writes new fields at their defaults — grep `Item.cs` for each dropped name
to prove it is dead, and say so in the report.

**8. Prove nothing else moved.** Every script checks that no open scene is modified before and after (`isDirty`).
Then run `tools/check-unity-code.sh`, `tools/check-asset-rules.py`, `tools/check-editor-writes.py`, and read the
WHOLE console (`level: log`, a generous tail). Delete the scratchpad scripts and pictures in the same reply.

**9. Report:** a table of item → model → hand; the weapon types still on the old models; whether any server needs a
rebuild (it did not on 2026-10-08: only the hand mounting reads the model, and `MapServerLink.CharacterBuild` is a
fixed string — re-check both); where the backup is.

## Traps in the editor link's script runner

- **No `using` lines** — `UnityEngine` and `UnityEditor` are already imported, and a `using` line fails to compile.
- `Object` is ambiguous: write `UnityEngine.Object`.
- Extension methods need their static class spelled out (there is no `using` to bring them in).
- The default timeout is 5000 ms; pass `timeout: 60000` or more for building and rendering.
- Editing a `.cs` file in the scratchpad with `sed` is refused by the performance hook; use the Edit tool.

## Posing a character outside Play — the only way that worked

`Animator.Play` + `Animator.Update(0)` and a `PlayableGraph` with `Evaluate()` both leave the model in its T-pose
inside a preview scene (the hand bone did not move a millimetre). Set the joints directly instead:
`HumanPoseHandler.GetHumanPose`, overwrite `muscles[i]` with each humanoid clip curve evaluated at the wanted time
(curve name → `HumanTrait.MuscleName`, after turning `LeftHand.` / `RightHand.` into `Left ` / `Right ` and `.` into
a space), then `SetHumanPose`. All 95 muscles matched on the player controller's clips. The first render of a
fresh preview can come back empty — render once, throw it away, render again.

## Profile script

```csharp
var r = new System.Text.StringBuilder();
var meshes = new System.Collections.Generic.List<UnityEngine.Mesh>();
// SEED mesh: sword_1, inside the pack that is already in the game
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/0_Raw/TossedSalads/LowPoly-MedievalWeapons/Meshes/lowPoly_medievalWeapons.fbx")) if (o is UnityEngine.Mesh m0 && m0.name == "sword_1") meshes.Add(m0);
// NEW PACK meshes
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("<PACK FBX PATH>")) if (o is UnityEngine.Mesh m2) meshes.Add(m2);
const int N = 24;
foreach (var m in meshes) {
  var v = m.vertices; var b = m.bounds;
  var mnx = new float[N]; var mxx = new float[N]; var mnz = new float[N]; var mxz = new float[N]; var cnt = new int[N];
  for (int i=0;i<N;i++){mnx[i]=mnz[i]=1e9f;mxx[i]=mxz[i]=-1e9f;}
  foreach (var p in v) { int s = UnityEngine.Mathf.Clamp((int)((p.y - b.min.y) / b.size.y * N), 0, N-1); cnt[s]++; mnx[s]=UnityEngine.Mathf.Min(mnx[s],p.x); mxx[s]=UnityEngine.Mathf.Max(mxx[s],p.x); mnz[s]=UnityEngine.Mathf.Min(mnz[s],p.z); mxz[s]=UnityEngine.Mathf.Max(mxz[s],p.z); }
  r.Append(m.name + " ymin " + b.min.y.ToString("F3") + " ymax " + b.max.y.ToString("F3") + " | ");
  for (int i=0;i<N;i++) r.Append(cnt[i]==0 ? "-- " : ((mxx[i]-mnx[i]).ToString("F2") + "/" + (mxz[i]-mnz[i]).ToString("F2") + " "));
  r.AppendLine();
}
return r.ToString();
```

Each column is one slice, low end first: `width across / width through`, `--` = no vertex in that slice (low-poly
meshes leave many empty). Slice of the centre point = `(0 - ymin) / (ymax - ymin) × 24`.

## Build script

```csharp
const string W = "Assets/1_Authored/Prefabs/Weapons/";   // where the seeds live
const string OUT = W;                                      // where the new prefabs + material are written
const string FBX = "<PACK FBX PATH>";
const string TEX = "<PACK TEXTURE PATH>";
bool BOW_FLIP = false;                      // decided from the pictures, per pack
var seedHandle = Vector3.zero;              // seed sword handle middle in its own mesh space, from the profile
var packGrip = Vector3.zero;                // the NEW pack's grip point in its own mesh space, from the profile
const string PACK_SWORD = "<new pack's sword mesh name>";   // sets the one scale for the whole pack
var picks = new[]{"<new pack mesh name>", "..."};
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name;}
var log = new System.Text.StringBuilder();
var matPath = OUT + "Materials/<material name>.mat";
var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
if (mat == null) {
  mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
  mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TEX));
  mat.SetFloat("_Smoothness", 0.5f); mat.SetFloat("_Metallic", 0f);     // the seed sword's material values
  AssetDatabase.CreateAsset(mat, matPath);
}
var meshes = new System.Collections.Generic.Dictionary<string, Mesh>();
foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FBX)) if (o is Mesh m) meshes[m.name] = m;
var seedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W + "sword_1_blue.prefab");
var seed = seedPrefab.GetComponent<WeaponGrip>();
var Rs = Quaternion.Euler(seed.localEuler);
float seedLen = seedPrefab.GetComponent<MeshFilter>().sharedMesh.bounds.size.y * seed.localScale.y;
var G = seed.localPosition + Rs * Vector3.Scale(seed.localScale, seedHandle);
float k = seedLen / meshes[PACK_SWORD].bounds.size.y;
G -= Rs * (packGrip * k);
var bowSeed = AssetDatabase.LoadAssetAtPath<GameObject>(W + "bow_1_blue.prefab").GetComponent<WeaponGrip>();
var Rb = Quaternion.Euler(bowSeed.localEuler) * (BOW_FLIP ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
foreach (var name in picks) {
  var path = OUT + name + "_<colour>.prefab";
  var root = new GameObject(name + "_<colour>");
  root.AddComponent<MeshFilter>().sharedMesh = meshes[name];
  root.AddComponent<MeshRenderer>().sharedMaterial = mat;
  var g = root.AddComponent<WeaponGrip>();
  if (name.StartsWith("bow")) { g.hand = bowSeed.hand; g.localEuler = Rb.eulerAngles; g.localPosition = bowSeed.localPosition; }
  else { g.hand = seed.hand; g.localEuler = Rs.eulerAngles; g.localPosition = G; }
  g.localScale = Vector3.one * k;
  PrefabUtility.SaveAsPrefabAsset(root, path);      // same path again keeps the prefab's ID
  UnityEngine.Object.DestroyImmediate(root);
  log.AppendLine(path);
}
AssetDatabase.SaveAssets();
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log.AppendLine("after " + sc.name + " dirty=" + sc.isDirty);}
return log.ToString();
```

`packGrip` stays zero when the new pack's centre point is the grip point (step 4); the bow line assumes the same
for bows. A sword-shaped list (`picks`) can hold any melee shape — the pack's own size differences carry through `k`.

## Picture script

```csharp
var outDir = @"<SCRATCHPAD>\";
var W = "Assets/1_Authored/Prefabs/Weapons/";
var sheets = new[] {      // prefab names; keep the two seeds in so every sheet has a reference row
  new[]{"sword_1_blue","<new prefab>","<new prefab>"},
  new[]{"bow_1_blue","<new bow prefab>"} };
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
```

Columns: standing, mid-attack, close-up of the hand (that close-up is framed from behind the wrist; for a clear view
of the handle in the fist, set every muscle to 0 instead — the T-pose close-ups are the easiest to judge). The model
draws black in these pictures because of the preview lighting; only the weapon and the hand matter here.
