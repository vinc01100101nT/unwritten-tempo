using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes the <see cref="WorldAtlas"/> the world-map HUD shows. Opens each generated map scene, snapshots it
/// straight down (fixed-north top-down render, same as the minimap), and composites every thumbnail into ONE
/// image by the map's <b>world-space rectangle</b> — free placement, any size (no grid). Writes the PNG +
/// a <see cref="WorldAtlas"/> asset into <c>Resources/</c> so <see cref="WorldMapHUD"/> can load it with no
/// scene wiring.
///
/// Driven from the World Creator window, which holds each map's scene name + world rect + type. The whole world
/// is rendered at a single <c>pixels-per-world-unit</c> scale so every map shares one consistent scale and lands
/// at its true relative position; empty space stays dark.
/// </summary>
public static class WorldAtlasBaker
{
    // World-space rect of one map. (x,y) = top-left corner; x→east, y→south. w,h in world units.
    public struct Entry { public string scene; public float x, y, w, h; public string type; }

    const string ResourcesDir = "Assets/3_Data/Resources";
    const string AtlasAssetPath = "Assets/3_Data/Resources/WorldAtlas.asset";
    const string AtlasPngPath = "Assets/3_Data/Resources/WorldMap/atlas.png";
    const int TargetLongSidePx = 2048;     // aim the longest world axis at ~this many pixels
    const int MaxAtlasPx = 4096;           // hard cap on either atlas dimension
    const int MinMapPx = 8;                // smallest thumbnail edge

    public static void Bake(List<Entry> entries, bool interactive = true)
    {
        if (entries == null || entries.Count == 0)
        { if (interactive) EditorUtility.DisplayDialog("Bake World Map", "No maps to bake.", "OK"); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { if (interactive) EditorUtility.DisplayDialog("Bake World Map", "Exit Play mode first, then re-run.", "OK"); return; }

        // World bounds across every map rect.
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var e in entries)
        {
            minX = Mathf.Min(minX, e.x);     maxX = Mathf.Max(maxX, e.x + e.w);
            minY = Mathf.Min(minY, e.y);     maxY = Mathf.Max(maxY, e.y + e.h);
        }
        float worldW = Mathf.Max(1f, maxX - minX), worldH = Mathf.Max(1f, maxY - minY);

        // One scale for the whole world; clamp so neither atlas dimension blows past the cap.
        float ppu = TargetLongSidePx / Mathf.Max(worldW, worldH);
        ppu = Mathf.Min(ppu, MaxAtlasPx / worldW, MaxAtlasPx / worldH);
        ppu = Mathf.Max(ppu, 0.5f);

        int atlasW = Mathf.Min(MaxAtlasPx, Mathf.CeilToInt(worldW * ppu));
        int atlasH = Mathf.Min(MaxAtlasPx, Mathf.CeilToInt(worldH * ppu));

        var atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGB24, false);
        // Survive the UnloadUnusedAssets that OpenScene(Single) triggers each iteration — otherwise this
        // in-memory texture is destroyed mid-loop (a C# local doesn't protect a UnityEngine.Object from it).
        atlas.hideFlags = HideFlags.HideAndDontSave;
        FillDark(atlas);

        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                string path = $"Assets/Scenes/{e.scene}.unity";
                if (!File.Exists(path)) { Debug.LogWarning($"[unwritten] World atlas: scene '{path}' missing; skipped."); continue; }
                EditorUtility.DisplayProgressBar("Bake World Map", $"Snapshotting '{e.scene}' ({i + 1}/{entries.Count})", (float)i / entries.Count);

                int pxW = Mathf.Max(MinMapPx, Mathf.RoundToInt(e.w * ppu));
                int pxH = Mathf.Max(MinMapPx, Mathf.RoundToInt(e.h * ppu));

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var thumb = SnapshotRect(scene, e.w, e.h, pxW, pxH);

