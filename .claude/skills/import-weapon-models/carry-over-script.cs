bool WRITE = false;
const string W = "Assets/1_Authored/Prefabs/Weapons/";
const string OLD = W + "<model the item uses today>.prefab";
const string NEW = W + "<its replacement>.prefab";
var oldGripPoint = Vector3.zero;   // OLD model's grip point in its own mesh space (step 4 / S3)
var newGripPoint = Vector3.zero;   // NEW model's grip point in its own mesh space
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if (sc.isDirty) return "ABORT: scene already dirty " + sc.name;}
var o = AssetDatabase.LoadAssetAtPath<GameObject>(OLD).GetComponent<WeaponGrip>();
var nPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NEW); var n = nPrefab.GetComponent<WeaponGrip>();
if (!o.stowAuthored) return "OLD has no put-away place: NEW stays not set (hidden while put away) - its spot is a separate job";
// Keep the turn; move the position so NEW's grip point lands where OLD's sat. A pair's other copy is mirrored (-x).
Vector3 Mirror(Vector3 s) => new Vector3(-s.x, s.y, s.z);
Vector3 Shift(Vector3 pos, Vector3 euler, Vector3 sOld, Vector3 sNew) {
  var R = Quaternion.Euler(euler);
  return pos + R * Vector3.Scale(sOld, oldGripPoint) - R * Vector3.Scale(sNew, newGripPoint);
}
var pos = Shift(o.stowPosition, o.stowEuler, o.localScale, n.localScale);
var pairPos = Shift(o.stowPairPosition, o.stowPairEuler, Mirror(o.localScale), Mirror(n.localScale));
var log = "bone " + o.stowBone + "  main " + pos.ToString("F5") + " " + o.stowEuler.ToString("F2")
        + "  pair " + pairPos.ToString("F5") + " " + o.stowPairEuler.ToString("F2");
if (!WRITE) return "DRY RUN " + log;
var so = new SerializedObject(n);
so.FindProperty("stowAuthored").boolValue = true;
so.FindProperty("stowBone").intValue = (int)o.stowBone;
so.FindProperty("stowPosition").vector3Value = pos;
so.FindProperty("stowEuler").vector3Value = o.stowEuler;
so.FindProperty("stowPairPosition").vector3Value = pairPos;
so.FindProperty("stowPairEuler").vector3Value = o.stowPairEuler;
so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(nPrefab); AssetDatabase.SaveAssets();
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); log += "  after " + sc.name + " dirty=" + sc.isDirty;}
return "WROTE " + log;
