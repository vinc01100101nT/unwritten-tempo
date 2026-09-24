using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Options window for the Map Painter pipeline. You DRAG your own assets into rows and pick each row's role
/// (Object / Wall / Ground) — no preset brushes, no hard-coded grass/path/stone. It <b>Exports</b>
/// <c>prop-manifest.json</c> (+ optional top-down thumbnails) into <c>tools/map-painter/</c> so the web painter
/// shows the real props and always knows current sizes. Ids, swatch colours and ground blend-channels are derived
/// by <see cref="BrushPalette.Resolve"/>, the single source of truth the importer reads too.
/// </summary>
public class BrushPaletteWindow : EditorWindow
{
    const string PropsDir = "Assets/1_Authored/Prefabs/Props";
    const string DefaultPalettePath = "Assets/3_Data/ScriptableObjects/BrushPalette.asset";

    BrushPalette _palette;
    Vector2 _scroll;
    string _status = "";
    // When on, Export Manifest also snaps a top-down PNG cutout of every prefab into tools/map-painter/thumbnails/
    // so the web painter can draw the real prop instead of a flat colour block. Off = manifest only (faster).
    bool _renderThumbnails = true;

    const int ThumbPx = 128;
    const string ThumbsSubdir = "thumbnails";

    [MenuItem("Tools/unwritten/World/Brush Palette…", priority = 190)]
    static void Open() => GetWindow<BrushPaletteWindow>("Brush Palette");

    void OnEnable() { if (_palette == null) _palette = FindPalette(); }

    static BrushPalette FindPalette()
    {
        var guids = AssetDatabase.FindAssets("t:BrushPalette");
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<BrushPalette>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        _palette = (BrushPalette)EditorGUILayout.ObjectField("Palette", _palette, typeof(BrushPalette), false);

        if (_palette == null)
        {
            EditorGUILayout.HelpBox("No Brush Palette found. Create one, then drag your assets in.", MessageType.Info);
            if (GUILayout.Button("Create Palette (auto-filled from Props)", GUILayout.Height(28))) CreateDefaultPalette();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
            return;
        }

        var so = new SerializedObject(_palette);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("cellSize"));
        so.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Brushes — drag assets in, pick where each goes", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        int groundOrdinal = 0;
        int removeAt = -1, moveFrom = -1, moveTo = -1;
        for (int i = 0; i < _palette.entries.Count; i++)
        {
            var e = _palette.entries[i];
            if (e == null) continue;
            // Match Resolve(): only ground rows with an asset consume a blend channel, in order.
            int myGround = (e.role == BrushRole.Ground && e.groundAsset != null) ? groundOrdinal++ : -1;

            if (DrawEntry(e, i, myGround, ref moveFrom, ref moveTo)) removeAt = i;
            EditorGUILayout.Space(2);
        }
        EditorGUILayout.EndScrollView();

        if (removeAt >= 0) { RecordUndo("Remove Brush"); _palette.entries.RemoveAt(removeAt); Save(); }
        if (moveFrom >= 0 && moveTo >= 0 && moveTo < _palette.entries.Count)
        {
            RecordUndo("Reorder Brush");
            var it = _palette.entries[moveFrom]; _palette.entries.RemoveAt(moveFrom); _palette.entries.Insert(moveTo, it); Save();
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("＋ Object group")) AddEntry(BrushRole.Object);
        if (GUILayout.Button("＋ Wall"))         AddEntry(BrushRole.Wall);
        if (GUILayout.Button("＋ Ground"))       AddEntry(BrushRole.Ground);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🔄 Auto-fill from Props", GUILayout.Height(30))) { PopulateDefaults(); }
        if (GUILayout.Button("📤 Export Manifest", GUILayout.Height(30))) ExportManifest();
        EditorGUILayout.EndHorizontal();
        _renderThumbnails = EditorGUILayout.ToggleLeft(
            new GUIContent("Render prop thumbnails on export",
                "Snap a top-down PNG of each prefab into tools/map-painter/thumbnails/ so the painter shows the real prop, not a colour block. Slower export."),
            _renderThumbnails);

