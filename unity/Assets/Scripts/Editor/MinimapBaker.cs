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
    // A seamless world: one overview for the whole-map window (sharper than a zone's, since it covers far more), and
    // the minimap's own square pictures, each TileUnits wide at TilePx — 8 pixels a unit, a little sharper than a zone.
    const int OverviewMaxPx = 2048;
    const float TileUnits = 32f;
    const int TilePx = 256;

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
        bool seamless;
        try
        {
            // A seamless world keeps its ground in chunks the game builds near people; all of them go into the copy.
            seamless = SeamlessWorldPreview.Fill(scene, out var world);
            PrepareForDrawing(scene);
            if (!TryComputeBounds(scene, out Bounds b)) return false;
            if (seamless)
                // The world's own corners, never how far a prop at its edge happens to reach.
                b = new Bounds(new Vector3((world.worldMin.x + world.worldMax.x) * 0.5f, b.center.y, (world.worldMin.y + world.worldMax.y) * 0.5f),
                               new Vector3(world.worldMax.x - world.worldMin.x, b.size.y, world.worldMax.y - world.worldMin.y));
            float extent = Mathf.Max(b.size.x, b.size.z) * 0.5f * (1f + MarginFrac);
            worldSize = extent * 2f;
            worldMin = new Vector2(b.center.x - extent, b.center.z - extent);
            px = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(worldSize * PixelsPerUnit)), MinPx, seamless ? OverviewMaxPx : MaxPx);
            var tex = seamless ? BakeTiles(map, world, b, scene, worldMin, worldSize, px) : Snapshot(b, extent, px, scene);
            png = tex.EncodeToPNG();
            // editor-write-ok: destroys the throwaway picture this bake just made in memory; nothing saved is touched.
            Object.DestroyImmediate(tex);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        SavePicture(map, png, px);
        SaveEntry(map, worldMin, worldSize, seamless ? TileUnits : 0f, seamless ? TilePx : 0);
        WorldMapExport.WritePreview(map, png);
        return true;
    }

    // Two things the game's own camera has and a camera high above a hidden copy does not:
    //  · THE SUN. A zone carries its own; a seamless world is lit by the one in the Systems scene, which a hidden copy of
    //    the world does not hold — drawn unlit it came out nearly black. A copy of that sun is put into the copy.
    //  · THE NEAR LOOK of every model. From that high up a tree is a few pixels tall, so its detail levels swap it for
    //    nothing at all — the world's 19,000 props were missing from its picture. Every one is held at its nearest look.
    static void PrepareForDrawing(Scene scene)
    {
        bool lit = false;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var l in root.GetComponentsInChildren<Light>(false))
                if (l.type == LightType.Directional && l.enabled) lit = true;
        if (!lit)
        {
            Light sun = null;
            for (int i = 0; i < SceneManager.sceneCount && sun == null; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || s.name != MapManager.SystemsScene) continue;
                foreach (var root in s.GetRootGameObjects())
                    foreach (var l in root.GetComponentsInChildren<Light>(false))
                        if (sun == null && l.type == LightType.Directional) sun = l;
            }
            if (sun == null) Debug.LogWarning("[minimap] '" + scene.name + "' has no sun and the Systems scene is not open to lend one — its picture is drawn unlit.");
            else
            {
                var go = new GameObject("~MinimapBakeSun") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, scene);
                // editor-write-ok: a throwaway light inside the hidden copy, matching the Systems sun; never saved.
                go.transform.rotation = sun.transform.rotation;
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.color = sun.color;
                l.intensity = sun.intensity;
                l.shadows = sun.shadows;
            }
        }
        foreach (var root in scene.GetRootGameObjects())
            foreach (var g in root.GetComponentsInChildren<LODGroup>(false))
                // editor-write-ok: holds a model at its nearest look inside the hidden copy only; never saved.
                g.ForceLOD(0);
    }

    // The minimap's square pictures of a seamless world: one per TileUnits square that holds any of its ground, under
    // Minimaps/<map>/. A square that no longer holds ground loses its picture.
    // THE WHOLE-MAP PICTURE (px across, from worldMin, worldSize wide) IS MADE FROM THE SQUARES, each output pixel the
    // average of the square pictures' pixels inside it. Drawn in one go instead, every grass and flower carpet of the
    // world would have to be laid at once (a few hundred thousand plants a square), and its trees would be a pixel across.
    static Texture2D BakeTiles(string map, SeamlessWorld world, Bounds b, Scene scene, Vector2 worldMin, float worldSize, int px)
    {
        var made = new HashSet<string>();
        var sum = new Vector3[px * px];
        var count = new int[px * px];
        float toPx = px / worldSize, step = TileUnits / TilePx;
        int gx0 = Mathf.FloorToInt(world.worldMin.x / TileUnits), gx1 = Mathf.CeilToInt(world.worldMax.x / TileUnits) - 1;
        int gy0 = Mathf.FloorToInt(world.worldMin.y / TileUnits), gy1 = Mathf.CeilToInt(world.worldMax.y / TileUnits) - 1;
        for (int gy = gy0; gy <= gy1; gy++)
            for (int gx = gx0; gx <= gx1; gx++)
            {
                var square = new Rect(gx * TileUnits, gy * TileUnits, TileUnits, TileUnits);
                if (!world.chunks.Exists(c => square.Overlaps(Rect.MinMaxRect(c.min.x, c.min.y, c.max.x, c.max.y)))) continue;
                var tb = new Bounds(new Vector3(square.center.x, b.center.y, square.center.y), new Vector3(TileUnits, b.size.y, TileUnits));
                var tex = Snapshot(tb, TileUnits * 0.5f, TilePx, scene);
                string name = MinimapCatalog.TilePicture(map, gx, gy);
                SavePicture(name, tex.EncodeToPNG(), TilePx);
                var pixels = tex.GetPixels32();
                for (int sy = 0; sy < TilePx; sy++)
                {
                    int oy = Mathf.FloorToInt((square.yMin + (sy + 0.5f) * step - worldMin.y) * toPx);
                    if (oy < 0 || oy >= px) continue;
                    for (int sx = 0; sx < TilePx; sx++)
                    {
                        int ox = Mathf.FloorToInt((square.xMin + (sx + 0.5f) * step - worldMin.x) * toPx);
                        if (ox < 0 || ox >= px) continue;
                        var c = pixels[sy * TilePx + sx];
                        sum[oy * px + ox] += new Vector3(c.r, c.g, c.b);
                        count[oy * px + ox]++;
                    }
                }
                // editor-write-ok: destroys the throwaway picture this bake just made in memory; nothing saved is touched.
                Object.DestroyImmediate(tex);
                made.Add(PictureDir + "/" + name + ".png");
            }
        var all = new Color32[px * px];
        Color32 none = Background;
        for (int i = 0; i < all.Length; i++)
            all[i] = count[i] == 0 ? none
                   : new Color32((byte)Mathf.RoundToInt(sum[i].x / count[i]), (byte)Mathf.RoundToInt(sum[i].y / count[i]), (byte)Mathf.RoundToInt(sum[i].z / count[i]), 255);
        var overview = new Texture2D(px, px, TextureFormat.RGB24, false);
        overview.SetPixels32(all);
        overview.Apply();
        string dir = PictureDir + "/" + map;
        if (!Directory.Exists(dir)) return overview;
        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            string path = f.Replace('\\', '/');
            // editor-write-ok: removes a square picture whose square no longer holds ground, during the user's bake.
            if (!made.Contains(path)) AssetDatabase.DeleteAsset(path);
        }
        return overview;
    }

    static void SavePicture(string map, byte[] png, int px)
    {
        string path = PictureDir + "/" + map + ".png";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
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

    static void SaveEntry(string map, Vector2 worldMin, float worldSize, float tileSize, int tilePixels)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MinimapCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<MinimapCatalog>();
            // editor-write-ok: makes the minimap list the first time a map is drawn, from the user's save or bake.
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        var entry = catalog.Find(map);
        if (entry != null && entry.worldMin == worldMin && Mathf.Approximately(entry.worldSize, worldSize)
            && Mathf.Approximately(entry.tileSize, tileSize) && entry.tilePixels == tilePixels) return;
        if (entry == null) { entry = new MinimapCatalog.Entry { map = map }; catalog.maps.Add(entry); }
        entry.worldMin = worldMin;
        entry.worldSize = worldSize;
        entry.tileSize = tileSize;
        entry.tilePixels = tilePixels;
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
    // DRAWN AT Supersample TIMES ITS SIZE, then halved until it fits, each halving the average of four pixels. Drawn at its
    // own size, every tree, bush and flower vanished and only solid models (rocks) showed: a leaf is cut out of its
    // texture where the texture is solid enough, and a plant a few pixels across reads that texture's blurred small copy,
    // which is solid nowhere (measured 2026-10-30, square 20,15: no tree at 8 pixels a unit, every one at 32).
    const int Supersample = 4;
    static readonly Color Background = new Color(0.08f, 0.10f, 0.09f, 1f);   // dark where there's no terrain

    static Texture2D Snapshot(Bounds b, float extent, int resolution, Scene inScene)
    {
        int big = resolution * Supersample;
        var rt = new RenderTexture(big, big, 24, RenderTextureFormat.ARGB32) { name = "MinimapBakeRT" };
        rt.Create();

        var camGo = new GameObject("~MinimapBakeCam") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(camGo, inScene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = inScene;
        cam.orthographic = true;
        cam.orthographicSize = extent;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.cullingMask = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("TransparentFX")));
        cam.allowMSAA = false;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = b.size.y + 400f;
        // editor-write-ok: places the throwaway camera inside the hidden copy of the map; it is never saved.
        cam.transform.position = new Vector3(b.center.x, b.max.y + 50f, b.center.z);
        // editor-write-ok: points that throwaway camera straight down, fixed north; it is never saved.
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.targetTexture = rt;

        // Grass and flower carpets draw only when handed to a camera: every one reaching into the view is handed to this one.
        var view = Rect.MinMaxRect(b.center.x - extent, b.center.z - extent, b.center.x + extent, b.center.z + extent);
        var carpets = new List<FlowerCarpet>();
        foreach (var root in inScene.GetRootGameObjects())
            foreach (var carpet in root.GetComponentsInChildren<FlowerCarpet>(false))
                if (carpet.patches.Exists(p => p.clip.Overlaps(view))) { carpet.DrawAllInto(cam); carpets.Add(carpet); }

        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = rt; RenderPipeline.SubmitRenderRequest(cam, req); }
        else cam.Render();
        foreach (var carpet in carpets) carpet.ForgetLaid();

        var shrunk = rt;
        for (int size = big / 2; size >= resolution; size /= 2)
        {
            var half = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            half.filterMode = FilterMode.Bilinear;
            Graphics.Blit(shrunk, half);
            if (shrunk != rt) RenderTexture.ReleaseTemporary(shrunk);
            shrunk = half;
        }

        var prev = RenderTexture.active;
        RenderTexture.active = shrunk;
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        if (shrunk != rt) RenderTexture.ReleaseTemporary(shrunk);

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
