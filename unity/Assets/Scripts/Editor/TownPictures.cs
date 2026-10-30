using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// THE TOWN EDITOR'S PICTURES (tools/town-editor): every kit piece and model of a town's plan, its plant brush's plants
/// and the stone bridge, photographed straight down at the size and scale the town builder sets them at, so the editor
/// draws each at its true size and turn. For the editor's 3D view, every piece and plant is also photographed from the
/// game camera's own tilt (read from the Systems scene's camera) at eight turns, and the bridge's model is written out
/// whole with its picture. Written to tools/town-editor/pictures/&lt;town&gt;/ with an index of where each picture's edges
/// lie around the piece's own point; nothing in the project is changed. The ground's tiles need no photograph: the
/// editor draws the tile pictures themselves.
/// </summary>
public static class TownPictures
{
    // Pixels per unit of ground, and the largest a picture may be.
    const float PixelsPerUnit = 48f;
    const int MaxPixels = 1024;
    // The turns each piece is photographed at for the 3D view: one every 360 / Turns degrees.
    const int Turns = 8;
    const string SystemsScene = "Assets/Scenes/Systems.unity";
    const string CameraScript = "Assets/Scripts/CameraFollow3D.cs";

    [System.Serializable] sealed class Plan { public string id; public Kit[] kit; public Plant[] plants; }
    [System.Serializable] sealed class Kit { public string kind, prefab, role; public float scale; }
    [System.Serializable] sealed class Plant { public string kind, prefab; }

    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    /// <summary>Photographs everything <paramref name="townId"/>'s plan can place. Returns a report.</summary>
    public static string Take(string townId)
    {
        var planPath = Path.Combine(Root, "data", "towns", townId + ".json");
        if (!File.Exists(planPath)) throw new System.Exception("no plan at " + planPath);
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
        var dir = Path.Combine(Root, "tools", "town-editor", "pictures", townId);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        float nature = SeamlessWorldBuilder.NatureScale(new StringBuilder());
        var camera = GameCamera(out float pitch);
        var index = new StringBuilder("{\n");
        var views = new StringBuilder();
        int taken = 0, skipped = 0;
        var pru = new PreviewRenderUtility();
        try
        {
            pru.camera.orthographic = true;
            pru.camera.clearFlags = CameraClearFlags.SolidColor;
            pru.lights[0].intensity = 1.2f;
            pru.lights[0].transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            pru.ambientColor = new Color(0.45f, 0.45f, 0.45f);
            var cam = pru.camera;

            // Twice, on black and on white: what differs between them is how see-through each pixel is.
            void Snap(int w, int h, float half, string file)
            {
                Texture2D On(Color bg)
                {
                    cam.backgroundColor = bg;
                    pru.BeginStaticPreview(new Rect(0, 0, w, h));
                    cam.aspect = w / (float)h;
                    cam.orthographicSize = half;
                    pru.Render(true);
                    return pru.EndStaticPreview();
                }
                var black = On(Color.black); var white = On(Color.white);
                var outPic = new Texture2D(black.width, black.height, TextureFormat.RGBA32, false);
                var pb = black.GetPixels(); var pw = white.GetPixels(); var po = new Color[pb.Length];
                for (int i = 0; i < pb.Length; i++)
                {
                    float a = Mathf.Clamp01(1f - ((pw[i].r - pb[i].r) + (pw[i].g - pb[i].g) + (pw[i].b - pb[i].b)) / 3f);
                    po[i] = a < 0.02f ? Color.clear : new Color(Mathf.Clamp01(pb[i].r / a), Mathf.Clamp01(pb[i].g / a), Mathf.Clamp01(pb[i].b / a), a);
                }
                outPic.SetPixels(po); outPic.Apply();
                File.WriteAllBytes(Path.Combine(dir, file), outPic.EncodeToPNG());
                Object.DestroyImmediate(black); Object.DestroyImmediate(white); Object.DestroyImmediate(outPic);
                taken++;
            }

            // A piece photographed from above (its entry returned), and, when `turned` is given, from the game camera's
            // tilt at each of the eight turns (their entries appended to it).
            string Shoot(string prefabPath, float scale, string file, List<string> turned)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) throw new System.Exception("no prefab " + prefabPath);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    // Unturned, as the town builder sets every piece's own turn over the prefab's (House1-3, the Tavern, the
                    // barrel, basket, sack, table and chair are saved turned 180 degrees, and were photographed back to front
                    // while the prefab's turn was kept).
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    // 0: the prefab keeps its own size, as the town builder leaves a kit piece the plan gives no scale.
                    if (scale > 0f) go.transform.localScale = Vector3.one * scale;
                    foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                    pru.AddSingleGO(go);
                    bool any = false; var b = new Bounds();
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        if (r is ParticleSystemRenderer || !r.enabled) continue;
                        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    }
                    if (!any || b.size.x < 1e-3f) { skipped++; return null; }
                    // A plant that is one upright card is a line from above: it is photographed from the front instead,
                    // and the editor draws it in a square as wide as it is.
                    bool front = b.size.z < 0.01f * b.size.x;
                    float across = front ? b.size.y : b.size.z;
                    int w = Mathf.Clamp(Mathf.CeilToInt(b.size.x * PixelsPerUnit), 8, MaxPixels);
                    int h = Mathf.Clamp(Mathf.CeilToInt(across * PixelsPerUnit), 8, MaxPixels);
                    cam.transform.rotation = front ? Quaternion.identity : Quaternion.Euler(90f, 0f, 0f);
                    cam.transform.position = front ? new Vector3(b.center.x, b.center.y, b.min.z - 2f) : new Vector3(b.center.x, b.max.y + 2f, b.center.z);
                    cam.nearClipPlane = 0.05f; cam.farClipPlane = (front ? b.size.z : b.size.y) + 4f;
                    Snap(w, h, across * 0.5f, file);