        int grounds = 0; foreach (var e in _palette.entries) if (e != null && e.role == BrushRole.Ground) grounds++;
        if (grounds > BrushPalette.MaxGroundChannels)
            EditorGUILayout.HelpBox($"{grounds} Ground rows, but the blend shader supports only {BrushPalette.MaxGroundChannels}. " +
                                    "The extras (greyed out) are ignored on export.", MessageType.Warning);

        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    // Draw one brush row; returns true if its ✕ (remove) was clicked. Populates moveFrom/moveTo when ▲/▼ pressed.
    bool DrawEntry(PaletteEntry e, int index, int groundOrdinal, ref int moveFrom, ref int moveTo)
    {
        bool remove = false;
        bool overLimit = groundOrdinal >= BrushPalette.MaxGroundChannels;   // ground beyond slot 5 → ignored
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var role = (BrushRole)EditorGUILayout.EnumPopup(e.role, GUILayout.Width(90));
            e.label = EditorGUILayout.TextField(e.label, GUILayout.MinWidth(70));
            if (EditorGUI.EndChangeCheck()) { RecordUndo("Edit Brush"); e.role = role; Save(); }

            // Swatch: effective colour (auto if the field is blank), editable — a change pins an explicit colour.
            Color eff = ColorUtility.TryParseHtmlString(string.IsNullOrWhiteSpace(e.colorHex) ? "#888888" : e.colorHex, out var c) ? c : Color.grey;
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.ColorField(GUIContent.none, eff, false, false, false, GUILayout.Width(40));
            if (EditorGUI.EndChangeCheck()) { RecordUndo("Brush Colour"); e.colorHex = "#" + ColorUtility.ToHtmlStringRGB(picked); Save(); }

            if (GUILayout.Button("▲", GUILayout.Width(22)) && index > 0) { moveFrom = index; moveTo = index - 1; }
            if (GUILayout.Button("▼", GUILayout.Width(22))) { moveFrom = index; moveTo = index + 1; }
            if (GUILayout.Button("✕", GUILayout.Width(22))) remove = true;
            EditorGUILayout.EndHorizontal();

            if (e.role == BrushRole.Ground) DrawGroundField(e, groundOrdinal, overLimit);
            else DrawPrefabList(e);
        }
        return remove;
    }

    void DrawGroundField(PaletteEntry e, int groundOrdinal, bool overLimit)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        var ga = EditorGUILayout.ObjectField(GUIContent.none, e.groundAsset, typeof(Object), false);   // texture / material / prop
        if (EditorGUI.EndChangeCheck()) { RecordUndo("Ground Asset"); e.groundAsset = ga; Save(); }
        string slot = groundOrdinal < 0 ? "— pick an asset —"
                    : overLimit ? "over limit — ignored"
                    : groundOrdinal == 0 ? "channel 0 · base layer" : $"channel {groundOrdinal} · overlay";
        EditorGUILayout.LabelField(slot, EditorStyles.miniLabel, GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();
    }

    // Object/Wall: a drop zone that accepts one-or-many dragged prefabs + the current list with per-item remove.
    void DrawPrefabList(PaletteEntry e)
    {
        var drop = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
        GUI.Box(drop, e.prefabs.Count == 0 ? "Drag prefab(s) here" : $"{e.prefabs.Count} asset(s) — drag more here", EditorStyles.helpBox);
        var ev = Event.current;
        if ((ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && drop.Contains(ev.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                RecordUndo("Add Prefabs");
                foreach (var obj in DragAndDrop.objectReferences)
                    if (obj is GameObject go && !e.prefabs.Contains(go)) e.prefabs.Add(go);
                Save();
            }
            ev.Use();
        }

        int rm = -1;
        for (int k = 0; k < e.prefabs.Count; k++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var pf = (GameObject)EditorGUILayout.ObjectField(e.prefabs[k], typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck()) { RecordUndo("Edit Prefab"); e.prefabs[k] = pf; Save(); }
            if (GUILayout.Button("✕", GUILayout.Width(22))) rm = k;
            EditorGUILayout.EndHorizontal();
        }
        if (rm >= 0) { RecordUndo("Remove Prefab"); e.prefabs.RemoveAt(rm); Save(); }

        if (e.role == BrushRole.Object)
        {
            EditorGUI.BeginChangeCheck();
            float fo = EditorGUILayout.FloatField(new GUIContent("Facing offset°", "Yaw added on import so the mesh's front matches the painter arrow (e.g. 180 for a house whose front faces −Z)."), e.forwardOffset);
            if (EditorGUI.EndChangeCheck()) { RecordUndo("Facing Offset"); e.forwardOffset = fo; Save(); }
        }
    }

    void AddEntry(BrushRole role)
    {
        RecordUndo("Add Brush");
        _palette.entries.Add(new PaletteEntry { role = role, label = role == BrushRole.Wall ? "Wall" : "" });
        Save();
    }

    void RecordUndo(string label) { if (_palette != null) Undo.RecordObject(_palette, label); }
    void Save() { EditorUtility.SetDirty(_palette); }

    // ── create + auto-fill ─────────────────────────────────────────────────────────────────────────────────
    void CreateDefaultPalette()
    {
        var pal = CreateInstance<BrushPalette>();
        pal.cellSize = 4f;
        AssetDatabase.CreateAsset(pal, DefaultPalettePath);
        AssetDatabase.SaveAssets();
        _palette = pal;
        PopulateDefaults();
        _status = $"Created {DefaultPalettePath} and auto-filled it. Tweak the rows, then Export Manifest.";
    }

    // Build a working starter palette from the Props folder by naming convention: object groups (houses/trees/wall/
    // landmark) + the five ground textures. You then drag/drop to customise. Replaces the whole entry list.
    void PopulateDefaults()
    {
        RecordUndo("Auto-fill Palette");
        var names = AllPropNames();
        var entries = new List<PaletteEntry>
        {
            ObjectGroup("Houses",   Match(names, new[] { "house" }, new[] { "door", "fireplace", "shutter" }), 180f),
            ObjectGroup("Trees",    Match(names, new[] { "tree" },  new[] { "stump" }), 0f),
            ObjectGroup("Landmark", Match(names, new[] { "tavern", "windmill" }, new[] { "door", "interior", "blade", "curtain", "chandelier" }), 0f),
        };
        var wall = new PaletteEntry { role = BrushRole.Wall, label = "Wall" };
        foreach (var n in Match(names, new[] { "wall_part", "wall_tower", "wall_corner", "wall_gate", "wall_entrance" }, new[] { "woodenwall", "village" }))
        { var go = LoadProp(n); if (go != null) wall.prefabs.Add(go); }
        entries.Add(wall);

        foreach (var g in new[] { "ground_grass", "ground_road", "ground_stones", "ground_rock", "ground_soil" })
        { var asset = FindGroundAsset(g); if (asset != null) entries.Add(new PaletteEntry { role = BrushRole.Ground, groundAsset = asset }); }

        _palette.entries = entries;
        Save();
        AssetDatabase.SaveAssets();
        int props = 0; foreach (var e in entries) props += e.prefabs.Count;
        _status = $"Auto-filled {entries.Count} brush(es), {props} prop(s). Tweak, then Export Manifest.";
    }

    static readonly string[] GlobalExclude = { "ucx_", "_lod1", "_lod2", "_lod3" };

    PaletteEntry ObjectGroup(string label, List<string> propNames, float fwd)
    {
        var e = new PaletteEntry { role = BrushRole.Object, label = label, forwardOffset = fwd };
        foreach (var n in propNames) { var go = LoadProp(n); if (go != null) e.prefabs.Add(go); }
        return e;
    }

    static List<string> Match(List<string> names, string[] keys, string[] extraExclude)
    {
        var matched = new List<string>();
        foreach (var n in names)
        {
            string low = n.ToLowerInvariant();
            if (ContainsAny(low, GlobalExclude) || ContainsAny(low, extraExclude)) continue;
            if (ContainsAny(low, keys)) matched.Add(n);
        }
        matched.Sort();
        return matched;
    }

    // ── export the manifest the web painter reads ────────────────────────────────────────────────────────
    void ExportManifest()
    {
        var mf = new Manifest
        {
            version = 1,
            exportedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            cellSize = _palette.cellSize,
            groundTexWorldSize = GroundBlendBuilder.GroundTexWorldSize,
        };

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/map-painter"));
        Directory.CreateDirectory(dir);
        string thumbsDir = Path.Combine(dir, ThumbsSubdir);
        if (_renderThumbnails) Directory.CreateDirectory(thumbsDir);

        int measured = 0, thumbs = 0;
        try
        {
            foreach (var rb in _palette.Resolve())
            {
                var mb = new MfBrush
                {
                    id = rb.id, label = rb.label, color = rb.colorHex,
                    kind = rb.IsGround ? "ground" : "object",
                    groundTex = rb.groundTex ?? "",
                    channel = rb.channel,
                };
                if (!rb.IsGround)
                {
                    float maxX = 0f, maxZ = 0f;
                    foreach (var n in rb.prefabNames)
                    {
                        if (!Footprint(n, 1f, out float fx, out float fz)) continue;   // native prefab size (no scaling)
                        string thumb = "";
                        if (_renderThumbnails)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar("Export Manifest",
                                    $"Rendering thumbnail: {n}", Mathf.Clamp01(thumbs / 64f)))
                                break;
                            thumb = RenderThumbnail(n, thumbsDir);
                            if (!string.IsNullOrEmpty(thumb)) thumbs++;
                        }
                        mb.prefabs.Add(new MfPrefab { name = n, path = PropResolver.PathFor(n) ?? $"{PropsDir}/{n}.prefab", x = fx, z = fz, thumb = thumb });
                        maxX = Mathf.Max(maxX, fx); maxZ = Mathf.Max(maxZ, fz);
                        measured++;
                    }
                    mb.hasFootprint = mb.prefabs.Count > 0;
                    mb.footprint = new MfFoot { x = maxX, z = maxZ };
                }
                else if (_renderThumbnails && !string.IsNullOrEmpty(rb.groundTex))
                {
                    // Ground's "thumbnail" is just its tile texture — write it out so the painter can draw the real ground.
                    var tex = FindGroundTexture(rb.groundTex);
                    byte[] png = tex != null ? TextureToPng(tex) : null;
                    if (png != null)
                    {
                        File.WriteAllBytes(Path.Combine(thumbsDir, rb.groundTex + ".png"), png);
                        mb.thumb = $"{ThumbsSubdir}/{rb.groundTex}.png";
                        thumbs++;
                    }
                }
                mf.brushes.Add(mb);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        string outPath = Path.Combine(dir, "prop-manifest.json");
        File.WriteAllText(outPath, JsonUtility.ToJson(mf, true));
        AssetDatabase.Refresh();
        string thumbMsg = _renderThumbnails ? $", {thumbs} thumbnail(s)" : "";
        _status = $"Exported {mf.brushes.Count} brush(es), {measured} measured prefab(s){thumbMsg} →\n{outPath}";
        Debug.Log($"[unwritten] Brush manifest exported: {outPath} ({mf.brushes.Count} brushes, {measured} prefabs{thumbMsg}).");
    }

    // ── top-down thumbnail render ─────────────────────────────────────────────────────────────────────────
    // Snap a top-down PNG cutout of a prop into <thumbsDir>/<name>.png; returns the manifest-relative path
    // ("thumbnails/<name>.png") or "" on failure. Uses an isolated PreviewRenderUtility scene (never touches the
    // open scene), orthographic straight-down so there's no perspective skew, +Z aligned to image-up so the picture
    // agrees with the painter's facing arrow. Alpha is extracted by rendering the prop over BLACK and over WHITE and
    // solving the compositing equation per pixel — a clean cutout regardless of how URP writes the background alpha.
    static string RenderThumbnail(string name, string thumbsDir)
    {
        var pf = LoadProp(name);
        if (pf == null) return "";

        var pru = new PreviewRenderUtility();
        GameObject inst = null;
        try
        {
            inst = Object.Instantiate(pf);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            if (!PropPlacement.TryWorldBounds(inst, out var bnds) || bnds.size.x <= 0f || bnds.size.z <= 0f)
                return "";

            pru.AddSingleGO(inst);

            // The painter stretches this image over the prop's EXACT footprint rect (bounds x × z) — so the render
            // must cover exactly that rect. The old SQUARE render (sized to the long axis + margin) left empty
            // margin along the short axis, and stretching that square into the non-square footprint squashed and
            // shrank every elongated prop (House_D drew at half height). Viewport pixel aspect = footprint aspect,
            // ortho half-height = half the Z extent, no margin → image edge == bounds edge, painter maps it 1:1.
            float fx = bnds.size.x, fz = bnds.size.z;
            int pxW = fx >= fz ? ThumbPx : Mathf.Max(8, Mathf.RoundToInt(ThumbPx * fx / fz));
            int pxH = fz >= fx ? ThumbPx : Mathf.Max(8, Mathf.RoundToInt(ThumbPx * fz / fx));
            var cam = pru.camera;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(0.025f, fz * 0.5f);
            cam.transform.position = new Vector3(bnds.center.x, bnds.max.y + 10f, bnds.center.z);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);               // look down; +Z = up in image
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = bnds.size.y + 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;

            pru.lights[0].intensity = 1.1f; pru.lights[0].transform.rotation = Quaternion.Euler(50f, 40f, 0f);
            pru.lights[1].intensity = 0.5f; pru.lights[1].transform.rotation = Quaternion.Euler(-20f, -140f, 0f);
            pru.ambientColor = new Color(0.55f, 0.55f, 0.55f, 1f);

            var onBlack = RenderOnce(pru, Color.black, pxW, pxH);
            var onWhite = RenderOnce(pru, Color.white, pxW, pxH);
            var cut = ExtractAlpha(onBlack, onWhite);
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);

            byte[] png = cut.EncodeToPNG();
            Object.DestroyImmediate(cut);
            File.WriteAllBytes(Path.Combine(thumbsDir, name + ".png"), png);
            return $"{ThumbsSubdir}/{name}.png";
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[unwritten] Thumbnail render failed for '{name}': {ex.Message}");
            return "";
        }
        finally
        {
            if (inst != null) Object.DestroyImmediate(inst);
            pru.Cleanup();
        }
    }

    static Texture2D RenderOnce(PreviewRenderUtility pru, Color bg, int w, int h)
    {
        pru.camera.backgroundColor = bg;
        pru.BeginStaticPreview(new Rect(0, 0, w, h));
        pru.Render(true);
        return pru.EndStaticPreview();   // readable Texture2D, opaque fill = bg
    }

    // Solve straight alpha from the two backdrops: over-black gives premultiplied colour (a·c); over-white adds (1−a).
    // So (white − black) = (1 − a) per channel → a = 1 − mean(white − black); then un-premultiply the colour by a.
    static Texture2D ExtractAlpha(Texture2D black, Texture2D white)
    {
        int w = black.width, h = black.height;
        var pb = black.GetPixels();
        var pw = white.GetPixels();
        var po = new Color[pb.Length];
        for (int i = 0; i < pb.Length; i++)
        {
            float a = Mathf.Clamp01(1f - ((pw[i].r - pb[i].r) + (pw[i].g - pb[i].g) + (pw[i].b - pb[i].b)) / 3f);
            po[i] = a > 0.0001f
                ? new Color(pb[i].r / a, pb[i].g / a, pb[i].b / a, a)
                : new Color(0f, 0f, 0f, 0f);
        }
        var outT = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outT.SetPixels(po);
        outT.Apply();
        return outT;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────
    static List<string> AllPropNames()
    {
        var list = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsDir }))
            list.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
        return list;
    }

    static GameObject LoadProp(string name) => PropResolver.Load(name);   // resolves by name across Props subfolders

    // Ground brushes paint with a tile from the one tile folder, found by name the same way the ground builder finds it.
    static Object FindGroundAsset(string name) => GroundTiles.Load(name);
    static Texture FindGroundTexture(string name) => GroundTiles.Load(name);

    // Encode any texture to a ThumbPx PNG via a RenderTexture blit — works even if the source isn't marked readable.
    static byte[] TextureToPng(Texture src)
    {
        var rt = RenderTexture.GetTemporary(ThumbPx, ThumbPx, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(ThumbPx, ThumbPx, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, ThumbPx, ThumbPx), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }
        catch (System.Exception e) { Debug.LogWarning($"[unwritten] Ground thumbnail failed: {e.Message}"); return null; }
        finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
    }

    static bool ContainsAny(string s, string[] tokens)
    {
        foreach (var t in tokens) if (!string.IsNullOrEmpty(t) && s.Contains(t)) return true;
        return false;
    }

    // Measure a prefab's world XZ footprint as it will actually be PLACED (identity rotation), via the shared bounds
    // helper. The prefab owns its size: the importer seats props at the prefab's OWN authored root scale and never
    // rescales, so we must measure at that scale too — `scale` is an extra multiplier on top (1 = as-authored).
    // (Bug: forcing localScale = 1 ignored trees' authored 0.3 root scale, so the painter drew them ~3.3× oversized.)
    static bool Footprint(string name, float scale, out float fx, out float fz)
    {
        fx = fz = 0f;
        var pf = LoadProp(name);
        if (pf == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale *= Mathf.Max(0.01f, scale);
        bool ok = PropPlacement.TryWorldBounds(go, out var bnds);
        if (ok) { fx = bnds.size.x; fz = bnds.size.z; }
        Object.DestroyImmediate(go);
        return ok;
    }

    // ── manifest DTOs (JsonUtility-serializable) ─────────────────────────────────────────────────────────
    [System.Serializable] class MfFoot   { public float x; public float z; }
    [System.Serializable] class MfPrefab { public string name; public string path; public float x; public float z; public string thumb; }
    [System.Serializable] class MfBrush
    {
        public string id, label, color, kind, groundTex, thumb;
        public int channel = -1;                 // ground blend channel (0 base … 4); -1 for objects
        public bool hasFootprint;
        public MfFoot footprint = new MfFoot();
        public List<MfPrefab> prefabs = new List<MfPrefab>();
    }
    [System.Serializable] class Manifest
    {
        public int version;
        public string exportedAt;
        public float cellSize;
        public float groundTexWorldSize;   // world units one ground-texture repeat spans in-game (painter preview density)
        public List<MfBrush> brushes = new List<MfBrush>();
    }
}
