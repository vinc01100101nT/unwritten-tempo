using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// DRAWS EVERY MAP'S MINIMAP PICTURE — the one place it is done, for every map at once, touching no scene.
///
/// <para>Each map the game can load (every scene in the build but Systems) is opened as a hidden copy that never
/// appears in the editor, drawn straight down from above, and saved as <c>Resources/Minimaps/&lt;map&gt;.png</c>; the
/// square it covers goes into <see cref="MinimapCatalog"/>. The HUD <see cref="Minimap"/> reads both and runs no
/// camera. The same picture is the wiki's map preview (<see cref="WorldMapExport.WritePreview"/>): one drawing, two
/// uses.</para>
///
/// <para><b>When it runs.</b> A map is drawn again whenever its scene is saved (agreed 2026-09-26: the save writes that
/// map's picture). The menu draws every map. A save during Play waits for Play to end.</para>
/// </summary>
[InitializeOnLoad]
public static class MinimapBaker
{
    const string PictureDir = "Assets/3_Data/Resources/" + MinimapCatalog.PictureFolder;
    const string CatalogPath = "Assets/3_Data/Resources/" + MinimapCatalog.ResourcePath + ".asset";
    const float MarginFrac = 0.04f;     // breathing room around the map bounds
    // As sharp as the live minimap it replaced: 256 pixels across the 40 units it showed.
    const float PixelsPerUnit = 6.4f;
    const int MinPx = 256, MaxPx = 1024;