                    if (turned != null)
                    {
                        var pivot = go.transform.position;
                        for (int k = 0; k < Turns; k++)
                        {
                            var rot = Quaternion.Euler(pitch, k * 360f / Turns, 0f);
                            Vector3 right = rot * Vector3.right, up = rot * Vector3.up, ahead = rot * Vector3.forward;
                            float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue, f0 = float.MaxValue, f1 = float.MinValue;
                            for (int c = 0; c < 8; c++)
                            {
                                var p = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                                float pr = Vector3.Dot(p, right), pu = Vector3.Dot(p, up), pf = Vector3.Dot(p, ahead);
                                r0 = Mathf.Min(r0, pr); r1 = Mathf.Max(r1, pr); u0 = Mathf.Min(u0, pu); u1 = Mathf.Max(u1, pu); f0 = Mathf.Min(f0, pf); f1 = Mathf.Max(f1, pf);
                            }
                            int tw = Mathf.Clamp(Mathf.CeilToInt((r1 - r0) * PixelsPerUnit), 8, MaxPixels);
                            int th = Mathf.Clamp(Mathf.CeilToInt((u1 - u0) * PixelsPerUnit), 8, MaxPixels);
                            cam.transform.rotation = rot;
                            cam.transform.position = right * ((r0 + r1) * 0.5f) + up * ((u0 + u1) * 0.5f) + ahead * (f0 - 2f);
                            cam.nearClipPlane = 0.05f; cam.farClipPlane = f1 - f0 + 4f;
                            string tf = Path.GetFileNameWithoutExtension(file) + "_t" + k + ".png";
                            Snap(tw, th, (u1 - u0) * 0.5f, tf);
                            // Its size in units, and where the piece's own point lies in it, from its left and bottom edges.
                            turned.Add($"{{\"file\": \"{tf}\", \"w\": {F(r1 - r0)}, \"h\": {F(u1 - u0)}, \"px\": {F(Vector3.Dot(pivot, right) - r0)}, \"py\": {F(Vector3.Dot(pivot, up) - u0)}}}");
                        }
                    }
                    if (front) return $"{{\"file\": \"{file}\", \"front\": true, \"x0\": {F(b.min.x)}, \"z0\": {F(-b.size.x / 2f)}, \"x1\": {F(b.max.x)}, \"z1\": {F(b.size.x / 2f)}}}";
                    return $"{{\"file\": \"{file}\", \"x0\": {F(b.min.x)}, \"z0\": {F(b.min.z)}, \"x1\": {F(b.max.x)}, \"z1\": {F(b.max.z)}}}";
                }
                finally { Object.DestroyImmediate(go); }
            }
            string Turned(List<string> t) => t.Count == Turns ? "[" + string.Join(", ", t) + "]" : "null";

            index.Append(" \"kit\": {");
            views.Append(" \"kitTurned\": {");
            for (int k = 0; k < plan.kit.Length; k++)
            {
                var kit = plan.kit[k];
                bool natural = kit.role == "tree" || kit.role == "bush" || kit.role == "effect";
                float scale = natural ? nature : kit.scale > 0f ? kit.scale : 0f;
                var shots = new List<string>(); var turns = new List<string>();
                for (int n = 1; ; n++)
                {
                    string path = kit.prefab.Replace("{n}", n.ToString());
                    if (n > 1 && (!kit.prefab.Contains("{n}") || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)) break;
                    var t = new List<string>();
                    shots.Add(Shoot(path, scale, $"kit_{kit.kind}_{n}.png", t) ?? "null");
                    turns.Add(Turned(t));
                }
                index.Append(k > 0 ? ",\n  " : "\n  ").Append($"\"{kit.kind}\": [{string.Join(", ", shots)}]");
                views.Append(k > 0 ? ",\n  " : "\n  ").Append($"\"{kit.kind}\": [{string.Join(", ", turns)}]");
            }
            index.Append("\n },\n \"plants\": {");
            views.Append("\n },\n \"plantsTurned\": {");
            for (int k = 0; k < (plan.plants?.Length ?? 0); k++)
            {
                var t = new List<string>();
                index.Append(k > 0 ? ",\n  " : "\n  ").Append($"\"{plan.plants[k].kind}\": {Shoot(plan.plants[k].prefab, nature, $"plant_{plan.plants[k].kind}.png", t) ?? "null"}");
                views.Append(k > 0 ? ",\n  " : "\n  ").Append($"\"{plan.plants[k].kind}\": {Turned(t)}");
            }
            index.Append("\n },\n \"bridge\": ").Append(Shoot(BridgeDecks.PrefabPath, 1f, "bridge.png", null) ?? "null").Append(",\n");
            views.Append("\n },\n");
        }
        finally { pru.Cleanup(); }
        // The town builder's numbers the 3D view lays the ground by: one curb's height, the stream's bed and water, the
        // bridge's walkway samples, the ramps up to its ends, and the curb's tile, how far along one picture of it runs and
        // how wide its band of stones is.
        var bed = new GroundBlendBuilder.CurbedExtras();
        string ground = $"{{\"levelHeight\": {F(GroundElevation.LevelHeight)}, \"bedDepth\": {F(bed.depth)}, \"bankSlope\": {F(bed.bankSlope)}, "
                        + $"\"waterBelow\": {F(bed.waterBelow)}, \"deckAlong\": {F(BridgeDecks.DeckAlong)}, \"deckSink\": {F(BridgeDecks.DeckSink)}, "
                        + $"\"rampLength\": {F(TownPieceBuilder.RampLength)}, \"curbTile\": \"{GroundBlendBuilder.CurbTile}\", "
                        + $"\"curbAlong\": {F(GroundBlendBuilder.CurbWorldPerTexture)}, \"curbBand\": {F(GroundBlendBuilder.CurbBandWidth(GroundTiles.Load(GroundBlendBuilder.CurbTile)))}}}";
        index.Append(views).Append(" \"camera\": ").Append(camera).Append(",\n \"ground\": ").Append(ground)
             .Append(",\n \"bridgeModel\": ").Append(BridgeModel(dir)).Append("\n}\n");
        File.WriteAllText(Path.Combine(dir, "index.json"), index.ToString());
        return $"{plan.id}: {taken} pictures taken, {skipped} pieces with nothing to photograph (effects), in {dir}.";
    }

    // THE GAME CAMERA as the Systems scene saves it (the scene's values win over the script's): its tilt at the zoom the game
    // starts at — height held under the steepest tilt it allows, looking at a point lookAtHeight above the player — its
    // field of view and how near and far it zooms.
    static string GameCamera(out float pitch)
    {
        string scene = File.ReadAllText(Path.Combine(Root, "unity", SystemsScene));
        string guid = Regex.Match(File.ReadAllText(Path.Combine(Root, "unity", CameraScript + ".meta")), @"guid: (\w+)").Groups[1].Value;
        string Block(string marker)
        {
            int at = scene.IndexOf(marker, System.StringComparison.Ordinal);
            if (at < 0) throw new System.Exception("the Systems scene has no " + marker);
            int start = scene.LastIndexOf("--- !u!", at, System.StringComparison.Ordinal), end = scene.IndexOf("--- !u!", at, System.StringComparison.Ordinal);
            return scene.Substring(start, (end < 0 ? scene.Length : end) - start);
        }
        float Num(string block, string field)
        {
            var m = Regex.Match(block, "\n  " + Regex.Escape(field) + @": ([-0-9.eE]+)");
            if (!m.Success) throw new System.Exception("the game camera has no " + field);
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        string follow = Block("guid: " + guid);
        string owner = Regex.Match(follow, @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;
        string lens = null;
        foreach (Match m in Regex.Matches(scene, @"--- !u!20 &\d+[^\n]*\n(?:(?!--- !u!)[\s\S])*"))
            if (m.Value.Contains("m_GameObject: {fileID: " + owner + "}")) lens = m.Value;
        if (lens == null) throw new System.Exception("the game camera's object has no Camera in the Systems scene");
        float distance = Num(follow, "distance"), look = Num(follow, "lookAtHeight");
        float height = Mathf.Clamp(Num(follow, "height"), Num(follow, "minHeight"), Num(follow, "maxHeight"));
        height = Mathf.Min(height, distance * Mathf.Tan(Mathf.Clamp(Num(follow, "maxPitch"), 5f, 89f) * Mathf.Deg2Rad));
        pitch = Mathf.Atan2(height - look, distance) * Mathf.Rad2Deg;
        return $"{{\"pitch\": {F(pitch)}, \"fov\": {F(Num(lens, "field of view"))}, \"distance\": {F(new Vector2(distance, height - look).magnitude)}, "
               + $"\"minDistance\": {F(Num(follow, "minDistance"))}, \"maxDistance\": {F(Num(follow, "maxDistance"))}, \"maxPitch\": {F(Num(follow, "maxPitch"))}, "
               + $"\"lookAtHeight\": {F(look)}, \"yaw\": {F(Num(follow, "yaw"))}}}";
    }

    // THE BRIDGE'S MODEL as the town builder joins it into the ground (BridgeDecks): its near-detail triangles at its own
    // size, their picture's places, its bounds, and its picture.
    static string BridgeModel(string dir)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BridgeDecks.PrefabPath));
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var drawn = BridgeDecks.DrawnUpClose(go);
            var b = BridgeDecks.BoundsOf(drawn, go.transform);
            var v = new StringBuilder(); var uv = new StringBuilder(); var tris = new StringBuilder();
            int baseAt = 0; Texture picture = null;
            foreach (var r in drawn)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh; var m = go.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var pts = mesh.vertices; var uvs = mesh.uv;
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = m.MultiplyPoint3x4(pts[i]);
                    v.Append(v.Length > 0 ? "," : "").Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z));
                    var q = i < uvs.Length ? uvs[i] : Vector2.zero;
                    uv.Append(uv.Length > 0 ? "," : "").Append(F(q.x)).Append(',').Append(F(q.y));
                }
                foreach (int t in mesh.triangles) tris.Append(tris.Length > 0 ? "," : "").Append(t + baseAt);
                baseAt += pts.Length;
                if (picture == null && r.sharedMaterial != null) picture = r.sharedMaterial.mainTexture;
            }
            if (picture != null)
            {
                var rt = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var was = RenderTexture.active;
                Graphics.Blit(picture, rt); RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); tex.Apply();
                RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(dir, "bridge_picture.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            return $"{{\"picture\": {(picture != null ? "\"bridge_picture.png\"" : "null")}, \"center\": [{F(b.center.x)}, {F(b.center.y)}, {F(b.center.z)}], "
                   + $"\"size\": [{F(b.size.x)}, {F(b.size.y)}, {F(b.size.z)}], \"v\": [{v}], \"uv\": [{uv}], \"tris\": [{tris}]}}";
        }
        finally { Object.DestroyImmediate(go); }
    }

    static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
