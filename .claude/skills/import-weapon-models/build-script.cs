const string W = "Assets/1_Authored/Prefabs/Weapons/";   // where the new prefabs go
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
// the seed sword: its saved grip (see "How the game mounts a weapon") on the raw pack mesh sword_1
Mesh seedMesh = null;
foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/0_Raw/TossedSalads/LowPoly-MedievalWeapons/Meshes/lowPoly_medievalWeapons.fbx")) if (o is Mesh m0 && m0.name == "sword_1") seedMesh = m0;
var seedPos = new Vector3(0.011f, 0.18756053f, 0.12156433f); var seedScale = Vector3.one * 0.8735323f;
var Rs = Quaternion.Euler(345.37842f, 86.71383f, 282.81427f);
float seedLen = seedMesh.bounds.size.y * seedScale.y;
var G = seedPos + Rs * Vector3.Scale(seedScale, seedHandle);
float k = seedLen / meshes[PACK_SWORD].bounds.size.y;
G -= Rs * (packGrip * k);
var bowPos = new Vector3(-0.024f, 0.226f, -0.005f);
var Rb = Quaternion.Euler(358.83505f, 51.418995f, 283.19495f) * (BOW_FLIP ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
foreach (var name in picks) {
  var path = OUT + name + "_<colour>.prefab";
  var oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
  var oldGrip = oldPrefab != null ? oldPrefab.GetComponent<WeaponGrip>() : null;   // read BEFORE it is overwritten
  var root = new GameObject(name + "_<colour>");
  root.AddComponent<MeshFilter>().sharedMesh = meshes[name];
  root.AddComponent<MeshRenderer>().sharedMaterial = mat;
  var g = root.AddComponent<WeaponGrip>();
  if (name.StartsWith("bow")) { g.hand = WeaponHand.Left; g.localEuler = Rb.eulerAngles; g.localPosition = bowPos; }
  else { g.hand = WeaponHand.Right; g.localEuler = Rs.eulerAngles; g.localPosition = G; }
  g.localScale = Vector3.one * k;
  if (oldGrip != null) {   // 5b: keep where it rests on the body — all six, the "set" flag included
    g.stowAuthored = oldGrip.stowAuthored; g.stowBone = oldGrip.stowBone;
    g.stowPosition = oldGrip.stowPosition; g.stowEuler = oldGrip.stowEuler;
    g.stowPairPosition = oldGrip.stowPairPosition; g.stowPairEuler = oldGrip.stowPairEuler;
    log.AppendLine("  put-away kept: set=" + oldGrip.stowAuthored);
  }
  PrefabUtility.SaveAsPrefabAsset(root, path);      // same path again keeps the prefab's ID
  UnityEngine.Object.DestroyImmediate(root);
  log.AppendLine(path);
}
AssetDatabase.SaveAssets();
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log.AppendLine("after " + sc.name + " dirty=" + sc.isDirty);}
return log.ToString();