                // Pixel origin: left = (x-minX); top row = (y-minY). Texture2D y=0 is the bottom, so flip.
                int px = Mathf.RoundToInt((e.x - minX) * ppu);
                int topFromTop = Mathf.RoundToInt((e.y - minY) * ppu);
                int py = atlasH - topFromTop - pxH;
                // Clamp into the atlas (rounding can push a hair past the edge).
                px = Mathf.Clamp(px, 0, Mathf.Max(0, atlasW - pxW));
                py = Mathf.Clamp(py, 0, Mathf.Max(0, atlasH - pxH));

                atlas.SetPixels(px, py, pxW, pxH, thumb.GetPixels());
                Object.DestroyImmediate(thumb);
            }
            atlas.Apply();
            EditorUtility.ClearProgressBar();
        }
        catch { EditorUtility.ClearProgressBar(); Object.DestroyImmediate(atlas); throw; }

        var texture = SaveAtlasPng(atlas);
        Object.DestroyImmediate(atlas);
        WriteAsset(texture, entries, minX, minY, worldW, worldH, ppu);

        Debug.Log($"[unwritten] Baked world atlas: {atlasW}×{atlasH}px from {entries.Count} maps ({ppu:0.##} px/unit) → {AtlasPngPath}.");
        if (interactive)
            EditorUtility.DisplayDialog("Bake World Map — done",
                $"Composited {entries.Count} map snapshots into a {atlasW}×{atlasH} world atlas.\n\n"
                + "Press Play and open the World Map (default key: N) to see it.", "OK");
    }

    // Render one map straight down at its own w×h extent (so rectangles aren't distorted). Mobs are hidden for the
    // snapshot (billboards are edge-on from above). Mirrors MinimapBaker's camera setup.
    static Texture2D SnapshotRect(Scene scene, float sizeX, float sizeZ, int pxW, int pxH)
    {
        var hidden = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Mobs" && root.activeSelf) { root.SetActive(false); hidden.Add(root); }

        var rt = new RenderTexture(pxW, pxH, 24, RenderTextureFormat.ARGB32) { name = "WorldAtlasRT" };
        rt.Create();

        var camGo = new GameObject("~WorldAtlasCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = sizeZ * 0.5f;                 // half-height (screen-up = world +Z / north)
        cam.aspect = sizeX / Mathf.Max(0.0001f, sizeZ);      // width/height = east/north
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.09f, 1f);
        cam.cullingMask = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("TransparentFX")));
        cam.allowMSAA = false;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1000f;
        cam.transform.position = new Vector3(0f, 300f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);          // straight down, fixed-north (+Z up, +X right)
        cam.targetTexture = rt;

        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = rt; RenderPipeline.SubmitRenderRequest(cam, req); }
        else cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(pxW, pxH, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, pxW, pxH), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);

        foreach (var go in hidden) go.SetActive(true);
        return tex;
    }

    static void FillDark(Texture2D t)
    {
        var c = new Color(0.05f, 0.06f, 0.06f, 1f);
        var px = new Color[t.width * t.height];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
    }

    static Texture2D SaveAtlasPng(Texture2D atlas)
    {
        if (!AssetDatabase.IsValidFolder(ResourcesDir)) AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/3_Data/Resources/WorldMap")) AssetDatabase.CreateFolder(ResourcesDir, "WorldMap");

        File.WriteAllBytes(AtlasPngPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(AtlasPngPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(AtlasPngPath) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = TargetLongSidePx;   // ASSETS.md: nothing above 2048; the atlas is baked at this size
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPngPath);
    }

    static void WriteAsset(Texture2D texture, List<Entry> entries, float minX, float minY, float worldW, float worldH, float ppu)
    {
        var asset = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasAssetPath);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<WorldAtlas>();

        asset.atlas = texture;
        asset.minX = minX; asset.minY = minY;
        asset.worldW = worldW; asset.worldH = worldH;
        asset.ppu = ppu;
        asset.maps.Clear();
        foreach (var e in entries)
            asset.maps.Add(new WorldAtlas.Entry { scene = e.scene, x = e.x, y = e.y, w = e.w, h = e.h, type = e.type });

        if (isNew) AssetDatabase.CreateAsset(asset, AtlasAssetPath);
        else EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
    }
}