    [MenuItem("Tools/unwritten/World/Bake Minimaps (all maps)", priority = 130)]
    public static void BakeAllMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Bake Minimaps", "Exit Play mode first, then re-run.", "OK"); return; }
        int n = BakeAll();
        Debug.Log($"[unwritten] Drew {n} map(s) for the minimap and the wiki.");
    }

    /// <summary>Draw every map the game can load. Returns how many were drawn.</summary>
    public static int BakeAll()
    {
        int done = 0;
        var maps = MapScenes();
        try
        {
            for (int i = 0; i < maps.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Bake Minimaps", $"Drawing '{Path.GetFileNameWithoutExtension(maps[i])}' " +
                                                 $"({i + 1}/{maps.Count})", (float)i / maps.Count);
                if (Bake(maps[i])) done++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        WorldMapExport.Write();
        return done;
    }

    // Every scene the game can load, except the one that holds the HUD and the player.
    static List<string> MapScenes()
    {
        var list = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && Path.GetFileNameWithoutExtension(s.path) != MapManager.SystemsScene) list.Add(s.path);
        return list;
    }

    /// <summary>Draw one map from its scene file: its picture into the game, its square into the catalog, and the same
    /// picture to the wiki. False when the scene has nothing that draws.</summary>
    public static bool Bake(string scenePath)
    {
        string map = Path.GetFileNameWithoutExtension(scenePath);
        var scene = EditorSceneManager.OpenPreviewScene(scenePath);
        byte[] png;
        Vector2 worldMin;
        float worldSize;
        int px;
        try
        {
            if (!TryComputeBounds(scene, out Bounds b)) return false;
            float extent = Mathf.Max(b.size.x, b.size.z) * 0.5f * (1f + MarginFrac);
            worldSize = extent * 2f;
            worldMin = new Vector2(b.center.x - extent, b.center.z - extent);
            px = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(worldSize * PixelsPerUnit)), MinPx, MaxPx);
            var tex = Snapshot(b, extent, px, scene);
            png = tex.EncodeToPNG();
            // editor-write-ok: destroys the throwaway picture this bake just made in memory; nothing saved is touched.
            Object.DestroyImmediate(tex);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        SavePicture(map, png, px);
        SaveEntry(map, worldMin, worldSize);
        WorldMapExport.WritePreview(map, png);
        return true;
    }

    static void SavePicture(string map, byte[] png, int px)
    {
        Directory.CreateDirectory(PictureDir);
        string path = PictureDir + "/" + map + ".png";
        if (File.Exists(path) && SameBytes(File.ReadAllBytes(path), png) && AssetImporter.GetAtPath(path) != null) return;
        // editor-write-ok: writes the picture this bake just drew, because the user saved the map or ran the bake.
        File.WriteAllBytes(path, png);
        // editor-write-ok: imports the picture this bake just drew, because the user saved the map or ran the bake.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            // Compressed, no alpha, not readable, never above the size it was drawn at (ASSETS.md rules 1–3).
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = false;
            ti.isReadable = false;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.maxTextureSize = px;
            ti.SaveAndReimport();
        }
    }

    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static void SaveEntry(string map, Vector2 worldMin, float worldSize)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MinimapCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<MinimapCatalog>();
            // editor-write-ok: makes the minimap list the first time a map is drawn, from the user's save or bake.
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        var entry = catalog.Find(map);
        if (entry != null && entry.worldMin == worldMin && Mathf.Approximately(entry.worldSize, worldSize)) return;
        if (entry == null) { entry = new MinimapCatalog.Entry { map = map }; catalog.maps.Add(entry); }
        entry.worldMin = worldMin;
        entry.worldSize = worldSize;
        catalog.maps.Sort((a, b) => string.CompareOrdinal(a.map, b.map));
        // editor-write-ok: records where the map's new picture sits, because the user saved the map or ran the bake.
        EditorUtility.SetDirty(catalog);
        // editor-write-ok: same — the list is saved with the picture it describes.
        AssetDatabase.SaveAssetIfDirty(catalog);
    }

    // Encapsulate every renderer's world bounds in the scene's roots.
    static bool TryComputeBounds(Scene scene, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer) continue;   // FX shouldn't define the map's footprint
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
        return any;
    }

    // Render the map straight down into a Texture2D. The camera lives in the hidden copy and sees only it, so the
    // scene open in the editor is never touched. UI and the weather quads (TransparentFX) are left out.
    static Texture2D Snapshot(Bounds b, float extent, int resolution, Scene inScene)
    {
        var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { name = "MinimapBakeRT" };
        rt.Create();

        var camGo = new GameObject("~MinimapBakeCam") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(camGo, inScene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = inScene;
        cam.orthographic = true;
        cam.orthographicSize = extent;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.09f, 1f);   // dark where there's no terrain
        cam.cullingMask = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("TransparentFX")));
        cam.allowMSAA = false;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = b.size.y + 400f;
        // editor-write-ok: places the throwaway camera inside the hidden copy of the map; it is never saved.
        cam.transform.position = new Vector3(b.center.x, b.max.y + 50f, b.center.z);
        // editor-write-ok: points that throwaway camera straight down, fixed north; it is never saved.
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.targetTexture = rt;

        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = rt; RenderPipeline.SubmitRenderRequest(cam, req); }
        else cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        // editor-write-ok: destroys the throwaway camera this bake made inside the hidden copy of the map.
        Object.DestroyImmediate(camGo);
        rt.Release();
        // editor-write-ok: destroys the throwaway render target this bake made in memory.
        Object.DestroyImmediate(rt);
        return tex;
    }

    // ---- the trigger: a saved map scene is drawn again ---------------------------------------------------------

    static MinimapBaker()
    {
        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorApplication.update += BakePending;
    }

    // Several scenes saved in one go (a world build) are drawn once each, after the saving is over.
    static readonly HashSet<string> pending = new HashSet<string>();

    static void OnSceneSaved(Scene scene)
    {
        if (string.IsNullOrEmpty(scene.path) || scene.name == MapManager.SystemsScene) return;
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && s.path == scene.path) { pending.Add(scene.path); return; }
    }

    // Runs on the editor's update (a background editor keeps updating but can leave its delayCall queue unrun), so it
    // returns at once when nothing was saved.
    static void BakePending()
    {
        if (pending.Count == 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var paths = new List<string>(pending);
        pending.Clear();
        foreach (var path in paths) Bake(path);
        WorldMapExport.Write();
    }
}
