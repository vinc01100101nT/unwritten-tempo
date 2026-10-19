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
